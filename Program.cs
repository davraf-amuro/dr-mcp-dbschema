using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.ComponentModel;

var workDir = Directory.GetCurrentDirectory();

// Radice di scansione degli appsettings*.json.
// workDir è la root del progetto che ospita il tool (es. C:\...\FoundryBridge).
// Priorità: DB_SCHEMA_ROOT (override esplicito) > src/ (convenzione standard) > workDir (fallback)
var searchRoot = Environment.GetEnvironmentVariable("DB_SCHEMA_ROOT")
    is { Length: > 0 } envRoot ? envRoot
    : Directory.Exists(Path.Combine(workDir, "src")) ? Path.Combine(workDir, "src")
    : workDir;

// Scansione ricorsiva di tutti gli appsettings*.json sotto searchRoot, esclusi bin/ e obj/
var appsettingsFiles = Directory.GetFiles(searchRoot, "appsettings*.json", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
             && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
    .OrderBy(f => f)
    .ToList();

var available = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var ddlSettings = new DdlSettings();

foreach (var file in appsettingsFiles)
{
    var config = new ConfigurationBuilder()
        .AddJsonFile(file, optional: true)
        .Build();

    foreach (var kv in config.GetSection("ConnectionStrings").GetChildren())
    {
        if (!string.IsNullOrWhiteSpace(kv.Value))
            available[kv.Key] = kv.Value;
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
}

// Override esplicito da argomento o variabile d'ambiente
var explicitCs = args.Length > 0
    ? args[0]
    : Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

if (!string.IsNullOrWhiteSpace(explicitCs))
    available["(override)"] = explicitCs;

var state = new ConnectionState { Available = available };

// Auto-selezione se c'è una sola connection string
if (available.Count == 1)
{
    var (name, cs) = available.First();
    state.ActiveName = name;
    state.ActiveConnectionString = cs;
}

var tokenStore = new DdlTokenStore();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();

builder.Services.AddSingleton(state);
builder.Services.AddSingleton(ddlSettings);
builder.Services.AddSingleton(tokenStore);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

// ---------------------------------------------------------------------------

public class ConnectionState
{
    public Dictionary<string, string> Available { get; set; } = new();
    public string? ActiveName { get; set; }
    public string? ActiveConnectionString { get; set; }
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
        // Rimuove token scaduti
        foreach (var key in _tokens.Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow).Select(kv => kv.Key).ToList())
            _tokens.TryRemove(key, out _);

        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _tokens[token] = pending;
        return token;
    }

    public PendingDdl? Consume(string token)
    {
        if (_tokens.TryRemove(token.Trim().ToUpperInvariant(), out var pending) && pending.ExpiresAt >= DateTime.UtcNow)
            return pending;
        return null;
    }
}

// ---------------------------------------------------------------------------

[McpServerToolType]
public class DbSchemaTools(ConnectionState state, DdlSettings ddlSettings, DdlTokenStore tokenStore)
{
    private static readonly string MigrationsDir =
        Path.Combine(Directory.GetCurrentDirectory(), "schema-migrations");

