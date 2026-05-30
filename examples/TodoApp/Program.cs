using System.Text.Json.Serialization;
using AdminForge;
using AdminForge.Core.Metadata;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    // Entity graphs are cyclic (Todo.Tags <-> Tag.Todos, etc.); avoid serializer blowing up.
    o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

var connectionString =
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=todoapp.db";

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

// Allow-all umbrella policy for the demo so anonymous browsing works.
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminForge.Demo", p => p.RequireAssertion(_ => true));
});

builder.Services.AddAdminForge<AppDbContext>(forge =>
    forge
        .WithTitle("Todo Admin")
        // Route prefix is pinned to "admin" in Phase 3 — see AdminForgeBuilder.WithRoutePrefix.
        .RequireAuthorizationPolicy("AdminForge.Demo")
        .WithAuditLog((evt, _) =>
        {
            Console.WriteLine(
                $"[audit] {evt.Timestamp:O} {evt.Action} {evt.EntityType}#{evt.EntityId} by {evt.User ?? "anonymous"} "
                + $"({evt.ChangedValues.Count} change(s))"
            );
            return Task.CompletedTask;
        })
        .AddTable<User>(e => e
            .Nav(n => n.Group("People").Order(1))
            .DisplayMember(u => u.DisplayName)
        )
        .AddTable<TodoList>(e => e.Nav(n => n.Group("Work").Order(1).Label("Lists")))
        .AddTable<Todo>(e => e
            .Nav(n => n.Group("Work").Order(2).Label("Tasks"))
            .Column(t => t.Title, c => c
                .Label("Headline")
                .Description("Short summary visible in lists.")
                .Validate(v => v is string s && s.Length >= 3, "Title must be at least 3 characters.")
            )
        )
        .AddTable<Tag>(e => e.Nav(n => n.Group("Work").Order(3)))
        .AddDashboard("operations", d => d
            .WithTitle("Operations")
            .Nav(n => n.Group("Overview").Order(0).Label("Operations"))
            .AddStatCard(
                "Open Todos",
                async (IServiceProvider sp, CancellationToken ct) =>
                {
                    var db = sp.GetRequiredService<AppDbContext>();
                    return (object?)await db.Todos.CountAsync(
                        t => t.Status != TodoStatus.Done && t.Status != TodoStatus.Cancelled,
                        ct
                    );
                },
                suffix: "open"
            )
            .AddStatCard(
                "Completion %",
                async (IServiceProvider sp, CancellationToken ct) =>
                {
                    var db = sp.GetRequiredService<AppDbContext>();
                    var total = await db.Todos.CountAsync(ct);
                    if (total == 0) return (object?)0d;
                    var done = await db.Todos.CountAsync(t => t.Status == TodoStatus.Done, ct);
                    return (object?)((double)done * 100.0 / total);
                },
                suffix: "%"
            )
            .AddLineChart<DailyCompletion>(
                "Completed per day (14d)",
                async (IServiceProvider sp, CancellationToken ct) =>
                {
                    var db = sp.GetRequiredService<AppDbContext>();
                    var since = DateTime.UtcNow.Date.AddDays(-13);
                    var raw = await db
                        .Todos.Where(t => t.CompletedAt != null && t.CompletedAt >= since)
                        .Select(t => t.CompletedAt!.Value.Date)
                        .ToListAsync(ct);
                    // Bucket into a continuous 14-day window so the chart shows zero days too.
                    var byDate = raw.GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
                    var result = new List<DailyCompletion>(14);
                    for (var i = 0; i < 14; i++)
                    {
                        var day = since.AddDays(i);
                        byDate.TryGetValue(day, out var count);
                        result.Add(new DailyCompletion(day, count));
                    }
                    return (IReadOnlyList<DailyCompletion>)result;
                },
                xAxis: p => p.Day,
                yAxis: p => p.Count,
                yAxisLabel: "todos"
            )
            .AddTable<Todo>(t => t
                .WithTitle("Recent Todos")
                .WithColumns(x => x.Title, x => x.Status, x => x.Priority, x => x.CreatedAt)
                .OrderBy(x => x.CreatedAt, descending: true)
                .Take(10)
            )
            .Layout(layout => layout
                .Row(r => r.Add("Open Todos").Add("Completion %"))
                .Row(r => r.Add("Completed per day (14d)", fullWidth: true))
                .Row(r => r.Add("Recent Todos", fullWidth: true))
            )
        )
);

var app = builder.Build();

// Support `dotnet run --project examples/TodoApp -- seed` for one-shot DB setup.
if (args.Length > 0 && string.Equals(args[0], "seed", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
    Console.WriteLine("Database seeded.");
    return;
}

// Make sure the DB exists for normal runs too (no migrations in Phase 0).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseAuthorization();

app.MapAdminForge();

app.MapGet(
    "/todos",
    async (AppDbContext db) =>
        await db
            .Todos.Include(t => t.Assignee)
            .Include(t => t.TodoList)
            .Include(t => t.Tags)
            .AsNoTracking()
            .ToListAsync()
);

app.MapGet(
    "/todos/{id:int}",
    async (int id, AppDbContext db) =>
    {
        var todo = await db
            .Todos.Include(t => t.Assignee)
            .Include(t => t.TodoList)
            .Include(t => t.Tags)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
        return todo is null ? Results.NotFound() : Results.Ok(todo);
    }
);

app.MapPost(
    "/todos",
    async (CreateTodoRequest req, AppDbContext db) =>
    {
        var list = await db.TodoLists.FindAsync(req.TodoListId);
        if (list is null)
            return Results.BadRequest($"TodoList {req.TodoListId} not found.");

        var todo = new Todo
        {
            Title = req.Title,
            TodoListId = req.TodoListId,
            Priority = req.Priority ?? TodoPriority.Normal,
        };
        db.Todos.Add(todo);
        await db.SaveChangesAsync();
        return Results.Created($"/todos/{todo.Id}", todo);
    }
);

app.MapPut(
    "/todos/{id:int}/complete",
    async (int id, AppDbContext db) =>
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null)
            return Results.NotFound();
        todo.Status = TodoStatus.Done;
        todo.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
);

app.MapDelete(
    "/todos/{id:int}",
    async (int id, AppDbContext db) =>
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null)
            return Results.NotFound();
        db.Todos.Remove(todo);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
);

app.Run();

public sealed record CreateTodoRequest(string Title, int TodoListId, TodoPriority? Priority);

/// <summary>One bucket in the "completed per day" dashboard chart.</summary>
public sealed record DailyCompletion(DateTime Day, int Count);

// Required for WebApplicationFactory<TodoApp> in integration tests.
public partial class Program { }
