namespace AdminForge.Core.Metadata;

/// <summary>
/// A single field on a generic form. Carries display metadata plus the optional
/// per-field validator and a typed <see cref="Options"/> object whose shape
/// depends on <see cref="Kind"/> (e.g. <see cref="TextFieldOptions"/> for text
/// fields, <see cref="NumberFieldOptions"/> for numbers, …).
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

    /// <summary>
    /// Kind-specific extra configuration. The concrete type depends on
    /// <see cref="Kind"/> — see <see cref="TextFieldOptions"/>,
    /// <see cref="NumberFieldOptions"/>, <see cref="FloatFieldOptions"/>, and
    /// <see cref="FileUploadFieldOptions"/>. May be null for kinds that need no
    /// extra options (Bool, Date, DateTime, Markdown).
    /// </summary>
    public object? Options { get; set; }
}

/// <summary>Type-specific options for a <see cref="FieldKind.Text"/> field.</summary>
public sealed class TextFieldOptions
{
    /// <summary>Render as a multi-line text area instead of a single-line input.</summary>
    public bool Multiline { get; set; }

    /// <summary>Maximum string length; null means no enforced cap.</summary>
    public int? MaxLength { get; set; }
}

/// <summary>Type-specific options for a <see cref="FieldKind.Number"/> field.</summary>
public sealed class NumberFieldOptions
{
    public long? Min { get; set; }
    public long? Max { get; set; }
}

/// <summary>Type-specific options for a <see cref="FieldKind.Float"/> field.</summary>
public sealed class FloatFieldOptions
{
    public double? Min { get; set; }
    public double? Max { get; set; }
}

/// <summary>Type-specific options for a <see cref="FieldKind.FileUpload"/> field.</summary>
public sealed class FileUploadFieldOptions
{
    /// <summary>Cap on the uploaded file size in bytes; null means no enforced cap.</summary>
    public long? MaxSizeBytes { get; set; }

    /// <summary>
    /// Whitelist of accepted file extensions (each starts with <c>.</c>, lowercased).
    /// Null/empty means anything is accepted.
    /// </summary>
    public IReadOnlyList<string>? AcceptedExtensions { get; set; }
}
