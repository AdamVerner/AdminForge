using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface for tweaking the metadata of one entity registered with
/// <c>AdminForgeBuilder.AddTable&lt;T&gt;</c>. All overrides mutate the wrapped
/// <see cref="EntityMeta"/> in place; the meta is built immutably afterwards.
/// </summary>
public sealed class EntityBuilder<T>
    where T : class
{
    private readonly EntityMeta _meta;
    private readonly Dictionary<string, ColumnMeta> _columnsByName;

    internal EntityBuilder(EntityMeta meta)
    {
        _meta = meta;
        _columnsByName = meta.Columns.ToDictionary(c => c.PropertyName, StringComparer.Ordinal);
    }

    /// <summary>Override the entity's display label.</summary>
    public EntityBuilder<T> Label(string label)
    {
        _meta.Label = label;
        return this;
    }

    /// <summary>Customise the sidebar nav entry (label, group, order, hidden).</summary>
    public EntityBuilder<T> Nav(Action<NavBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new NavBuilder(_meta.Nav));
        return this;
    }

    /// <summary>
    /// Tweak a single column (label, helper text, validators, visibility).
    /// </summary>
    public EntityBuilder<T> Column<TProp>(
        Expression<Func<T, TProp>> selector,
        Action<ColumnBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(configure);

        var propertyName = GetPropertyName(selector);
        if (!_columnsByName.TryGetValue(propertyName, out var column))
        {
            throw new InvalidOperationException(
                $"Column '{propertyName}' was not discovered on entity '{typeof(T).Name}'."
            );
        }

        configure(new ColumnBuilder(column));
        return this;
    }

    /// <summary>
    /// Override the <c>DisplayLabel</c> resolver — the short string shown when this
    /// entity is referenced as a navigation target. Defaults to the heuristic in
    /// <see cref="DisplayLabelResolver"/>.
    /// </summary>
    public EntityBuilder<T> DisplayMember<TProp>(Expression<Func<T, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var compiled = selector.Compile();
        _meta.DisplayLabel = instance =>
        {
            var value = compiled((T)instance);
            return value?.ToString() ?? string.Empty;
        };
        return this;
    }

    /// <summary>
    /// Hide a single auto-discovered column from list and edit surfaces (the column is
    /// still part of the entity model and is editable through the underlying provider).
    /// </summary>
    public EntityBuilder<T> HideColumn<TProp>(Expression<Func<T, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var propertyName = GetPropertyName(selector);
        if (!_columnsByName.TryGetValue(propertyName, out var column))
        {
            throw new InvalidOperationException(
                $"Column '{propertyName}' was not discovered on entity '{typeof(T).Name}'."
            );
        }
        column.HiddenInList = true;
        column.HiddenInEdit = true;
        return this;
    }

    /// <summary>
    /// Add a custom computed column. <paramref name="name"/> doubles as the property key
    /// (used for sort/filter routing); the configure callback must call
    /// <c>From(...)</c> with a server-side projection — failing to do so throws.
    /// </summary>
    public EntityBuilder<T> AddColumn<TValue>(
        string name,
        Action<CustomColumnBuilder<T, TValue>> configure
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        if (_columnsByName.ContainsKey(name))
        {
            throw new InvalidOperationException(
                $"Column '{name}' is already defined on entity '{typeof(T).Name}'."
            );
        }

        var column = new ColumnMeta
        {
            PropertyName = name,
            Label = name,
            ClrType = typeof(TValue),
            IsNullable =
                !typeof(TValue).IsValueType
                || Nullable.GetUnderlyingType(typeof(TValue)) is not null,
            Kind = ColumnKind.Scalar,
            IsCustom = true,
            // Computed columns default to opt-in for sort/filter — the user enables
            // each per call so the SQL surface stays predictable.
            IsSortable = false,
            IsFilterable = false,
        };

        var builder = new CustomColumnBuilder<T, TValue>(column);
        configure(builder);

        if (builder.Selector is null)
        {
            throw new InvalidOperationException(
                $"Custom column '{name}' on entity '{typeof(T).Name}' is missing a .From(...) selector."
            );
        }

        // Stamp the lambda onto the meta in a non-strongly-typed slot so the provider
        // can consume it without knowing TValue.
        var finalMeta = new ColumnMeta
        {
            PropertyName = column.PropertyName,
            Label = column.Label,
            ClrType = column.ClrType,
            IsNullable = column.IsNullable,
            Kind = column.Kind,
            IsPrimaryKey = false,
            IsForeignKey = false,
            ForeignKeyNavigation = null,
            RelatedEntityType = null,
            EnumType = null,
            IsGenerated = true, // never written back through the provider
            MaxLength = null,
            IsRequired = false,
            Description = column.Description,
            HiddenInList = column.HiddenInList,
            HiddenInEdit = true, // computed columns are read-only
            IsCustom = true,
            CustomValueSelector = builder.Selector,
            IsSortable = column.IsSortable,
            IsFilterable = column.IsFilterable,
        };
        _meta.Columns.Add(finalMeta);
        _columnsByName[name] = finalMeta;
        return this;
    }

    /// <summary>
    /// Register a custom entity-level action (button on the entity view page).
    /// </summary>
    public EntityBuilder<T> AddAction(
        string name,
        Func<IServiceProvider, T, IActionContext, Task> handler,
        Action<ActionBuilder>? configure = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);

        if (_meta.Actions.Any(a => string.Equals(a.Name, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Action '{name}' is already registered on entity '{typeof(T).Name}'."
            );
        }

        var meta = new ActionMeta
        {
            Name = name,
            Handler = (sp, instance, ctx) => handler(sp, (T)instance, ctx),
        };
        configure?.Invoke(new ActionBuilder(meta));
        _meta.Actions.Add(meta);
        return this;
    }

    /// <summary>
    /// Enable live polling on the <em>entity view</em> page for this entity. While the
    /// view is mounted the page re-fetches the displayed row every
    /// <paramref name="interval"/> via the existing data provider's find-by-key path
    /// — no separate delegate is required. The entity <em>list</em> is NOT polled
    /// (table-level live updates were removed after the initial Phase 5 build).
    /// </summary>
    public EntityBuilder<T> WithLivePolling(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "Interval must be positive."
            );
        _meta.LivePollingInterval = interval;
        return this;
    }

    /// <summary>
    /// Suppress the auto-generated "View N {label}" link for a collection navigation.
    /// </summary>
    public EntityBuilder<T> HideRelatedLink<TCollection>(Expression<Func<T, TCollection>> selector)
        where TCollection : IEnumerable
    {
        ArgumentNullException.ThrowIfNull(selector);
        var navName = GetPropertyName(selector);
        _meta.HiddenRelatedNavigations.Add(navName);
        return this;
    }

    /// <summary>
    /// Override the label/icon of an auto-generated collection-nav related link
    /// (or, when the nav was suppressed, restore it).
    /// </summary>
    public EntityBuilder<T> RelatedLink<TCollection>(
        Expression<Func<T, TCollection>> selector,
        Action<RelatedLinkBuilder> configure
    )
        where TCollection : IEnumerable
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(configure);
        var navName = GetPropertyName(selector);

        // The collection-element type is the related entity.
        var collectionType = typeof(TCollection);
        var elementType =
            collectionType
                .GetInterfaces()
                .Append(collectionType)
                .Where(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                )
                .Select(i => i.GetGenericArguments()[0])
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Cannot resolve element type for collection navigation '{navName}'."
            );

        // FilterBuilder is wired by the bridge when it materialises the link (we don't
        // know the inverse-FK property until we have an EF model). Stash a stub here
        // and let BlazorUIBridge swap it for the real one keyed by SourceNavigationName.
        var meta = new RelatedLinkMeta
        {
            RelatedEntityType = elementType,
            Label = navName,
            SourceNavigationName = navName,
            FilterBuilder = static _ => new Dictionary<string, object?>(),
        };
        configure(new RelatedLinkBuilder(meta));
        // Drop any existing explicit registration for the same nav so the latest wins.
        for (var i = _meta.RelatedLinks.Count - 1; i >= 0; i--)
        {
            if (
                string.Equals(
                    _meta.RelatedLinks[i].SourceNavigationName,
                    navName,
                    StringComparison.Ordinal
                )
            )
                _meta.RelatedLinks.RemoveAt(i);
        }
        _meta.RelatedLinks.Add(meta);
        return this;
    }

    /// <summary>
    /// Register a cross-entity related link with an arbitrary predicate. The predicate
    /// must decompose into a conjunction of <c>target.Prop == source.X</c> equalities;
    /// the source-side expressions are evaluated against the source instance at runtime
    /// to produce a filter dictionary keyed by the target's property names.
    /// </summary>
    public EntityBuilder<T> RelatedLink<TTarget>(
        string label,
        Expression<Func<T, Expression<Func<TTarget, bool>>>> predicateBuilder
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(predicateBuilder);

        // Recover the inner Expression<Func<TTarget,bool>> from the outer builder.
        // Callers write `source => target => target.X == source.Y`. The C# compiler
        // wraps the inner lambda in `UnaryExpression { NodeType = Quote }` when the
        // outer type's return is itself an Expression<>; unwrap when present.
        Expression innerBody = predicateBuilder.Body;
        if (innerBody is UnaryExpression { NodeType: ExpressionType.Quote } quoted)
            innerBody = quoted.Operand;
        if (innerBody is not LambdaExpression inner)
        {
            throw new ArgumentException(
                "RelatedLink<TTarget> expects an expression of the form "
                    + "`source => target => predicate`.",
                nameof(predicateBuilder)
            );
        }
        if (inner.Parameters.Count != 1 || inner.Parameters[0].Type != typeof(TTarget))
        {
            throw new ArgumentException(
                $"Inner predicate must take a single {typeof(TTarget).Name} parameter.",
                nameof(predicateBuilder)
            );
        }

        var sourceParam = predicateBuilder.Parameters[0];
        var targetParam = inner.Parameters[0];

        // Decompose body into target.Prop == sourceExpr clauses.
        var equalities = new List<(string TargetProperty, Func<T, object?> SourceEvaluator)>();
        foreach (var clause in FlattenAndAlso(inner.Body))
        {
            if (clause is not BinaryExpression { NodeType: ExpressionType.Equal } eq)
            {
                throw new ArgumentException(
                    $"RelatedLink predicate must be a conjunction of equality checks; got '{clause}'.",
                    nameof(predicateBuilder)
                );
            }

            var (targetSide, sourceSide) =
                IdentifyTargetSide(eq.Left, eq.Right, targetParam)
                ?? throw new ArgumentException(
                    $"Each predicate clause must have one side be a member access on the target parameter; got '{clause}'.",
                    nameof(predicateBuilder)
                );

            if (
                targetSide is not MemberExpression targetMember
                || targetMember.Member is not PropertyInfo targetProp
            )
            {
                throw new ArgumentException(
                    $"Target side of equality must be a property access; got '{targetSide}'.",
                    nameof(predicateBuilder)
                );
            }

            // Compile the source-side expression in isolation so we can evaluate it
            // against the source instance later. It may be a captured constant (no
            // parameter) or a member access on `sourceParam`.
            var sourceLambda = Expression.Lambda(
                Expression.Convert(sourceSide, typeof(object)),
                sourceParam
            );
            var compiled = (Func<T, object?>)sourceLambda.Compile();
            equalities.Add((targetProp.Name, compiled));
        }

        if (equalities.Count == 0)
        {
            throw new ArgumentException(
                "RelatedLink predicate decomposed to zero equality clauses.",
                nameof(predicateBuilder)
            );
        }

        Func<object, IReadOnlyDictionary<string, object?>> filterBuilder = source =>
        {
            var typed = (T)source;
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (propName, evaluator) in equalities)
            {
                dict[propName] = evaluator(typed);
            }
            return dict;
        };

        var meta = new RelatedLinkMeta
        {
            RelatedEntityType = typeof(TTarget),
            Label = label,
            FilterBuilder = filterBuilder,
            SourceNavigationName = null, // cross-entity, no source nav
        };
        _meta.RelatedLinks.Add(meta);
        return this;
    }

    private static IEnumerable<Expression> FlattenAndAlso(Expression node)
    {
        if (node is BinaryExpression { NodeType: ExpressionType.AndAlso } and)
        {
            foreach (var left in FlattenAndAlso(and.Left))
                yield return left;
            foreach (var right in FlattenAndAlso(and.Right))
                yield return right;
        }
        else
        {
            yield return node;
        }
    }

    /// <summary>
    /// Returns (targetSide, sourceSide) if exactly one operand of <paramref name="left"/>/<paramref name="right"/>
    /// dereferences <paramref name="targetParam"/>. Null when neither or both sides reference the target.
    /// </summary>
    private static (Expression Target, Expression Source)? IdentifyTargetSide(
        Expression left,
        Expression right,
        ParameterExpression targetParam
    )
    {
        var leftRefsTarget = ReferencesParameter(left, targetParam);
        var rightRefsTarget = ReferencesParameter(right, targetParam);
        if (leftRefsTarget && !rightRefsTarget)
            return (left, right);
        if (rightRefsTarget && !leftRefsTarget)
            return (right, left);
        return null;
    }

    private static bool ReferencesParameter(Expression expression, ParameterExpression target)
    {
        var visitor = new ParameterFinder(target);
        visitor.Visit(expression);
        return visitor.Found;
    }

    private sealed class ParameterFinder(ParameterExpression target) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == target)
                Found = true;
            return base.VisitParameter(node);
        }
    }

    private static string GetPropertyName<TProp>(Expression<Func<T, TProp>> selector)
    {
        // Unwrap conversions inserted for value-type → object selectors etc.
        Expression body = selector.Body;
        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            body = unary.Operand;

        if (body is MemberExpression member && member.Member is PropertyInfo property)
            return property.Name;

        throw new ArgumentException(
            $"Expected a simple property access expression, got: {selector}",
            nameof(selector)
        );
    }
}
