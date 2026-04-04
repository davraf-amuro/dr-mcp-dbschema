using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("dr-mcp-dbschema.Tests")]

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
    Console.Error.WriteLine($"[dr-mcp-dbschema] ATTENZIONE: searchRoot non esiste — nessun appsettings sarà trovato");

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
            if (name.Equals("appsettings.local.json", StringComparison.OrdinalIgnoreCase)) return 3;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^appsettings\..+\.json$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return 2;
            return 1;
        })
        .ThenBy(f => f)
        .ToList()
    : new List<string>();

Console.Error.WriteLine($"[dr-mcp-dbschema] file appsettings trovati: {appsettingsFiles.Count}");
foreach (var f in appsettingsFiles)
    Console.Error.WriteLine($"[dr-mcp-dbschema]   {f}");

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
        if (bool.TryParse(ddlSection["AllowCreate"], out var allowCreate))
            ddlSettings.AllowCreate = allowCreate;
        if (bool.TryParse(ddlSection["AllowAlter"], out var allowAlter))
            ddlSettings.AllowAlter = allowAlter;
        if (bool.TryParse(ddlSection["AllowDrop"], out var allowDrop))
            ddlSettings.AllowDrop = allowDrop;
    }

    // Legge impostazioni di logging (l'ultimo file trovato con la sezione vince)
    var loggingSection = config.GetSection("Logging");
    if (loggingSection.Exists())
    {
        if (bool.TryParse(loggingSection["EnableFileLog"], out var efl))
            enableFileLog = efl;
        if (!string.IsNullOrWhiteSpace(loggingSection["LogFile"]))
            logFilePath = loggingSection["LogFile"]!;
    }
}

Console.Error.WriteLine($"[dr-mcp-dbschema] connection string trovate: {available.Count}{(available.Count == 0 ? " — ATTENZIONE: nessuna ConnectionStrings nei file scansionati" : $" ({string.Join(", ", available.Keys)})")}");

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

// Auto-selezione 1: se c'è una sola connection string
if (available.Count == 1)
{
    var (name, cs) = available.First();
    state.ActiveName = name;
    state.ActiveConnectionString = cs;
    Console.Error.WriteLine($"[dr-mcp-dbschema] auto-selezione connessione: '{name}'");
}

// Auto-selezione 2: per ambiente (ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT)
// Cerca la CS che proviene da appsettings.{env}.json e la seleziona silentemente
var aspEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
          ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

if (state.ActiveName is null && aspEnv is { Length: > 0 })
{
    var envFileName = $"appsettings.{aspEnv}.json";
    var envCandidates = availableSources
        .Where(kv => Path.GetFileName(kv.Value).Equals(envFileName, StringComparison.OrdinalIgnoreCase))
        .Select(kv => kv.Key)
        .ToList();

    if (envCandidates.Count == 1)
    {
        state.ActiveName = envCandidates[0];
        state.ActiveConnectionString = available[envCandidates[0]];
        Console.Error.WriteLine($"[dr-mcp-dbschema] auto-selezione per ambiente '{aspEnv}': '{state.ActiveName}' (da {envFileName})");
    }
    else if (envCandidates.Count > 1)
    {
        Console.Error.WriteLine($"[dr-mcp-dbschema] ambiente '{aspEnv}' trovato ma {envCandidates.Count} CS corrispondenti ({string.Join(", ", envCandidates)}) — nessuna auto-selezione, usa UseConnection");
    }
    else
    {
        Console.Error.WriteLine($"[dr-mcp-dbschema] ambiente '{aspEnv}' rilevato ma nessuna CS da {envFileName} — nessuna auto-selezione");
    }
}

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

// ---------------------------------------------------------------------------

public class ConnectionState
{
    public Dictionary<string, string> Available { get; set; } = new();
    /// <summary>File appsettings sorgente per ogni CS (chiave = nome CS, valore = path assoluto del file)</summary>
    public Dictionary<string, string> AvailableSources { get; set; } = new();
    public string? ActiveName { get; set; }
    public string? ActiveConnectionString { get; set; }
    public string WorkDir { get; set; } = string.Empty;
    public string SearchRoot { get; set; } = string.Empty;
    public List<string> ScannedFiles { get; set; } = new();
}

