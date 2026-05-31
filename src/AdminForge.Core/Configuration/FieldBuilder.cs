using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Entry-point fluent surface for declaring a single form field. The user picks
/// the field kind via one of the typed methods (<see cref="Text"/>,
/// <see cref="Number"/>, …), which returns a kind-specific sub-builder carrying
/// shared overrides (<c>Label</c>, <c>Required</c>, …) plus type-specific
/// options.
/// </summary>
public sealed class FieldBuilder
{
    private FieldMeta? _built;
    private bool _consumed;

    public FieldBuilder() { }

    private TBuilder Begin<TBuilder>(string name, FieldKind kind, object? options)
        where TBuilder : FieldSubBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_consumed)
            throw new InvalidOperationException(
                "FieldBuilder.AddField may only declare a single field per call."
            );
        _consumed = true;
        var meta = new FieldMeta
        {
            Name = name,
            Label = name,
            Kind = kind,
            Options = options,
        };
        _built = meta;
        return (TBuilder)Activator.CreateInstance(typeof(TBuilder), meta)!;
    }

    /// <summary>Declare a single-line text field (use <c>.Multiline()</c> for multi-line).</summary>
    public TextFieldBuilder Text(string name) =>
        Begin<TextFieldBuilder>(name, FieldKind.Text, new TextFieldOptions());

    /// <summary>Declare an integer field.</summary>
    public NumberFieldBuilder Number(string name) =>
        Begin<NumberFieldBuilder>(name, FieldKind.Number, new NumberFieldOptions());

    /// <summary>Declare a floating-point field.</summary>
    public FloatFieldBuilder Float(string name) =>
        Begin<FloatFieldBuilder>(name, FieldKind.Float, new FloatFieldOptions());

    /// <summary>Declare a boolean (checkbox) field.</summary>
    public BoolFieldBuilder Bool(string name) =>
        Begin<BoolFieldBuilder>(name, FieldKind.Bool, null);

    /// <summary>Declare a date-only field.</summary>
    public DateFieldBuilder Date(string name) =>
        Begin<DateFieldBuilder>(name, FieldKind.Date, null);

    /// <summary>Declare a date+time field.</summary>
    public DateTimeFieldBuilder DateTime(string name) =>
        Begin<DateTimeFieldBuilder>(name, FieldKind.DateTime, null);

    /// <summary>Declare a markdown-editor field.</summary>
    public MarkdownFieldBuilder Markdown(string name) =>
        Begin<MarkdownFieldBuilder>(name, FieldKind.Markdown, null);

    /// <summary>Declare a file-upload field.</summary>
    public FileUploadFieldBuilder FileUpload(string name) =>
        Begin<FileUploadFieldBuilder>(name, FieldKind.FileUpload, new FileUploadFieldOptions());

    /// <summary>
    /// Returns the built field, or throws if no kind method was called. Internal —
    /// consumed by <see cref="FormBuilder.AddField"/>.
    /// </summary>
    internal FieldMeta Build()
    {
        if (_built is null)
            throw new InvalidOperationException(
                "AddField callback must invoke a field-kind method (e.g. f.Text(...))."
            );
        return _built;
    }
}

/// <summary>
/// Base for the kind-specific fluent sub-builders. Carries the shared overrides
/// (<c>Label</c>, <c>Description</c>, <c>Required</c>, <c>Validate</c>) and
/// keeps the wrapped <see cref="FieldMeta"/> for derived classes to tweak
/// <see cref="FieldMeta.Options"/>.
/// </summary>
public abstract class FieldSubBuilder
{
    protected internal FieldMeta Meta { get; }

    protected FieldSubBuilder(FieldMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        Meta = meta;
    }
}

/// <summary>Shared chainable overrides typed on the concrete sub-builder.</summary>
public abstract class FieldSubBuilder<TSelf> : FieldSubBuilder
    where TSelf : FieldSubBuilder<TSelf>
{
    protected FieldSubBuilder(FieldMeta meta)
        : base(meta) { }

    private TSelf Self => (TSelf)(object)this;

    /// <summary>Override the field's display label (defaults to the field name).</summary>
    public TSelf Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Meta.Label = label;
        return Self;
    }

    /// <summary>Set the helper text rendered below the field.</summary>
    public TSelf Description(string description)
    {
        Meta.Description = description;
        return Self;
    }

    /// <summary>Marks the field as required; the submission must supply a non-empty value.</summary>
    public TSelf Required(bool required = true)
    {
        Meta.Required = required;
        return Self;
    }

    /// <summary>
    /// Adds a custom validator. <paramref name="predicate"/> returns true for
    /// valid values; when it returns false, <paramref name="message"/> is
    /// surfaced for this field.
    /// </summary>
    public TSelf Validate(Func<object?, bool> predicate, string message)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Meta.Validators.Add(new ColumnValidator(value => predicate(value) ? null : message));
        return Self;
    }
}

