using ModelContextProtocol.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace DrMcpDbSchema.IntegrationTests;

/// <summary>
/// Ciclo completo di tutti i tool MCP su un database TEST reale (Testcontainers SQL Server).
/// Ogni step è sequenziale e stateful: l'ordine di esecuzione è obbligatorio.
/// </summary>
[Collection("MCP")]
public class FullCycleTests(McpEnvironmentFixture fx, ITestOutputHelper output)
{
    [Fact]
    public async Task FullCycle_AllTools_Succeed()
    {
        // ── Step 1: list_connections ──────────────────────────────────────────
        Step(1, "list_connections");
        var listConn = await CallAsync("list_connections");
        output.WriteLine(listConn);
        Assert.Contains("(override)", listConn);
        Assert.Contains("attiva", listConn);

        // ── Step 2: use_connection ────────────────────────────────────────────
        Step(2, "use_connection");
        var useConn = await CallAsync("use_connection",
            new Dictionary<string, object?> { ["name"] = "(override)" });
        output.WriteLine(useConn);
        Assert.Contains("attiva", useConn, StringComparison.OrdinalIgnoreCase);

        // ── Step 3: list_views ────────────────────────────────────────────────
        Step(3, "list_views");
        var views = await CallAsync("list_views");
        output.WriteLine(views);
        Assert.Contains("Customers", views);
        Assert.Contains("ActiveCustomers", views);

        // ── Step 4: get_view_definition (vista) ────────────────────────────────
        Step(4, "get_view_definition — ActiveCustomers");
        var viewDef = await CallAsync("get_view_definition",
            new Dictionary<string, object?> { ["viewName"] = "ActiveCustomers" });
        output.WriteLine(viewDef);
        Assert.Contains("SELECT", viewDef, StringComparison.OrdinalIgnoreCase);

        // ── Step 5: get_view_definition (tabella) ──────────────────────────────
        Step(5, "get_view_definition — Customers (tabella)");
        var tableDef = await CallAsync("get_view_definition",
            new Dictionary<string, object?> { ["viewName"] = "Customers" });
        output.WriteLine(tableDef);
        Assert.Contains("tabella", tableDef, StringComparison.OrdinalIgnoreCase);

        // ── Step 6: get_view_columns ───────────────────────────────────────────
        Step(6, "get_view_columns — Customers");
        var cols = await CallAsync("get_view_columns",
            new Dictionary<string, object?> { ["viewName"] = "Customers" });
        output.WriteLine(cols);
        Assert.Contains("Id", cols);
        Assert.Contains("Email", cols);
        Assert.Contains("Name", cols);

        // ── Step 7: preview_create ────────────────────────────────────────────
        Step(7, "preview_create — dbo.Orders");
        const string createSql = """
            CREATE TABLE dbo.Orders (
                Id         INT           PRIMARY KEY IDENTITY,
                CustomerId INT           NOT NULL,
                Amount     DECIMAL(10,2) NOT NULL,
                CreatedAt  DATETIME2     NOT NULL DEFAULT GETUTCDATE()
            )
            """;
        var preview = await CallAsync("preview_create",
            new Dictionary<string, object?> { ["sql"] = createSql });
        output.WriteLine(preview);
        Assert.Contains("token", preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orders", preview);

        var createToken = ExtractToken(preview);
        Assert.NotNull(createToken);
        output.WriteLine($"  → token estratto: {createToken}");

        // ── Step 8: execute_create ────────────────────────────────────────────
        Step(8, "execute_create");
        var execCreate = await CallAsync("execute_create",
            new Dictionary<string, object?> { ["confirmationToken"] = createToken! });
        output.WriteLine(execCreate);
        Assert.Contains("[OK]", execCreate);

        // ── Step 9: list_views — verifica presenza Orders ─────────────────────
        Step(9, "list_views — verifica Orders");
        var viewsAfter = await CallAsync("list_views");
        output.WriteLine(viewsAfter);
        Assert.Contains("Orders", viewsAfter);

        // ── Step 10: preview_alter ────────────────────────────────────────────
        Step(10, "preview_alter — aggiungi colonna Note");
        const string alterSql = "ALTER TABLE dbo.Orders ADD Note NVARCHAR(500) NULL";
        var previewAlter = await CallAsync("preview_alter",
            new Dictionary<string, object?>
            {
                ["tableName"] = "Orders",
                ["sql"] = alterSql
            });
        output.WriteLine(previewAlter);
        Assert.Contains("token", previewAlter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orders", previewAlter);

        var alterToken = ExtractToken(previewAlter);
        Assert.NotNull(alterToken);
        output.WriteLine($"  → token estratto: {alterToken}");

        // ── Step 11: execute_alter ────────────────────────────────────────────
        Step(11, "execute_alter");
        var execAlter = await CallAsync("execute_alter",
            new Dictionary<string, object?> { ["confirmationToken"] = alterToken! });
        output.WriteLine(execAlter);
        Assert.Contains("[OK]", execAlter);

        // ── Step 12: get_view_columns — verifica colonna Note ──────────────────
        Step(12, "get_view_columns — Orders post-ALTER");
        var colsAfter = await CallAsync("get_view_columns",
            new Dictionary<string, object?> { ["viewName"] = "Orders" });
        output.WriteLine(colsAfter);
        Assert.Contains("Note", colsAfter);
        Assert.Contains("nvarchar", colsAfter, StringComparison.OrdinalIgnoreCase);

        // ── Step 13: preview_drop ─────────────────────────────────────────────
        Step(13, "preview_drop — dbo.Orders");
        var previewDrop = await CallAsync("preview_drop",
            new Dictionary<string, object?> { ["tableName"] = "Orders" });
        output.WriteLine(previewDrop);
        Assert.Contains("token", previewDrop, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orders", previewDrop);

        var dropToken = ExtractToken(previewDrop);
        Assert.NotNull(dropToken);
        output.WriteLine($"  → token estratto: {dropToken}");

        // ── Step 14: execute_drop ─────────────────────────────────────────────
        Step(14, "execute_drop");
        var execDrop = await CallAsync("execute_drop",
            new Dictionary<string, object?> { ["confirmationToken"] = dropToken! });
        output.WriteLine(execDrop);
        Assert.Contains("[OK]", execDrop);

        // ── Step 15: list_views — verifica assenza Orders ─────────────────────
        Step(15, "list_views — verifica Orders assente dopo DROP");
        var viewsFinal = await CallAsync("list_views");
        output.WriteLine(viewsFinal);
        Assert.DoesNotContain("Orders", viewsFinal);
    }

    // ---------------------------------------------------------------------------

    private async Task<string> CallAsync(string tool,
        IReadOnlyDictionary<string, object?>? args = null,
        CancellationToken ct = default)
    {
        var result = await fx.Client.CallToolAsync(tool, args, cancellationToken: ct);
        return result.Content
            .OfType<TextContentBlock>()
            .FirstOrDefault()?.Text ?? string.Empty;
    }

    private void Step(int n, string name) =>
        output.WriteLine($"\n[Step {n:D2}] {name}");

    private static string? ExtractToken(string response) =>
        response.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("token", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Split(':', 2).ElementAtOrDefault(1)?.Trim())
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
}
