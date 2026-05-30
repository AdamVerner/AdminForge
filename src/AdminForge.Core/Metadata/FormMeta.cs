namespace AdminForge.Core.Metadata;

/// <summary>
/// Phase 1 stub for a registered generic form. The fluent <c>FormBuilder</c>,
/// submit handler invocation, and rendering land in Phase 4.
/// </summary>
public sealed class FormMeta
{
    /// <summary>Stable key used for routing (e.g. "send-notification").</summary>
    public required string Key { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; set; }

    /// <summary>Fields in declaration order.</summary>
    public List<FieldMeta> Fields { get; } = [];

    /// <summary>Sidebar nav entry.</summary>
    public NavMeta Nav { get; set; } = new();
}
