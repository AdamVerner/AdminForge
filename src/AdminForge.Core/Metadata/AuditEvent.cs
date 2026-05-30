namespace AdminForge.Core.Metadata;

/// <summary>
/// Classification of an audited action.
/// </summary>
public enum AuditAction
{
    Read,
    Create,
    Update,
    Delete,
    FormSubmit,
    CustomAction,
}

/// <summary>
/// One audited admin action. Plain DTO — no framework dependencies — handed to
/// <c>IAuditSink</c> by mutating operations. The consumer decides what to do with it.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>Logical entity name (CLR <c>Type.Name</c>) or form key for <see cref="AuditAction.FormSubmit"/>.</summary>
    public required string EntityType { get; init; }

    /// <summary>Action that fired.</summary>
    public required AuditAction Action { get; init; }

    /// <summary>Primary key of the affected entity (string form, multi-part for composite keys). Null for create-before-save or form submits.</summary>
    public string? EntityId { get; init; }

    /// <summary>Per-property before/after pairs for <see cref="AuditAction.Update"/>. Empty otherwise.</summary>
    public IReadOnlyDictionary<string, AuditValueChange> ChangedValues { get; init; } =
        new Dictionary<string, AuditValueChange>();

    /// <summary>UTC timestamp at which the action was applied.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Identifier of the acting user, sourced from the host's auth context. May be null in tests.</summary>
    public string? User { get; init; }
}

/// <summary>
/// Old/new value pair for one property in an update audit event.
/// </summary>
public sealed record AuditValueChange(object? OldValue, object? NewValue);
