using System.Net;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodoApp.Data;

namespace AdminForge.IntegrationTests;

/// <summary>
/// Smoke tests for booting AdminForge via <see cref="WebApplicationFactory{TEntryPoint}"/>
/// against the TodoApp example.
/// </summary>
public class AdminPanelBootTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _factory;

    public AdminPanelBootTests(TodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Admin_Root_Returns_Blazor_Html()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Blazor", body); // server-rendered comments
        Assert.Contains("Todo Admin", body);
        Assert.Contains("background-color:#2e7d32", body);
        Assert.Contains(">local<", body);
    }

    [Fact]
    public async Task Entity_List_Page_Renders()
    {
        // Seed a known row through the host's DI graph so the page has something to show.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(
                new TodoApp.Entities.User { DisplayName = "Alice", Email = "alice@example.com" }
            );
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/admin/entities/User");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alice", body);
    }

    [Fact]
    public async Task User_List_Page_Only_Shows_Configured_Columns()
    {
        // List is opt-in. TodoApp configures Email, DisplayName, Role, CreatedAt — not Id.
        using (var scope = _factory.Services.CreateScope())
        {
            var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
            var userMeta = bridge.FindEntityByRouteName("User")!;
            var shown = userMeta
                .Columns.Where(c => c.ShowInList)
                .Select(c => c.PropertyName)
                .ToHashSet();
            Assert.Contains(nameof(TodoApp.Entities.User.Email), shown);
            Assert.Contains(nameof(TodoApp.Entities.User.DisplayName), shown);
            Assert.Contains(nameof(TodoApp.Entities.User.Role), shown);
            Assert.Contains(nameof(TodoApp.Entities.User.CreatedAt), shown);
            // Id is NOT opted in.
            Assert.DoesNotContain(nameof(TodoApp.Entities.User.Id), shown);
        }
    }
}

/// <summary>
/// Forks the TodoApp host so each test gets a fresh in-memory SQLite database and
/// can swap in an audit-capturing sink.
/// </summary>
public class TodoAppFactory : WebApplicationFactory<Program>
{
    public CapturingAuditSink AuditSink { get; } = new();

    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-{Guid.NewGuid():N}.db"
    );

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch
        { /* best-effort */
        }
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
    }
}

public sealed class CapturingAuditSink : IAuditSink
{
    public List<AuditEvent> Events { get; } = new();

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}
