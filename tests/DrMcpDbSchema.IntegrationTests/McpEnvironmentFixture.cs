using System.Diagnostics;
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
/// - pubblica il server MCP con dotnet publish
/// - avvia l'exe pubblicato e ne espone il client
/// </summary>
public sealed class McpEnvironmentFixture : IAsyncLifetime
{
    private MsSqlContainer _container = null!;
    private string _tempSettingsDir = null!;
    private string _publishDir = null!;

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

        // 3. appsettings temporaneo con DDL completamente abilitato
        _tempSettingsDir = Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempSettingsDir);

        var settings = new { Ddl = new { AllowCreate = true, AllowAlter = true, AllowDrop = true } };
        await File.WriteAllTextAsync(
            Path.Combine(_tempSettingsDir, "appsettings.json"),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        // 4. Pubblica il binario in una cartella temp
        var csproj = ResolveMcpCsprojPath();
        var configuration = typeof(McpEnvironmentFixture).Assembly.Location
            .Contains("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

        _publishDir = await PublishServerAsync(csproj, configuration);

        var exeName = OperatingSystem.IsWindows() ? "dr-mcp-dbschema.exe" : "dr-mcp-dbschema";
        var exePath = Path.Combine(_publishDir, exeName);

        // 5. Client MCP collegato all'exe pubblicato
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = exePath,
            Arguments = [],
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
            Directory.Delete(_tempSettingsDir, recursive: true);

        if (Directory.Exists(_publishDir))
            Directory.Delete(_publishDir, recursive: true);
    }

    // ---------------------------------------------------------------------------

    private static async Task<string> PublishServerAsync(string csproj, string configuration)
    {
        var publishDir = Path.Combine(Path.GetTempPath(), $"mcp-pub-{Guid.NewGuid():N}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{csproj}\" -c {configuration} -o \"{publishDir}\" --nologo -v q",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stderr = await stderrTask;
        await stdoutTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet publish fallito (exit {process.ExitCode}):\n{stderr}");

        return publishDir;
    }

    private static string ResolveMcpCsprojPath()
    {
        var dir = Path.GetDirectoryName(typeof(McpEnvironmentFixture).Assembly.Location);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "dr-mcp-dbschema.csproj");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("dr-mcp-dbschema.csproj non trovato risalendo dalla directory del test assembly.");
    }

    private static async Task SeedDatabaseAsync(string masterCs)
    {
        await using var conn = new SqlConnection(masterCs);
        await conn.OpenAsync();

        await using (var cmd = new SqlCommand(
            "IF DB_ID('TEST') IS NULL CREATE DATABASE [TEST]", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        conn.ChangeDatabase("TEST");

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
