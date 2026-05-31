using System.Linq.Expressions;
using System.Text.Json.Serialization;
using AdminForge;
using Microsoft.EntityFrameworkCore;
using TodoApp;
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

// Live updates demo wiring: a shared streaming channel + background producers. The
// fluent dashboard builder needs an IAsyncEnumerable at registration time, so we
// instantiate the stream up-front and feed it into both DI (so the BackgroundService
// can resolve it) and the dashboard chart's WithStreaming(...) call.
var metricsTickStream = new MetricsTickStream();
builder.Services.AddSingleton(metricsTickStream);
builder.Services.AddHostedService<MetricsBackgroundService>();

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
        // Phase 6: exercise the theming hook end-to-end. Inline-SVG data URL avoids
        // shipping a separate asset file with the example; teal primary makes it
        // obvious at a glance that the configured palette is in effect.
        .WithTheme(theme =>
        {
            theme.LogoUrl =
                "data:image/svg+xml;utf8,"
                + "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='white'>"
                + "<path d='M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z'/></svg>";
            theme.LogoAlt = "Todo Admin";
            theme.PrimaryColor = "#00897b"; // teal
            theme.SecondaryColor = "#ff8a65"; // coral accent
        })
        .WithAuditLog(
            (evt, _) =>
            {
                Console.WriteLine(
                    $"[audit] {evt.Timestamp:O} {evt.Action} {evt.EntityType}#{evt.EntityId} by {evt.User ?? "anonymous"} "
                        + $"({evt.ChangedValues.Count} change(s))"
                );
                return Task.CompletedTask;
            }
        )
        .AddTable<User>(e =>
            e.Nav(n => n.Group("People").Order(1))
                .DisplayMember(u => u.DisplayName)
                // Phase 3.5: custom action — exercises ConfirmAsync + ShowSuccess + audit.
                .AddAction(
                    "Send Welcome Email",
                    async (sp, u, ctx) =>
                    {
                        if (!await ctx.ConfirmAsync($"Send to {u.Email}?"))
                            return;
                        // No real SMTP wired — this just exercises the surface.
                        ctx.ShowSuccess($"Welcome email sent to {u.DisplayName}.");
                    },
                    cfg => cfg.Icon("Email").Color("Primary").RequireConfirmation()
                )
                // Phase 3.5: cross-entity related link — pre-filters Todo list by FK.
                // Predicate decomposition is equality-only by design (target.Prop == source.X
                // entries map to URL filter dictionary keys); the "exclude Done" axis from the
                // plan text is left to the consumer's filter bar since `!=` cannot be encoded
                // as an exact-match filter.
                .RelatedLink<Todo>(
                    "Active Tasks",
                    source => target => target.AssigneeId == source.Id
                )
                // Phase 3.5: opt-out of the (low value) auto-link to TodoLists owned by this user.
                .HideRelatedLink(u => u.TodoLists)
        )
        .AddTable<TodoList>(e => e.Nav(n => n.Group("Work").Order(1).Label("Lists")))
        .AddTable<Todo>(e =>
            e.Nav(n => n.Group("Work").Order(2).Label("Tasks"))
                .Column(
                    t => t.Title,
                    c =>
                        c.Label("Headline")
                            .Description("Short summary visible in lists.")
                            .Validate(
                                v => v is string s && s.Length >= 3,
                                "Title must be at least 3 characters."
                            )
                )
                // Phase 3.5: source-side reference-nav LinkText override.
                // We pass the typed lambda as a LambdaExpression (Option A) since
                // ColumnBuilder doesn't know the related entity type at compile time.
                .Column(
                    t => t.Assignee,
                    c =>
                        c.LinkText(
                            (Expression<Func<User, string>>)(u => $"Owned by {u.DisplayName}")
                        )
                )
                // Phase 3.5: HideColumn on a low-value field.
                .HideColumn(t => t.CreatedAt)
                // Phase 5 (narrowed): live polling on the single-entity VIEW page only.
                // Visiting /admin/entities/Todo/{id} re-fetches the displayed row every 5s.
                .WithLivePolling(TimeSpan.FromSeconds(5))
        )
        .AddTable<Tag>(e =>
            e.Nav(n => n.Group("Work").Order(3))
                // Phase 3.5: custom computed column — projected server-side via the user's expression.
                .AddColumn<int>(
                    "TodoCount",
                    c => c.Label("# Todos").From(t => t.Todos.Count).Sortable()
                )
        )
        // Phase 4: generic form exercising every supported field kind. The submit
        // handler just logs + shows a snackbar via the IActionContext; the audit
        // sink captures the serialised values for inspection.
        .AddForm(
            "send-notification",
            form =>
                form.WithTitle("Send Notification")
                    .WithDescription(
                        "Demonstrates every form field type. Submission is logged to the console audit sink."
                    )
                    .Nav(n => n.Group("Tools").Order(1).Label("Send Notification"))
                    .AddField(f =>
                        f.Text("Title")
                            .Label("Title")
                            .Description("Headline shown in the inbox.")
                            .Required()
                    )
                    .AddField(f =>
                        f.Text("Body").Label("Body").Multiline().MaxLength(1000).Required()
                    )
                    .AddField(f =>
                        f.Markdown("RichBody").Label("Rich Body").Description("Markdown editor.")
                    )
                    .AddField(f => f.Number("Priority").Label("Priority").Min(0).Max(5))
                    .AddField(f => f.Float("AmplificationFactor").Label("Amplification Factor"))
                    .AddField(f => f.Bool("Urgent").Label("Mark as urgent"))
                    .AddField(f =>
                        f.Date("ScheduledDate")
                            .Label("Scheduled Date")
                            .Description("Leave empty for immediate send.")
                    )
                    .AddField(f => f.DateTime("ExpiresAt").Label("Expires At"))
                    .AddField(f =>
                        f.FileUpload("Attachment")
                            .Label("Attachment")
                            .MaxSizeBytes(5 * 1024 * 1024)
                            .AcceptedExtensions(".pdf", ".png", ".jpg")
                    )
                    .OnSubmit(
                        (sp, submission, ctx) =>
                        {
                            var title = submission.Get<string>("Title");
                            ctx.ShowSuccess($"Queued notification: {title}");
                            return Task.CompletedTask;
                        }
                    )
        )
        .AddDashboard(
            "operations",
            d =>
                d.WithTitle("Operations")
                    .Nav(n => n.Group("Overview").Order(0).Label("Operations"))
                    .AddStatCard(
                        "Open Todos",
                        async (IServiceProvider sp, CancellationToken ct) =>
                        {
                            var db = sp.GetRequiredService<AppDbContext>();
                            return (object?)
                                await db.Todos.CountAsync(
                                    t =>
                                        t.Status != TodoStatus.Done
                                        && t.Status != TodoStatus.Cancelled,
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
                            if (total == 0)
                                return (object?)0d;
                            var done = await db.Todos.CountAsync(
                                t => t.Status == TodoStatus.Done,
                                ct
                            );
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
                            var byDate = raw.GroupBy(d => d)
                                .ToDictionary(g => g.Key, g => g.Count());
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
                    .AddLineChart<MetricsTick>(
                        "Live metrics",
                        xAxis: p => p.At,
                        yAxis: p => p.Value,
                        configure: c =>
                            c.WithStreaming(metricsTickStream.Reader).WithWindowSize(40),
                        yAxisLabel: "value"
                    )
                    // Phase 5 (narrowed): polling-variant line chart. The chart's fetch
                    // delegate is re-invoked every 10s; no separate poll delegate is
                    // taken — the library reuses the dashboard widget materialiser.
                    .AddLineChart<OpenTodoSnapshot>(
                        "Open todos (live)",
                        async (IServiceProvider sp, CancellationToken ct) =>
                        {
                            var db = sp.GetRequiredService<AppDbContext>();
                            var open = await db.Todos.CountAsync(
                                t =>
                                    t.Status != TodoStatus.Done && t.Status != TodoStatus.Cancelled,
                                ct
                            );
                            // Single-point series — the chart appends each snapshot and
                            // trims to the configured window.
                            return (IReadOnlyList<OpenTodoSnapshot>)
                                new[] { new OpenTodoSnapshot(DateTime.UtcNow, open) };
                        },
                        xAxis: p => p.At,
                        yAxis: p => p.Count,
                        xAxisLabel: null,
                        yAxisLabel: "open",
                        configure: c =>
                            c.WithLivePolling(TimeSpan.FromSeconds(10)).WithWindowSize(30)
                    )
                    .AddTable<Todo>(t =>
                        t.WithTitle("Recent Todos")
                            .WithColumns(
                                x => x.Title,
                                x => x.Status,
                                x => x.Priority,
                                x => x.CreatedAt
                            )
                            .OrderBy(x => x.CreatedAt, descending: true)
                            .Take(10)
                    )
                    .Layout(layout =>
                        layout
                            .Row(r => r.Add("Open Todos").Add("Completion %"))
                            .Row(r => r.Add("Completed per day (14d)", fullWidth: true))
                            .Row(r => r.Add("Live metrics", fullWidth: true))
                            .Row(r => r.Add("Open todos (live)", fullWidth: true))
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

/// <summary>One snapshot for the polling "Open todos (live)" chart.</summary>
public sealed record OpenTodoSnapshot(DateTime At, int Count);

// Required for WebApplicationFactory<TodoApp> in integration tests.
public partial class Program { }
