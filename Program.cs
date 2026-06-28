using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog;

[assembly: InternalsVisibleTo("dr-mcp-dbschema.Tests")]

if (args.Length == 1 && args[0] == "--version")
{
    var version = typeof(Program).Assembly.GetName().Version;
    Console.WriteLine($"{version?.Major}.{version?.Minor}.{version?.Build}");
    return;
}

var workDir = Directory.GetCurrentDirectory();

Console.Error.WriteLine($"[dr-mcp-dbschema] avvio — CWD: {workDir}");

// Radice di scansione degli appsettings*.json.
// workDir è la root del progetto che ospita il tool (es. C:\...\FoundryBridge).
// Priorità: DB_SCHEMA_ROOT (override esplicito) > src/ (convenzione standard) > workDir (fallback)
var searchRootRaw = Environment.GetEnvironmentVariable("DB_SCHEMA_ROOT")
    is { Length: > 0 } envRoot ? envRoot
    : Directory.Exists(Path.Combine(workDir, "src")) ? Path.Combine(workDir, "src")
    : workDir;

// Risolve sempre a percorso assoluto per evitare dipendenze dal CWD del processo host
var searchRoot = Path.GetFullPath(searchRootRaw, workDir);

Console.Error.WriteLine($"[dr-mcp-dbschema] searchRoot: {searchRoot}");

if (!Directory.Exists(searchRoot))
{
    Console.Error.WriteLine($"[dr-mcp-dbschema] ATTENZIONE: searchRoot non esiste — nessun appsettings sarà trovato");
}

// Scansione ricorsiva di tutti gli appsettings*.json sotto searchRoot, esclusi bin/ e obj/.
// Ordine di lettura (last-wins, priorità crescente):
//   1 — appsettings.json e varianti base  (appsettings*.json senza punto interno)
//   2 — appsettings.{env}.json            (appsettings.Development.json, ecc.)
//   3 — appsettings.local.json            (override locale, non committato — vince su tutto)
var appsettingsFiles = Directory.Exists(searchRoot)
    ? Directory.GetFiles(searchRoot, "appsettings*.json", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
        .OrderBy(f =>
        {
            var name = Path.GetFileName(f);
            if (name.Equals("appsettings.local.json", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^appsettings\..+\.json$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return 2;
            }

            return 1;
        })
        .ThenBy(f => f)
        .ToList()
    : new List<string>();

Console.Error.WriteLine($"[dr-mcp-dbschema] file appsettings trovati: {appsettingsFiles.Count}");
foreach (var f in appsettingsFiles)
{
    Console.Error.WriteLine($"[dr-mcp-dbschema]   {f}");
}

var available = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
// Traccia il file sorgente di ogni CS (usato per metadati e auto-selezione per ambiente)
var availableSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var ddlSettings = new DdlSettings();
var enableFileLog = false;
var logFilePath = "dr-mcp-dbschema.log";

foreach (var file in appsettingsFiles)
{
    var config = new ConfigurationBuilder()
        .AddJsonFile(file, optional: true)
        .Build();

    foreach (var kv in config.GetSection("ConnectionStrings").GetChildren())
    {
        if (!string.IsNullOrWhiteSpace(kv.Value))
        {
            available[kv.Key] = kv.Value;
            availableSources[kv.Key] = file;
        }
    }

    // Legge impostazioni DDL (l'ultimo file trovato con la sezione vince)
    var ddlSection = config.GetSection("Ddl");
    if (ddlSection.Exists())
    {
        if (bool.TryParse(ddlSection["AllowSelect"], out var allowSelect))
        {
            ddlSettings.AllowSelect = allowSelect;
        }

        if (bool.TryParse(ddlSection["AllowCreate"], out var allowCreate))
        {
            ddlSettings.AllowCreate = allowCreate;
        }

        if (bool.TryParse(ddlSection["AllowAlter"], out var allowAlter))
        {
            ddlSettings.AllowAlter = allowAlter;
        }

        if (bool.TryParse(ddlSection["AllowDrop"], out var allowDrop))
        {
            ddlSettings.AllowDrop = allowDrop;
        }
    }

    // Legge impostazioni di logging (l'ultimo file trovato con la sezione vince)
    var loggingSection = config.GetSection("Logging");
    if (loggingSection.Exists())
    {
        if (bool.TryParse(loggingSection["EnableFileLog"], out var efl))
        {
            enableFileLog = efl;
        }

        if (!string.IsNullOrWhiteSpace(loggingSection["LogFile"]))
        {
            logFilePath = loggingSection["LogFile"]!;
        }
    }
}

Console.Error.WriteLine($"[dr-mcp-dbschema] connection string trovate: {available.Count}{(available.Count == 0 ? " — ATTENZIONE: nessuna ConnectionStrings nei file scansionati" : $" ({string.Join(", ", available.Keys)})")}");

// Override log via env var — utile quando il tool gira compilato senza appsettings accessibili.
// DR_MCP_ENABLE_LOG=true  → attiva log su file
// DR_MCP_LOG_FILE=<path>  → percorso assoluto del file di log
if (Environment.GetEnvironmentVariable("DR_MCP_ENABLE_LOG") is { Length: > 0 } envLogFlag
    && bool.TryParse(envLogFlag, out var envLogEnabled))
{
    enableFileLog = envLogEnabled;
}

if (Environment.GetEnvironmentVariable("DR_MCP_LOG_FILE") is { Length: > 0 } envLogFile)
{
    logFilePath = envLogFile;
}

// Se il log è abilitato ma il path è ancora il default relativo,
// redirige in %TEMP% per garantire scrivibilità indipendentemente dal CWD dell'host.
if (enableFileLog && !Path.IsPathRooted(logFilePath))
{
    logFilePath = Path.Combine(Path.GetTempPath(), "dr-mcp-dbschema", logFilePath);
}

// Override esplicito da variabile d'ambiente.
// NOTA: args[0] non è supportato — passerebbe la CS in chiaro nella process list del SO (ps aux, Task Manager).
var explicitCs = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

if (!string.IsNullOrWhiteSpace(explicitCs))
{
    available["(override)"] = explicitCs;
    availableSources["(override)"] = "DB_CONNECTION_STRING (env var)";
    Console.Error.WriteLine($"[dr-mcp-dbschema] connection string da DB_CONNECTION_STRING (env var): aggiunta come '(override)'");
}

var state = new ConnectionState
{
    Available = available,
    AvailableSources = availableSources,
    WorkDir = workDir,
    SearchRoot = searchRoot,
    ScannedFiles = appsettingsFiles
};

Console.Error.WriteLine($"[dr-mcp-dbschema] connessione attiva: nessuna — usa GetActiveConnection o qualsiasi tool per la selezione guidata");

var tokenStore = new DdlTokenStore();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();

if (enableFileLog)
{
    var logFileAbsolute = Path.GetFullPath(logFilePath, workDir);
    var serilogLogger = new LoggerConfiguration()
        .MinimumLevel.Verbose()
        .WriteTo.File(logFileAbsolute, rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true)
        .CreateLogger();
    builder.Logging.AddSerilog(serilogLogger);
    Console.Error.WriteLine($"[dr-mcp-dbschema] log file attivo: {logFileAbsolute}");
}

builder.Services.AddSingleton(state);
builder.Services.AddSingleton(ddlSettings);
builder.Services.AddSingleton(tokenStore);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

Console.Error.WriteLine($"[dr-mcp-dbschema] MCP server pronto");
await builder.Build().RunAsync();
