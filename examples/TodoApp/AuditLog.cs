using AdminForge;

namespace TodoApp;

/// <summary>One audited admin operation, flattened so each column is one property.</summary>
public sealed record AuditLogEntry(
    int Id,
    DateTimeOffset At,
    AdminAuditOperation Operation,
    string? Table,
    string? Key,
    string? Action,
    string? User,
    AdminSurface Surface,
    int Changes,
    IReadOnlyList<string> ChangedProperties
);

/// <summary>The audit sink's memory: the last 500 events, newest first.</summary>
public sealed class AuditLogStore
{
    private readonly List<AuditLogEntry> _entries = [];
    private readonly Lock _lock = new();

    public void Record(AdminAuditEvent e)
    {
        lock (_lock)
        {
            _entries.Insert(
                0,
                new AuditLogEntry(
                    _entries.Count == 0 ? 1 : _entries[0].Id + 1,
                    e.At,
                    e.Operation,
                    e.Table,
                    e.Key,
                    e.Action,
                    e.User.Identity?.Name,
                    e.Surface,
                    e.Changes.Count,
                    [.. e.Changes.Keys]
                )
            );
            if (_entries.Count > 500)
                _entries.RemoveAt(_entries.Count - 1);
        }
    }

    public IReadOnlyList<AuditLogEntry> Snapshot()
    {
        lock (_lock)
            return _entries.ToArray();
    }

    public AuditLogEntry? Find(int id) => Snapshot().FirstOrDefault(e => e.Id == id);

    /// <summary>A page of entries, of one user when <paramref name="user"/> names one; the search matches the table, the action and the user.</summary>
    public AdminPage<AuditLogEntry> List(AdminQuery<AuditLogEntry> query, string? user)
    {
        IEnumerable<AuditLogEntry> rows = Snapshot();
        if (user is not null)
            rows = rows.Where(r => r.User == user);
        if (query.Search is { } s)
            rows = rows.Where(r =>
                $"{r.Table} {r.Action} {r.User}".Contains(s, StringComparison.OrdinalIgnoreCase)
            );
        return AdminPage.From(rows, query);
    }
}

/// <summary>The signed-in admin's own entries. A source can inject <see cref="IUserAccessor"/> like any service.</summary>
public sealed class MyActivity(AuditLogStore store, IUserAccessor user)
{
    public AdminPage<AuditLogEntry> List(AdminQuery<AuditLogEntry> query) =>
        store.List(query, Name());

    public AuditLogEntry? Find(int id) => store.Find(id) is { } e && e.User == Name() ? e : null;

    private string Name() => user.GetUser().Identity?.Name ?? "";
}

/// <summary>Writes every panel and MCP write into the store and the console.</summary>
public sealed class AuditLogSink(AuditLogStore store) : IAdminAuditSink
{
    public Task RecordAsync(AdminAuditEvent e, CancellationToken ct)
    {
        Console.WriteLine(
            $"[audit] {e.At:O} {e.Operation} {e.Table ?? e.Action}#{e.Key} by {e.User.Identity?.Name ?? "anonymous"} via {e.Surface} ({e.Changes.Count} change(s))"
        );
        store.Record(e);
        return Task.CompletedTask;
    }
}
