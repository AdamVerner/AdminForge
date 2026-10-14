namespace AdminForge;

public sealed record McpClaim(string Type, string Value);

/// <summary>One MCP client's standing with one admin: what it may call, until when, and whether it still may.</summary>
public sealed record McpConnection
{
    public required string Id { get; init; }
    public required string AdminId { get; init; }
    public required string AdminName { get; init; }
    public required string ClientName { get; init; }

    /// <summary>Tool names the admin ticked on the consent page; each is built from a table, action, form or dashboard id.</summary>
    public required IReadOnlyList<string> Grant { get; init; }

    /// <summary>The admin's claims when they consented; the bearer principal is rebuilt from them.</summary>
    public required IReadOnlyList<McpClaim> Claims { get; init; }

    /// <summary>The claim types the admin's identity took its name and roles from, so a role policy reads the same over MCP.</summary>
    public string NameClaimType { get; init; } = System.Security.Claims.ClaimTypes.Name;

    /// <inheritdoc cref="NameClaimType"/>
    public string RoleClaimType { get; init; } = System.Security.Claims.ClaimTypes.Role;

    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public DateTime? LastCallAt { get; init; }

    /// <summary>0 until the code is redeemed, then one more per refresh; a token naming an older value was replayed.</summary>
    public int RefreshCounter { get; init; }
    public bool Revoked { get; init; }

    public bool IsLive(DateTime now) => !Revoked && ExpiresAt > now;
}

/// <summary>Where connections live. The in-memory default forgets them on restart; a host persists them.</summary>
public interface IMcpConnectionStore
{
    Task AddAsync(McpConnection connection, CancellationToken cancellationToken = default);
    Task<McpConnection?> FindAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>One admin's connections, newest first; every admin's when <paramref name="adminId"/> is null.</summary>
    Task<IReadOnlyList<McpConnection>> ListAsync(
        string? adminId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sets <see cref="McpConnection.RefreshCounter"/> to <paramref name="expectedCounter"/> + 1 only if it
    /// still equals <paramref name="expectedCounter"/> and the connection is not revoked, atomically.
    /// </summary>
    Task<bool> TryAdvanceRefreshAsync(
        string id,
        int expectedCounter,
        CancellationToken cancellationToken = default
    );

    Task TouchAsync(string id, DateTime lastCallAt, CancellationToken cancellationToken = default);
    Task RevokeAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Deletes every connection whose <see cref="McpConnection.ExpiresAt"/> is before <paramref name="before"/>.</summary>
    Task PurgeExpiredAsync(DateTime before, CancellationToken cancellationToken = default);
}

public sealed class InMemoryMcpConnectionStore : IMcpConnectionStore
{
    // ponytail: one lock over every connection; a host with real traffic persists the store anyway.
    private readonly Lock _gate = new();
    private readonly Dictionary<string, McpConnection> _connections = new(StringComparer.Ordinal);

    public Task AddAsync(McpConnection connection, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _connections.Add(connection.Id, connection);
        return Task.CompletedTask;
    }

    public Task<McpConnection?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_connections.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<McpConnection>> ListAsync(
        string? adminId,
        CancellationToken cancellationToken = default
    )
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<McpConnection>>(
                _connections
                    .Values.Where(c => adminId is null || c.AdminId == adminId)
                    .OrderByDescending(c => c.CreatedAt)
                    .ToList()
            );
    }

    public Task<bool> TryAdvanceRefreshAsync(
        string id,
        int expectedCounter,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Change(
                id,
                c =>
                    c.RefreshCounter == expectedCounter && !c.Revoked
                        ? c with
                        {
                            RefreshCounter = expectedCounter + 1,
                        }
                        : null
            )
        );

    public Task TouchAsync(
        string id,
        DateTime lastCallAt,
        CancellationToken cancellationToken = default
    )
    {
        Change(id, c => c with { LastCallAt = lastCallAt });
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string id, CancellationToken cancellationToken = default)
    {
        Change(id, c => c with { Revoked = true });
        return Task.CompletedTask;
    }

    public Task PurgeExpiredAsync(DateTime before, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            foreach (var c in _connections.Values.Where(c => c.ExpiresAt < before).ToList())
                _connections.Remove(c.Id);
        return Task.CompletedTask;
    }

    private bool Change(string id, Func<McpConnection, McpConnection?> change)
    {
        lock (_gate)
        {
            if (
                !_connections.TryGetValue(id, out var current) || change(current) is not { } changed
            )
                return false;
            _connections[id] = changed;
            return true;
        }
    }
}