public class DdlSettings
{
    public bool AllowCreate { get; set; } = false;
    public bool AllowAlter { get; set; } = false;
    public bool AllowDrop { get; set; } = false;
}

public enum DdlKind { Create, Alter, Drop }

public class PendingDdl
{
    public required string Sql { get; init; }
    public required DdlKind Kind { get; init; }
    public string? TableName { get; init; }
    public required string ConnectionName { get; init; }
    public required string ConnectionString { get; init; }
    public DateTime ExpiresAt { get; init; }
}

public class DdlTokenStore
{
    private readonly ConcurrentDictionary<string, PendingDdl> _tokens = new();

    public string Add(PendingDdl pending)
    {
        PurgeExpired();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _tokens[token] = pending;
        return token;
    }

    public PendingDdl? Consume(string token)
    {
        PurgeExpired();
        if (_tokens.TryRemove(token.Trim().ToUpperInvariant(), out var pending) && pending.ExpiresAt >= DateTime.UtcNow)
            return pending;
        return null;
    }

    private void PurgeExpired()
    {
        foreach (var key in _tokens.Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow).Select(kv => kv.Key).ToList())
            _tokens.TryRemove(key, out _);
    }
}

// ---------------------------------------------------------------------------

[McpServerToolType]
public class DbSchemaTools(ConnectionState state, DdlSettings ddlSettings, DdlTokenStore tokenStore, ILogger<DbSchemaTools> logger)
{
    private static readonly string MigrationsDir =
        Path.Combine(Directory.GetCurrentDirectory(), "schema-migrations");

