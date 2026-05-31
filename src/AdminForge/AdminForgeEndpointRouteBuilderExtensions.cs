using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace AdminForge;

/// <summary>
/// Endpoint-routing extensions for mounting AdminForge into a host application.
/// Thin facade over <see cref="AdminForge.Middleware.AdminForgeEndpointRouteBuilderExtensions.MapAdminForge"/>
/// so consumers can keep the two-line registration (<c>builder.AddAdminForge…</c> +
/// <c>app.MapAdminForge()</c>) without referencing the Middleware project directly.
/// </summary>
public static class AdminForgeEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Mounts AdminForge at the configured prefix.
    /// </summary>
    public static IEndpointConventionBuilder MapAdminForge(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return AdminForge.Middleware.AdminForgeEndpointRouteBuilderExtensions.MapAdminForge(
            endpoints
        );
    }
}
