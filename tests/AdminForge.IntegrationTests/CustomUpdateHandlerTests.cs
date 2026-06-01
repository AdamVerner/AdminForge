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
/// End-to-end behaviour of <c>EntityBuilder&lt;T&gt;.OnUpdate(...)</c>: a registered
/// custom update handler intercepts the bridge's update path entirely. Verifies
/// success (audit with before/after diff, data provider NOT called), failure
/// (no audit, <see cref="EntityUpdateFailedException"/>, row unchanged), and
/// the regression where an entity without a handler still uses the data-provider
/// update path.
/// </summary>
public class CustomUpdateHandlerTests : IClassFixture<CustomUpdateTodoAppFactory>
{
    private readonly CustomUpdateTodoAppFactory _factory;

    public CustomUpdateHandlerTests(CustomUpdateTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Custom_Update_Success_Persists_And_Emits_Audit_With_Diff()
    {
        _factory.AuditSink.Events.Clear();

        // Seed.
        int userId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = new User { DisplayName = "Old", Email = "old@example.test" };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            userId = u.Id;
        }
        _factory.AuditSink.Events.Clear();

        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var userMeta = bridge.FindEntityByRouteName("User")!;
        Assert.NotNull(userMeta.CustomUpdateHandler);

        var edit = await bridge.LoadForEditAsync(userMeta, Uri.EscapeDataString(userId.ToString()));
        Assert.NotNull(edit);
        edit!.Values["DisplayName"] = "New";

        await bridge.UpdateAsync(userMeta, edit);

        // Row persisted.
        var verifyDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await verifyDb.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal("New", persisted.DisplayName);

        // Audit emitted, with the DisplayName diff (Old → New).
        var auditEvent = Assert.Single(_factory.AuditSink.Events);
        Assert.Equal(AuditAction.Update, auditEvent.Action);
        Assert.Equal(nameof(User), auditEvent.EntityType);
        Assert.True(auditEvent.ChangedValues.ContainsKey(nameof(User.DisplayName)));
        var diff = auditEvent.ChangedValues[nameof(User.DisplayName)];
        Assert.Equal("Old", diff.OldValue);
        Assert.Equal("New", diff.NewValue);
    }

    [Fact]
    public async Task Custom_Update_Failure_Throws_And_Emits_No_Audit_And_Row_Unchanged()
    {
        _factory.AuditSink.Events.Clear();

        int userId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { DisplayName = "Other", Email = "other@example.test" });
            var u = new User { DisplayName = "Self", Email = "self@example.test" };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            userId = u.Id;
        }
        _factory.AuditSink.Events.Clear();

        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var userMeta = bridge.FindEntityByRouteName("User")!;

        var edit = await bridge.LoadForEditAsync(userMeta, Uri.EscapeDataString(userId.ToString()));
        Assert.NotNull(edit);
        // Try to change Self's email to Other's — handler must reject.
        edit!.Values["Email"] = "other@example.test";

        var ex = await Assert.ThrowsAsync<EntityUpdateFailedException>(() =>
            bridge.UpdateAsync(userMeta, edit)
        );
        Assert.Contains("other@example.test", ex.Message);
        Assert.Equal(nameof(User), ex.EntityName);

        // No audit recorded.
        Assert.Empty(_factory.AuditSink.Events);

        // Row unchanged.
        var verifyDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await verifyDb.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal("self@example.test", persisted.Email);
    }

    [Fact]
    public async Task Entity_Without_Custom_Update_Handler_Uses_Data_Provider_Path()
    {
        _factory.AuditSink.Events.Clear();

        int todoId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = new User { DisplayName = "Own", Email = $"own-{Guid.NewGuid():N}@x.y" };
            db.Users.Add(owner);
            await db.SaveChangesAsync();
            var list = new TodoList { Name = "L", OwnerId = owner.Id };
            db.TodoLists.Add(list);
            await db.SaveChangesAsync();
            var todo = new Todo { Title = "First", TodoListId = list.Id };
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            todoId = todo.Id;
        }
        _factory.AuditSink.Events.Clear();

        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var todoMeta = bridge.FindEntityByRouteName("Todo")!;
        Assert.Null(todoMeta.CustomUpdateHandler);

        var edit = await bridge.LoadForEditAsync(todoMeta, Uri.EscapeDataString(todoId.ToString()));
        Assert.NotNull(edit);
        edit!.Values["Title"] = "Renamed";

        await bridge.UpdateAsync(todoMeta, edit);

        // The data-provider's UpdateAsync emits its own audit; that's what we expect here.
        var auditEvent = Assert.Single(_factory.AuditSink.Events);
        Assert.Equal(AuditAction.Update, auditEvent.Action);
        Assert.Equal(nameof(Todo), auditEvent.EntityType);
    }
}

/// <summary>
/// Test host that registers <c>OnUpdate</c> on <see cref="User"/> and routes audit
/// events into a capturing sink. Companion to <see cref="CustomCreateTodoAppFactory"/>.
/// </summary>
public class CustomUpdateTodoAppFactory : WebApplicationFactory<Program>
{
    public CapturingAuditSink AuditSink { get; } = new();

    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-custom-update-{Guid.NewGuid():N}.db"
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
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scanner = sp.GetRequiredService<EfCoreReflectionScanner>();
                var scanned = scanner.Scan(context);

                var b = new AdminForgeBuilder(scanned);
                b.WithTitle("Custom Update Test")
                    .AddTable<User>(e =>
                        e.OnUpdate(
                            async (csp, original, patched, _, ct) =>
                            {
                                var db = csp.GetRequiredService<AppDbContext>();
                                if (
                                    !string.Equals(
                                        original.Email,
                                        patched.Email,
                                        StringComparison.Ordinal
                                    )
                                )
                                {
                                    var conflict = await db.Users.AnyAsync(
                                        u => u.Email == patched.Email && u.Id != patched.Id,
                                        ct
                                    );
                                    if (conflict)
                                        return UpdateResult.Error(
                                            $"Email '{patched.Email}' is already registered."
                                        );
                                }
                                db.Users.Update(patched);
                                await db.SaveChangesAsync(ct);
                                return UpdateResult.Ok();
                            }
                        )
                    )
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
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch { }
    }
}
