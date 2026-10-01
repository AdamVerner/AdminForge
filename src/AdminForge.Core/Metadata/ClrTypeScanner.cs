using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AdminForge.Core.Metadata;

/// <summary>
/// Describes a plain CLR type — a read model a host-registered <c>IAdminDataProvider&lt;T&gt;</c>
/// serves — the way <c>EfCoreReflectionScanner</c> describes an EF entity. Scalar properties become
/// columns, lists become detail-only <see cref="ColumnKind.Collection"/> columns; the key is the
/// <see cref="KeyAttribute"/>-marked properties, else <c>Id</c>.
/// Nothing is known about what the provider can sort or filter on, so columns start with neither.
/// </summary>
public static class ClrTypeScanner
{
    public static EntityMeta Scan(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var nullability = new NullabilityInfoContext();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.CanRead
                && p.GetIndexParameters().Length == 0
                && (IsScalar(p.PropertyType) || ElementType(p.PropertyType) is not null)
            )
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
                if (!IsScalar(p.PropertyType))
                    return new ColumnMeta
                    {
                        PropertyName = p.Name,
                        Label = Humanize(p.Name),
                        ClrType = p.PropertyType,
                        IsNullable = isNullable,
                        Kind = ColumnKind.Collection,
                        ElementType = ElementType(p.PropertyType),
                        IsGenerated = true,
                        HiddenInEdit = true,
                        IsSortable = false,
                        IsFilterable = false,
                    };
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

    private static bool IsScalar(Type type) => ScalarTypes.IsScalar(type);

    /// <summary>The element type of a list property; null for a scalar, a string or a byte array.</summary>
    public static Type? ElementType(Type type)
    {
        if (IsScalar(type) || type == typeof(byte[]))
            return null;
        return new[] { type }
            .Concat(type.GetInterfaces())
            .FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            )
            ?.GetGenericArguments()[0];
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
