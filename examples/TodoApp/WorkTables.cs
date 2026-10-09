using System.ComponentModel.DataAnnotations;
using AdminForge;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Entities;

namespace TodoApp;

public sealed record SnoozeInput([Range(1, 30)] int Days, string? Reason);

public sealed record CommentInput([Required] string Body, AdminFile? Screenshot);

/// <summary>The EF tables: users, lists, tasks and tags, each served by an <see cref="EfSource{TDb, T}"/>.</summary>
public static class WorkTables
{
    public static PanelBuilder Users(this PanelBuilder panel) =>
        panel.Table<User>(t =>
            t.Title("Users")
                .Source<UserSource>()
                .Columns(u => u.DisplayName, u => u.Email, u => u.Role, u => u.CreatedAt)
                .Column(u => u.CreatedAt, c => c.ReadOnly())
                .Related<Todo>(todo => todo.AssigneeId, r => r.Title("Tasks").Inline())
                .Action(
                    "send-welcome",
                    "Send welcome email",
                    a =>
                        a.Confirm("Sends the welcome email again.")
                            .Run(ctx =>
                            {
                                ctx.Success($"Welcome email sent to {ctx.Row.DisplayName}.");
                                ctx.Result(
                                    $"| To | Status |\n|---|---|\n| `{ctx.Row.Email}` | **queued** |"
                                );
                            })
                )
        );

    public static PanelBuilder Lists(this PanelBuilder panel) =>
        panel.Table<TodoList>(t =>
            t.Title("Lists")
                .Columns(l => l.Name, l => l.OwnerId, l => l.IsArchived, l => l.CreatedAt)
                .Column(l => l.OwnerId, c => c.Title("Owner").LinksTo<User>())
                .Column(l => l.CreatedAt, c => c.ReadOnly())
                .Related<Todo>(todo => todo.TodoListId, r => r.Title("Tasks in this list").Inline())
                // An archived list is kept as it is: no edit, no delete.
                .Authorize(
                    (_, op, list) =>
                        op is not (AdminOp.Update or AdminOp.Delete) || list?.IsArchived != true
                )
        );

    public static PanelBuilder Tasks(this PanelBuilder panel) =>
        panel.Table<Todo>(t =>
            t.Title("Tasks")
                .Columns(
                    x => x.Title,
                    x => x.Status,
                    x => x.Priority,
                    x => x.AssigneeId,
                    x => x.DueAt
                )
                .Column(x => x.Title, c => c.Validate(s => s.Length >= 3, "At least 3 characters."))
                .Column(x => x.Description, c => c.Content(Content.Markdown))
                .Column(
                    x => x.Status,
                    c =>
                        c.Status(s =>
                            s.Tone(TodoStatus.Done, Tone.Success)
                                .Tone(TodoStatus.InProgress, Tone.Info)
                                .Tone(TodoStatus.Blocked, Tone.Danger)
                        )
                )
                .Column(x => x.AssigneeId, c => c.Title("Assignee").LinksTo<User>())
                .Column(x => x.TodoListId, c => c.Title("List").LinksTo<TodoList>())
                .Column(x => x.DueAt, c => c.Description("Leave empty for no deadline."))
                .Column(x => x.CreatedAt, c => c.ReadOnly().Format("yyyy-MM-dd"))
                // SaveChanges stamps it, so it is noise on every surface.
                .Hide(x => x.UpdatedAt)
                .Computed("due", x => DuePhrase(x.DueAt), c => c.Title("Due"))
                .Computed<AuditLogStore, int>(
                    "admin-edits",
                    (log, x, _) =>
                        Task.FromResult(
                            log.Snapshot().Count(e => e.Table == "todo" && e.Key == $"{x.Id}")
                        ),
                    c => c.Title("Admin edits").InList()
                )
                .Poll(TimeSpan.FromSeconds(5))
                .Live<MetricsTickStream, MetricsTick>(
                    "Activity",
                    (stream, todo, ct) => stream.Ticks(todo.Id, ct),
                    w => w.Chart(p => p.At, p => p.Value).Window(40)
                )
                .Live<MetricsTickStream, MetricsTick>(
                    "Samples",
                    (stream, todo, ct) => stream.Ticks(todo.Id + 1000, ct),
                    w => w.Feed(p => p.At, p => p.Value).Keep(20)
                )
                .Action(
                    "snooze",
                    "Snooze",
                    a =>
                        a.Confirm("Pushes the due date out.")
                            .Input<SnoozeInput>()
                            .Field(
                                i => i.Reason,
                                f => f.Options("waiting on someone", "low priority", "blocked")
                            )
                            .Run<AppDbContext>(
                                async (db, ctx) =>
                                {
                                    ctx.Row.DueAt = (ctx.Row.DueAt ?? DateTime.UtcNow).AddDays(
                                        ctx.Input.Days
                                    );
                                    await db.SaveChangesAsync(ctx.CancellationToken);
                                    ctx.Success(
                                        $"Snoozed {ctx.Input.Days} day(s): {ctx.Input.Reason}"
                                    );
                                }
                            )
                )
                .Action(
                    "comment",
                    "Add comment",
                    a =>
                        a.Tone(Tone.Info)
                            .Input<CommentInput>()
                            .Field(i => i.Body, f => f.Markdown())
                            .Field(
                                i => i.Screenshot,
                                f => f.Accept(".png", ".jpg").MaxBytes(2 * 1024 * 1024)
                            )
                            .Run<AppDbContext>(
                                async (db, ctx) =>
                                {
                                    var attached = ctx.Input.Screenshot is { } shot
                                        ? $"\n\n_Screenshot: {shot.Name}, {shot.Length} bytes_"
                                        : "";
                                    var comment = ctx.Input.Body + attached;
                                    ctx.Row.Description = ctx.Row.Description is { } before
                                        ? $"{before}\n\n---\n\n{comment}"
                                        : comment;
                                    await db.SaveChangesAsync(ctx.CancellationToken);
                                    ctx.Success("Comment added.");
                                }
                            )
                )
        );

    /// <summary>Tags have no foreign key to link from, so the sidebar leaves them out; the URL and MCP still reach them.</summary>
    public static PanelBuilder Tags(this PanelBuilder panel) => panel.Table<Tag>(t => t.Hidden());

    private static string DuePhrase(DateTime? due) =>
        due is not { } d ? "no date"
        : d.Date < DateTime.UtcNow.Date ? $"overdue by {(DateTime.UtcNow.Date - d.Date).Days}d"
        : d.Date == DateTime.UtcNow.Date ? "today"
        : $"in {(d.Date - DateTime.UtcNow.Date).Days}d";
}

/// <summary>Users through EF, with one rule of the host's: an email belongs to one user.</summary>
public sealed class UserSource(AppDbContext db) : EfSource<AppDbContext, User>(db)
{
    public override async Task<User> CreateAsync(User row, CancellationToken ct)
    {
        if (await Db.Users.AnyAsync(u => u.Email == row.Email, ct))
            throw new AdminRejection(
                new Dictionary<string, string> { ["Email"] = $"{row.Email} is already registered." }
            );
        row.CreatedAt = DateTime.UtcNow;
        return await base.CreateAsync(row, ct);
    }
}
