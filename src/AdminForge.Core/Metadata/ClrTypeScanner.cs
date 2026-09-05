using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AdminForge.Core.Metadata;

/// <summary>
/// Describes a plain CLR type — a read model a host-registered <c>IAdminDataProvider&lt;T&gt;</c>
/// serves — the way <c>EfCoreReflectionScanner</c> describes an EF entity. Only scalar properties
/// become columns; the key is the <see cref="KeyAttribute"/>-marked properties, else <c>Id</c>.
/// Nothing is known about what the provider can sort or filter on, so columns start with neither.
/// </summary>
public static class ClrTypeScanner
{
    public static EntityMeta Scan(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var nullability = new NullabilityInfoContext();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && IsScalar(p.PropertyType))
            .ToList();

        var keys = properties
            .Where(p => p.GetCustomAttribute<KeyAttribute>() is not null)
            .Select(p => p.Name)
            .ToList();
        if (keys.Count == 0 && properties.Any(p => p.Name == "Id"))
            keys = ["Id"];
        if (keys.Count == 0)
            throw new InvalidOperationException(
                $"'{type.Name}' has no key: mark one or more properties with [Key], or name one 'Id'."
            );

        var columns = properties
            .Select(p =>
            {
                var underlying = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                var isNullable =
                    Nullable.GetUnderlyingType(p.PropertyType) is not null
                    || nullability.Create(p).ReadState == NullabilityState.Nullable;
                var isKey = keys.Contains(p.Name);
                return new ColumnMeta
                {
                    PropertyName = p.Name,
                    Label = Humanize(p.Name),
                    ClrType = p.PropertyType,
                    IsNullable = isNullable,
                    Kind = underlying.IsEnum ? ColumnKind.Enum : ColumnKind.Scalar,
                    IsPrimaryKey = isKey,
                    EnumType = underlying.IsEnum ? underlying : null,
                    IsGenerated = isKey,
                    IsRequired = !isNullable && !isKey,
                    IsSortable = false,
                    IsFilterable = false,
                };
            })
            .ToList();

        return new EntityMeta
        {
            ClrType = type,
            Name = type.Name,
            RouteName = type.Name,
            Label = Humanize(type.Name),
            Columns = columns,
            PrimaryKeyPropertyNames = keys,
        };
    }

    private static bool IsScalar(Type type)
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
            || t == typeof(TimeSpan);
    }

    /// <summary>"CreatedAt" → "Created At"; an identifier already containing spaces is kept.</summary>
    public static string Humanize(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.Contains(' '))
            return identifier;

        var buffer = new System.Text.StringBuilder(identifier.Length + 4);
        for (var i = 0; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(identifier[i - 1]))
                buffer.Append(' ');
            buffer.Append(c);
        }
        return buffer.ToString();
    }
}
