using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.DataAccess.EfCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// Verifies auto-generated and explicit <c>RelatedLinkMeta</c> entries surface on
/// <c>EntityViewVM.RelatedLinks</c> with the right filter dictionary, and that
/// <c>HideRelatedLink</c> suppresses them.
/// </summary>
public class RelatedLinkTests : IClassFixture<RelatedLinkTodoAppFactory>
{
    private readonly RelatedLinkTodoAppFactory _factory;

    public RelatedLinkTests(RelatedLinkTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TodoList_View_Surfaces_Auto_Link_For_Todos_Collection()
    {
        int todoListId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var u = new User { DisplayName = "U", Email = "u@x.y" };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            var list = new TodoList { Name = "L", OwnerId = u.Id };
            db.TodoLists.Add(list);
            await db.SaveChangesAsync();
            db.Todos.AddRange(
                new Todo { Title = "T1", TodoListId = list.Id },
                new Todo { Title = "T2", TodoListId = list.Id }
            );
            await db.SaveChangesAsync();
            todoListId = list.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
            var meta = bridge.FindEntityByRouteName("TodoList")!;
            var view = (await bridge.FindAsync(meta, todoListId.ToString()))!;
            var link = view.RelatedLinks.FirstOrDefault(l => l.RouteName == "Todo");
            Assert.NotNull(link);
            Assert.Contains("2", link!.Label); // "View 2 Todo"
            Assert.Equal(todoListId, link.Filter["TodoListId"]);
        }
    }

    [Fact]
    public async Task User_View_Excludes_Hidden_TodoLists_Auto_Link()
    {
        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var u = new User { DisplayName = "Hider", Email = "h@x.y" };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            userId = u.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
            var meta = bridge.FindEntityByRouteName("User")!;
            var view = (await bridge.FindAsync(meta, userId.ToString()))!;
            // We hid TodoLists; auto-link to TodoList must not appear.
            Assert.DoesNotContain(view.RelatedLinks, l => l.RouteName == "TodoList");
            // The explicit RelatedLink<Todo> "Active" must appear with filter on AssigneeId.
            var explicitLink = view.RelatedLinks.FirstOrDefault(l => l.Label == "Active");
            Assert.NotNull(explicitLink);
            Assert.Equal(userId, explicitLink!.Filter["AssigneeId"]);
        }
    }
}

public class RelatedLinkTodoAppFactory : WebApplicationFactory<Program>
{
    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-related-{Guid.NewGuid():N}.db"
    );

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AdminForgeOptions>();
            services.AddSingleton(sp =>
            {
                using var scope = sp.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scanner = sp.GetRequiredService<EfCoreReflectionScanner>();
                var scanned = scanner.Scan(ctx);

                var b = new AdminForgeBuilder(scanned);
                b.WithTitle("Related Tests")
                    .AddTable<User>(e =>
                        e.HideRelatedLink(u => u.TodoLists)
                            .RelatedLink<Todo>(
                                "Active",
                                source => target => target.AssigneeId == source.Id
                            )
                    )
                    .AddTable<TodoList>()
                    .AddTable<Todo>()
                    .AddTable<Tag>();
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
