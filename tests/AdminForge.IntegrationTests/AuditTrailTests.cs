using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.Core.ViewModels;
using AdminForge.DataAccess.EfCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// Exercises the data provider through the running host's DI graph and asserts the
/// audit sink fires once per mutation.
/// </summary>
public class AuditTrailTests : IClassFixture<AuditableTodoAppFactory>
{
    private readonly AuditableTodoAppFactory _factory;

    public AuditTrailTests(AuditableTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_Update_Delete_Each_Fire_Audit_Events()
    {
        _factory.AuditSink.Events.Clear();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Seed minimal data.
        var alice = new User { DisplayName = "Alice", Email = "a@x.test" };
        db.Users.Add(alice);
        await db.SaveChangesAsync();
        var list = new TodoList { Name = "Test List", OwnerId = alice.Id };
        db.TodoLists.Add(list);
        await db.SaveChangesAsync();

        var provider = scope.ServiceProvider.GetRequiredService<IAdminDataProvider<Todo>>();

        var created = await provider.CreateAsync(new Todo
        {
            Title = "Walk the dog",
            TodoListId = list.Id,
        });

        created.Title = "Walk the puppy";
        await provider.UpdateAsync(created);

        Assert.True(await provider.DeleteAsync([created.Id]));

        Assert.Equal(3, _factory.AuditSink.Events.Count);
        Assert.Equal(AuditAction.Create, _factory.AuditSink.Events[0].Action);
        Assert.Equal(AuditAction.Update, _factory.AuditSink.Events[1].Action);
        Assert.Equal(AuditAction.Delete, _factory.AuditSink.Events[2].Action);

        var update = _factory.AuditSink.Events[1];
        Assert.Contains(nameof(Todo.Title), update.ChangedValues.Keys);
        Assert.Equal("Walk the dog", update.ChangedValues[nameof(Todo.Title)].OldValue);
        Assert.Equal("Walk the puppy", update.ChangedValues[nameof(Todo.Title)].NewValue);
    }
}

public class AuditableTodoAppFactory : WebApplicationFactory<Program>
{
    public CapturingAuditSink AuditSink { get; } = new();

    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-audit-{Guid.NewGuid():N}.db"
    );

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services =>
        {
            // Replace AdminForgeOptions singleton with one that uses our capturing sink.
            services.RemoveAll<AdminForgeOptions>();
            services.AddSingleton(sp =>
            {
                using var scope = sp.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scanner = sp.GetRequiredService<EfCoreReflectionScanner>();
                var scanned = scanner.Scan(context);

                var b = new AdminForgeBuilder(scanned);
                b.WithTitle("Todo Admin Test")
                    .AddTable<User>()
                    .AddTable<TodoList>()
                    .AddTable<Todo>()
                    .AddTable<Tag>()
                    .WithAuditLog(AuditSink);
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

internal static class ServiceCollectionExtensionsForTests
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        var toRemove = services.Where(s => s.ServiceType == typeof(T)).ToList();
        foreach (var d in toRemove) services.Remove(d);
    }
}
