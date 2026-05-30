using System.Net;
using AdminForge.Core.Configuration;
using AdminForge.DataAccess.EfCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// Verifies that an umbrella authorization policy actually gates AdminForge endpoints.
/// </summary>
public class UmbrellaPolicyTests : IClassFixture<DenyAllPolicyTodoAppFactory>
{
    private readonly DenyAllPolicyTodoAppFactory _factory;

    public UmbrellaPolicyTests(DenyAllPolicyTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_Gets_401_When_Umbrella_Policy_Rejects()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin");
        // Anonymous principal → 401 per AdminForgeMiddleware behaviour.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public class DenyAllPolicyTodoAppFactory : WebApplicationFactory<Program>
{
    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-deny-{Guid.NewGuid():N}.db"
    );

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services =>
        {
            // Override the umbrella policy to a deny-all assertion.
            services.AddAuthorization(o =>
            {
                o.AddPolicy("AdminForge.Demo", p => p.RequireAssertion(_ => false));
            });

            // Rebuild AdminForgeOptions with the policy configured.
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
        try { if (File.Exists(DbPath)) File.Delete(DbPath); } catch { }
    }
}
