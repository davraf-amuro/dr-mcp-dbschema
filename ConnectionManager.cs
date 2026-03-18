using System.Text.Json;

namespace mcp_db_schema;

public class ConnectionManager
{
    private readonly Dictionary<string, string> _connections = [];
    private string? _activeConnectionName;
    private string? _activeConnectionString;

    public void LoadFromPath(string projectPath)
    {
        var files = new[]
        {
            Path.Combine(projectPath, "appsettings.json"),
            Path.Combine(projectPath, "appsettings.Development.json"),
            Path.Combine(projectPath, "appsettings.local.json"),
        };

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;

            using var stream = File.OpenRead(file);
            var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

            if (!doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)) continue;

            foreach (var prop in cs.EnumerateObject())
            {
                var value = prop.Value.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(value))
                    _connections[prop.Name] = value;
            }
        }
    }

    public IReadOnlyDictionary<string, string> GetRealConnections() =>
        _connections
            .Where(kv => !IsFake(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    private static bool IsFake(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower.Contains("placeholder") ||
               lower.Contains("your_server") ||
               lower.Contains("your-server") ||
               lower.Contains("yourserver") ||
               lower.Contains("<server>") ||
               lower.Contains("{server}") ||
               lower.Contains("fake") ||
               lower.Contains("example.com");
    }

    public void SetActive(string name, string connectionString)
    {
        _activeConnectionName = name;
        _activeConnectionString = connectionString;
    }

    public string? ActiveConnectionName => _activeConnectionName;
    public string? ActiveConnectionString => _activeConnectionString;
}
