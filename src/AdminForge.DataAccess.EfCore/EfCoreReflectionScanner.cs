using AdminForge.Core.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AdminForge.DataAccess.EfCore;

/// <summary>
/// Walks a <see cref="DbContext"/>'s model and emits <see cref="EntityMeta"/>
/// instances populated with scalar, enum, and navigation columns. The output
/// feeds <c>AdminForgeBuilder.AddTable&lt;T&gt;</c>.
/// </summary>
public sealed class EfCoreReflectionScanner
{
    /// <summary>Scans the supplied context's model and returns one <see cref="EntityMeta"/> per CLR entity type.</summary>
    public IReadOnlyList<EntityMeta> Scan(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Scan(context.Model);
    }

    /// <summary>Scans a pre-built <see cref="IModel"/>. Exposed for tests that don't want a live context.</summary>
    public IReadOnlyList<EntityMeta> Scan(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var result = new List<EntityMeta>();
        foreach (var entityType in model.GetEntityTypes())
        {
            // Owned types appear as separate entity types in the EF model — skip them at the top
            // level; they're surfaced as Owned columns on their owners.
            if (entityType.IsOwned())
                continue;

            result.Add(BuildEntityMeta(entityType));
        }
        return result;
    }

    private static EntityMeta BuildEntityMeta(IEntityType entityType)
    {
        var clrType = entityType.ClrType;
        var columns = new List<ColumnMeta>();

        var primaryKey = entityType.FindPrimaryKey();
        var pkPropertyNames =
            primaryKey?.Properties.Select(p => p.Name).ToArray() ?? Array.Empty<string>();
        var pkPropertyNameSet = new HashSet<string>(pkPropertyNames, StringComparer.Ordinal);

        // Foreign keys: map FK CLR property -> owning navigation property name.
        var fkPropertyToNav = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fk in entityType.GetForeignKeys())
        {
            var nav = fk.DependentToPrincipal;
            if (nav is null)
                continue;
            foreach (var fkProperty in fk.Properties)
            {
                // First occurrence wins; composite FKs share the same nav.
                fkPropertyToNav.TryAdd(fkProperty.Name, nav.Name);
            }
        }

        // Scalar + enum properties.
        foreach (var property in entityType.GetProperties())
        {
            // EF surfaces shadow properties (e.g. join-table FKs) we cannot reflect on; skip.
            if (property.IsShadowProperty())
                continue;

            var clrPropertyType = property.ClrType;
            var underlying = Nullable.GetUnderlyingType(clrPropertyType) ?? clrPropertyType;
            var isEnum = underlying.IsEnum;

            var isFk = fkPropertyToNav.TryGetValue(property.Name, out var fkNav);
            var isPk = pkPropertyNameSet.Contains(property.Name);

            columns.Add(
                new ColumnMeta
                {
                    PropertyName = property.Name,
                    Label = Humanize(property.Name),
                    ClrType = clrPropertyType,
                    IsNullable = property.IsNullable,
                    Kind = isEnum ? ColumnKind.Enum : ColumnKind.Scalar,
                    IsPrimaryKey = isPk,
                    IsForeignKey = isFk,
                    ForeignKeyNavigation = fkNav,
                    EnumType = isEnum ? underlying : null,
                    IsGenerated =
                        property.ValueGenerated
                        != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never,
                    MaxLength = property.GetMaxLength(),
                    IsRequired = !property.IsNullable && !isPk, // PK rendered separately
                }
            );
        }

        // Navigation properties (reference + collection). Owned navigations surface as Owned columns.
        foreach (var navigation in entityType.GetNavigations())
        {
            var targetType = navigation.TargetEntityType;
            var kind = navigation.IsCollection
                ? ColumnKind.NavigationCollection
                : (targetType.IsOwned() ? ColumnKind.Owned : ColumnKind.NavigationReference);

            columns.Add(
                new ColumnMeta
                {
                    PropertyName = navigation.Name,
                    Label = Humanize(navigation.Name),
                    ClrType = navigation.ClrType,
                    IsNullable = !navigation.IsCollection,
                    Kind = kind,
                    RelatedEntityType = targetType.ClrType,
                    IsRequired = false,
                }
            );
        }

        // Skip-navigations represent the "many" side of implicit many-to-many relationships
        // (e.g. Todo.Tags backed by an implicit join entity).
        foreach (var skip in entityType.GetSkipNavigations())
        {
            columns.Add(
                new ColumnMeta
                {
                    PropertyName = skip.Name,
                    Label = Humanize(skip.Name),
                    ClrType = skip.ClrType,
                    IsNullable = false,
                    Kind = ColumnKind.NavigationCollection,
                    RelatedEntityType = skip.TargetEntityType.ClrType,
                    IsRequired = false,
                }
            );
        }

        // Detect implicit many-to-many join entities so the renderer can hide them from default nav.
        var isJoinEntity =
            entityType.HasSharedClrType
            && entityType.GetSkipNavigations().Any() == false
            && entityType.GetForeignKeys().Count() >= 2
            && entityType.IsImplicitlyCreatedJoinEntity();

        return new EntityMeta
        {
            ClrType = clrType,
            Name = clrType.Name,
            RouteName = clrType.Name,
            Label = Humanize(clrType.Name),
            Columns = columns,
            PrimaryKeyPropertyNames = pkPropertyNames,
            IsJoinEntity = isJoinEntity,
        };
    }

    /// <summary>
    /// Best-effort label humaniser: splits PascalCase into spaced words ("DueAt" → "Due At").
    /// Keeps already-spaced or all-lower labels as-is.
    /// </summary>
    internal static string Humanize(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;
        if (identifier.Contains(' '))
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

internal static class EntityTypeExtensions
{
    /// <summary>
    /// Heuristic for implicit many-to-many join entities: shared CLR type, exactly two FKs,
    /// and the FK columns make up the PK. EF 8+ exposes <c>IsImplicitlyCreatedJoinEntityType</c>
    /// — fall back to this heuristic which works against the public surface only.
    /// </summary>
    public static bool IsImplicitlyCreatedJoinEntity(this IEntityType entityType)
    {
        if (!entityType.HasSharedClrType)
            return false;
        var fks = entityType.GetForeignKeys().ToArray();
        if (fks.Length != 2)
            return false;
        var pk = entityType.FindPrimaryKey();
        if (pk is null)
            return false;
        var pkProps = pk.Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var fkProps = fks.SelectMany(f => f.Properties)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        return pkProps.SetEquals(fkProps);
    }
}