    [McpServerTool, Description("Diagnostica: mostra CWD, searchRoot, file appsettings trovati e connection string disponibili (oscurate)")]
    public string Diagnostics()
    {
        var lines = new List<string>
        {
            "STATUS: OK",
            "CODE: DIAGNOSTICS",
            "---",
            $"cwd         : {state.WorkDir}",
            $"searchRoot  : {state.SearchRoot}",
            $"searchRoot_exists: {Directory.Exists(state.SearchRoot)}",
            "",
            $"file scansionati ({state.ScannedFiles.Count}):"
        };

        if (state.ScannedFiles.Count == 0)
            lines.Add("  (nessuno)");
        else
            foreach (var f in state.ScannedFiles)
                lines.Add($"  {f}");

        lines.Add("");
        lines.Add($"connection string disponibili ({state.Available.Count}):");

        if (state.Available.Count == 0)
        {
            lines.Add("  (nessuna trovata)");
        }
        else
        {
            foreach (var kvp in state.Available)
            {
                var masked = DbSchemaHelpers.MaskConnectionString(kvp.Value);
                var active = kvp.Key == state.ActiveName ? " (attiva)" : "";
                var source = state.AvailableSources.TryGetValue(kvp.Key, out var f)
                    ? $" [{Path.GetFileName(f)}]"
                    : "";
                lines.Add($"  {kvp.Key}{active}{source}: {masked}");
            }
        }

        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Elenca le connection string disponibili nel progetto (da appsettings*.json), con il file sorgente di ciascuna")]
    public string ListConnections()
    {
        if (state.Available.Count == 0)
            return "[NO_CONNECTIONS_CONFIGURED] Nessuna connection string trovata. Aggiungi una sezione ConnectionStrings in appsettings.json oppure imposta la variabile d'ambiente DB_CONNECTION_STRING.";

        var lines = state.Available.Keys.Select(name =>
        {
            var active = name == state.ActiveName ? " (attiva)" : "";
            var source = state.AvailableSources.TryGetValue(name, out var f)
                ? $"  [{Path.GetFileName(f)}]"
                : "";
            var prefix = name == state.ActiveName ? "*" : " ";
            return $"{prefix} {name}{active}{source}";
        });

        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Seleziona quale connection string usare per le query successive")]
    public string UseConnection([Description("Nome della connection string (come restituito da ListConnections)")] string name)
    {
        if (!state.Available.TryGetValue(name, out var cs))
            return $"Connection string '{name}' non trovata. Disponibili: {string.Join(", ", state.Available.Keys)}";

        state.ActiveName = name;
        state.ActiveConnectionString = cs;
        logger.LogInformation("Connessione attivata: {Name}", name);
        return $"Connessione '{name}' attiva.";
    }

    [McpServerTool, Description("Lista tutte le viste e le tabelle presenti nel database")]
    public async Task<string> ListViews(CancellationToken ct = default)
    {
        if (state.ActiveConnectionString is null)
            return NoConnectionMessage();

        logger.LogInformation("ListViews — tentativo connessione: {Name}", state.ActiveName);
        await using var conn = new SqlConnection(state.ActiveConnectionString);
        await conn.OpenAsync(ct);
        logger.LogInformation("ListViews — connessione aperta");

        var rows = new List<string>();
        var cmd = new SqlCommand("""
            SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
            FROM INFORMATION_SCHEMA.TABLES
            ORDER BY TABLE_TYPE, TABLE_SCHEMA, TABLE_NAME
            """, conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var tableType = reader.GetString(2) == "VIEW" ? "VIEW" : "TABLE";
            rows.Add($"[{tableType}] {reader.GetString(0)}.{reader.GetString(1)}");
        }

        return rows.Count == 0
            ? "Nessuna tabella o vista trovata."
            : string.Join("\n", rows);
    }

    [McpServerTool, Description("Restituisce la definizione SQL (CREATE VIEW) di una vista, o indica se l'oggetto è una tabella")]
    public async Task<string> GetViewDefinition(
        [Description("Nome della vista o tabella (senza schema, o nella forma schema.nome)")] string viewName,
        CancellationToken ct = default)
    {
        if (state.ActiveConnectionString is null)
            return NoConnectionMessage();

        var parts = viewName.Split('.', 2);
        var schema = parts.Length == 2 ? parts[0] : null;
        var name = parts.Length == 2 ? parts[1] : parts[0];

        logger.LogInformation("GetViewDefinition — tentativo connessione: {Name}, oggetto: {ViewName}", state.ActiveName, viewName);
        await using var conn = new SqlConnection(state.ActiveConnectionString);
        await conn.OpenAsync(ct);

        // Cerca prima tra viste, poi tra tabelle
        var viewSql = schema != null
            ? "SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name"
            : "SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = @name";

        var viewCmd = new SqlCommand(viewSql, conn);
        if (schema != null) viewCmd.Parameters.AddWithValue("@schema", schema);
        viewCmd.Parameters.AddWithValue("@name", name);

        var viewResult = await viewCmd.ExecuteScalarAsync(ct);
        if (viewResult is not (DBNull or null))
            return viewResult.ToString()!;

        // Controlla se esiste come tabella
        var tableSql = schema != null
            ? "SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name"
            : "SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name";

        var tableCmd = new SqlCommand(tableSql, conn);
        if (schema != null) tableCmd.Parameters.AddWithValue("@schema", schema);
        tableCmd.Parameters.AddWithValue("@name", name);

        var tableResult = await tableCmd.ExecuteScalarAsync(ct);
        if (tableResult is not (DBNull or null))
            return $"'{viewName}' è una tabella (TABLE), non ha VIEW_DEFINITION. Usa GetViewColumns per le colonne.";

        return $"[OBJECT_NOT_FOUND] L'oggetto '{viewName}' non esiste tra tabelle e viste nel database attivo.";
    }

    [McpServerTool, Description("Restituisce le colonne di una tabella o vista (nome, tipo, nullable, posizione)")]
    public async Task<string> GetViewColumns(
        [Description("Nome della tabella o vista (senza schema, o nella forma schema.nome)")] string viewName,
        CancellationToken ct = default)
    {
        if (state.ActiveConnectionString is null)
            return NoConnectionMessage();

        var parts = viewName.Split('.', 2);
        var schema = parts.Length == 2 ? parts[0] : null;
        var name = parts.Length == 2 ? parts[1] : parts[0];

        logger.LogInformation("GetViewColumns — tentativo connessione: {Name}, oggetto: {ViewName}", state.ActiveName, viewName);
        await using var conn = new SqlConnection(state.ActiveConnectionString);
        await conn.OpenAsync(ct);

        var sql = schema != null
            ? """
              SELECT ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE,
                     CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE
              FROM INFORMATION_SCHEMA.COLUMNS
              WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name
              ORDER BY ORDINAL_POSITION
              """
            : """
              SELECT ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE,
                     CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE
              FROM INFORMATION_SCHEMA.COLUMNS
              WHERE TABLE_NAME = @name
              ORDER BY ORDINAL_POSITION
              """;

        var cmd = new SqlCommand(sql, conn);
        if (schema != null) cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@name", name);

        var rows = new List<string> { "pos | column | type | max_len | precision | scale | nullable" };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var pos = reader.GetInt32(0);
            var col = reader.GetString(1);
            var type = reader.GetString(2);
            var maxLen = reader.IsDBNull(3) ? "-" : reader.GetInt32(3).ToString();
            var prec = reader.IsDBNull(4) ? "-" : reader.GetByte(4).ToString();
            var scale = reader.IsDBNull(5) ? "-" : reader.GetInt32(5).ToString();
            var nullable = reader.GetString(6);
            rows.Add($"{pos} | {col} | {type} | {maxLen} | {prec} | {scale} | {nullable}");
        }

        return rows.Count == 1
            ? $"[OBJECT_NOT_FOUND] L'oggetto '{viewName}' non esiste tra tabelle e viste nel database attivo."
            : string.Join("\n", rows);
    }

    // -------------------------------------------------------------------------
    // DDL — CREATE
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Analizza uno statement CREATE TABLE e genera un token di conferma. " +
        "NON esegue nulla sul database. " +
        "Richiede Ddl.AllowCreate: true in appsettings.json.")]
    public string PreviewCreate(
        [Description("Statement SQL CREATE TABLE completo")] string sql)
    {
        if (!ddlSettings.AllowCreate)
            return DdlDisabledMessage("CREATE", "AllowCreate");

        if (state.ActiveConnectionString is null)
            return NoConnectionMessage();

        var tableName = DbSchemaHelpers.ExtractObjectName(sql);
        var token = tokenStore.Add(new PendingDdl
        {
            Sql = sql,
            Kind = DdlKind.Create,
            TableName = tableName,
            ConnectionName = state.ActiveName ?? "(override)",
            ConnectionString = state.ActiveConnectionString,
            ExpiresAt = DateTime.UtcNow.AddSeconds(60)
        });

        logger.LogWarning("PreviewCreate — token {Token} generato per tabella {Table} su {Db}", token, tableName, state.ActiveName);

        return $"""
            STATUS: PENDING_CONFIRM
            CODE: DDL_PREVIEW
            risk_level: DANGER
            operation: CREATE_TABLE
            table: {tableName ?? "(non rilevata)"}
            database: {state.ActiveName}
            token: {token}
            expires_in: 60s
            action: ExecuteCreate("{token}") per procedere, ignora per annullare
            ---
            {sql.Trim()}
            """;
    }

    [McpServerTool, Description(
        "Esegue la CREATE TABLE associata al token generato da PreviewCreate. " +
        "Il token e' monouso e scade in 60 secondi.")]
    public async Task<string> ExecuteCreate(
        [Description("Token restituito da PreviewCreate")] string confirmationToken,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowCreate)
            return DdlDisabledMessage("CREATE", "AllowCreate");

        var pending = tokenStore.Consume(confirmationToken);
        if (pending is null)
            return "Token non valido o scaduto. Esegui nuovamente PreviewCreate per ottenere un nuovo token.";

        if (pending.Kind != DdlKind.Create)
            return "Il token fornito non e' associato a una CREATE. Usa ExecuteAlter per le operazioni ALTER.";

        logger.LogWarning("ExecuteCreate — esecuzione CREATE TABLE {Table} su {Db}", pending.TableName, pending.ConnectionName);
        await using var conn = new SqlConnection(pending.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(pending.Sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        logger.LogInformation("ExecuteCreate — completata: {Table}", pending.TableName);

        return $"""
            STATUS: OK
            CODE: DDL_EXECUTED
            operation: CREATE_TABLE
            table: {pending.TableName ?? "(non rilevata)"}
            database: {pending.ConnectionName}
            timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            """;
    }

    // -------------------------------------------------------------------------
    // DDL — ALTER
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Analizza uno statement ALTER TABLE, mostra lo schema corrente della tabella e genera un token di conferma. " +
        "NON esegue nulla sul database. " +
        "Scrive lo script in schema-migrations/ per l'audit trail. " +
        "Richiede Ddl.AllowAlter: true in appsettings.json.")]
    public async Task<string> PreviewAlter(
        [Description("Nome della tabella da modificare (senza schema, o nella forma schema.nome)")] string tableName,
        [Description("Statement SQL ALTER TABLE completo")] string sql,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowAlter)
            return DdlDisabledMessage("ALTER", "AllowAlter");

        if (state.ActiveConnectionString is null)
            return NoConnectionMessage();

        // Recupera schema corrente della tabella
        var currentSchema = await GetColumnsText(tableName, ct);

        // Analisi rischio dell'operazione
        var riskDetails = DbSchemaHelpers.AnalyzeAlterRisk(sql);

        // Genera token prima di scrivere il file (il token e' nel file)
        var token = tokenStore.Add(new PendingDdl
        {
            Sql = sql,
            Kind = DdlKind.Alter,
            TableName = tableName,
            ConnectionName = state.ActiveName ?? "(override)",
            ConnectionString = state.ActiveConnectionString,
            ExpiresAt = DateTime.UtcNow.AddSeconds(60)
        });

        logger.LogWarning("PreviewAlter — token {Token} generato per tabella {Table} su {Db}, risk: {Risk}", token, tableName, state.ActiveName, riskDetails.Level);

        // Scrive lo script in schema-migrations/ come audit trail del tentativo
        var auditFile = WriteAuditFile(tableName, sql, token, "PENDING");

        return $"""
            STATUS: PENDING_CONFIRM
            CODE: DDL_PREVIEW
            risk_level: {riskDetails.Level}
            operation: ALTER_TABLE
            table: {tableName}
            database: {state.ActiveName}
            token: {token}
            expires_in: 60s
            audit_file: {auditFile}
            action: ExecuteAlter("{token}") per procedere, ignora per annullare
            ---
            Avvertenze:
            {riskDetails.Warnings}

            Schema corrente:
            {currentSchema}

            SQL proposto:
            {sql.Trim()}
            """;
    }

    [McpServerTool, Description(
        "Esegue l'ALTER TABLE associato al token generato da PreviewAlter. " +
        "Il token e' monouso e scade in 60 secondi. " +
        "Aggiorna il file di audit in schema-migrations/.")]
    public async Task<string> ExecuteAlter(
        [Description("Token restituito da PreviewAlter")] string confirmationToken,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowAlter)
            return DdlDisabledMessage("ALTER", "AllowAlter");

        var pending = tokenStore.Consume(confirmationToken);
        if (pending is null)
            return "Token non valido o scaduto. Esegui nuovamente PreviewAlter per ottenere un nuovo token.";

        if (pending.Kind != DdlKind.Alter)
            return "Il token fornito non e' associato a un ALTER. Usa ExecuteCreate per le operazioni CREATE.";

        logger.LogWarning("ExecuteAlter — esecuzione ALTER TABLE {Table} su {Db}", pending.TableName, pending.ConnectionName);
        await using var conn = new SqlConnection(pending.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(pending.Sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        // Aggiorna l'audit file da PENDING a EXECUTED
        WriteAuditFile(pending.TableName ?? "unknown", pending.Sql, confirmationToken, "EXECUTED");
        logger.LogInformation("ExecuteAlter — completata: {Table}", pending.TableName);

        return $"""
            STATUS: OK
            CODE: DDL_EXECUTED
            operation: ALTER_TABLE
            table: {pending.TableName ?? "(non rilevata)"}
            database: {pending.ConnectionName}
            timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            audit: schema-migrations/ aggiornato
            """;
    }

    // -------------------------------------------------------------------------
    // DDL — DROP
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Mostra lo schema corrente della tabella e genera un token di conferma per eliminarla. " +
        "NON esegue nulla sul database. " +
        "Richiede Ddl.AllowDrop: true in appsettings.json.")]
    public async Task<string> PreviewDrop(
        [Description("Nome della tabella da eliminare (senza schema, o nella forma schema.nome)")] string tableName,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowDrop)
            return DdlDisabledMessage("DROP", "AllowDrop");

        if (state.ActiveConnectionString is null)
            return NoConnectionMessage();

        var parts = tableName.Split('.', 2);
        var schema = parts.Length == 2 ? parts[0] : "dbo";
        var name = parts.Length == 2 ? parts[1] : parts[0];

        logger.LogInformation("PreviewDrop — tentativo connessione: {Db}, tabella: {Table}", state.ActiveName, tableName);
        await using var conn = new SqlConnection(state.ActiveConnectionString);
        await conn.OpenAsync(ct);

        var checkCmd = new SqlCommand(
            "SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name",
            conn);
        checkCmd.Parameters.AddWithValue("@schema", schema);
        checkCmd.Parameters.AddWithValue("@name", name);
        var tableType = await checkCmd.ExecuteScalarAsync(ct);

        if (tableType is DBNull or null)
            return $"[OBJECT_NOT_FOUND] La tabella '{tableName}' non esiste nel database.";

        var currentSchema = await GetColumnsText(tableName, ct);
        var sql = $"DROP TABLE [{schema}].[{name}]";

        var token = tokenStore.Add(new PendingDdl
        {
            Sql = sql,
            Kind = DdlKind.Drop,
            TableName = tableName,
            ConnectionName = state.ActiveName ?? "(override)",
            ConnectionString = state.ActiveConnectionString,
            ExpiresAt = DateTime.UtcNow.AddSeconds(60)
        });

        logger.LogWarning("PreviewDrop — token {Token} generato per tabella {Table} su {Db}", token, tableName, state.ActiveName);

        return $"""
            STATUS: PENDING_CONFIRM
            CODE: DDL_PREVIEW
            risk_level: DANGER
            operation: DROP_TABLE
            table: {tableName}
            database: {state.ActiveName}
            token: {token}
            expires_in: 60s
            action: ExecuteDrop("{token}") per procedere, ignora per annullare
            ---
            ATTENZIONE: questa operazione DISTRUGGE la tabella e tutti i suoi dati.

            Schema che verrà eliminato:
            {currentSchema}
            """;
    }

    [McpServerTool, Description(
        "Esegue la DROP TABLE associata al token generato da PreviewDrop. " +
        "Il token e' monouso e scade in 60 secondi.")]
    public async Task<string> ExecuteDrop(
        [Description("Token restituito da PreviewDrop")] string confirmationToken,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowDrop)
            return DdlDisabledMessage("DROP", "AllowDrop");

        var pending = tokenStore.Consume(confirmationToken);
        if (pending is null)
            return "Token non valido o scaduto. Esegui nuovamente PreviewDrop per ottenere un nuovo token.";

        if (pending.Kind != DdlKind.Drop)
            return "Il token fornito non e' associato a una DROP. Usa ExecuteCreate o ExecuteAlter per le rispettive operazioni.";

        logger.LogWarning("ExecuteDrop — esecuzione DROP TABLE {Table} su {Db}", pending.TableName, pending.ConnectionName);
        await using var conn = new SqlConnection(pending.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(pending.Sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        logger.LogInformation("ExecuteDrop — completata: {Table}", pending.TableName);

        return $"""
            STATUS: OK
            CODE: DDL_EXECUTED
            operation: DROP_TABLE
            table: {pending.TableName ?? "(non rilevata)"}
            database: {pending.ConnectionName}
            timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            """;
    }

    // -------------------------------------------------------------------------
    // Helpers privati
    // -------------------------------------------------------------------------

    private string NoConnectionMessage()
    {
        if (state.Available.Count == 0)
            return "[NO_CONNECTIONS_CONFIGURED] Nessuna connection string configurata. Aggiungi ConnectionStrings in appsettings.json o imposta DB_CONNECTION_STRING. Usa Diagnostics() per verificare cosa ha trovato il tool.";

        return $"[NO_ACTIVE_CONNECTION] Nessuna connessione attiva. Disponibili: {string.Join(", ", state.Available.Keys)}. Usa UseConnection per selezionarne una.";
    }

    private static string DdlDisabledMessage(string operation, string flag) =>
        $"Operazione {operation} non abilitata.\n" +
        $"Per abilitarla, aggiungi in appsettings.json:\n\n" +
        $"  \"Ddl\": {{\n" +
        $"    \"{flag}\": true\n" +
        $"  }}\n\n" +
        "Attenzione: abilitare operazioni DDL consente modifiche strutturali al database.\n" +
        "Abilita solo negli ambienti in cui e' necessario.";

    private async Task<string> GetColumnsText(string tableName, CancellationToken ct)
    {
        if (state.ActiveConnectionString is null) return "(connessione non attiva)";

        var parts = tableName.Split('.', 2);
        var schema = parts.Length == 2 ? parts[0] : null;
        var name = parts.Length == 2 ? parts[1] : parts[0];

        try
        {
            await using var conn = new SqlConnection(state.ActiveConnectionString);
            await conn.OpenAsync(ct);

            var sql = schema != null
                ? "SELECT ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name ORDER BY ORDINAL_POSITION"
                : "SELECT ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @name ORDER BY ORDINAL_POSITION";

            var cmd = new SqlCommand(sql, conn);
            if (schema != null) cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@name", name);

            var rows = new List<string> { "pos | column | type | max_len | nullable" };
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var maxLen = reader.IsDBNull(3) ? "-" : reader.GetInt32(3).ToString();
                rows.Add($"{reader.GetInt32(0)} | {reader.GetString(1)} | {reader.GetString(2)} | {maxLen} | {reader.GetString(4)}");
            }

            return rows.Count == 1
                ? "(tabella non trovata o senza colonne)"
                : string.Join("\n", rows);
        }
        catch (Exception ex)
        {
            return $"(errore lettura schema: {ex.Message})";
        }
    }

    private static string WriteAuditFile(string tableName, string sql, string token, string status)
    {
        try
        {
            Directory.CreateDirectory(MigrationsDir);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmm");
            var safeName = System.Text.RegularExpressions.Regex.Replace(tableName, @"[^\w.]", "_");
            var fileName = $"{timestamp}_{safeName}.sql";
            var filePath = Path.Combine(MigrationsDir, fileName);

            var content = $"""
                -- ============================================================
                -- ALTER TABLE audit record
                -- status    : {status}
                -- tabella   : {tableName}
                -- token     : {token}
                -- timestamp : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
                -- ============================================================

                {sql.Trim()}
                """;

            File.WriteAllText(filePath, content);
            return filePath;
        }
        catch
        {
            return "(scrittura audit file fallita)";
        }
    }

}

