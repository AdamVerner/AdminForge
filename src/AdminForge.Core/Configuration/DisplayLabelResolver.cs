using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Default strategy for picking a short, human-readable label for an entity instance
/// (used when the entity is referenced as a navigation target). Lookup order:
/// <list type="number">
///   <item>Property carrying a <see cref="DisplayAttribute"/> (uses its value, not the attribute's Name).</item>
///   <item>Property named <c>Name</c>, <c>Title</c>, <c>Label</c>, <c>DisplayName</c>, or <c>Email</c>.</item>
///   <item>Primary key value(s), joined with "-".</item>
/// </list>
/// Strategy lives in <c>Core</c> so it stays UI-agnostic. Overridable per-entity
/// via <c>EntityBuilder&lt;T&gt;.DisplayMember(...)</c>.
/// </summary>
public static class DisplayLabelResolver
{
    private static readonly string[] PreferredPropertyNames =
        ["Name", "Title", "Label", "DisplayName", "Email"];

    // Cache the resolved property selector per entity type so reflection happens once.
    private static readonly ConcurrentDictionary<Type, Func<object, string>> Cache = new();

    /// <summary>
    /// Builds and caches a label resolver for <paramref name="entityType"/> using the
    /// default heuristic. <paramref name="primaryKeyNames"/> is the fallback used when
    /// no preferred property exists.
    /// </summary>
    public static Func<object, string> Build(Type entityType, IReadOnlyList<string> primaryKeyNames)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(primaryKeyNames);

        return Cache.GetOrAdd(entityType, t => BuildCore(t, primaryKeyNames));
    }

    private static Func<object, string> BuildCore(Type entityType, IReadOnlyList<string> pkNames)
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // 1. Display(Name=...) attribute wins.
        var attributed = properties.FirstOrDefault(p =>
            p.GetCustomAttribute<DisplayAttribute>() is not null && CanRead(p)
        );
        if (attributed is not null)
            return BuildSingleProp(attributed);

        // 2. Conventional names.
        foreach (var preferred in PreferredPropertyNames)
        {
            var match = properties.FirstOrDefault(p =>
                string.Equals(p.Name, preferred, StringComparison.Ordinal) && CanRead(p)
            );
            if (match is not null)
                return BuildSingleProp(match);
        }

        // 3. Primary key fallback.
        var pkProperties = pkNames
            .Select(name => properties.FirstOrDefault(p => p.Name == name))
            .Where(p => p is not null)
            .Cast<PropertyInfo>()
            .ToArray();
        if (pkProperties.Length == 0)
            return _ => entityType.Name;

        return instance =>
        {
            var parts = pkProperties.Select(p => p.GetValue(instance)?.ToString() ?? string.Empty);
            return string.Join("-", parts);
        };
    }

    private static Func<object, string> BuildSingleProp(PropertyInfo property) =>
        instance => property.GetValue(instance)?.ToString() ?? string.Empty;

    private static bool CanRead(PropertyInfo p) => p.CanRead && p.GetIndexParameters().Length == 0;
}
