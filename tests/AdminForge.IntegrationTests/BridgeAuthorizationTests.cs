using System.Security.Claims;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// Exercises the bridge-level integration of <see cref="IAdminAuthorizationPolicy"/>:
/// when the policy denies an action, the bridge throws <see cref="AdminForbiddenException"/>
/// before the data layer is hit. Custom policy is registered via the host's DI container.
/// </summary>
public class BridgeAuthorizationTests : IClassFixture<DenyingTodoAppFactory>
{
    private readonly DenyingTodoAppFactory _factory;

    public BridgeAuthorizationTests(DenyingTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Delete_Throws_When_Policy_Denies()
    {
        // Seed a row to delete.
        int todoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var user = new User { DisplayName = "U", Email = "u@x.y" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var list = new TodoList { Name = "L", OwnerId = user.Id };
            db.TodoLists.Add(list);
            await db.SaveChangesAsync();
            var t = new Todo { Title = "Doomed", TodoListId = list.Id };
            db.Todos.Add(t);
            await db.SaveChangesAsync();
            todoId = t.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
            var todoMeta = bridge.FindEntityByRouteName("Todo")!;

            await Assert.ThrowsAsync<AdminForbiddenException>(
                () => bridge.DeleteAsync(todoMeta, todoId.ToString())
            );

            // Read path still works (the policy denies only mutations).
            var listVM = await bridge.ListAsync(todoMeta, new ListQuery { PageSize = 50 });
            Assert.True(listVM.TotalCount >= 1);
        }
    }

    [Fact]
    public async Task Update_Throws_When_Policy_Denies()
    {
        int todoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var user = new User { DisplayName = "U2", Email = "u2@x.y" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var list = new TodoList { Name = "L2", OwnerId = user.Id };
            db.TodoLists.Add(list);
            await db.SaveChangesAsync();
            var t = new Todo { Title = "Locked", TodoListId = list.Id };
            db.Todos.Add(t);
            await db.SaveChangesAsync();
            todoId = t.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
            var todoMeta = bridge.FindEntityByRouteName("Todo")!;
            var edit = (await bridge.LoadForEditAsync(todoMeta, todoId.ToString()))!;
            edit.Values["Title"] = "should not stick";

            await Assert.ThrowsAsync<AdminForbiddenException>(
                () => bridge.UpdateAsync(todoMeta, edit)
            );
        }
    }
}

/// <summary>
/// Replaces the default <see cref="AllowAllAuthorizationPolicy"/> with one that
/// denies <c>Update</c>/<c>Delete</c> for every entity.
/// </summary>
public class DenyingTodoAppFactory : WebApplicationFactory<Program>
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
            services.AddSingleton<IAdminAuthorizationPolicy, DenyMutations>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (File.Exists(DbPath)) File.Delete(DbPath); } catch { /* best-effort */ }
    }

    private sealed class DenyMutations : IAdminAuthorizationPolicy
    {
        public Task<bool> IsAuthorizedAsync(
            string entityName,
            AdminAction action,
            ClaimsPrincipal user,
            object? instance = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(action == AdminAction.Read);
    }
}