// ---------------------------------------------------------------------------

internal static class DbSchemaHelpers
{
    /// <summary>Estrae il nome dell'oggetto (tabella/vista) da uno statement SQL contenente TABLE &lt;nome&gt;.</summary>
    internal static string? ExtractObjectName(string sql)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            sql, @"\bTABLE\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var schema = match.Groups[1].Value;
        var name = match.Groups[2].Value;
        return string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";
    }

    /// <summary>Analizza il rischio di uno statement ALTER TABLE e restituisce livello e descrizione.</summary>
    internal static (string Level, string Warnings) AnalyzeAlterRisk(string sql)
    {
        var upper = sql.ToUpperInvariant();
        var warnings = new List<string>();

        if (upper.Contains("DROP COLUMN"))
            warnings.Add("- DROP COLUMN: rimozione colonna, i dati nella colonna saranno PERSI DEFINITIVAMENTE.");
        if (upper.Contains("ALTER COLUMN"))
            warnings.Add("- ALTER COLUMN: modifica tipo o vincoli, possibile perdita/troncamento dati esistenti.");
        if (upper.Contains("DROP DEFAULT") || upper.Contains("DROP CONSTRAINT"))
            warnings.Add("- DROP CONSTRAINT/DEFAULT: rimozione vincolo, possibile impatto su integrità referenziale.");
        if (upper.Contains("ADD") && warnings.Count == 0)
            warnings.Add("- ADD: operazione additiva, nessun dato esistente viene modificato.");

        var level = warnings.Any(w => w.Contains("PERSI") || w.Contains("troncamento") || w.Contains("integrità"))
            ? "DANGER"
            : "WARN";

        return (level, warnings.Count > 0 ? string.Join("\n", warnings) : "- Nessuna operazione distruttiva rilevata.");
    }

    /// <summary>Maschera la password in una connection string per l'output diagnostico.</summary>
    internal static string MaskConnectionString(string cs) =>
        System.Text.RegularExpressions.Regex.Replace(
            cs,
            @"(?i)(password|pwd)\s*=\s*[^;]*",
            "$1=***");
}
