using System.Security.Claims;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// <c>MapAdminForge()</c> must refuse to mount a panel that nobody gates. The panel is
/// full read/write over every registered entity, so an unconfigured host is a boot-time
/// error rather than something to discover in production.
/// </summary>
public class MountAuthorizationGuardTests
{
    [Fact]
    public void Mount_Throws_When_Host_Configured_No_Authorization()
    {
        using var app = BuildHost();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapAdminForge());
        Assert.Contains("refuses to mount without authorization", ex.Message);
        Assert.Contains("AllowAnonymousAccess", ex.Message);
    }

    [Fact]
    public void Mount_Throws_When_Host_Re_Registers_The_AllowAll_Default()
    {
        // Explicitly re-registering the stock allow-all policy is the tempting "fix" for a
        // deny-by-default panel; it must not count as configured authorization.
        using var app = BuildHost(services =>
            services.AddSingleton<IAdminAuthorizationPolicy, AllowAllAuthorizationPolicy>()
        );

        Assert.Throws<InvalidOperationException>(() => app.MapAdminForge());
    }

    /// <remarks>
    /// Calls the guard directly rather than <c>MapAdminForge()</c>: the rest of the mount
    /// path needs a static-web-assets manifest that a bare test host has no build step to
    /// produce. The throw cases above prove the guard runs first in the real entry point,
    /// and <see cref="AdminPanelBootTests"/> covers a full mount of a policy-gated host.
    /// </remarks>
    [Fact]
    public void Guard_Passes_With_Custom_Per_Action_Policy_Or_Explicit_Opt_Out()
    {
        using var withCustomPolicy = BuildHost(services =>
            services.AddSingleton<IAdminAuthorizationPolicy, DenyEverything>()
        );
        Middleware.AdminForgeEndpointRouteBuilderExtensions.GuardAuthorizationIsConfigured(
            withCustomPolicy.Services
        );

        using var withOptOut = BuildHost(forge: f => f.AllowAnonymousAccess());
        Middleware.AdminForgeEndpointRouteBuilderExtensions.GuardAuthorizationIsConfigured(
            withOptOut.Services
        );
    }

    private static WebApplication BuildHost(
        Action<IServiceCollection>? services = null,
        Action<AdminForgeBuilder>? forge = null
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        builder.Services.AddAdminForge<AppDbContext>(f =>
        {
            f.AddTable<User>();
            forge?.Invoke(f);
        });
        services?.Invoke(builder.Services);
        return builder.Build();
    }

    private sealed class DenyEverything : IAdminAuthorizationPolicy
    {
        public Task<bool> IsAuthorizedAsync(
            string entityName,
            AdminAction action,
            ClaimsPrincipal user,
            object? instance = null,
            string? actionName = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(false);
    }
}
