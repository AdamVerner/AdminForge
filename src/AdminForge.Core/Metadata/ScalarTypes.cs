using System.Globalization;
using System.Reflection;

namespace AdminForge.Core.Metadata;

/// <summary>
/// What counts as one column value: the CLR primitives, plus any type that parses itself
/// (<see cref="IParsable{TSelf}"/>) — a strongly typed id, a money amount — which round-trips
/// through routes and filters as its <c>ToString()</c>.
/// </summary>
public static class ScalarTypes
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

    /// <summary>The text form back to <paramref name="type"/> (or its nullable underlying type).</summary>
    public static object Parse(string text, Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string))
            return text;
        if (t.IsEnum)
            return Enum.Parse(t, text, ignoreCase: true);
        if (t == typeof(Guid))
            return Guid.Parse(text);
        if (ParseMethod(t) is { } parse)
            return parse.Invoke(null, [text, CultureInfo.InvariantCulture])!;
        return Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
    }

    // IParsable<T>.Parse(string, IFormatProvider) as the type implements it; null for the CLR
    // primitives, which Convert.ChangeType already handles.
    private static MethodInfo? ParseMethod(Type t) =>
        t.IsPrimitive || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
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