/// <summary>Kind-specific builder for <see cref="FieldKind.Text"/> fields.</summary>
public sealed class TextFieldBuilder : FieldSubBuilder<TextFieldBuilder>
{
    public TextFieldBuilder(FieldMeta meta)
        : base(meta) { }

    private TextFieldOptions Opts => (TextFieldOptions)Meta.Options!;

    /// <summary>Render the field as a multi-line text area.</summary>
    public TextFieldBuilder Multiline(bool multiline = true)
    {
        Opts.Multiline = multiline;
        return this;
    }

    /// <summary>Enforce a maximum string length on submission.</summary>
    public TextFieldBuilder MaxLength(int max)
    {
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max), "MaxLength must be positive.");
        Opts.MaxLength = max;
        return this;
    }
}

/// <summary>Kind-specific builder for <see cref="FieldKind.Number"/> fields (integer).</summary>
public sealed class NumberFieldBuilder : FieldSubBuilder<NumberFieldBuilder>
{
    public NumberFieldBuilder(FieldMeta meta)
        : base(meta) { }

    private NumberFieldOptions Opts => (NumberFieldOptions)Meta.Options!;

    public NumberFieldBuilder Min(long min)
    {
        Opts.Min = min;
        return this;
    }

    public NumberFieldBuilder Max(long max)
    {
        Opts.Max = max;
        return this;
    }
}

/// <summary>Kind-specific builder for <see cref="FieldKind.Float"/> fields.</summary>
public sealed class FloatFieldBuilder : FieldSubBuilder<FloatFieldBuilder>
{
    public FloatFieldBuilder(FieldMeta meta)
        : base(meta) { }

    private FloatFieldOptions Opts => (FloatFieldOptions)Meta.Options!;

    public FloatFieldBuilder Min(double min)
    {
        Opts.Min = min;
        return this;
    }

    public FloatFieldBuilder Max(double max)
    {
        Opts.Max = max;
        return this;
    }
}

/// <summary>Kind-specific builder for <see cref="FieldKind.Bool"/> fields.</summary>
public sealed class BoolFieldBuilder : FieldSubBuilder<BoolFieldBuilder>
{
    public BoolFieldBuilder(FieldMeta meta)
        : base(meta) { }
}

/// <summary>Kind-specific builder for <see cref="FieldKind.Date"/> fields.</summary>
public sealed class DateFieldBuilder : FieldSubBuilder<DateFieldBuilder>
{
    public DateFieldBuilder(FieldMeta meta)
        : base(meta) { }
}

/// <summary>Kind-specific builder for <see cref="FieldKind.DateTime"/> fields.</summary>
public sealed class DateTimeFieldBuilder : FieldSubBuilder<DateTimeFieldBuilder>
{
    public DateTimeFieldBuilder(FieldMeta meta)
        : base(meta) { }
}

/// <summary>Kind-specific builder for <see cref="FieldKind.Markdown"/> fields.</summary>
public sealed class MarkdownFieldBuilder : FieldSubBuilder<MarkdownFieldBuilder>
{
    public MarkdownFieldBuilder(FieldMeta meta)
        : base(meta) { }
}

/// <summary>Kind-specific builder for <see cref="FieldKind.FileUpload"/> fields.</summary>
public sealed class FileUploadFieldBuilder : FieldSubBuilder<FileUploadFieldBuilder>
{
    public FileUploadFieldBuilder(FieldMeta meta)
        : base(meta) { }

    private FileUploadFieldOptions Opts => (FileUploadFieldOptions)Meta.Options!;

    /// <summary>Cap the uploaded file size in bytes. Submissions exceeding this are rejected.</summary>
    public FileUploadFieldBuilder MaxSizeBytes(long max)
    {
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max), "MaxSizeBytes must be positive.");
        Opts.MaxSizeBytes = max;
        return this;
    }

    /// <summary>
    /// Restrict acceptable file extensions (each starts with <c>.</c>). The
    /// list is normalised to lowercase; an empty list means anything goes.
    /// </summary>
    public FileUploadFieldBuilder AcceptedExtensions(params string[] extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var normalised = new List<string>(extensions.Length);
        foreach (var ext in extensions)
        {
            if (string.IsNullOrWhiteSpace(ext))
                continue;
            var lower = ext.Trim().ToLowerInvariant();
            normalised.Add(lower.StartsWith('.') ? lower : "." + lower);
        }
        Opts.AcceptedExtensions = normalised;
        return this;
    }
}
