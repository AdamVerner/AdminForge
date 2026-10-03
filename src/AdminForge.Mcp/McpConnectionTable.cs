using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.Mcp;

/// <summary>The signed-in admin's own connections, in the panel, with one action to end one.</summary>
internal sealed class McpConnectionDataProvider(IMcpConnectionStore store, IUserAccessor user)
    : ReadOnlyDataProvider<McpConnection>
{
    public override async Task<ListResult<McpConnection>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var mine = await store
            .ListAsync(user.GetUserId() ?? "", cancellationToken)
            .ConfigureAwait(false);
        return new ListResult<McpConnection>
        {
            Items = mine.Skip(query.Page * query.PageSize).Take(query.PageSize).ToList(),
            TotalCount = mine.Count,
        };
    }

    public override async Task<McpConnection?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    )
    {
        var c = await store
            .FindAsync(keyValues[0]?.ToString() ?? "", cancellationToken)
            .ConfigureAwait(false);
        return c is not null && c.AdminId == user.GetUserId() ? c : null;
    }

    public static void Register(AdminForgeBuilder forge) =>
        forge.AddTable<McpConnection>(t =>
            t.Label("My MCP connections")
                .ReadOnly()
                .Nav(n => n.Area(NavArea.Account))
                .Column(c => c.ClientName)
                .Column(c => c.CreatedAt)
                .Column(c => c.ExpiresAt)
                .Column(c => c.LastCallAt)
                .Column(c => c.Revoked, col => col.Label("Ended"))
                .HideColumn(c => c.Claims)
                .HideColumn(c => c.AdminId)
                .AddAction(
                    "End connection",
                    (sp, connection, ctx) => EndAsync(sp, connection.Id, ctx),
                    a => a.Color("Warning")
                )
        );

    internal static async Task EndAsync(IServiceProvider sp, string id, IActionContext ctx)
    {
        var store = sp.GetRequiredService<IMcpConnectionStore>();
        var connection = await store.FindAsync(id).ConfigureAwait(false);
        if (connection is null)
            return;
        connection.Revoked = true;
        await store.SaveAsync(connection).ConfigureAwait(false);
        ctx.ShowSuccess($"Ended the connection of {connection.ClientName}.");
        ctx.Refresh();
    }
}

/// <summary>A connection as support sees it, among every admin's.</summary>
public sealed record McpConnectionEntry(
    string Id,
    string AdminName,
    string ClientName,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? LastCallAt,
    bool Revoked
)
{
    internal static McpConnectionEntry Of(McpConnection c) =>
        new(c.Id, c.AdminName, c.ClientName, c.CreatedAt, c.ExpiresAt, c.LastCallAt, c.Revoked);
}

/// <summary>Every admin's connections, under Settings, so one admin can end another's.</summary>
internal sealed class McpConnectionEntryDataProvider(IMcpConnectionStore store)
    : ReadOnlyDataProvider<McpConnectionEntry>
{
    public override async Task<ListResult<McpConnectionEntry>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var all = await store.ListAsync(null, cancellationToken).ConfigureAwait(false);
        return new ListResult<McpConnectionEntry>
        {
            Items = all.Skip(query.Page * query.PageSize)
                .Take(query.PageSize)
                .Select(McpConnectionEntry.Of)
                .ToList(),
            TotalCount = all.Count,
        };
    }

    public override async Task<McpConnectionEntry?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    )
    {
        var c = await store
            .FindAsync(keyValues[0]?.ToString() ?? "", cancellationToken)
            .ConfigureAwait(false);
        return c is null ? null : McpConnectionEntry.Of(c);
    }

    public static void Register(AdminForgeBuilder forge) =>
        forge.AddTable<McpConnectionEntry>(t =>
            t.Label("All MCP connections")
                .ReadOnly()
                .Nav(n => n.Area(NavArea.Settings))
                .Column(c => c.AdminName, col => col.Label("Admin"))
                .Column(c => c.ClientName)
                .Column(c => c.CreatedAt)
                .Column(c => c.ExpiresAt)
                .Column(c => c.LastCallAt)
                .Column(c => c.Revoked, col => col.Label("Ended"))
                .AddAction(
                    "End connection",
                    (sp, entry, ctx) => McpConnectionDataProvider.EndAsync(sp, entry.Id, ctx),
                    a => a.Color("Warning")
                )
        );
}
