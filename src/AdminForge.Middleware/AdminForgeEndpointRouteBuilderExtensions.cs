using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Middleware.Authorization;
using AdminForge.UI.Blazor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdminForge.Middleware;

/// <summary>
/// Endpoint-routing extensions for mounting AdminForge into a host application.
/// Hosts call <c>app.MapAdminForge()</c> after their <c>UseAuthorization</c>.
/// </summary>
public static class AdminForgeEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Mounts the AdminForge Blazor Server panel at the configured route prefix.
    /// </summary>
    public static IEndpointConventionBuilder MapAdminForge(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = GuardAuthorizationIsConfigured(endpoints.ServiceProvider);

        // Razor Components in .NET 8+ refuse to serve unless antiforgery middleware is present.
        if (endpoints is IApplicationBuilder appBuilder)
            appBuilder.UseAntiforgery();

        // Serve _framework/blazor.web.js + _content/{RCL}/* (MudBlazor CSS/JS).
        // Idempotent under .NET 9+: hosts that already call MapStaticAssets get a no-op.
        endpoints.MapStaticAssets();

        var builder = endpoints.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        // The umbrella policy goes on the endpoints, so the host's authentication scheme decides
        // what an unauthorized request gets: a cookie scheme redirects to its login page, a bearer
        // scheme answers 401. It also covers the Blazor circuit endpoints the render mode adds.
        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
            builder.RequireAuthorization(options.AuthorizationPolicy);

        return builder;
    }

    /// <summary>
    /// Refuses to mount an admin panel that nobody is gating. Fails at boot rather than at
    /// request time: a stock-default panel authorizes everything, and the failure mode of a
    /// deny-by-default alternative is an empty grid whose obvious "fix" is re-registering
    /// <see cref="AllowAllAuthorizationPolicy"/>.
    /// </summary>
    internal static AdminForgeOptions GuardAuthorizationIsConfigured(
        IServiceProvider serviceProvider
    )
    {
        // Scoped, so a host that registered IAdminAuthorizationPolicy per-request still resolves.
        using var scope = serviceProvider.CreateScope();
        var options =
            scope.ServiceProvider.GetService<AdminForgeOptions>()
            ?? throw new InvalidOperationException(
                "AdminForge is not registered. Call services.AddAdminForge<TDbContext>(...) before MapAdminForge()."
            );

        if (options.AllowAnonymousAccess || !string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
            return options;

        var perAction = scope.ServiceProvider.GetService<IAdminAuthorizationPolicy>();
        if (perAction is not null and not AllowAllAuthorizationPolicy)
            return options;

        throw new InvalidOperationException(
            "AdminForge refuses to mount without authorization: the panel exposes read/write access to every "
                + "registered entity. Configure one of:\n"
                + "  • an umbrella policy — AddAdminForge<T>(f => f.RequireAuthorizationPolicy(\"Admins\")), or\n"
                + "  • a per-action hook — services.AddSingleton<IAdminAuthorizationPolicy, MyPolicy>().\n"
                + "If the panel is genuinely meant to be open (demo, local tooling, tests), say so explicitly "
                + "with AddAdminForge<T>(f => f.AllowAnonymousAccess()). Do not silence this by registering "
                + "AllowAllAuthorizationPolicy — that reads as a real policy and hides the same hole."
        );
    }

    /// <summary>
    /// DI-side helper that complements <c>AddAdminForge</c> with the Blazor + auth wiring
    /// needed for Blazor + auth wiring. Called from the meta-package extension.
    /// </summary>
    public static IServiceCollection AddAdminForgeBlazor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRazorComponents().AddInteractiveServerComponents();
        AdminForge.UI.Blazor.BlazorServiceCollectionExtensions.AddAdminForgeBlazorUI(services);
        services.AddAntiforgery();
        services.AddHttpContextAccessor();
        services.AddAuthorization();
        services.TryAddSingleton<IUserAccessor, HttpContextUserAccessor>();

        // Replace the default authorization-policy provider with AdminForge's lazy variant.
        services.RemoveAll<IAuthorizationPolicyProvider>();
        services.AddSingleton<IAuthorizationPolicyProvider, AdminPolicyProvider>();

        return services;
    }
}
