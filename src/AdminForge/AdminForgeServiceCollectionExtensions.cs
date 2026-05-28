using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

/// <summary>
/// Host-side composition root for AdminForge. Phase 0 wires only the options object;
/// real services (data provider, audit sink, auth policies, UI bridge) are registered in later phases.
/// </summary>
public static class AdminForgeServiceCollectionExtensions
{
    public static IServiceCollection AddAdminForge(
        this IServiceCollection services,
        Action<AdminForgeOptions>? configure = null
    )
    {
        var options = new AdminForgeOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        return services;
    }
}
