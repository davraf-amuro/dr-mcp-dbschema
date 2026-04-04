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
        // ── Step 1: ListConnections ──────────────────────────────────────────
        Step(1, "ListConnections");
        var listConn = await CallAsync("ListConnections");
        output.WriteLine(listConn);
        Assert.Contains("(override)", listConn);
        Assert.Contains("attiva", listConn);

        // ── Step 2: UseConnection ────────────────────────────────────────────
        Step(2, "UseConnection");
        var useConn = await CallAsync("UseConnection",
            new Dictionary<string, object?> { ["name"] = "(override)" });
        output.WriteLine(useConn);
        Assert.Contains("attiva", useConn, StringComparison.OrdinalIgnoreCase);

        // ── Step 3: ListViews ────────────────────────────────────────────────
        Step(3, "ListViews");
        var views = await CallAsync("ListViews");
        output.WriteLine(views);
        Assert.Contains("Customers", views);
        Assert.Contains("ActiveCustomers", views);

        // ── Step 4: GetViewDefinition (vista) ────────────────────────────────
        Step(4, "GetViewDefinition — ActiveCustomers");
        var viewDef = await CallAsync("GetViewDefinition",
            new Dictionary<string, object?> { ["viewName"] = "ActiveCustomers" });
        output.WriteLine(viewDef);
        Assert.Contains("SELECT", viewDef, StringComparison.OrdinalIgnoreCase);

        // ── Step 5: GetViewDefinition (tabella) ──────────────────────────────
        Step(5, "GetViewDefinition — Customers (tabella)");
        var tableDef = await CallAsync("GetViewDefinition",
            new Dictionary<string, object?> { ["viewName"] = "Customers" });
        output.WriteLine(tableDef);
        Assert.Contains("tabella", tableDef, StringComparison.OrdinalIgnoreCase);

        // ── Step 6: GetViewColumns ───────────────────────────────────────────
        Step(6, "GetViewColumns — Customers");
        var cols = await CallAsync("GetViewColumns",
            new Dictionary<string, object?> { ["viewName"] = "Customers" });
        output.WriteLine(cols);
        Assert.Contains("Id", cols);
        Assert.Contains("Email", cols);
        Assert.Contains("Name", cols);

        // ── Step 7: PreviewCreate ────────────────────────────────────────────
        Step(7, "PreviewCreate — dbo.Orders");
        const string createSql = """
            CREATE TABLE dbo.Orders (
                Id         INT           PRIMARY KEY IDENTITY,
                CustomerId INT           NOT NULL,
                Amount     DECIMAL(10,2) NOT NULL,
                CreatedAt  DATETIME2     NOT NULL DEFAULT GETUTCDATE()
            )
            """;
        var preview = await CallAsync("PreviewCreate",
            new Dictionary<string, object?> { ["sql"] = createSql });
        output.WriteLine(preview);
        Assert.Contains("token", preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orders", preview);

        var createToken = ExtractToken(preview);
        Assert.NotNull(createToken);
        output.WriteLine($"  → token estratto: {createToken}");

        // ── Step 8: ExecuteCreate ────────────────────────────────────────────
        Step(8, "ExecuteCreate");
        var execCreate = await CallAsync("ExecuteCreate",
            new Dictionary<string, object?> { ["confirmationToken"] = createToken! });
        output.WriteLine(execCreate);
        Assert.Contains("[OK]", execCreate);

        // ── Step 9: ListViews — verifica presenza Orders ─────────────────────
        Step(9, "ListViews — verifica Orders");
        var viewsAfter = await CallAsync("ListViews");
        output.WriteLine(viewsAfter);
        Assert.Contains("Orders", viewsAfter);

        // ── Step 10: PreviewAlter ────────────────────────────────────────────
        Step(10, "PreviewAlter — aggiungi colonna Note");
        const string alterSql = "ALTER TABLE dbo.Orders ADD Note NVARCHAR(500) NULL";
        var previewAlter = await CallAsync("PreviewAlter",
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

        // ── Step 11: ExecuteAlter ────────────────────────────────────────────
        Step(11, "ExecuteAlter");
        var execAlter = await CallAsync("ExecuteAlter",
            new Dictionary<string, object?> { ["confirmationToken"] = alterToken! });
        output.WriteLine(execAlter);
        Assert.Contains("[OK]", execAlter);

        // ── Step 12: GetViewColumns — verifica colonna Note ──────────────────
        Step(12, "GetViewColumns — Orders post-ALTER");
        var colsAfter = await CallAsync("GetViewColumns",
            new Dictionary<string, object?> { ["viewName"] = "Orders" });
        output.WriteLine(colsAfter);
        Assert.Contains("Note", colsAfter);
        Assert.Contains("nvarchar", colsAfter, StringComparison.OrdinalIgnoreCase);
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
