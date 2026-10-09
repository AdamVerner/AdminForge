using System.Security.Claims;

namespace AdminForge;

/// <summary>What an audited operation did.</summary>
public enum AdminAuditOperation
{
    Create,
    Update,
    Delete,
    Action,
    Form,
}

/// <summary>Where an operation came from.</summary>
public enum AdminSurface
{
    Panel,
    Mcp,
}

/// <summary>A property's value before and after the operation; null on the side that has none.</summary>
public sealed record AdminChange(object? Before, object? After);

/// <summary>One successful operation, as the audit sink receives it.</summary>
/// <remarks>
/// <see cref="Changes"/> holds every stored property on create and delete, the changed ones on update.
/// <see cref="Action"/> is the action id, or the form id for a form.
/// </remarks>
public sealed record AdminAuditEvent(
    AdminAuditOperation Operation,
    string? Table,
    string? Key,
    string? Action,
    IReadOnlyDictionary<string, object?> Input,
    IReadOnlyDictionary<string, AdminChange> Changes,
    ClaimsPrincipal User,
    AdminSurface Surface,
    DateTimeOffset At
);

/// <summary>Receives every successful write, action and form submission. Resolved from the operation's scope.</summary>
public interface IAdminAuditSink
{
    Task RecordAsync(AdminAuditEvent auditEvent, CancellationToken ct);
}
