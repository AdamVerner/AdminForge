using System.Linq.Expressions;

namespace AdminForge.Mcp;

/// <summary>The signed-in admin's own connections, in the account menu, and every admin's under Settings; each ends with one action.</summary>
internal static class McpConnectionTables
{
    public static void Register(PanelBuilder panel, string path)
    {
        var empty =
            $"Add this server's {path} URL to an MCP client; it opens this panel to sign in.";
        Table(panel, "my-mcp-connections", "My MCP connections", NavArea.Account, true, empty);
        Table(panel, "mcp-connections", "All MCP connections", NavArea.Settings, false, empty);
    }

    private static void Table(
        PanelBuilder panel,
        string id,
        string title,
        NavArea area,
        bool mine,
        string empty
    )
    {
        Expression<Func<McpConnection, object?>>[] columns =
        [
            c => c.ClientName,
            c => c.CreatedAt,
            c => c.ExpiresAt,
            c => c.LastCallAt,
            c => c.Revoked,
        ];
        panel.Table<McpConnection>(t =>
            t.Id(id)
                .Title(title)
                .Area(area)
                .Empty(empty)
                .Source<Connections, string>(
                    (s, q, ct) => s.ListAsync(mine, q, ct),
                    (s, key, ct) => s.FindAsync(mine, key, ct)
                )
                .Columns(mine ? columns : [c => c.AdminName, .. columns])
                .Column(c => c.AdminName, c => c.Title("Admin"))
                .Column(c => c.Revoked, c => c.Title("Ended"))
                .Hide(c => c.Claims, c => c.NameClaimType, c => c.RoleClaimType, c => c.AdminId, c => c.RefreshCounter)
                .Action(
                    "end",
                    "End connection",
                    a =>
                        a.Tone(Tone.Warning)
                            .Run<IMcpConnectionStore>(
                                async (store, ctx) =>
                                {
                                    await store.RevokeAsync(ctx.Row.Id, ctx.CancellationToken);
                                    ctx.Success($"Ended the connection of {ctx.Row.ClientName}.");
                                }
                            )
                )
        );
    }

    private sealed class Connections(IMcpConnectionStore store, IUserAccessor user)
    {
        public async Task<AdminPage<McpConnection>> ListAsync(
            bool mine,
            AdminQuery<McpConnection> query,
            CancellationToken ct
        ) => AdminPage.From(await store.ListAsync(mine ? user.GetUserId() ?? "" : null, ct), query);

        public async Task<McpConnection?> FindAsync(bool mine, string id, CancellationToken ct) =>
            await store.FindAsync(id, ct) is { } c && (!mine || c.AdminId == user.GetUserId())
                ? c
                : null;
    }
}
