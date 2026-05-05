using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace DrMcpDbSchema.IntegrationTests;

/// <summary>
/// Smoke test del flusso di installazione reale.
/// Esegue setup.ps1 -Client all in una directory temp, verifica binario, --version e config MCP.
/// Richiede internet e una release pubblicata su GitHub.
/// Eseguire manualmente post-release: dotnet test --filter "Category=Setup"
/// </summary>
[Trait("Category", "Setup")]
public class SetupSmokeTests(ITestOutputHelper output)
{
    private const int SetupTimeoutMs = 120_000;

    [Fact]
    public async Task SetupPs1_DownloadsLatestRelease_AndConfiguresAllClients()
    {
        var setupScript = ResolveSetupPs1Path();
        var tempDir = Path.Combine(Path.GetTempPath(), $"mcp-setup-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // ── Step 1: esegui setup.ps1 -Client all ────────────────────────────
            output.WriteLine($"[Setup] tempDir  : {tempDir}");
            output.WriteLine($"[Setup] script   : {setupScript}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    Arguments = $"-NonInteractive -File \"{setupScript}\" -Client all",
                    WorkingDirectory = tempDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(SetupTimeoutMs));

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"setup.ps1 non completato in {SetupTimeoutMs / 1000}s.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            output.WriteLine("[setup.ps1 output]");
            foreach (var line in stdout.Split('\n'))
                output.WriteLine(line);

            Assert.True(process.ExitCode == 0,
                $"setup.ps1 fallito (exit {process.ExitCode}).\nstdout:\n{stdout}\nstderr:\n{stderr}");

            // ── Step 2: binario estratto ─────────────────────────────────────────
            var exePath = Path.Combine(tempDir, "tools", "dr-mcp-dbschema", "dr-mcp-dbschema.exe");
            Assert.True(File.Exists(exePath), $"Binario non trovato: {exePath}");
            output.WriteLine($"[OK] binario: {exePath}");

            // ── Step 3: --version risponde ───────────────────────────────────────
            var version = await RunVersionCheckAsync(exePath);
            output.WriteLine($"[OK] --version: {version}");
            Assert.Matches(@"^\d+\.\d+\.\d+$", version);

            // ── Step 4: .mcp.json (Claude Code) ─────────────────────────────────
            var mcpJson = Path.Combine(tempDir, ".mcp.json");
            Assert.True(File.Exists(mcpJson), ".mcp.json non creato");
            VerifyConfig(mcpJson, "mcpServers", "db-schema", "command",
                "tools/dr-mcp-dbschema/dr-mcp-dbschema.exe");
            output.WriteLine("[OK] .mcp.json");

            // ── Step 5: .vscode/mcp.json (VS Code + GitHub Copilot) ─────────────
            var vscodeMcp = Path.Combine(tempDir, ".vscode", "mcp.json");
            Assert.True(File.Exists(vscodeMcp), ".vscode/mcp.json non creato");
            VerifyConfig(vscodeMcp, "servers", "db-schema", "command",
                "tools/dr-mcp-dbschema/dr-mcp-dbschema.exe", contains: true);
            output.WriteLine("[OK] .vscode/mcp.json");

            // ── Step 6: .cursor/mcp.json ─────────────────────────────────────────
            var cursorMcp = Path.Combine(tempDir, ".cursor", "mcp.json");
            Assert.True(File.Exists(cursorMcp), ".cursor/mcp.json non creato");
            VerifyConfig(cursorMcp, "mcpServers", "db-schema", "command",
                "tools/dr-mcp-dbschema/dr-mcp-dbschema.exe");
            output.WriteLine("[OK] .cursor/mcp.json");

            // ── Step 7: MCP server — list_connections + list_views su DB locale ───────
            output.WriteLine("\n[Step 7] MCP server avviato dall'exe installato — list_connections + list_views");

            var localCs = Environment.GetEnvironmentVariable("DB_LOCAL_CONNECTION_STRING")
                ?? LocalDbFixture.DefaultConnectionString;

            // Verifica connettività — skip se DB non raggiungibile
            bool dbReachable;
            try
            {
                await using var probe = new SqlConnection(localCs);
                await probe.OpenAsync();
                dbReachable = true;
            }
            catch
            {
                dbReachable = false;
            }

            if (!dbReachable)
            {
                output.WriteLine("  [SKIP] DB locale non raggiungibile — step saltato");
            }
            else
            {
                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Command = exePath,
                    Arguments = [],
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        ["DB_CONNECTION_STRING"] = localCs
                    }
                });

                await using var client = await McpClient.CreateAsync(transport);

                var connText = await CallToolText(client, "list_connections");
                output.WriteLine($"  list_connections:\n  {connText}");
                Assert.Contains("(override)", connText);
                Assert.Contains("attiva", connText);
                output.WriteLine("  [OK] list_connections");

                var viewsText = await CallToolText(client, "list_views");
                output.WriteLine($"  list_views:\n{viewsText}");
                Assert.False(string.IsNullOrWhiteSpace(viewsText), "list_views ha restituito risposta vuota");
                Assert.DoesNotContain("[NO_ACTIVE_CONNECTION]", viewsText);
                output.WriteLine("  [OK] list_views — risposta ricevuta");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                // Il processo MCP server potrebbe tenere un lock sull'exe per un breve momento
                // dopo la dispose del client. Retry con backoff.
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                        break;
                    }
                    catch (UnauthorizedAccessException) when (attempt < 4)
                    {
                        await Task.Delay(300);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------------------

    private static string ResolveSetupPs1Path()
    {
        var dir = Path.GetDirectoryName(typeof(SetupSmokeTests).Assembly.Location);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "setup.ps1");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("setup.ps1 non trovato risalendo dalla directory del test assembly.");
    }

    private static async Task<string> RunVersionCheckAsync(string exePath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout.Trim();
    }

    private static async Task<string> CallToolText(McpClient client, string tool,
        IReadOnlyDictionary<string, object?>? args = null)
    {
        var result = await client.CallToolAsync(tool, args);
        return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
    }

    private static void VerifyConfig(string path, string rootKey, string serverKey,
        string property, string expectedValue, bool contains = false)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty(rootKey, out var servers),
            $"{Path.GetFileName(path)}: chiave '{rootKey}' mancante");
        Assert.True(servers.TryGetProperty(serverKey, out var server),
            $"{Path.GetFileName(path)}: server '{serverKey}' mancante");
        Assert.True(server.TryGetProperty(property, out var prop),
            $"{Path.GetFileName(path)}: proprietà '{property}' mancante");

        var actual = prop.GetString() ?? string.Empty;
        if (contains)
            Assert.Contains(expectedValue, actual);
        else
            Assert.Equal(expectedValue, actual);
    }
}
