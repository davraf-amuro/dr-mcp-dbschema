using Microsoft.Extensions.Logging.Abstractions;

namespace dr_mcp_dbschema.Tests;

public class ListConnectionsTests
{
    private static DbSchemaTools MakeTools(ConnectionState state) =>
        new(state, new DdlSettings(), new DdlTokenStore(), NullLogger<DbSchemaTools>.Instance);

    private static ConnectionState StateWith(params (string name, string cs, string? source)[] connections)
    {
        var state = new ConnectionState();
        foreach (var (name, cs, source) in connections)
        {
            state.Available[name] = cs;
            if (source is not null)
            {
                state.AvailableSources[name] = source;
            }
        }
        return state;
    }

    [Fact]
    public void ListConnections_EmptyState_ReturnsNoConnectionsConfigured()
    {
        var tools = MakeTools(new ConnectionState());
        var result = tools.ListConnections();
        Assert.Contains("[NO_CONNECTIONS_CONFIGURED]", result);
    }

    [Fact]
    public void ListConnections_ThreeConnections_NumbersOneToThree()
    {
        var state = StateWith(
            ("Default", "Server=a;", "/app/appsettings.json"),
            ("Staging", "Server=b;", "/app/appsettings.Staging.json"),
            ("LocalTest", "Server=c;", null));
        var tools = MakeTools(state);

        var result = tools.ListConnections();
        var lines = result.Split('\n');

        Assert.Contains("1.", lines[0]);
        Assert.Contains("2.", lines[1]);
        Assert.Contains("3.", lines[2]);
    }

    [Fact]
    public void ListConnections_ActiveConnection_MarkedWithAsterisk()
    {
        var state = StateWith(
            ("Default", "Server=a;", null),
            ("Staging", "Server=b;", null));
        state.ActiveName = "Staging";
        var tools = MakeTools(state);

        var result = tools.ListConnections();
        var lines = result.Split('\n');

        Assert.StartsWith("  ", lines[0]);
        Assert.StartsWith("*", lines[1]);
    }

    [Fact]
    public void ListConnections_AlwaysContainsHint()
    {
        var state = StateWith(("Default", "Server=a;", null));
        var tools = MakeTools(state);

        var result = tools.ListConnections();

        Assert.Contains("UseConnection", result);
    }
}
