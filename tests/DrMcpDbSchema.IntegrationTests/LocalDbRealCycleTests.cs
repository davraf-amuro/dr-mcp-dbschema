using ModelContextProtocol.Protocol;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace DrMcpDbSchema.IntegrationTests;

/// <summary>
/// Ciclo completo DDL su database locale DrNutrizioNino tramite server MCP nativo.
/// Simula un caso reale: crea IA_TEST, modifica schema, recupera metadati, elimina.
/// Salta automaticamente se il database non è raggiungibile.
/// Escludi dalla CI con: --filter "Category!=LocalDB"
/// Il log di ogni run viene scritto in test-logs/ (escluso da git).
/// </summary>
[Collection("LocalDB")]
[Trait("Category", "LocalDB")]
public class LocalDbRealCycleTests
{
    private readonly LocalDbFixture _fx;
    private readonly ITestOutputHelper _output;
    private readonly string _logFile;

    public LocalDbRealCycleTests(LocalDbFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _output = output;

        var assemblyDir = Path.GetDirectoryName(typeof(LocalDbRealCycleTests).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "../../../../"));
        var logDir = Path.Combine(repoRoot, "test-logs");
        Directory.CreateDirectory(logDir);
        _logFile = Path.Combine(logDir, $"localdb-{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.log");
    }

    [Fact]
    public async Task FullCycle_IA_TEST_OnRealDb()
    {
        Log($"=== LocalDB Full Cycle Test — {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ===");
        Log($"    database : {_fx.ConnectionString}");
        Log(string.Empty);

        if (_fx.SkipReason is not null)
        {
            _output.WriteLine($"[SKIPPED] {_fx.SkipReason}");
            Log($"[SKIPPED] {_fx.SkipReason}");
            return;
        }

        // ── Step 01: list_views — IA_TEST deve essere assente ────────────────
        Step(1, "list_views — verifica assenza IA_TEST (pre-condizione)");
        var viewsBefore = await CallAsync("list_views");
        Assert.DoesNotContain("IA_TEST", viewsBefore);

        // ── Step 02: list_connections — verifica (override) auto-selezionato ─
        Step(2, "list_connections — verifica connessione attiva");
        var connections = await CallAsync("list_connections");
        Assert.Contains("(override)", connections);
        Assert.Contains("attiva", connections);

        // ── Step 03: preview_create — CREATE TABLE dbo.IA_TEST ──────────────
        Step(3, "preview_create — dbo.IA_TEST con 4 colonne");
        const string createSql = """
            CREATE TABLE dbo.IA_TEST (
                Id        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
                Nome      NVARCHAR(200)    NOT NULL,
                Valore    DECIMAL(10,2)    NULL,
                CreatedAt DATETIME2        NOT NULL DEFAULT GETUTCDATE()
            )
            """;
        var previewCreate = await CallAsync("preview_create",
            new Dictionary<string, object?> { ["sql"] = createSql });
        Assert.Contains("token", previewCreate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IA_TEST", previewCreate);

        var createToken = ExtractToken(previewCreate);
        Assert.NotNull(createToken);

        // ── Step 04: execute_create ───────────────────────────────────────────
        Step(4, "execute_create");
        var execCreate = await CallAsync("execute_create",
            new Dictionary<string, object?> { ["confirmationToken"] = createToken! });
        Assert.Contains("[OK]", execCreate);

        // ── Step 05: list_views — IA_TEST deve comparire ─────────────────────
        Step(5, "list_views — verifica presenza IA_TEST dopo CREATE");
        var viewsAfterCreate = await CallAsync("list_views");
        Assert.Contains("IA_TEST", viewsAfterCreate);

        // ── Step 06: get_view_columns — schema iniziale ───────────────────────
        Step(6, "get_view_columns — verifica 4 colonne iniziali");
        var colsInitial = await CallAsync("get_view_columns",
            new Dictionary<string, object?> { ["viewName"] = "IA_TEST" });
        Assert.Contains("Id", colsInitial);
        Assert.Contains("Nome", colsInitial);
        Assert.Contains("Valore", colsInitial);
        Assert.Contains("CreatedAt", colsInitial);

        // ── Step 07: preview_alter — ADD COLUMN Aggiunta ─────────────────────
        Step(7, "preview_alter — ADD COLUMN Aggiunta NVARCHAR(500)");
        const string alterAddSql = "ALTER TABLE dbo.IA_TEST ADD Aggiunta NVARCHAR(500) NULL";
        var previewAdd = await CallAsync("preview_alter",
            new Dictionary<string, object?>
            {
                ["tableName"] = "IA_TEST",
                ["sql"] = alterAddSql
            });
        Assert.Contains("token", previewAdd, StringComparison.OrdinalIgnoreCase);

        var addToken = ExtractToken(previewAdd);
        Assert.NotNull(addToken);

        // ── Step 08: execute_alter — ADD Aggiunta ────────────────────────────
        Step(8, "execute_alter — ADD Aggiunta");
        var execAdd = await CallAsync("execute_alter",
            new Dictionary<string, object?> { ["confirmationToken"] = addToken! });
        Assert.Contains("[OK]", execAdd);

        // ── Step 09: get_view_columns — verifica colonna Aggiunta ────────────
        Step(9, "get_view_columns — verifica presenza colonna Aggiunta");
        var colsAfterAdd = await CallAsync("get_view_columns",
            new Dictionary<string, object?> { ["viewName"] = "IA_TEST" });
        Assert.Contains("Aggiunta", colsAfterAdd);

        // ── Step 10: preview_alter — RENAME Aggiunta → Modificata ───────────
        Step(10, "preview_alter — RENAME Aggiunta → Modificata (sp_rename)");
        const string renameSql = "EXEC sp_rename 'dbo.IA_TEST.Aggiunta', 'Modificata', 'COLUMN'";
        var previewRename = await CallAsync("preview_alter",
            new Dictionary<string, object?>
            {
                ["tableName"] = "IA_TEST",
                ["sql"] = renameSql
            });
        Assert.Contains("token", previewRename, StringComparison.OrdinalIgnoreCase);

        var renameToken = ExtractToken(previewRename);
        Assert.NotNull(renameToken);

        // ── Step 11: execute_alter — RENAME ──────────────────────────────────
        Step(11, "execute_alter — RENAME");
        var execRename = await CallAsync("execute_alter",
            new Dictionary<string, object?> { ["confirmationToken"] = renameToken! });
        Assert.Contains("[OK]", execRename);

        // ── Step 12: get_view_columns — verifica Modificata, assenza Aggiunta ─
        Step(12, "get_view_columns — verifica Modificata presente e Aggiunta assente");
        var colsAfterRename = await CallAsync("get_view_columns",
            new Dictionary<string, object?> { ["viewName"] = "IA_TEST" });
        Assert.Contains("Modificata", colsAfterRename);
        Assert.DoesNotContain("Aggiunta", colsAfterRename);

        // ── Step 13: preview_drop — IA_TEST ──────────────────────────────────
        Step(13, "preview_drop — dbo.IA_TEST");
        var previewDrop = await CallAsync("preview_drop",
            new Dictionary<string, object?> { ["tableName"] = "IA_TEST" });
        Assert.Contains("token", previewDrop, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IA_TEST", previewDrop);

        var dropToken = ExtractToken(previewDrop);
        Assert.NotNull(dropToken);

        // ── Step 14: execute_drop ─────────────────────────────────────────────
        Step(14, "execute_drop");
        var execDrop = await CallAsync("execute_drop",
            new Dictionary<string, object?> { ["confirmationToken"] = dropToken! });
        Assert.Contains("[OK]", execDrop);

        // ── Step 15: list_views — IA_TEST deve essere sparita ────────────────
        Step(15, "list_views — verifica assenza IA_TEST dopo DROP");
        var viewsAfterDrop = await CallAsync("list_views");
        Assert.DoesNotContain("IA_TEST", viewsAfterDrop);

        Log(string.Empty);
        Log("=== TEST COMPLETATO CON SUCCESSO ===");
    }

    // ---------------------------------------------------------------------------

    private async Task<string> CallAsync(string tool,
        IReadOnlyDictionary<string, object?>? args = null,
        CancellationToken ct = default)
    {
        // Log comando inviato
        var sb = new StringBuilder();
        sb.AppendLine($"  → TOOL: {tool}");
        if (args is not null)
        {
            foreach (var kv in args)
            {
                var val = kv.Value?.ToString() ?? "(null)";
                sb.AppendLine($"    {kv.Key}: {val}");
            }
        }
        Log(sb.ToString().TrimEnd());

        var result = await _fx.Client!.CallToolAsync(tool, args, cancellationToken: ct);
        var text = result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault()?.Text ?? string.Empty;

        // Log risposta dal DB (tramite MCP)
        Log("  ← RESPONSE:");
        foreach (var line in text.Split('\n'))
            Log($"    {line}");
        Log(string.Empty);

        _output.WriteLine(text);
        return text;
    }

    private void Step(int n, string name)
    {
        var header = $"[Step {n:D2}] {name}";
        _output.WriteLine($"\n{header}");
        Log(string.Empty);
        Log(new string('─', 60));
        Log(header);
    }

    private void Log(string line) =>
        File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);

    private static string? ExtractToken(string response) =>
        response.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("token", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Split(':', 2).ElementAtOrDefault(1)?.Trim())
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
}
