using System.Security.Claims;
using AdminForge.Core.Configuration;
using AdminForge.Middleware.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdminForge.UnitTests.Authorization;

public class AdminPolicyProviderTests
{
    private static AdminPolicyProvider Build(
        string? umbrella = null,
        Action<AuthorizationOptions>? configure = null
    )
    {
        var authzOptions = new AuthorizationOptions();
        configure?.Invoke(authzOptions);
        var wrapped = Options.Create(authzOptions);
        var forgeOptions = new AdminForgeOptions { AuthorizationPolicy = umbrella };
        return new AdminPolicyProvider(wrapped, forgeOptions);
    }

    [Fact]
    public async Task Returns_Null_For_Non_AdminForge_Policy()
    {
        var provider = Build();
        var policy = await provider.GetPolicyAsync("Random:Policy");
        Assert.Null(policy);
    }

    [Fact]
    public async Task Materialises_Permissive_Policy_When_No_Umbrella()
    {
        var provider = Build();
        var policy = await provider.GetPolicyAsync("AdminForge:User:Read");
        Assert.NotNull(policy);
        // Permissive: an authorization-service evaluation should succeed for any principal.
        var authService = BuildAuthorizationService(provider);
        var result = await authService.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            null,
            "AdminForge:User:Read"
        );
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Explicit_Policy_Wins_Over_Default()
    {
        var provider = Build(configure: o =>
            o.AddPolicy("AdminForge:User:Delete", p => p.RequireAssertion(_ => false))
        );
        var policy = await provider.GetPolicyAsync("AdminForge:User:Delete");
        Assert.NotNull(policy);
        var authService = BuildAuthorizationService(provider);
        var result = await authService.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            null,
            "AdminForge:User:Delete"
        );
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Inherits_Umbrella_Policy_Requirements()
    {
        var provider = Build(
            umbrella: "AdminAccess",
            configure: o => o.AddPolicy("AdminAccess", p => p.RequireClaim("role", "admin"))
        );
        var policy = await provider.GetPolicyAsync("AdminForge:Tag:Update");
        Assert.NotNull(policy);
        Assert.Contains(policy!.Requirements, r => r is ClaimsAuthorizationRequirement);

        var authService = BuildAuthorizationService(provider);
        var unauthorized = await authService.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            null,
            "AdminForge:Tag:Update"
        );
        Assert.False(unauthorized.Succeeded);

        var identity = new ClaimsIdentity(new[] { new Claim("role", "admin") }, "test");
        var authorized = await authService.AuthorizeAsync(
            new ClaimsPrincipal(identity),
            null,
            "AdminForge:Tag:Update"
        );
        Assert.True(authorized.Succeeded);
    }

    private static IAuthorizationService BuildAuthorizationService(
        IAuthorizationPolicyProvider provider
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton(provider);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }
}
