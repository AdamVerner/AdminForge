using System.Net;
using System.Text.Encodings.Web;
using AdminForge.Core.Configuration;
using AdminForge.DataAccess.EfCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// The umbrella policy sits on the panel's endpoints, so the host's authentication scheme decides
/// what a rejected request gets — the same way it would for any other endpoint.
/// </summary>
public class UmbrellaPolicyTests
    : IClassFixture<DenyAllPolicyTodoAppFactory>,
        IClassFixture<CookieGatedTodoAppFactory>
{
    private readonly DenyAllPolicyTodoAppFactory _denyAll;
    private readonly CookieGatedTodoAppFactory _cookie;

    public UmbrellaPolicyTests(
        DenyAllPolicyTodoAppFactory denyAll,
        CookieGatedTodoAppFactory cookie
    )
    {
        _denyAll = denyAll;
        _cookie = cookie;
    }

    [Fact]
    public async Task Anonymous_Gets_401_When_Umbrella_Policy_Rejects()
    {
        var client = _denyAll.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        var response = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Is_Redirected_To_The_Cookie_Schemes_Login_Page()
    {
        var client = _cookie.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        var response = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/login", response.Headers.Location!.ToString());
    }
}

/// <summary>TodoApp with the demo policy replaced by one that admits nobody.</summary>
public abstract class GatedTodoAppFactory : WebApplicationFactory<Program>
{
    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-gated-{Guid.NewGuid():N}.db"
    );

    protected abstract void ConfigureAuthentication(IServiceCollection services);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services =>
        {
            ConfigureAuthentication(services);

            ServiceCollectionExtensionsForTests.RemoveAll<AdminForgeOptions>(services);
            services.AddSingleton(sp =>
            {
                using var scope = sp.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scanner = sp.GetRequiredService<EfCoreReflectionScanner>();
                var b = new AdminForgeBuilder(scanner.Scan(context));
                b.WithTitle("Locked")
                    .RequireAuthorizationPolicy("AdminForge.Demo")
                    .AddTable<User>();
                return b.Build();
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch { }
    }
}

public class DenyAllPolicyTodoAppFactory : GatedTodoAppFactory
{
    protected override void ConfigureAuthentication(IServiceCollection services)
    {
        services
            .AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, NeverAuthenticatesHandler>("Test", _ => { });
        services.AddAuthorization(o =>
            o.AddPolicy("AdminForge.Demo", p => p.RequireAssertion(_ => false))
        );
    }

    /// <summary>The stock challenge: a bare 401, as a bearer scheme would answer.</summary>
    private sealed class NeverAuthenticatesHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}

public class CookieGatedTodoAppFactory : GatedTodoAppFactory
{
    protected override void ConfigureAuthentication(IServiceCollection services)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o => o.LoginPath = "/login");
        services.AddAuthorization(o =>
            o.AddPolicy("AdminForge.Demo", p => p.RequireAuthenticatedUser())
        );
    }
}
