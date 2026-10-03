using AdminForge.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdminForge.Bridge;

public static class BridgeServiceCollectionExtensions
{
    /// <summary>The operations over the registered panel and the caller they run as: the operation user, else the circuit user, else the request user.</summary>
    public static IServiceCollection AddAdminForgeBridge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAdminUIBridge, AdminBridge>();
        services.AddHttpContextAccessor();
        services.AddScoped<OperationUserAccessor>();
        services.TryAddScoped<IUserAccessor, CurrentUserAccessor>();
        return services;
    }
}
