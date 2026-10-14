using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace AdminForge;

internal static class Members
{
    /// <summary>The property a selector such as <c>x => x.Name</c> reads.</summary>
    public static string Name(LambdaExpression selector) => Property(selector).Name;

    public static PropertyInfo Property(LambdaExpression selector)
    {
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert
            ? convert.Operand
            : selector.Body;
        return body is MemberExpression { Member: PropertyInfo property }
            ? property
            : throw new ArgumentException(
                $"Expected a property, as in x => x.Name; got {selector}."
            );
    }

    /// <summary>"CreatedAt" → "Created at"; "sales overview" → "Sales overview".</summary>
    public static string Humanize(string identifier)
    {
        var sb = new StringBuilder(identifier.Length + 4);
        for (var i = 0; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(identifier[i - 1]))
            {
                sb.Append(' ');
                // Sentence case, but an acronym such as "MCP" keeps its capitals.
                var acronym = i + 1 < identifier.Length && char.IsUpper(identifier[i + 1]);
                c = acronym ? c : char.ToLowerInvariant(c);
            }
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }

    /// <summary>"TodoList" or "Todo list" → "todo-list"; the default id of a table, form or action.</summary>
    public static string Kebab(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsUpper(c) && i > 0 && char.IsLower(text[i - 1]))
                sb.Append('-');
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }
        var s = sb.ToString();
        while (s.Contains("--"))
            s = s.Replace("--", "-");
        return s.Trim('-');
    }
}

/// <summary>
/// What counts as one column value: the CLR primitives, plus any type that parses itself
/// (<see cref="IParsable{TSelf}"/>), which round-trips through routes and filters as its <c>ToString()</c>.
/// </summary>
internal static class ScalarTypes
{
    public static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive
            || t.IsEnum
            || t == typeof(string)
            || t == typeof(decimal)
            || t == typeof(Guid)
            || t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly)
            || t == typeof(TimeOnly)
            || t == typeof(TimeSpan)
            || ParseMethod(t) is not null;
    }

    public static bool IsInteger(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte);

    public static bool IsFraction(Type t) =>
        t == typeof(double) || t == typeof(float) || t == typeof(decimal);

    /// <summary>The element type of a list property; null for a scalar, a string or a byte array.</summary>
    public static Type? ElementType(Type type) =>
        IsScalar(type) || type == typeof(byte[])
            ? null
            : new[] { type }
                .Concat(type.GetInterfaces())
                .FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                )
                ?.GetGenericArguments()[0];

    public static object Parse(string text, Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string))
            return text;
        if (t.IsEnum)
            return Enum.Parse(t, text, ignoreCase: true);
        if (t == typeof(Guid))
            return Guid.Parse(text);
        if (t == typeof(DateTime))
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (t == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
        if (ParseMethod(t) is { } parse)
            return parse.Invoke(
                null,
                BindingFlags.DoNotWrapExceptions,
                null,
                [text, CultureInfo.InvariantCulture],
                null
            )!;
        return Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
    }

    /// <summary>False when <paramref name="value"/> does not convert to <paramref name="type"/>.</summary>
    public static bool TryCoerce(object? value, Type type, out object? result)
    {
        try
        {
            result = Coerce(value, type);
            return true;
        }
        catch (Exception ex)
            when (ex
                    is FormatException
                        or InvalidCastException
                        or OverflowException
                        or ArgumentException
            )
        {
            result = null;
            return false;
        }
    }

    public static object? Coerce(object? value, Type type)
    {
        if (value is null || value is string { Length: 0 } && type != typeof(string))
            return null;
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsInstanceOfType(value))
            return value;
        if (value is string text)
            return Parse(text, t);
        if (t.IsEnum)
            return Enum.ToObject(t, value);
        if (t == typeof(string))
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (value is IEnumerable items && ElementType(t) == typeof(string))
        {
            var strings = items.Cast<object?>().Select(i => i?.ToString() ?? "").ToList();
            return t.IsArray ? strings.ToArray() : strings;
        }
        if (t == typeof(DateOnly) && value is DateTime dt)
            return DateOnly.FromDateTime(dt);
        return Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
    }

    // IParsable<T>.Parse(string, IFormatProvider) as the type implements it; null for the CLR
    // primitives, which Convert.ChangeType already handles.
    private static MethodInfo? ParseMethod(Type t) =>
        t.IsPrimitive
        || t == typeof(decimal)
        || t == typeof(DateTime)
        || t == typeof(DateTimeOffset)
            ? null
        : t.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IParsable<>))
            ? t.GetMethod(
                "Parse",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(string), typeof(IFormatProvider)]
            )
        : null;
}

