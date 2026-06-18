using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Middleware.Authorization;
using AdminForge.UI.Blazor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Endpoints;
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

        // Insert antiforgery + the umbrella-policy middleware in front of the Razor endpoints.
        // Razor Components in .NET 8+ refuse to serve unless antiforgery middleware is present.
        if (endpoints is IApplicationBuilder appBuilder)
        {
            appBuilder.UseAntiforgery();
            appBuilder.UseMiddleware<AdminForgeMiddleware>();
        }

        // Serve _framework/blazor.web.js + _content/{RCL}/* (MudBlazor CSS/JS).
        // Idempotent under .NET 9+: hosts that already call MapStaticAssets get a no-op.
        endpoints.MapStaticAssets();

        var builder = endpoints.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        return builder;
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
