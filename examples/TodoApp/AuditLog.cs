using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;

namespace TodoApp;

/// <summary>One audited admin action, flattened so each column is one property.</summary>
public sealed record AuditLogEntry(
    int Id,
    DateTime Timestamp,
    AuditAction Action,
    string EntityType,
    string? EntityId,
    string? User,
    int Changes
);

/// <summary>The audit sink's memory: the last 500 events, newest first.</summary>
public sealed class AuditLogStore
{
    private readonly List<AuditLogEntry> _entries = [];
    private readonly Lock _lock = new();

    public void Record(AuditEvent evt)
    {
        lock (_lock)
        {
            _entries.Insert(
                0,
                new AuditLogEntry(
                    _entries.Count == 0 ? 1 : _entries[0].Id + 1,
                    evt.Timestamp,
                    evt.Action,
                    evt.EntityType,
                    evt.EntityId,
                    evt.User,
                    evt.ChangedValues.Count
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
}

/// <summary>
/// A read-only, provider-backed table: AdminForge describes <see cref="AuditLogEntry"/> from its
/// properties, this provider answers the list and view pages, and <c>ReadOnly()</c> hides the rest.
/// Sorts by timestamp only, so that is the one column registered as <c>Sortable()</c>.
/// </summary>
public sealed class AuditLogDataProvider(AuditLogStore store) : ReadOnlyDataProvider<AuditLogEntry>
{
    public override Task<ListResult<AuditLogEntry>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    )
    {
        IEnumerable<AuditLogEntry> rows = store.Snapshot();
        if (!string.IsNullOrWhiteSpace(query.Search))
            rows = rows.Where(r =>
                r.EntityType.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || (r.User?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false)
            );
        if (query.SortBy == nameof(AuditLogEntry.Timestamp) && !query.SortDescending)
            rows = rows.OrderBy(r => r.Timestamp);

        var all = rows.ToList();
        return Task.FromResult(
            new ListResult<AuditLogEntry>
            {
                Items = all.Skip(query.Page * query.PageSize).Take(query.PageSize).ToList(),
                TotalCount = all.Count,
            }
        );
    }

    public override Task<AuditLogEntry?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            keyValues is [int id] ? store.Snapshot().FirstOrDefault(r => r.Id == id) : null
        );
}