/// <summary>
/// Builds instances of a row or input type from values by property name, through the constructor
/// whose parameters name properties (a positional record), or the parameterless one and setters.
/// </summary>
internal sealed class RowFactory
{
    private readonly ConstructorInfo _constructor;
    private readonly ParameterInfo[] _parameters;
    private readonly IReadOnlyList<PropertyInfo> _properties;

    public RowFactory(Type type)
    {
        _properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToList();
        var names = _properties.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _constructor =
            type.GetConstructor(Type.EmptyTypes)
            ?? type.GetConstructors()
                .Where(c => c.GetParameters().All(p => names.Contains(p.Name!)))
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"{type.Name} needs a parameterless constructor or one whose parameters are named after its properties."
            );
        _parameters = _constructor.GetParameters();
    }

    /// <summary>The constructor parameter that sets <paramref name="property"/>, where its attributes may sit.</summary>
    public ParameterInfo? Parameter(string property) =>
        _parameters.FirstOrDefault(p =>
            string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase)
        );

    public bool CanSet(PropertyInfo property) =>
        Parameter(property.Name) is not null || property.SetMethod is { IsPublic: true };

    /// <summary>A new instance; a property not in <paramref name="values"/> keeps its default.</summary>
    public object Create(IReadOnlyDictionary<string, object?> values)
    {
        var named = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        var args = _parameters
            .Select(p =>
                named.TryGetValue(p.Name!, out var v) ? v
                : p.HasDefaultValue ? p.DefaultValue
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                : null
            )
            .ToArray();
        var row = _constructor.Invoke(args);
        foreach (var property in _properties)
            if (
                Parameter(property.Name) is null
                && property.SetMethod is not null
                && values.TryGetValue(property.Name, out var value)
            )
                property.SetValue(row, value);
        return row;
    }

    /// <summary>A copy of <paramref name="row"/> with <paramref name="overrides"/> applied.</summary>
    public object Copy(object row, IReadOnlyDictionary<string, object?> overrides)
    {
        // A private setter is copied too: a source that stores the row whole would reset it.
        var values = _properties
            .Where(p => p.SetMethod is not null || Parameter(p.Name) is not null)
            .ToDictionary(p => p.Name, p => p.GetValue(row), StringComparer.Ordinal);
        foreach (var (name, value) in overrides)
            values[name] = value;
        return Create(values);
    }

    /// <summary>The values a fresh instance starts with: its initializers, or the constructor defaults.</summary>
    public Dictionary<string, object?> Defaults()
    {
        if (_parameters.Length == 0)
        {
            var fresh = _constructor.Invoke([]);
            return _properties.ToDictionary(p => p.Name, p => p.GetValue(fresh));
        }
        return _parameters.ToDictionary(
            p =>
                _properties
                    .First(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                    .Name,
            p => p.HasDefaultValue ? p.DefaultValue : null
        );
    }
}

/// <summary>A table's key as a route segment: each part URL-escaped, so a composite key joins on "," and splits cleanly.</summary>
internal sealed class KeyCodec(IReadOnlyList<PropertyInfo> properties)
{
    public IReadOnlyList<PropertyInfo> Properties { get; } = properties;

    public object?[] Values(object row) => Properties.Select(p => p.GetValue(row)).ToArray();

    public string Encode(object row) => Encode(Values(row));

    public string Encode(object?[] values) =>
        string.Join(
            ',',
            values.Select(v =>
                Uri.EscapeDataString(
                    // "O" round-trips a date or time whole; the invariant default drops its fraction and kind.
                    v is DateTime or DateTimeOffset or DateOnly or TimeOnly
                        ? ((IFormattable)v).ToString("O", CultureInfo.InvariantCulture)
                        : Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
                )
            )
        );

    /// <summary>Null when <paramref name="key"/> is not a key of this table.</summary>
    public object?[]? TryDecode(string key)
    {
        var parts = key.Split(',');
        if (parts.Length != Properties.Count)
            return null;
        var values = new object?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (
                !ScalarTypes.TryCoerce(
                    Uri.UnescapeDataString(parts[i]),
                    Properties[i].PropertyType,
                    out values[i]
                ) || values[i] is null
            )
                return null;
        return values;
    }

    public object?[] Decode(string key) =>
        TryDecode(key) ?? throw new AdminRejection($"'{key}' is not a valid key.");
}
