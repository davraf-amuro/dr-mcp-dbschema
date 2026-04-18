public class PendingDdl
{
    public required string Sql { get; init; }
    public required DdlKind Kind { get; init; }
    public string? TableName { get; init; }
    public required string ConnectionName { get; init; }
    public required string ConnectionString { get; init; }
    public DateTime ExpiresAt { get; init; }
}
