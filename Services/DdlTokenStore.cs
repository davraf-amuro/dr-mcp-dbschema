using System.Collections.Concurrent;

public class DdlTokenStore
{
    private readonly ConcurrentDictionary<string, PendingDdl> _tokens = new();

    /// <summary>Aggiunge un'operazione DDL pendente e restituisce il token monouso.</summary>
    public string Add(PendingDdl pending)
    {
        PurgeExpired();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _tokens[token] = pending;
        return token;
    }

    /// <summary>Consuma il token: restituisce il DDL pendente se valido e non scaduto, altrimenti null.</summary>
    public PendingDdl? Consume(string token)
    {
        PurgeExpired();
        if (_tokens.TryRemove(token.Trim().ToUpperInvariant(), out var pending) && pending.ExpiresAt >= DateTime.UtcNow)
        {
            return pending;
        }

        return null;
    }

    private void PurgeExpired()
    {
        foreach (var key in _tokens.Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow).Select(kv => kv.Key).ToList())
        {
            _tokens.TryRemove(key, out _);
        }
    }
}
