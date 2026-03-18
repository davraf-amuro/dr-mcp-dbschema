using System.ComponentModel;
using System.Text;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

namespace mcp_db_schema;

[McpServerToolType]
public class SchemaTools(ConnectionManager connectionManager)
{
    [McpServerTool]
    [Description("Elenca i nomi delle connection string reali trovate negli appsettings del progetto. Usare prima di get_schema se non si sa quale connection string è disponibile.")]
    public string list_connections()
    {
        var real = connectionManager.GetRealConnections();
        if (real.Count == 0)
            return "Nessuna connection string reale trovata negli appsettings.";

        var sb = new StringBuilder();
        sb.AppendLine("Connection string disponibili:");
        foreach (var (name, _) in real)
            sb.AppendLine($"  - {name}");

        if (connectionManager.ActiveConnectionName is not null)
            sb.AppendLine($"\nAttiva: {connectionManager.ActiveConnectionName}");

        return sb.ToString();
    }

    [McpServerTool]
    [Description("Cambia la connection string attiva per la sessione corrente. Usare list_connections per vedere i nomi disponibili. L'utente può anche dire 'cambia connection'.")]
    public string change_connection(
        [Description("Nome della connection string da attivare")] string connectionName)
    {
        var real = connectionManager.GetRealConnections();

        if (!real.TryGetValue(connectionName, out var cs))
            return $"Connection string '{connectionName}' non trovata. Disponibili: {string.Join(", ", real.Keys)}";

        connectionManager.SetActive(connectionName, cs);
        return $"Connection attiva cambiata in '{connectionName}'.";
    }

    [McpServerTool]
    [Description("Legge lo schema del database: tabelle, viste e le relative colonne con tipo, nullabilità e vincoli. Se ci sono più connection string disponibili e nessuna è attiva, chiede all'utente quale usare.")]
    public async Task<string> get_schema(
        [Description("Filtra per nome esatto di una tabella o vista (opzionale)")] string? tableName = null,
        CancellationToken cancellationToken = default)
    {
        if (connectionManager.ActiveConnectionString is null)
        {
            var real = connectionManager.GetRealConnections();

            if (real.Count == 0)
                return "Nessuna connection string reale trovata negli appsettings. Verificare MCP_PROJECT_PATH.";

            if (real.Count == 1)
            {
                var (name, cs) = real.First();
                connectionManager.SetActive(name, cs);
            }
            else
            {
                return $"Trovate {real.Count} connection string. Usare change_connection con uno di questi nomi: {string.Join(", ", real.Keys)}";
            }
        }

        try
        {
            return await QuerySchemaAsync(connectionManager.ActiveConnectionString!, tableName, cancellationToken);
        }
        catch (SqlException ex)
        {
            return $"Errore SQL (connection: {connectionManager.ActiveConnectionName}): {ex.Message}";
        }
    }

    private static async Task<string> QuerySchemaAsync(string connectionString, string? tableFilter, CancellationToken ct)
    {
        const string sql = """
            SELECT
                t.TABLE_SCHEMA,
                t.TABLE_NAME,
                t.TABLE_TYPE,
                c.COLUMN_NAME,
                c.ORDINAL_POSITION,
                c.IS_NULLABLE,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.TABLES t
            JOIN INFORMATION_SCHEMA.COLUMNS c
                ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
            WHERE (@TableName IS NULL OR t.TABLE_NAME = @TableName)
            ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION
            """;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@TableName", (object?)tableFilter ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var sb = new StringBuilder();
        string? currentTable = null;

        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var tableType = reader.GetString(2) == "VIEW" ? "VIEW" : "TABLE";
            var column = reader.GetString(3);
            var nullable = reader.GetString(5) == "YES";
            var dataType = reader.GetString(6);
            var maxLen = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
            var precision = reader.IsDBNull(8) ? (int?)null : (int)reader.GetByte(8);
            var scale = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9);

            var fullName = $"{schema}.{table}";
            if (fullName != currentTable)
            {
                if (currentTable is not null) sb.AppendLine();
                sb.AppendLine($"[{tableType}] {fullName}");
                currentTable = fullName;
            }

            var typeDesc = dataType;
            if (maxLen.HasValue)
                typeDesc += maxLen == -1 ? "(MAX)" : $"({maxLen})";
            else if (precision.HasValue)
                typeDesc += scale.HasValue ? $"({precision},{scale})" : $"({precision})";

            sb.AppendLine($"  {column} {typeDesc}{(nullable ? "" : " NOT NULL")}");
        }

        return sb.Length == 0 ? "Nessuna tabella o vista trovata." : sb.ToString();
    }
}
