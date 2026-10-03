using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
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
        var audit = new AuditLogStore();
        audit.Record(new AuditEvent { EntityType = "Probe", Action = AuditAction.Custom });
        builder.Services.AddSingleton(audit);
        builder.Services.AddAdminForgeDataProvider<AuditLogEntry, AuditLogDataProvider>();
        builder.Services.AddAdminForge(f =>
            f.AllowAnonymousAccess().AddTable<AuditLogEntry>().AddTable<Unserved>()
        );
        using var app = builder.Build();
        Middleware.AdminForgeEndpointRouteBuilderExtensions.GuardAuthorizationIsConfigured(
            app.Services
        );

        using var scope = app.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();

        var log = await bridge.ListAsync(
            bridge.FindEntityByRouteName("AuditLogEntry")!,
            new ListQuery()
        );
        Assert.Equal("Probe", Assert.Single(log.Rows).Values["EntityType"]);

        var unserved = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bridge.ListAsync(bridge.FindEntityByRouteName("Unserved")!, new ListQuery())
        );
        Assert.Contains("AddAdminForgeDataProvider<Unserved", unserved.Message);
    }

    [Fact]
    public void Mounting_Refuses_A_Table_Nobody_Serves()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAdminForge(f => f.AllowAnonymousAccess().AddTable<AuditLogEntry>());
        using var app = builder.Build();

        var failure = Assert.Throws<InvalidOperationException>(() => app.MapAdminForge());
        Assert.Contains("AuditLogEntry", failure.Message);
        Assert.Contains("AddAdminForgeDataProvider<AuditLogEntry", failure.Message);
    }
}

public sealed class Unserved
{
    public int Id { get; set; }
}
