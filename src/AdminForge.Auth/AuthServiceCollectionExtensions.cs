using AdminForge.Core.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.Auth;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// The panel signs its admins in itself: a cookie scheme, a login page over
    /// <typeparamref name="TValidator"/>, and sign-out. The panel's umbrella policy becomes the
    /// signed-in policy, so a host calls nothing else on the builder for authorization.
    /// </summary>
    public static IServiceCollection AddAdminForgeAuth<TValidator>(
        this IServiceCollection services,
        Action<CookieAuthenticationOptions>? configure = null
    )
        where TValidator : class, IAdminCredentialValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAdminCredentialValidator, TValidator>();
        services
            .AddAuthentication()
            .AddCookie(
                AdminForgeAuth.Scheme,
                o =>
                {
                    o.Cookie.Name = "adminforge";
                    o.LoginPath = AdminForgeAuth.LoginPath;
                    o.ExpireTimeSpan = TimeSpan.FromHours(12);
                    o.SlidingExpiration = true;
                    configure?.Invoke(o);
                }
            );
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                AdminForgeAuth.Policy,
                p => p.AddAuthenticationSchemes(AdminForgeAuth.Scheme).RequireAuthenticatedUser()
            );
        services.AddAntiforgery();
        services.AddSingleton(
            new AdminForgeContribution(forge =>
                forge
                    .RequireAuthorizationPolicy(AdminForgeAuth.Policy)
                    .WithSignOut(AdminForgeAuth.LogoutPath)
            )
        );
        return services;
    }
}
