using System.ComponentModel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

[McpServerToolType]
public class DbSchemaTools(ConnectionState state, DdlSettings ddlSettings, DdlTokenStore tokenStore, ILogger<DbSchemaTools> logger)
{
    private static readonly string _migrationsDir =
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
        {
            lines.Add("  (nessuno)");
        }
        else
        {
            foreach (var f in state.ScannedFiles)
            {
                lines.Add($"  {f}");
            }
        }

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
        {
            return "[NO_CONNECTIONS_CONFIGURED] Nessuna connection string trovata. Aggiungi una sezione ConnectionStrings in appsettings.json oppure imposta la variabile d'ambiente DB_CONNECTION_STRING.";
        }

        var lines = state.Available.Keys.Select((name, i) =>
        {
            var active = name == state.ActiveName ? " (attiva)" : "";
            var source = state.AvailableSources.TryGetValue(name, out var f)
                ? $"  [{Path.GetFileName(f)}]"
                : "";
            var prefix = name == state.ActiveName ? "*" : " ";
            return $"{prefix} {i + 1}. {name}{active}{source}";
        });

        return string.Join("\n", lines)
            + "\n\nRispondi con il numero oppure usa UseConnection(\"<nome>\") direttamente.";
    }

    [McpServerTool, Description("Seleziona quale connection string usare per le query successive")]
    public string UseConnection([Description("Nome della connection string (come restituito da ListConnections)")] string name)
    {
        if (!state.Available.TryGetValue(name, out var cs))
        {
            return $"Connection string '{name}' non trovata. Disponibili: {string.Join(", ", state.Available.Keys)}";
        }

        state.ActiveName = name;
        state.ActiveConnectionString = cs;
        logger.LogInformation("Connessione attivata: {Name}", name);
        return $"Connessione '{name}' attiva.";
    }

    [McpServerTool, Description("Imposta una connection string personalizzata non presente nel progetto. Valida solo per la sessione corrente, non viene salvata ne' loggata.")]
    public string UseCustomConnection(
        [Description("Connection string completa (es. Server=...;Database=...;User Id=...;Password=...)")] string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "[INVALID_INPUT] Connection string vuota.";
        }

        state.ActiveName = "(custom)";
        state.ActiveConnectionString = connectionString;
        logger.LogInformation("Connessione custom impostata (valore non loggato)");
        var masked = DbSchemaHelpers.MaskConnectionString(connectionString);
        return $"Connessione custom attiva: {masked}\nNon verra' salvata ne' loggata. Usa UseConnection per tornare a una connessione di progetto.";
    }

    [McpServerTool, Description("Mostra la connessione attiva (mascherata) e l'elenco di quelle disponibili")]
    public string GetActiveConnection()
    {
        if (state.ActiveConnectionString is null)
        {
            return NoConnectionMessage();
        }

        var masked = DbSchemaHelpers.MaskConnectionString(state.ActiveConnectionString);
        return $"active: {state.ActiveName} — {masked}\n\n{ListConnections()}";
    }

    [McpServerTool(Name = "list_views"), Description("Lista tutte le viste e le tabelle presenti nel database")]
    public async Task<string> ListViewsAsync(CancellationToken ct = default)
    {
        if (state.ActiveConnectionString is null)
        {
            return NoConnectionMessage();
        }

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

    [McpServerTool(Name = "get_view_definition"), Description("Restituisce la definizione SQL (CREATE VIEW) di una vista, o indica se l'oggetto è una tabella")]
    public async Task<string> GetViewDefinitionAsync(
        [Description("Nome della vista o tabella (senza schema, o nella forma schema.nome)")] string viewName,
        CancellationToken ct = default)
    {
        if (state.ActiveConnectionString is null)
        {
            return NoConnectionMessage();
        }

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
        if (schema != null)
        {
            viewCmd.Parameters.AddWithValue("@schema", schema);
        }

        viewCmd.Parameters.AddWithValue("@name", name);

        var viewResult = await viewCmd.ExecuteScalarAsync(ct);
        if (viewResult is not (DBNull or null))
        {
            return viewResult.ToString()!;
        }

        // Controlla se esiste come tabella
        var tableSql = schema != null
            ? "SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name"
            : "SELECT TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name";

        var tableCmd = new SqlCommand(tableSql, conn);
        if (schema != null)
        {
            tableCmd.Parameters.AddWithValue("@schema", schema);
        }

        tableCmd.Parameters.AddWithValue("@name", name);

        var tableResult = await tableCmd.ExecuteScalarAsync(ct);
        if (tableResult is not (DBNull or null))
        {
            return $"'{viewName}' è una tabella (TABLE), non ha VIEW_DEFINITION. Usa GetViewColumns per le colonne.";
        }

        return $"[OBJECT_NOT_FOUND] L'oggetto '{viewName}' non esiste tra tabelle e viste nel database attivo.";
    }

    [McpServerTool(Name = "get_view_columns"), Description("Restituisce le colonne di una tabella o vista (nome, tipo, nullable, posizione)")]
    public async Task<string> GetViewColumnsAsync(
        [Description("Nome della tabella o vista (senza schema, o nella forma schema.nome)")] string viewName,
        CancellationToken ct = default)
    {
        if (state.ActiveConnectionString is null)
        {
            return NoConnectionMessage();
        }

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
        if (schema != null)
        {
            cmd.Parameters.AddWithValue("@schema", schema);
        }

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
    // Query — SELECT (read-only) e generazione comandi non eseguiti
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "run_select"), Description(
        "Esegue una query SELECT (sola lettura) su tabelle o viste e ne restituisce le righe. " +
        "Accetta solo SELECT o CTE (WITH ... SELECT): qualsiasi comando di scrittura, DDL o esecuzione viene rifiutato. " +
        "Richiede Ddl.AllowSelect: true in appsettings.json.")]
    public async Task<string> RunSelectAsync(
        [Description("Query SELECT da eseguire (un solo statement)")] string sql,
        [Description("Numero massimo di righe da restituire (default 1000)")] int maxRows = 1000,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowSelect)
        {
            return DdlDisabledMessage("SELECT", "AllowSelect");
        }

        if (state.ActiveConnectionString is null)
        {
            return NoConnectionMessage();
        }

        if (!DbSchemaHelpers.IsReadOnlySelect(sql, out var reason))
        {
            logger.LogWarning("RunSelect — statement rifiutato: {Reason}", reason);
            return $"[INVALID_INPUT] {reason}";
        }

        if (maxRows <= 0)
        {
            maxRows = 1000;
        }

        logger.LogInformation("RunSelect — esecuzione query su {Db}, maxRows {MaxRows}", state.ActiveName, maxRows);
        await using var conn = new SqlConnection(state.ActiveConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<string> { string.Join(" | ", columns) };

        var count = 0;
        var truncated = false;
        while (await reader.ReadAsync(ct))
        {
            if (count >= maxRows)
            {
                truncated = true;
                break;
            }

            var values = Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString());
            rows.Add(string.Join(" | ", values));
            count++;
        }

        if (count == 0)
        {
            return $"Query eseguita: 0 righe.\n\n{string.Join(" | ", columns)}";
        }

        var footer = truncated
            ? $"\n\n({count} righe mostrate, output troncato a maxRows={maxRows})"
            : $"\n\n({count} righe)";

        return string.Join("\n", rows) + footer;
    }

    [McpServerTool, Description(
        "Genera un comando SQL diverso da SELECT (INSERT/UPDATE/DELETE/DDL/EXEC...) SENZA eseguirlo. " +
        "Il comando viene restituito commentato (prefisso '-- ' per riga) per impedirne l'esecuzione immediata. " +
        "Non apre alcuna connessione al database. Per le query di lettura usa RunSelect.")]
    public string GenerateCommand(
        [Description("Comando SQL non-SELECT da restituire commentato")] string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "[INVALID_INPUT] Comando vuoto.";
        }

        if (DbSchemaHelpers.IsReadOnlySelect(sql, out _))
        {
            return "[INVALID_INPUT] Questo è uno statement SELECT di sola lettura: usa RunSelect per eseguirlo.";
        }

        logger.LogInformation("GenerateCommand — comando generato (non eseguito)");

        return $"""
            STATUS: NOT_EXECUTED
            CODE: COMMAND_GENERATED
            note: comando NON eseguito. Restituito commentato per impedire l'esecuzione immediata.
            note: rimuovi i prefissi '-- ' per eseguirlo manualmente in un contesto controllato.
            ---
            {DbSchemaHelpers.CommentOutSql(sql.Trim())}
            """;
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
        {
            return DdlDisabledMessage("CREATE", "AllowCreate");
        }

        if (state.ActiveConnectionString is null)
        {
            return NoConnectionMessage();
        }

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

    [McpServerTool(Name = "execute_create"), Description(
        "Esegue la CREATE TABLE associata al token generato da PreviewCreate. " +
        "Il token e' monouso e scade in 60 secondi.")]
    public async Task<string> ExecuteCreateAsync(
        [Description("Token restituito da PreviewCreate")] string confirmationToken,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowCreate)
        {
            return DdlDisabledMessage("CREATE", "AllowCreate");
        }

        var pending = tokenStore.Consume(confirmationToken);
        if (pending is null)
        {
            return "Token non valido o scaduto. Esegui nuovamente PreviewCreate per ottenere un nuovo token.";
        }

        if (pending.Kind != DdlKind.Create)
        {
            return "Il token fornito non e' associato a una CREATE. Usa ExecuteAlter per le operazioni ALTER.";
        }

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

    [McpServerTool(Name = "preview_alter"), Description(
        "Analizza uno statement ALTER TABLE, mostra lo schema corrente della tabella e genera un token di conferma. " +
        "NON esegue nulla sul database. " +
        "Scrive lo script in schema-migrations/ per l'audit trail. " +
        "Richiede Ddl.AllowAlter: true in appsettings.json.")]
    public async Task<string> PreviewAlterAsync(
        [Description("Nome della tabella da modificare (senza schema, o nella forma schema.nome)")] string tableName,
        [Description("Statement SQL ALTER TABLE completo")] string sql,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowAlter)
        {
            return DdlDisabledMessage("ALTER", "AllowAlter");
        }

        if (state.ActiveConnectionString is null)
        {
            return NoConnectionMessage();
        }

        // Recupera schema corrente della tabella
        var currentSchema = await GetColumnsTextAsync(tableName, ct);

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

    [McpServerTool(Name = "execute_alter"), Description(
        "Esegue l'ALTER TABLE associato al token generato da PreviewAlter. " +
        "Il token e' monouso e scade in 60 secondi. " +
        "Aggiorna il file di audit in schema-migrations/.")]
    public async Task<string> ExecuteAlterAsync(
        [Description("Token restituito da PreviewAlter")] string confirmationToken,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowAlter)
        {
            return DdlDisabledMessage("ALTER", "AllowAlter");
        }

        var pending = tokenStore.Consume(confirmationToken);
        if (pending is null)
        {
            return "Token non valido o scaduto. Esegui nuovamente PreviewAlter per ottenere un nuovo token.";
        }

        if (pending.Kind != DdlKind.Alter)
        {
            return "Il token fornito non e' associato a un ALTER. Usa ExecuteCreate per le operazioni CREATE.";
        }

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

    [McpServerTool(Name = "preview_drop"), Description(
        "Mostra lo schema corrente della tabella e genera un token di conferma per eliminarla. " +
        "NON esegue nulla sul database. " +
        "Richiede Ddl.AllowDrop: true in appsettings.json.")]
    public async Task<string> PreviewDropAsync(
        [Description("Nome della tabella da eliminare (senza schema, o nella forma schema.nome)")] string tableName,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowDrop)
        {
            return DdlDisabledMessage("DROP", "AllowDrop");
        }

        if (state.ActiveConnectionString is null)
        {
            return NoConnectionMessage();
        }

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
        {
            return $"[OBJECT_NOT_FOUND] La tabella '{tableName}' non esiste nel database.";
        }

        var currentSchema = await GetColumnsTextAsync(tableName, ct);
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

    [McpServerTool(Name = "execute_drop"), Description(
        "Esegue la DROP TABLE associata al token generato da PreviewDrop. " +
        "Il token e' monouso e scade in 60 secondi.")]
    public async Task<string> ExecuteDropAsync(
        [Description("Token restituito da PreviewDrop")] string confirmationToken,
        CancellationToken ct = default)
    {
        if (!ddlSettings.AllowDrop)
        {
            return DdlDisabledMessage("DROP", "AllowDrop");
        }

        var pending = tokenStore.Consume(confirmationToken);
        if (pending is null)
        {
            return "Token non valido o scaduto. Esegui nuovamente PreviewDrop per ottenere un nuovo token.";
        }

        if (pending.Kind != DdlKind.Drop)
        {
            return "Il token fornito non e' associato a una DROP. Usa ExecuteCreate o ExecuteAlter per le rispettive operazioni.";
        }

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
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[NO_ACTIVE_CONNECTION] Nessuna connessione attiva.");
        sb.AppendLine();

        if (state.Available.Count == 0)
        {
            sb.AppendLine("Nessuna ConnectionStrings trovata negli appsettings. Puoi:");
            sb.AppendLine("  UseCustomConnection(\"Server=...;Database=...;\")  — connection string custom (solo sessione)");
            sb.AppendLine("  Diagnostics()  — verifica cosa ha trovato il tool");
        }
        else
        {
            sb.AppendLine("Connessioni disponibili nel progetto:");
            foreach (var kvp in state.Available)
            {
                var src = state.AvailableSources.TryGetValue(kvp.Key, out var f)
                    ? $" [{Path.GetFileName(f)}]" : "";
                sb.AppendLine($"  • {kvp.Key}{src}");
            }
            sb.AppendLine();
            sb.AppendLine("Seleziona:");
            sb.AppendLine($"  UseConnection(\"{state.Available.Keys.First()}\")  — usa connessione di progetto");
            sb.AppendLine("  UseCustomConnection(\"...\")  — inserisci connection string custom (solo sessione)");
        }

        return sb.ToString().TrimEnd();
    }

    private static string DdlDisabledMessage(string operation, string flag) =>
        $"Operazione {operation} non abilitata.\n" +
        $"Per abilitarla, aggiungi in appsettings.json:\n\n" +
        $"  \"Ddl\": {{\n" +
        $"    \"{flag}\": true\n" +
        $"  }}\n\n" +
        "Attenzione: abilitare operazioni DDL consente modifiche strutturali al database.\n" +
        "Abilita solo negli ambienti in cui e' necessario.";

    private async Task<string> GetColumnsTextAsync(string tableName, CancellationToken ct)
    {
        if (state.ActiveConnectionString is null)
        {
            return "(connessione non attiva)";
        }

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
            if (schema != null)
            {
                cmd.Parameters.AddWithValue("@schema", schema);
            }

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
            Directory.CreateDirectory(_migrationsDir);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmm");
            var safeName = System.Text.RegularExpressions.Regex.Replace(tableName, @"[^\w.]", "_");
            var fileName = $"{timestamp}_{safeName}.sql";
            var filePath = Path.Combine(_migrationsDir, fileName);

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
