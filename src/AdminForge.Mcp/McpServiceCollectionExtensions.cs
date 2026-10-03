using AdminForge.Bridge;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AdminForge.Mcp;

/// <summary>Every tool the registered panel yields, built once.</summary>
internal sealed class PanelTools(IReadOnlyList<PanelTool> all)
{
    public IReadOnlyList<PanelTool> All { get; } = all;
}

public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Serves the registered panel as MCP tools behind the panel's own OAuth: an MCP client signs
    /// in through the panel session, the admin ticks the tools the connection may call, and every
    /// call then runs as that admin through the same policy, scope and audit as the panel.
    /// </summary>
    public static IServiceCollection AddAdminForgeMcp(
        this IServiceCollection services,
        Action<AdminForgeMcpOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AdminForgeMcpOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddAdminForgeBridge();
        services.AddDataProtection();
        services.AddAntiforgery();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMcpConnectionStore, InMemoryMcpConnectionStore>();
        services.AddSingleton<TokenSealer>();
        services.AddScoped<McpConnectionDataProvider>();
        services.AddScoped<IAdminDataProvider<McpConnection>>(sp =>
            sp.GetRequiredService<McpConnectionDataProvider>()
        );
        services.AddScoped<
            IAdminDataProvider<McpConnectionEntry>,
            McpConnectionEntryDataProvider
        >();
        services.AddSingleton(new AdminForgeContribution(McpConnectionDataProvider.Register));
        services.AddSingleton(new AdminForgeContribution(McpConnectionEntryDataProvider.Register));
        services.AddSingleton(sp => new PanelTools(
            ToolCatalog.Build(
                sp.GetRequiredService<AdminForgeOptions>(),
                options,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("AdminForge.Mcp")
            )
        ));

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
                        var bridge = ctx.Services!.GetRequiredService<IAdminUIBridge>();
                        var user = ctx.Services!.GetRequiredService<IUserAccessor>().GetUser();
                        var byName = ctx.Services!.GetRequiredService<PanelTools>()
                            .All.ToDictionary(t => t.ProtocolTool.Name, StringComparer.Ordinal);
                        var visible = new List<Tool>();
                        foreach (var tool in result.Tools)
                        {
                            if (!byName.TryGetValue(tool.Name, out var panelTool))
                            {
                                visible.Add(tool);
                                continue;
                            }
                            if (!PanelTool.Granted(user, tool.Name))
                                continue;
                            if (!await panelTool.IsAllowedAsync(bridge, ct).ConfigureAwait(false))
                                continue;
                            await panelTool.RefreshAsync(bridge, ct).ConfigureAwait(false);
                            visible.Add(panelTool.ProtocolTool);
                        }
                        result.Tools = visible;
                        return result;
                    }
                )
            );

        services
            .AddOptions<McpServerOptions>()
            .Configure<AdminForgeOptions, PanelTools>(
                (mcp, forge, tools) =>
                {
                    mcp.ToolCollection ??= [];
                    foreach (var tool in tools.All)
                        mcp.ToolCollection.Add(tool);
                    mcp.ServerInfo = new Implementation
                    {
                        Name = forge.Title,
                        Version =
                            typeof(McpServiceCollectionExtensions)
                                .Assembly.GetName()
                                .Version?.ToString(3)
                            ?? "0.0.0",
                    };
                    var env = forge.Environment is null ? "" : $" ({forge.Environment.Label})";
                    mcp.ServerInstructions =
                        $"{forge.Title}{env}: the admin panel as tools. list_* returns the list columns of a page; "
                        + "get_* returns every column of one row and the first rows of its related tables. "
                        + "Keys are the strings list_* shows. Tools you do not see are ones this connection may not run.";
                }
            );

        return services;
    }

    /// <summary>
    /// Maps the MCP endpoint behind its bearer policy, the OAuth endpoints under it and the two
    /// well-known documents at the root. The authorize page runs under the panel's own policy, so
    /// the panel must have one: <c>AddAdminForgeAuth</c> or <c>RequireAuthorizationPolicy</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapAdminForgeMcp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<AdminForgeMcpOptions>();
        var forge = endpoints.ServiceProvider.GetRequiredService<AdminForgeOptions>();
        if (string.IsNullOrWhiteSpace(forge.AuthorizationPolicy))
            throw new InvalidOperationException(
                "AdminForge MCP needs a signed-in panel for its consent page: call AddAdminForgeAuth<T>() "
                    + "or RequireAuthorizationPolicy(...) on the panel."
            );

        if (endpoints is IApplicationBuilder app)
            app.UseAntiforgery();
        OAuthEndpoints.Map(endpoints, options, forge.AuthorizationPolicy);
        return endpoints.MapMcp(options.Path).RequireAuthorization(AdminForgeMcp.Policy);
    }
}
