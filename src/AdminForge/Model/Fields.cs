using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AdminForge;

/// <summary>What the host said about one input beyond its type and attributes.</summary>
internal sealed class FieldSettings
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool Markdown { get; set; }
    public bool Multiline { get; set; }
    public Func<
        IServiceProvider,
        CancellationToken,
        Task<IReadOnlyList<AdminOption>>
    >? Options { get; set; }
    public IReadOnlyList<string>? Accept { get; set; }
    public long? MaxBytes { get; set; }
    public List<Func<object?, string?>> Rules { get; } = [];

    public static FieldSettings Of(Dictionary<string, FieldSettings> all, string name) =>
        all.TryGetValue(name, out var s) ? s : all[name] = new FieldSettings();
}

internal static class Fields
{
    /// <summary>The input for one property; null when its type is not one a form can edit.</summary>
    /// <param name="valueTypesRequired">An input record's value-type parameter without a default must be given; a stored row's keeps its default.</param>
    public static FieldModel? For(
        PropertyInfo property,
        ParameterInfo? parameter,
        bool valueTypesRequired,
        FieldSettings? settings
    )
    {
        var type = property.PropertyType;
        var t = Nullable.GetUnderlyingType(type) ?? type;
        var attributes = property
            .GetCustomAttributes(true)
            .Concat(parameter?.GetCustomAttributes(true) ?? [])
            .ToList();
        var maxLength =
            attributes.OfType<MaxLengthAttribute>().FirstOrDefault()?.Length
            ?? attributes.OfType<StringLengthAttribute>().FirstOrDefault()?.MaximumLength;
        var multiline =
            settings?.Multiline == true
            || attributes
                .OfType<DataTypeAttribute>()
                .Any(d => d.DataType == DataType.MultilineText);

        FieldKind? kind =
            settings?.Options is not null ? FieldKind.Select
            : t == typeof(AdminFile) ? FieldKind.File
            : t == typeof(string)
                ? (
                    settings?.Markdown == true ? FieldKind.Markdown
                    : multiline ? FieldKind.Multiline
                    : FieldKind.Text
                )
            : t == typeof(bool) ? FieldKind.Bool
            : t.IsEnum ? FieldKind.Enum
            : ScalarTypes.IsInteger(t) ? FieldKind.Integer
            : ScalarTypes.IsFraction(t) ? FieldKind.Number
            : t == typeof(DateTime) || t == typeof(DateTimeOffset) ? FieldKind.DateTime
            : t == typeof(DateOnly) ? FieldKind.Date
            : ScalarTypes.IsScalar(t) ? FieldKind.Text
            : null;
        if (kind is null)
            return null;

        var nullable =
            Nullable.GetUnderlyingType(type) is not null
            || !type.IsValueType
                && new NullabilityInfoContext().Create(property).ReadState
                    == NullabilityState.Nullable;
        var title =
            settings?.Title
            ?? attributes.OfType<DisplayAttribute>().FirstOrDefault()?.Name
            ?? attributes.OfType<DisplayNameAttribute>().FirstOrDefault()?.DisplayName
            ?? Members.Humanize(property.Name);
        var range = attributes.OfType<RangeAttribute>().FirstOrDefault();
        var rules = attributes
            .OfType<ValidationAttribute>()
            .Where(a => a is not RequiredAttribute)
            .Select(a =>
                (Func<object?, string?>)(
                    v => v is null || a.IsValid(v) ? null : a.FormatErrorMessage(title)
                )
            )
            .Concat(settings?.Rules ?? [])
            .ToList();
        return new FieldModel
        {
            Name = property.Name,
            Title = title,
            Description =
                settings?.Description
                ?? attributes.OfType<DisplayAttribute>().FirstOrDefault()?.Description
                ?? attributes.OfType<DescriptionAttribute>().FirstOrDefault()?.Description,
            ClrType = type,
            Kind = kind.Value,
            Required =
                attributes.OfType<RequiredAttribute>().Any()
                || !nullable && !type.IsValueType
                || valueTypesRequired
                    && !nullable
                    && type.IsValueType
                    && t != typeof(bool)
                    && parameter is { HasDefaultValue: false },
            MaxLength = maxLength,
            Min = range is null ? null : Convert.ToDouble(range.Minimum),
            Max = range is null ? null : Convert.ToDouble(range.Maximum),
            Rules = rules,
            Options = settings?.Options,
            Multiple = kind == FieldKind.Select && ScalarTypes.ElementType(t) == typeof(string),
            Accept = settings?.Accept,
            MaxBytes = settings?.MaxBytes,
        };
    }

    /// <summary>An input record's fields, in declaration order.</summary>
    public static InputModel Input(Type type, IReadOnlyDictionary<string, FieldSettings> settings)
    {
        var factory = new RowFactory(type);
        var fields = new List<FieldModel>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!factory.CanSet(property))
                continue;
            fields.Add(
                For(
                    property,
                    factory.Parameter(property.Name),
                    valueTypesRequired: true,
                    settings.GetValueOrDefault(property.Name)
                )
                    ?? throw new InvalidOperationException(
                        $"{type.Name}.{property.Name} is a {property.PropertyType.Name}, which no input edits; a list of strings needs Options(...)."
                    )
            );
        }
        foreach (var name in settings.Keys.Where(n => fields.All(f => f.Name != n)))
            throw new InvalidOperationException($"{type.Name} has no settable property '{name}'.");
        return new InputModel(fields, factory);
    }
}
