namespace dr_mcp_dbschema.Tests;

public class DdlTokenStoreTests
{
    private static PendingDdl MakePending(DateTime? expiresAt = null) => new()
    {
        Sql = "CREATE TABLE Test (Id INT)",
        Kind = DdlKind.Create,
        TableName = "Test",
        ConnectionName = "TestDb",
        ConnectionString = "Server=localhost;Database=Test;",
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddSeconds(60)
    };

    [Fact]
    public void Add_ReturnsNonEmptyToken()
    {
        var store = new DdlTokenStore();
        var token = store.Add(MakePending());
        Assert.NotEmpty(token);
    }

    [Fact]
    public void Add_ReturnsUppercaseAlphanumericToken()
    {
        var store = new DdlTokenStore();
        var token = store.Add(MakePending());
        Assert.Matches("^[A-F0-9]{12}$", token);
    }

    [Fact]
    public void Consume_ValidToken_ReturnsPendingDdl()
    {
        var store = new DdlTokenStore();
        var pending = MakePending();
        var token = store.Add(pending);

        var result = store.Consume(token);

        Assert.NotNull(result);
        Assert.Equal(pending.Sql, result.Sql);
        Assert.Equal(pending.Kind, result.Kind);
    }

    [Fact]
    public void Consume_SameTokenTwice_ReturnsNullOnSecond()
    {
        var store = new DdlTokenStore();
        var token = store.Add(MakePending());

        store.Consume(token);
        var second = store.Consume(token);

        Assert.Null(second);
    }

    [Fact]
    public void Consume_InvalidToken_ReturnsNull()
    {
        var store = new DdlTokenStore();
        var result = store.Consume("NONEXISTENT");
        Assert.Null(result);
    }

    [Fact]
    public void Consume_ExpiredToken_ReturnsNull()
    {
        var store = new DdlTokenStore();
        var token = store.Add(MakePending(expiresAt: DateTime.UtcNow.AddSeconds(-1)));

        var result = store.Consume(token);

        Assert.Null(result);
    }

    [Fact]
    public void Consume_TokenIsCaseInsensitive()
    {
        var store = new DdlTokenStore();
        var token = store.Add(MakePending());

        var result = store.Consume(token.ToLowerInvariant());

        Assert.NotNull(result);
    }

    [Fact]
    public void Consume_TokenWithWhitespace_IsAccepted()
    {
        var store = new DdlTokenStore();
        var token = store.Add(MakePending());

        var result = store.Consume($"  {token}  ");

        Assert.NotNull(result);
    }
}