    [McpServerTool, Description("Elenca le connection string disponibili nel progetto (da appsettings*.json)")]
    public string ListConnections()
    {
        if (state.Available.Count == 0)
            return "Nessuna connection string trovata. Aggiungi una sezione ConnectionStrings in appsettings.json oppure imposta la variabile d'ambiente DB_CONNECTION_STRING.";

        var lines = state.Available.Keys.Select(name =>
            name == state.ActiveName ? $"* {name}  (attiva)" : $"  {name}");

        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Seleziona quale connection string usare per le query successive")]
    public string UseConnection([Description("Nome della connection string (come restituito da ListConnections)")] string name)
    {
        if (!state.Available.TryGetValue(name, out var cs))
            return $"Connection string '{name}' non trovata. Disponibili: {string.Join(", ", state.Available.Keys)}";

        state.ActiveName = name;
        state.ActiveConnectionString = cs;
        return $"Connessione '{name}' attiva.";
    }

    [McpServerTool, Description("Lista tutte le viste e le tabelle presenti nel database")]
    public async Task<string> ListViews(CancellationToken ct = default)
    {
        if (state.ActiveConnectionString is null)
            return NoConnectionMessage();

        await using var conn = new SqlConnection(state.ActiveConnectionString);
        await conn.OpenAsync(ct);

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

        return "non trovo l'oggetto tra tabelle e viste";
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
            ? "non trovo l'oggetto tra tabelle e viste"
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

        var tableName = ExtractObjectName(sql);
        var token = tokenStore.Add(new PendingDdl
        {
            Sql = sql,
            Kind = DdlKind.Create,
            TableName = tableName,
            ConnectionName = state.ActiveName ?? "(override)",
            ConnectionString = state.ActiveConnectionString,
            ExpiresAt = DateTime.UtcNow.AddSeconds(60)
        });

        return $"""
            !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            !!  ATTENZIONE — OPERAZIONE DDL — RICHIESTA CONFERMA     !!
            !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

            risk_level  : DANGER
            operazione  : CREATE TABLE
            tabella     : {tableName ?? "(non rilevata)"}
            database    : {state.ActiveName}
            token       : {token}
            scade tra   : 60 secondi

            SQL proposto:
            ------------------------------------------------------------
            {sql.Trim()}
            ------------------------------------------------------------

            !! Questa operazione creera' una nuova tabella nel database.
            !! Per procedere: ExecuteCreate("{token}")
            !! Per annullare: ignora questo messaggio (il token scadera').
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

        await using var conn = new SqlConnection(pending.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(pending.Sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        return $"""
            [OK] CREATE TABLE eseguita con successo.
            tabella   : {pending.TableName ?? "(non rilevata)"}
            database  : {pending.ConnectionName}
            timestamp : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
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
        var riskDetails = AnalyzeAlterRisk(sql);

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

        // Scrive lo script in schema-migrations/ come audit trail del tentativo
        var auditFile = WriteAuditFile(tableName, sql, token, "PENDING");

        return $"""
            !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            !!  ATTENZIONE — OPERAZIONE DDL — RICHIESTA CONFERMA     !!
            !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

            risk_level  : {riskDetails.Level}
            operazione  : ALTER TABLE
            tabella     : {tableName}
            database    : {state.ActiveName}
            token       : {token}
            scade tra   : 60 secondi
            audit file  : {auditFile}

            Avvertenze rilevate:
            {riskDetails.Warnings}

            Schema corrente della tabella:
            ------------------------------------------------------------
            {currentSchema}
            ------------------------------------------------------------

            SQL proposto:
            ------------------------------------------------------------
            {sql.Trim()}
            ------------------------------------------------------------

            !! Per procedere: ExecuteAlter("{token}")
            !! Per annullare: ignora questo messaggio (il token scadera').
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

        await using var conn = new SqlConnection(pending.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(pending.Sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        // Aggiorna l'audit file da PENDING a EXECUTED
        WriteAuditFile(pending.TableName ?? "unknown", pending.Sql, confirmationToken, "EXECUTED");

        return $"""
            [OK] ALTER TABLE eseguita con successo.
            tabella   : {pending.TableName ?? "(non rilevata)"}
            database  : {pending.ConnectionName}
            timestamp : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            audit     : schema-migrations/ aggiornato
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

        await using var conn = new SqlConnection(state.ActiveConnectionString);
        await conn.OpenAsync(ct);

        var checkCmd = new SqlCommand(
            "SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name",
            conn);
        checkCmd.Parameters.AddWithValue("@schema", schema);
        checkCmd.Parameters.AddWithValue("@name", name);
        var tableType = await checkCmd.ExecuteScalarAsync(ct);

        if (tableType is DBNull or null)
            return $"La tabella '{tableName}' non esiste nel database.";

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

        return $"""
            !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            !!  ATTENZIONE — OPERAZIONE DDL — RICHIESTA CONFERMA     !!
            !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

            risk_level  : DANGER
            operazione  : DROP TABLE
            tabella     : {tableName}
            database    : {state.ActiveName}
            token       : {token}
            scade tra   : 60 secondi

            Schema che verra' ELIMINATO:
            ------------------------------------------------------------
            {currentSchema}
            ------------------------------------------------------------

            !! ATTENZIONE: questa operazione DISTRUGGE la tabella e tutti i suoi dati.
            !! Per procedere: ExecuteDrop("{token}")
            !! Per annullare: ignora questo messaggio (il token scadera').
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

        await using var conn = new SqlConnection(pending.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(pending.Sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        return $"""
            [OK] DROP TABLE eseguita con successo.
            tabella   : {pending.TableName ?? "(non rilevata)"}
            database  : {pending.ConnectionName}
            timestamp : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            """;
    }

    // -------------------------------------------------------------------------
    // Helpers privati
    // -------------------------------------------------------------------------

    private string NoConnectionMessage()
    {
        if (state.Available.Count == 0)
            return "Nessuna connection string configurata. Aggiungi ConnectionStrings in appsettings.json o imposta DB_CONNECTION_STRING.";

        return $"Nessuna connessione attiva. Disponibili: {string.Join(", ", state.Available.Keys)}. Usa UseConnection per selezionarne una.";
    }

    private static string DdlDisabledMessage(string operation, string flag) =>
        $"Operazione {operation} non abilitata.\n" +
        $"Per abilitarla, aggiungi in appsettings.json:\n\n" +
        $"  \"Ddl\": {{\n" +
        $"    \"{flag}\": true\n" +
        $"  }}\n\n" +
        "Attenzione: abilitare operazioni DDL consente modifiche strutturali al database.\n" +
        "Abilita solo negli ambienti in cui e' necessario.";

    private static string? ExtractObjectName(string sql)
    {
        // Cerca il pattern TABLE <nome> nella SQL (euristica semplice)
        var match = System.Text.RegularExpressions.Regex.Match(
            sql, @"\bTABLE\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var schema = match.Groups[1].Value;
        var name = match.Groups[2].Value;
        return string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";
    }

    private static (string Level, string Warnings) AnalyzeAlterRisk(string sql)
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
