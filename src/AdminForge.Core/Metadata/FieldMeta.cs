namespace AdminForge.Core.Metadata;

/// <summary>
/// A single field on a generic form. Phase 1 carries only the shape — the
/// fluent <c>FormBuilder</c> and rendering land in Phase 4.
/// </summary>
public sealed class FieldMeta
{
    /// <summary>Key used in the submitted value bag.</summary>
    public required string Name { get; init; }

    /// <summary>Display label.</summary>
    public required string Label { get; set; }

    /// <summary>Field type, drives both rendering and validation.</summary>
    public required FieldKind Kind { get; init; }

    /// <summary>Optional helper text rendered below the field.</summary>
    public string? Description { get; set; }

    /// <summary>True if the field must be filled in for the form to submit.</summary>
    public bool Required { get; set; }

    /// <summary>User-supplied validators (see <see cref="ColumnValidator"/>).</summary>
    public List<ColumnValidator> Validators { get; } = [];
}
