using AdminForge.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TodoApp;

namespace AdminForge.IntegrationTests;

/// <summary>
/// A host with no DbContext registers its tables as provider-backed types, and one that names a
/// type nobody serves is told which registration is missing.
/// </summary>
public class HostWithoutDbContextTests
{
    [Fact]
    public async Task Provider_Backed_Tables_Are_Served_And_An_Unserved_One_Names_The_Fix()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<SiteSettingsStore>();
        builder.Services.AddAdminForgeDataProvider<SiteSettings, SiteSettingsDataProvider>();
        builder.Services.AddAdminForge(f =>
            f.AllowAnonymousAccess().AddTable<SiteSettings>().AddTable<AuditLogEntry>()
        );
        using var app = builder.Build();
        Middleware.AdminForgeEndpointRouteBuilderExtensions.GuardAuthorizationIsConfigured(
            app.Services
        );

        using var scope = app.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();

        var settings = await bridge.ListAsync(
            bridge.FindEntityByRouteName("SiteSettings")!,
            new ListQuery()
        );
        Assert.Equal("Welcome to Todo Admin!", Assert.Single(settings.Rows).Values["WelcomeMessage"]);

        var unserved = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bridge.ListAsync(bridge.FindEntityByRouteName("AuditLogEntry")!, new ListQuery())
        );
        Assert.Contains("AddAdminForgeDataProvider<AuditLogEntry", unserved.Message);
    }
}
