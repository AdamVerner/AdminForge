using AdminForge.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace AdminForge.UI.Blazor;

/// <summary>
/// MudBlazor / Blazor-specific service registrations. Lives in this project so
/// MudBlazor remains contained — the meta-package and Middleware project don't
/// take a direct MudBlazor dependency.
/// </summary>
public static class BlazorServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Blazor-side services needed by AdminForge's UI pages: MudBlazor
    /// providers, the renderer bridge, and the user accessor that proxies the
    /// current <c>HttpContext</c> principal.
    /// </summary>
    public static IServiceCollection AddAdminForgeBlazorUI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMudServices();
        services.AddScoped<IAdminUIBridge, BlazorUIBridge>();
        return services;
    }
}
