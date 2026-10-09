using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using AdminForge;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Entities;

namespace TodoApp;

/// <summary>One bucket of the "completed per day" chart.</summary>
public sealed record DailyCompletion(DateTime Day, int Count);

/// <summary>Every kind of field a form or an action input offers.</summary>
public sealed record NotificationInput(
    [property: Description("Headline shown in the inbox.")] string Title,
    [MaxLength(1000)] string Body,
    string? RichBody,
    [Range(0, 5)] int Priority,
    double? AmplificationFactor,
    bool Urgent,
    DateOnly? ScheduledDate,
    DateTime? ExpiresAt,
    AdminFile? Attachment,
    string Channel,
    IReadOnlyList<string>? Recipients
);

/// <summary>The home dashboard, a form, and the entries in the Settings and Account areas.</summary>
public static class OperationsPanel
{
    public static PanelBuilder Notifications(this PanelBuilder panel) =>
        panel.Form<NotificationInput>(
            "send-notification",
            f =>
                f.Title("Send notification")
                    .Description("The result echoes what was submitted.")
                    .Field(i => i.RichBody, x => x.Markdown())
                    .Field(i => i.Body, x => x.Multiline())
                    .Field(i => i.Channel, x => x.Options("inbox", "email", "sms"))
                    .Field(
                        i => i.Recipients,
                        x =>
                            x.Title("Recipients")
                                .Options<AppDbContext>(
                                    async (db, ct) =>
                                        await db
                                            .Users.OrderBy(u => u.Email)
                                            .Select(u => new AdminOption(u.Id.ToString(), u.Email))
                                            .ToListAsync(ct)
                                )
                    )
                    .Field(
                        i => i.Attachment,
                        x =>
                            x.Description("A PDF or an image, up to 5 MB.")
                                .Accept(".pdf", ".png", ".jpg")
                                .MaxBytes(5 * 1024 * 1024)
                    )
                    .Field(
                        i => i.Title,
                        x => x.Validate(t => !t.StartsWith(' '), "No leading space.")
                    )
                    .Run(ctx =>
                    {
                        ctx.Success($"Queued notification: {ctx.Input.Title}");
                        ctx.Result(
                            $"## Queued: {ctx.Input.Title}\n\n| Field | Value |\n|---|---|\n"
                                + $"| Channel | {ctx.Input.Channel} |\n| Recipients | {string.Join(", ", ctx.Input.Recipients ?? [])} |\n"
                                + $"| Attachment | {ctx.Input.Attachment?.Name ?? "none"} |"
                        );
                    })
        );

    public static PanelBuilder Operations(this PanelBuilder panel) =>
        panel.Dashboard(
            "operations",
            d =>
                d.Title("Operations")
                    .Home()
                    .Row(r =>
                        r.Stat<AppDbContext, int>(
                                "Open tasks",
                                (db, ct) =>
                                    db.Todos.CountAsync(
                                        x =>
                                            x.Status != TodoStatus.Done
                                            && x.Status != TodoStatus.Cancelled,
                                        ct
                                    ),
                                suffix: "open"
                            )
                            .Stat<AppDbContext, double>("Completion", CompletionAsync, suffix: "%")
                    )
                    .Row(r =>
                        r.Chart<DailyCompletion>(
                                "Completed per day",
                                c =>
                                    c.Points<AppDbContext>(LastFourteenDaysAsync)
                                        .X(p => p.Day)
                                        .Y(p => p.Count, "tasks")
                                        .Poll(TimeSpan.FromSeconds(10))
                            )
                            .Chart<MetricsTick>(
                                "Live metrics",
                                c =>
                                    c.Stream<MetricsTickStream>((s, ct) => s.Ticks(0, ct))
                                        .X(p => p.At)
                                        .Y(p => p.Value)
                                        .Window(40)
                            )
                    )
                    .Row(r =>
                        r.Table<Todo>(
                                "Recent tasks",
                                w =>
                                    w.Columns(x => x.Title, x => x.Status, x => x.CreatedAt)
                                        .Sort(x => x.CreatedAt, descending: true)
                                        .Take(10)
                            )
                            // AuditLogEntry has two tables, so the widget names one.
                            .Table<AuditLogEntry>(
                                "Latest admin edits",
                                w => w.Table("audit-log").Sort(e => e.At, descending: true).Take(5)
                            )
                    )
        );

    /// <summary>The audit sink's log and a host page, both pinned to the bottom of the sidebar.</summary>
    public static PanelBuilder Settings(this PanelBuilder panel) =>
        panel
            .Table<AuditLogEntry>(t =>
                t.Id("audit-log")
                    .Title("Audit log")
                    .Area(NavArea.Settings)
                    .Empty("Every create, edit, delete, action and form submission lands here.")
                    .Source<AuditLogStore, int>(
                        (log, q, _) => Task.FromResult(log.List(q, user: null)),
                        (log, id, _) => Task.FromResult(log.Find(id)),
                        s => s.Search().Filters(e => e.Operation, e => e.Table).Sorts(e => e.At)
                    )
                    .Columns(
                        e => e.At,
                        e => e.Operation,
                        e => e.Table,
                        e => e.Key,
                        e => e.Action,
                        e => e.User,
                        e => e.Surface
                    )
                    .Column(e => e.At, c => c.Format("yyyy-MM-dd HH:mm:ss"))
            )
            .Page<Pages.StatusPage>(p => p.Area(NavArea.Settings));

    /// <summary>The same read model as the audit log, a second table of it, narrowed to the signed-in admin.</summary>
    public static PanelBuilder Account(this PanelBuilder panel) =>
        panel.Table<AuditLogEntry>(t =>
            t.Id("my-activity")
                .Title("My activity")
                .Area(NavArea.Account)
                .Source<MyActivity, int>(
                    (mine, q, _) => Task.FromResult(mine.List(q)),
                    (mine, id, _) => Task.FromResult(mine.Find(id)),
                    s => s.Sorts(e => e.At)
                )
                .Columns(e => e.At, e => e.Operation, e => e.Table, e => e.Key, e => e.Surface)
        );

    private static async Task<double> CompletionAsync(AppDbContext db, CancellationToken ct)
    {
        var total = await db.Todos.CountAsync(ct);
        return total == 0
            ? 0
            : 100.0 * await db.Todos.CountAsync(x => x.Status == TodoStatus.Done, ct) / total;
    }

    private static async Task<IReadOnlyList<DailyCompletion>> LastFourteenDaysAsync(
        AppDbContext db,
        CancellationToken ct
    )
    {
        var since = DateTime.UtcNow.Date.AddDays(-13);
        var done = await db
            .Todos.Where(x => x.CompletedAt >= since)
            .Select(x => x.CompletedAt!.Value)
            .ToListAsync(ct);
        return Enumerable
            .Range(0, 14)
            .Select(i => since.AddDays(i))
            .Select(day => new DailyCompletion(day, done.Count(at => at.Date == day)))
            .ToList();
    }
}
