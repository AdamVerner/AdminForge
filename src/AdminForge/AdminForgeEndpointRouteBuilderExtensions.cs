using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

/// <summary>
/// Endpoint-routing extensions for mounting AdminForge into a host application.
/// </summary>
public static class AdminForgeEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Mounts the AdminForge admin panel at the configured route prefix.
    /// Phase 0 stub: returns a placeholder text response so integration is observable.
    /// Real Blazor host + middleware land in Phase 2.
    /// </summary>
    public static IEndpointConventionBuilder MapAdminForge(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<AdminForgeOptions>()
                      ?? new AdminForgeOptions();

        var prefix = NormalisePrefix(options.RoutePrefix);

        return endpoints.MapGet(prefix, () => Results.Text("AdminForge mounted"));
    }

    private static string NormalisePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return "/";
        return prefix.StartsWith('/') ? prefix : "/" + prefix;
    }
}
