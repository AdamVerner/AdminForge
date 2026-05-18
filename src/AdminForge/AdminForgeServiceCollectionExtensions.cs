using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

public static class AdminForgeServiceCollectionExtensions
{
    public static IServiceCollection AddAdminForge(this IServiceCollection services, Action<AdminForgeOptions>? configure = null)
    {
        var options = new AdminForgeOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        return services;
    }

    public static IApplicationBuilder UseAdminForge(this IApplicationBuilder app)
    {
        return app;
    }
}
