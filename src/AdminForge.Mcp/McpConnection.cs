using System.Collections.Concurrent;

namespace AdminForge.Mcp;

public sealed record McpClaim(string Type, string Value);

/// <summary>One MCP client's standing with one admin: what it may call, until when, and whether it still may.</summary>
public sealed class McpConnection
{
    public required string Id { get; init; }
    public required string AdminId { get; init; }
    public required string AdminName { get; init; }
    public required string ClientName { get; init; }

    /// <summary>Tool names the admin ticked on the consent page.</summary>
    public required IReadOnlyList<string> Grant { get; init; }

    /// <summary>The admin's claims when they consented; the bearer principal is rebuilt from them.</summary>
    public required IReadOnlyList<McpClaim> Claims { get; init; }

    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public DateTime? LastCallAt { get; set; }

    /// <summary>Increments on every refresh; a refresh token naming an older value was stolen or replayed.</summary>
    public int RefreshCounter { get; set; }
    public bool CodeRedeemed { get; set; }
    public bool Revoked { get; set; }

    public bool IsLive(DateTime now) => !Revoked && ExpiresAt > now;
}

/// <summary>Where connections live. The in-memory default forgets them on restart; a host persists them.</summary>
public interface IMcpConnectionStore
{
    /// <summary>Inserts or replaces by <see cref="McpConnection.Id"/>.</summary>
    Task SaveAsync(McpConnection connection, CancellationToken cancellationToken = default);
    Task<McpConnection?> FindAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>One admin's connections, newest first; every admin's when <paramref name="adminId"/> is null.</summary>
    Task<IReadOnlyList<McpConnection>> ListAsync(
        string? adminId,
        CancellationToken cancellationToken = default
    );
}

public sealed class InMemoryMcpConnectionStore : IMcpConnectionStore
{
    private readonly ConcurrentDictionary<string, McpConnection> _connections = new(
        StringComparer.Ordinal
    );

    public Task SaveAsync(McpConnection connection, CancellationToken cancellationToken = default)
    {
        _connections[connection.Id] = connection;
        return Task.CompletedTask;
    }

    public Task<McpConnection?> FindAsync(
        string id,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_connections.GetValueOrDefault(id));

    public Task<IReadOnlyList<McpConnection>> ListAsync(
        string? adminId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyList<McpConnection>>(
            _connections
                .Values.Where(c => adminId is null || c.AdminId == adminId)
                .OrderByDescending(c => c.CreatedAt)
                .ToList()
        );
}
