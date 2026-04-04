using System.Text.Json;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Client;
using Testcontainers.MsSql;
using Xunit;

namespace DrMcpDbSchema.IntegrationTests;

/// <summary>
/// Fixture condivisa per tutti i test MCP:
/// - avvia un container SQL Server via Testcontainers
/// - crea il database TEST con schema seed
/// - crea un appsettings temporaneo con AllowCreate/AllowAlter: true
/// - avvia il server MCP come subprocess e ne espone il client
/// </summary>
public sealed class McpEnvironmentFixture : IAsyncLifetime
{
    private MsSqlContainer _container = null!;
    private string _tempSettingsDir = null!;

    public McpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // 1. Container SQL Server
        _container = new MsSqlBuilder()
            .WithPassword("Test_P@ssw0rd1!")
            .Build();

        await _container.StartAsync();

        // 2. Database TEST + schema seed
        var masterCs = _container.GetConnectionString();
        await SeedDatabaseAsync(masterCs);

        var testCs = new SqlConnectionStringBuilder(masterCs)
        {
            InitialCatalog = "TEST",
            TrustServerCertificate = true
        }.ConnectionString;

        // 3. appsettings temporaneo con DDL abilitato
        _tempSettingsDir = Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempSettingsDir);

        var settings = new { Ddl = new { AllowCreate = true, AllowAlter = true } };
        await File.WriteAllTextAsync(
            Path.Combine(_tempSettingsDir, "appsettings.json"),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        // 4. Client MCP collegato al processo del server
        var csproj = ResolveMcpCsprojPath();
        var configuration = typeof(McpEnvironmentFixture).Assembly.Location
            .Contains("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--project", csproj, "--configuration", configuration],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["DB_CONNECTION_STRING"] = testCs,
                ["DB_SCHEMA_ROOT"] = _tempSettingsDir
            }
        });

        Client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        await Client.DisposeAsync();
        await _container.DisposeAsync();

        if (Directory.Exists(_tempSettingsDir))
        {
            Directory.Delete(_tempSettingsDir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------

    private static string ResolveMcpCsprojPath()
    {
        // Il test assembly si trova in:
        //   <repoRoot>/tests/DrMcpDbSchema.IntegrationTests/bin/{config}/net10.0/
        // Risaliamo 5 livelli per arrivare alla root del repo.
        var assemblyDir = Path.GetDirectoryName(typeof(McpEnvironmentFixture).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "../../../../../"));
        return Path.Combine(repoRoot, "dr-mcp-dbschema.csproj");
    }

    private static async Task SeedDatabaseAsync(string masterCs)
    {
        await using var conn = new SqlConnection(masterCs);
        await conn.OpenAsync();

        // Crea il database TEST se non esiste
        await using (var cmd = new SqlCommand(
            "IF DB_ID('TEST') IS NULL CREATE DATABASE [TEST]", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        conn.ChangeDatabase("TEST");

        // Tabella Customers
        await using (var cmd = new SqlCommand("""
            IF OBJECT_ID('dbo.Customers', 'U') IS NULL
                CREATE TABLE dbo.Customers (
                    Id        INT           PRIMARY KEY IDENTITY,
                    Name      NVARCHAR(100) NOT NULL,
                    Email     NVARCHAR(200) NULL,
                    CreatedAt DATETIME2     NOT NULL DEFAULT GETUTCDATE()
                )
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Vista ActiveCustomers (CREATE VIEW deve essere statement standalone)
        await using (var cmd = new SqlCommand("""
            IF OBJECT_ID('dbo.ActiveCustomers', 'V') IS NULL
                EXEC('CREATE VIEW dbo.ActiveCustomers AS
                      SELECT Id, Name, Email
                      FROM   dbo.Customers
                      WHERE  Email IS NOT NULL')
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition("MCP")]
public class McpCollection : ICollectionFixture<McpEnvironmentFixture> { }
