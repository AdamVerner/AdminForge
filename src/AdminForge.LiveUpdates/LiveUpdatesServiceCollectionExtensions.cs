using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdminForge.LiveUpdates;

/// <summary>
/// DI registration helpers for AdminForge live updates.
/// </summary>
public static class LiveUpdatesServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="ILiveSourceRegistry"/>. Called by the
    /// AdminForge meta-package; consumers normally don't invoke this directly.
    /// </summary>
    public static IServiceCollection AddAdminForgeLiveUpdates(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Registry is singleton — explicit factory so we hand it the root provider rather
        // than a scoped one (a singleton resolved from a scope still gets the scope's
        // provider through DI parameter injection, which causes "scoped service captured
        // by a singleton" issues for any scoped fetch the user wires up).
        services.TryAddSingleton<ILiveSourceRegistry>(sp => new LiveSourceRegistry(sp));
        return services;
    }
}
