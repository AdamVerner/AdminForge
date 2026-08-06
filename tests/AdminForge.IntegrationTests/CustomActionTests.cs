using System.Security.Claims;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// End-to-end integration of custom entity actions: handler runs in a fresh DI scope,
/// audit fires with <see cref="AuditAction.Custom"/>, and the action context flows
/// through to the handler. Denying policy short-circuits before the handler.
/// </summary>
public class CustomActionTests : IClassFixture<CustomActionTodoAppFactory>
{
    private readonly CustomActionTodoAppFactory _factory;

    public CustomActionTests(CustomActionTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task InvokeAction_Runs_Handler_And_Fires_Custom_Audit()
    {
        _factory.AuditSink.Events.Clear();
        _factory.Counter.Reset();

        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var user = new User { DisplayName = "Pinger", Email = "p@x.test" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
            var ctx = new StubActionContext();
            await bridge.InvokeActionAsync("User", userId.ToString(), "Ping", ctx);
            Assert.Equal(1, _factory.Counter.Count);

            var audit = Assert.Single(
                _factory.AuditSink.Events,
                e => e.Action == AuditAction.Custom
            );
            Assert.Equal("User", audit.EntityType);
            Assert.Equal(userId.ToString(), audit.EntityId);
            Assert.True(audit.ChangedValues.ContainsKey("ActionName"));
            Assert.Equal("Ping", audit.ChangedValues["ActionName"].NewValue);
        }
    }

    [Fact]
    public async Task InvokeAction_Throws_AdminForbidden_When_Policy_Denies()
    {
        // Swap in a denying policy through a forked factory so we don't pollute the shared one.
        await using var denyingFactory = new DenyingCustomActionFactory(
            _factory.Counter,
            _factory.AuditSink
        );
        denyingFactory.AuditSink.Events.Clear();
        denyingFactory.Counter.Reset();

        int userId;
        using (var scope = denyingFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var user = new User { DisplayName = "Denied", Email = "d@x.test" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        using (var scope = denyingFactory.Services.CreateScope())
        {
            var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
            var ctx = new StubActionContext();
            await Assert.ThrowsAsync<AdminForbiddenException>(() =>
                bridge.InvokeActionAsync("User", userId.ToString(), "Ping", ctx)
            );
            Assert.Equal(0, denyingFactory.Counter.Count);
        }
    }

    private sealed class StubActionContext : IActionContext
    {
        public Task<bool> ConfirmAsync(string message) => Task.FromResult(true);

        public void ShowSuccess(string message) { }

        public void ShowError(string message) { }

        public void NavigateTo(string url) { }

        public void Refresh() { }
    }
}

public sealed class InvocationCounter
{
    private int _count;
    public int Count => _count;

    public void Increment() => Interlocked.Increment(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);
}

/// <summary>
/// Default factory: replaces the seeded options with one carrying a Ping action that
/// bumps a shared counter, so the test can assert the handler ran.
/// </summary>
public class CustomActionTodoAppFactory : WebApplicationFactory<Program>
{
    public CapturingAuditSink AuditSink { get; } = new();
    public InvocationCounter Counter { get; } = new();

    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-action-{Guid.NewGuid():N}.db"
    );

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services =>
        {
            ConfigureForActions(services, Counter, AuditSink);
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

    internal static void ConfigureForActions(
        IServiceCollection services,
        InvocationCounter counter,
        CapturingAuditSink auditSink
    )
    {
        services.RemoveAll<AdminForgeOptions>();
        services.AddSingleton(sp =>
        {
            using var scope = sp.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var scanner = sp.GetRequiredService<EfCoreReflectionScanner>();
            var scanned = scanner.Scan(ctx);

            var b = new AdminForgeBuilder(scanned);
            b.WithTitle("Action Tests")
                .AllowAnonymousAccess()
                .WithAuditLog(auditSink)
                .AddTable<User>(e =>
                    e.AddAction(
                        "Ping",
                        (_, _, ctx) =>
                        {
                            counter.Increment();
                            return Task.CompletedTask;
                        }
                    )
                )
                .AddTable<TodoList>()
                .AddTable<Todo>()
                .AddTable<Tag>();
            return b.Build();
        });
    }
}

/// <summary>
/// Forked host that denies every <see cref="AdminAction.Custom"/> invocation. Reuses the
/// same registered action so we can prove the deny path short-circuits before the handler.
/// </summary>
public class DenyingCustomActionFactory : WebApplicationFactory<Program>
{
    public CapturingAuditSink AuditSink { get; }
    public InvocationCounter Counter { get; }

    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-action-deny-{Guid.NewGuid():N}.db"
    );

    public DenyingCustomActionFactory(InvocationCounter counter, CapturingAuditSink sink)
    {
        Counter = counter;
        AuditSink = sink;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services =>
        {
            CustomActionTodoAppFactory.ConfigureForActions(services, Counter, AuditSink);
            services.AddSingleton<IAdminAuthorizationPolicy, DenyCustom>();
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

    private sealed class DenyCustom : IAdminAuthorizationPolicy
    {
        public Task<bool> IsAuthorizedAsync(
            string entityName,
            AdminAction action,
            ClaimsPrincipal user,
            object? instance = null,
            string? actionName = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(action != AdminAction.Custom);
    }
}
