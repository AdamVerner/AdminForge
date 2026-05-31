using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// Verifies the dashboard page is reachable and emits widget titles into the
/// server-rendered Blazor HTML. We don't drive the interactive circuit — the
/// initial render is enough to prove routing, materialisation, and grid layout
/// are wired correctly.
/// </summary>
public class DashboardPageTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _factory;

    public DashboardPageTests(TodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Operations_Dashboard_Renders_Widgets()
    {
        // Seed enough rows that the open-todos counter is non-zero.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var user = new User { DisplayName = "Test", Email = "t@example.com" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var list = new TodoList { Name = "Inbox", OwnerId = user.Id };
            db.TodoLists.Add(list);
            await db.SaveChangesAsync();
            db.Todos.Add(new Todo { Title = "Pay rent", TodoListId = list.Id });
            db.Todos.Add(
                new Todo
                {
                    Title = "Old task",
                    TodoListId = list.Id,
                    Status = TodoStatus.Done,
                    CompletedAt = DateTime.UtcNow.AddDays(-1),
                }
            );
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/admin/dashboards/operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Operations", body);
        Assert.Contains("Open Todos", body);
        Assert.Contains("Completion %", body);
        Assert.Contains("Completed per day", body);
        Assert.Contains("Recent Todos", body);
        // The seeded recent todo should leak into the rendered table widget.
        Assert.Contains("Pay rent", body);
    }

    [Fact]
    public async Task Unknown_Dashboard_Returns_404_Body()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/admin/dashboards/does-not-exist");
        // Page exists (200) but renders an "Unknown dashboard" error inline.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unknown dashboard", body);
    }
}
