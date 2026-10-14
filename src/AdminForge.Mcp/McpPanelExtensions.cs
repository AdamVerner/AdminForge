using AdminForge.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AdminForge;

public static class McpPanelExtensions
{
    /// <summary>
    /// Serves the panel as MCP tools behind the panel's own OAuth: an MCP client signs in through
    /// the panel session, the admin ticks the tools the connection may call, and every call runs as
    /// that admin through the same pipeline as the panel. <c>MapAdminForge()</c> maps it.
    /// </summary>
    public static PanelBuilder UseMcp(
        this PanelBuilder panel,
        Action<AdminForgeMcpOptions>? configure = null
    )
    {
        var options = new AdminForgeMcpOptions();
        configure?.Invoke(options);
        var services = panel.Services;
        services.AddSingleton(options);
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMcpConnectionStore, InMemoryMcpConnectionStore>();
        services.AddSingleton<TokenSealer>();
        services.AddSingleton<RefreshGrace>();
        services.AddSingleton(sp =>
            PanelTools.Build(
                sp.GetRequiredService<PanelModel>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("AdminForge.Mcp")
            )
        );
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, McpBearerHandler>(AdminForgeMcp.Scheme, null);
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                AdminForgeMcp.Policy,
                p =>
                    p.AddAuthenticationSchemes(AdminForgeMcp.Scheme)
                        .RequireAuthenticatedUser()
                        .RequireClaim(AdminForgeMcp.ConnectionClaim)
            );
        services
            .AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithRequestFilters(f =>
                f.AddListToolsFilter(next =>
                    async (ctx, ct) =>
                    {
                        var result = await next(ctx, ct).ConfigureAwait(false);
                        var adminPanel = PanelTool.Panel(ctx.Services!);
                        var user = ctx.Services!.GetRequiredService<IUserAccessor>().GetUser();
                        var byName = ctx.Services!.GetRequiredService<PanelTools>().ByName;
                        var visible = new List<Tool>();
                        foreach (var tool in result.Tools)
                        {
                            if (!byName.TryGetValue(tool.Name, out var panelTool))
                            {
                                visible.Add(tool);
                                continue;
                            }
                            if (
                                !PanelTool.Granted(user, tool.Name)
                                || !await panelTool
                                    .IsAllowedAsync(adminPanel, ct)
                                    .ConfigureAwait(false)
                            )
                                continue;
                            // A choice loader that fails, already logged, lists its tool without choices.
                            try
                            {
                                visible.Add(
                                    await panelTool
                                        .DescribeAsync(adminPanel, ct)
                                        .ConfigureAwait(false)
                                );
                            }
                            catch (AdminRejection)
                            {
                                visible.Add(tool);
                            }
                        }
                        result.Tools = visible;
                        return result;
                    }
                )
            );
        services
            .AddOptions<McpServerOptions>()
            .Configure<PanelModel, PanelTools>(
                (mcp, model, tools) =>
                {
                    mcp.ToolCollection ??= [];
                    foreach (var tool in tools.All)
                        mcp.ToolCollection.Add(tool);
                    mcp.ServerInfo = new Implementation
                    {
                        Name = model.Title,
                        Version =
                            typeof(McpPanelExtensions).Assembly.GetName().Version?.ToString(3)
                            ?? "0.0.0",
                    };
                    var env = model.Environment is { } e ? $" ({e.Label})" : "";
                    mcp.ServerInstructions =
                        $"{model.Title}{env}: the admin panel as tools. list_* returns the list columns of a page; "
                        + "get_* returns every column of one row and the first rows of its related tables. "
                        + "Keys are the strings list_* shows. Tools you do not see are ones this connection may not run.";
                }
            );

        McpConnectionTables.Register(panel, options.Path);
        panel.Endpoints.Add(
            (endpoints, model) =>
            {
                // The consent page runs under the panel's own policy, so the panel must have one.
                if (model.Policy is null)
                    throw new InvalidOperationException(
                        "AdminForge MCP needs a signed-in panel for its consent page: call UseLogin<T>() or RequireAuthorization(...) on the panel."
                    );
                // Builds the catalogue now, so a tool name clash fails startup instead of a call.
                endpoints.ServiceProvider.GetRequiredService<PanelTools>();
                OAuthEndpoints.Map(endpoints, options, model.Policy);
                endpoints.MapMcp(options.Path).RequireAuthorization(AdminForgeMcp.Policy);
            }
        );
        return panel;
    }
}
