using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AdminForge.DataAccess.EfCore;

/// <summary>
/// Materialises and (de)serialises primary-key values for an entity. Each part is URL-component
/// encoded, which never lets a comma through, so a composite key is joined with "," and splits
/// cleanly whatever the parts hold.
/// </summary>
public sealed class KeyAccessor
{
    private const char Separator = ',';

    private readonly IReadOnlyList<KeyProperty> _keyProperties;

    public KeyAccessor(IEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        var primaryKey =
            entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity '{entityType.ClrType.Name}' has no primary key — AdminForge requires keyed entities."
            );
        _keyProperties = primaryKey
            .Properties.Select(p => new KeyProperty(p.Name, p.ClrType, p.PropertyInfo))
            .ToList();
    }

    /// <summary>For a type outside the EF model: the key is whatever the metadata names.</summary>
    public KeyAccessor(Type clrType, IEnumerable<string> keyPropertyNames)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        ArgumentNullException.ThrowIfNull(keyPropertyNames);
        _keyProperties = keyPropertyNames
            .Select(name =>
            {
                var property =
                    clrType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException(
                        $"Key property '{name}' does not exist on '{clrType.Name}'."
                    );
                return new KeyProperty(name, property.PropertyType, property);
            })
            .ToList();
        if (_keyProperties.Count == 0)
            throw new InvalidOperationException(
                $"'{clrType.Name}' has no primary key — AdminForge requires keyed entities."
            );
    }

    /// <summary>The properties that make up the primary key, in declared order.</summary>
    public IReadOnlyList<KeyProperty> KeyProperties => _keyProperties;

    /// <summary>Extracts the boxed PK values from an entity instance, suitable for <see cref="DbContext.Find"/>.</summary>
    public object?[] GetKeyValues(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var values = new object?[_keyProperties.Count];
        for (var i = 0; i < _keyProperties.Count; i++)
        {
            values[i] = _keyProperties[i].PropertyInfo?.GetValue(entity);
        }
        return values;
    }

    /// <summary>Encodes the key as a routable string.</summary>
    public string EncodeKey(object entity)
    {
        var values = GetKeyValues(entity);
        return EncodeKeyValues(values);
    }

    /// <summary>Encodes pre-extracted key values to the routable string form.</summary>
    public string EncodeKeyValues(object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != _keyProperties.Count)
        {
            throw new ArgumentException(
                $"Expected {_keyProperties.Count} key value(s), got {values.Length}.",
                nameof(values)
            );
        }

        return string.Join(Separator, values.Select(EncodePart));
    }

    public static string EncodePart(object? value) =>
        Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);

    /// <summary>
    /// Decodes a routable string key back into typed key values suitable for
    /// <see cref="DbContext.Find"/>. Each part is converted to the underlying CLR
    /// type via <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/>.
    /// </summary>
    public object?[] DecodeKey(string encoded)
    {
        ArgumentException.ThrowIfNullOrEmpty(encoded);

        var parts = encoded.Split(Separator);
        if (parts.Length != _keyProperties.Count)
        {
            throw new ArgumentException(
                $"Expected {_keyProperties.Count} key part(s), got {parts.Length}.",
                nameof(encoded)
            );
        }

        var values = new object?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var raw = Uri.UnescapeDataString(parts[i]);
            var targetType =
                Nullable.GetUnderlyingType(_keyProperties[i].ClrType) ?? _keyProperties[i].ClrType;
            values[i] = ConvertValue(raw, targetType);
        }
        return values;
    }

    private static object ConvertValue(string raw, Type targetType)
    {
        if (targetType == typeof(string))
            return raw;
        if (targetType == typeof(Guid))
            return Guid.Parse(raw);
        return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture)!;
    }
}

/// <summary>One primary-key property. <paramref name="PropertyInfo"/> is null for an EF shadow property.</summary>
public sealed record KeyProperty(string Name, Type ClrType, PropertyInfo? PropertyInfo);
