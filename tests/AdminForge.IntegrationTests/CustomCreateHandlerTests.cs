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
/// End-to-end behaviour of <c>EntityBuilder&lt;T&gt;.OnCreate(...)</c>: a registered
/// custom create handler intercepts the bridge's create path entirely. Verifies
/// both <see cref="CreateResult.Success"/> (audit + encoded id) and
/// <see cref="CreateResult.Failure"/> (no audit, <see cref="EntityCreateFailedException"/>),
/// plus the regression case where an entity with no handler still goes through
/// the data-provider path.
/// </summary>
public class CustomCreateHandlerTests : IClassFixture<CustomCreateTodoAppFactory>
{
    private readonly CustomCreateTodoAppFactory _factory;

    public CustomCreateHandlerTests(CustomCreateTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Custom_Create_Success_Returns_Encoded_Id_And_Emits_Audit()
    {
        _factory.AuditSink.Events.Clear();

        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var userMeta = bridge.FindEntityByRouteName("User")!;
        Assert.NotNull(userMeta.CustomCreateHandler);

        var edit = bridge.NewEditModel(userMeta);
        edit.Values["DisplayName"] = "  Sue  "; // tests handler-side trim
        edit.Values["Email"] = "sue@example.test";

        var newKey = await bridge.CreateAsync(userMeta, edit);
        Assert.False(string.IsNullOrEmpty(newKey));

        // Verify the row was persisted with the business-logic transformations
        // applied by the handler (trim + server-stamped CreatedAt).
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Users.SingleAsync(u => u.Email == "sue@example.test");
        Assert.Equal("Sue", persisted.DisplayName);
        Assert.NotEqual(default, persisted.CreatedAt);
        Assert.Equal(Uri.EscapeDataString(persisted.Id.ToString()), newKey);

        // Audit fires from the bridge (data provider is bypassed).
        var auditEvent = Assert.Single(_factory.AuditSink.Events);
        Assert.Equal(AuditAction.Create, auditEvent.Action);
        Assert.Equal(nameof(User), auditEvent.EntityType);
        Assert.Equal(newKey, auditEvent.EntityId);
        Assert.True(auditEvent.ChangedValues.ContainsKey(nameof(User.Email)));
    }

    [Fact]
    public async Task Custom_Create_Failure_Throws_And_Emits_No_Audit()
    {
        _factory.AuditSink.Events.Clear();

        // Seed a user via the data layer so the handler's duplicate-email check trips.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { DisplayName = "Dup", Email = "dup@example.test" });
            await db.SaveChangesAsync();
        }
        _factory.AuditSink.Events.Clear();

        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var userMeta = bridge.FindEntityByRouteName("User")!;
        var edit = bridge.NewEditModel(userMeta);
        edit.Values["DisplayName"] = "Other";
        edit.Values["Email"] = "dup@example.test";

        var ex = await Assert.ThrowsAsync<EntityCreateFailedException>(() =>
            bridge.CreateAsync(userMeta, edit)
        );
        Assert.Contains("dup@example.test", ex.Message);
        Assert.Equal(nameof(User), ex.EntityName);

        // No audit event (no row was created from the library's POV).
        Assert.Empty(_factory.AuditSink.Events);

        // Confirm no second row landed.
        using var verifyScope = _factory.Services.CreateScope();
        var dbVerify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await dbVerify.Users.CountAsync(u => u.Email == "dup@example.test"));
    }

    [Fact]
    public async Task Entity_Without_Custom_Handler_Uses_Data_Provider_Path()
    {
        _factory.AuditSink.Events.Clear();

        // Seed prerequisites: a user + list (FK target).
        int listId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = new User { DisplayName = "Owner", Email = $"owner-{Guid.NewGuid():N}@x.y" };
            db.Users.Add(owner);
            await db.SaveChangesAsync();
            var list = new TodoList { Name = "Regression", OwnerId = owner.Id };
            db.TodoLists.Add(list);
            await db.SaveChangesAsync();
            listId = list.Id;
        }
        // Clear any audit produced by the data-provider seeding above (none, but be safe).
        _factory.AuditSink.Events.Clear();

        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var todoMeta = bridge.FindEntityByRouteName("Todo")!;
        Assert.Null(todoMeta.CustomCreateHandler);

        var edit = bridge.NewEditModel(todoMeta);
        edit.Values["Title"] = "Regression";
        edit.Values["TodoListId"] = listId;

        var newKey = await bridge.CreateAsync(todoMeta, edit);
        Assert.False(string.IsNullOrEmpty(newKey));

        // Data provider emits its own audit on the legacy path.
        var auditEvent = Assert.Single(_factory.AuditSink.Events);
        Assert.Equal(AuditAction.Create, auditEvent.Action);
        Assert.Equal(nameof(Todo), auditEvent.EntityType);
    }
}

/// <summary>
/// Test host that registers <c>OnCreate</c> on <see cref="User"/> (success +
/// failure paths) and routes audit events into a capturing sink.
/// </summary>
public class CustomCreateTodoAppFactory : WebApplicationFactory<Program>
{
    public CapturingAuditSink AuditSink { get; } = new();

    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-custom-create-{Guid.NewGuid():N}.db"
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
                b.WithTitle("Custom Create Test")
                    .AddTable<User>(e =>
                        e.OnCreate(
                            async (csp, user, _, ct) =>
                            {
                                var db = csp.GetRequiredService<AppDbContext>();
                                var exists = await db.Users.AnyAsync(
                                    u => u.Email == user.Email,
                                    ct
                                );
                                if (exists)
                                    return CreateResult.Error(
                                        $"Email '{user.Email}' is already registered."
                                    );

                                user.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                                    ? user.Email.Split('@')[0]
                                    : user.DisplayName.Trim();
                                user.CreatedAt = DateTime.UtcNow;

                                db.Users.Add(user);
                                await db.SaveChangesAsync(ct);
                                return CreateResult.Ok(user.Id);
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
