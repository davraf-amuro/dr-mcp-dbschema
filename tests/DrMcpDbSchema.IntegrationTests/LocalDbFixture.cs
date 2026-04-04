using System.Text.Json;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Client;
using Xunit;

namespace DrMcpDbSchema.IntegrationTests;

/// <summary>
/// Fixture per test di integrazione sul database locale DrNutrizioNino.
/// Legge la connection string da DB_LOCAL_CONNECTION_STRING (env var) o usa il default.
/// Salta i test automaticamente se il database non è raggiungibile.
/// Pre-pulisce e post-pulisce la tabella dbo.IA_TEST come safety net.
/// </summary>
public sealed class LocalDbFixture : IAsyncLifetime
{
    public const string DefaultConnectionString =
        "Data Source=localhost;Initial Catalog=DrNutrizioNino;Integrated Security=True;Encrypt=True;Trust Server Certificate=True";

    private string? _tempSettingsDir;

    public McpClient? Client { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = DefaultConnectionString;

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("DB_LOCAL_CONNECTION_STRING")
            ?? DefaultConnectionString;

        // Verifica connettività e pre-pulizia tabella di test
        try
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            await using var cleanCmd = new SqlCommand(
                "IF OBJECT_ID('dbo.IA_TEST', 'U') IS NOT NULL DROP TABLE dbo.IA_TEST", conn);
            await cleanCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"Database DrNutrizioNino non raggiungibile: {ex.Message}";
            return;
        }

        // appsettings temporaneo con DDL completamente abilitato
        _tempSettingsDir = Path.Combine(Path.GetTempPath(), $"mcp-localdb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempSettingsDir);

        var settings = new { Ddl = new { AllowCreate = true, AllowAlter = true, AllowDrop = true } };
        await File.WriteAllTextAsync(
            Path.Combine(_tempSettingsDir, "appsettings.json"),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        var csproj = ResolveMcpCsprojPath();
        var configuration = typeof(LocalDbFixture).Assembly.Location
            .Contains("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--project", csproj, "--configuration", configuration],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["DB_CONNECTION_STRING"] = ConnectionString,
                ["DB_SCHEMA_ROOT"] = _tempSettingsDir
            }
        });

        Client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
            await Client.DisposeAsync();

        // Safety net: elimina IA_TEST se ancora presente (es. test fallito a metà)
        if (SkipReason is null)
        {
            try
            {
                await using var conn = new SqlConnection(ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "IF OBJECT_ID('dbo.IA_TEST', 'U') IS NOT NULL DROP TABLE dbo.IA_TEST", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* ignora errori di cleanup */ }
        }

        if (_tempSettingsDir is not null && Directory.Exists(_tempSettingsDir))
            Directory.Delete(_tempSettingsDir, recursive: true);
    }

    private static string ResolveMcpCsprojPath()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(LocalDbFixture).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "../../../../"));
        return Path.Combine(repoRoot, "dr-mcp-dbschema.csproj");
    }
}

[CollectionDefinition("LocalDB")]
public class LocalDbCollection : ICollectionFixture<LocalDbFixture> { }
