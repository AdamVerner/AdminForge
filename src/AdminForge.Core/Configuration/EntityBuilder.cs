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
    /// Tweak a single column (label, helper text, validators, visibility). Note:
    /// this does <em>not</em> opt the column into list views — use the
    /// <see cref="AddColumn{TProp}(Expression{Func{T, TProp}}, Action{ColumnBuilder{TProp}}?)"/>
    /// overload for that. (Both surfaces accept a <see cref="ColumnBuilder{TProp}"/>
    /// so they're functionally interchangeable apart from list opt-in semantics.)
    /// </summary>
    public EntityBuilder<T> Column<TProp>(
        Expression<Func<T, TProp>> selector,
        Action<ColumnBuilder<TProp>> configure
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

        configure(new ColumnBuilder<TProp>(column));
        return this;
    }

    /// <summary>
    /// Opt an auto-discovered column into the list view (and the filter bar). Lists
    /// are opt-in: by default no auto-discovered column appears in the table — the
    /// host calls <c>AddColumn(t =&gt; t.Title)</c> for each column it wants visible.
    /// The optional <paramref name="configure"/> callback tweaks the same surface
    /// area as <see cref="Column{TProp}"/> (label, description, validators, etc.).
    /// <para>
    /// Custom computed columns (added via the
    /// <see cref="AddColumn{TValue}(string, Action{CustomColumnBuilder{T, TValue}})"/>
    /// overload) are list-visible automatically.
    /// </para>
    /// </summary>
    public EntityBuilder<T> AddColumn<TProp>(
        Expression<Func<T, TProp>> selector,
        Action<ColumnBuilder<TProp>>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(selector);
        var propertyName = GetPropertyName(selector);
        if (!_columnsByName.TryGetValue(propertyName, out var column))
        {
            throw new InvalidOperationException(
                $"Column '{propertyName}' was not discovered on entity '{typeof(T).Name}'."
            );
        }
        if (column.Kind == ColumnKind.NavigationCollection)
        {
            throw new InvalidOperationException(
                $"Column '{propertyName}' is a navigation collection and cannot be added to the list view."
            );
        }
        column.ShowInList = true;
        configure?.Invoke(new ColumnBuilder<TProp>(column));
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
        column.ShowInList = false;
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
            // Custom (computed) columns are list-visible by default — the user added
            // them precisely so they'd render in the table.
            ShowInList = true,
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
            ShowInList = column.ShowInList,
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
    /// Register an opt-in custom create handler. When set, the bridge bypasses the
    /// data provider's <c>CreateAsync</c> path entirely on the entity-create page:
    /// the form is still auto-built from the column metadata and the typed entity
    /// is materialised from the submitted values, but persistence (and any business
    /// rules around it) is the handler's responsibility.
    /// <para>
    /// Return <see cref="CreateResult.Ok(object)"/> with the new entity's identifier
    /// on success; the bridge emits an <c>AuditAction.Create</c> event and the page
    /// navigates to the entity view. Return <see cref="CreateResult.Error(string)"/>
    /// to reject the submission — no audit is emitted and the message is surfaced
    /// inline on the create form (the bridge wraps the failure in an
    /// <see cref="EntityCreateFailedException"/>).
    /// </para>
    /// <para>
    /// The handler runs inside a fresh DI scope so it can resolve scoped services
    /// (e.g. its own <c>DbContext</c>) without entangling with the bridge's request
    /// scope. v1 caveat: the returned <c>Id</c> is round-tripped to the entity-view
    /// route as <c>Uri.EscapeDataString(Id.ToString())</c> — single-property keys
    /// (<see cref="Guid"/>, <see cref="int"/>, <see cref="long"/>, <see cref="string"/>)
    /// round-trip cleanly; composite keys are out of scope.
    /// </para>
    /// </summary>
    public EntityBuilder<T> OnCreate(
        Func<IServiceProvider, T, IActionContext, CancellationToken, Task<CreateResult>> handler
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_meta.CustomCreateHandler is not null)
        {
            throw new InvalidOperationException(
                $"A custom create handler is already registered on entity '{typeof(T).Name}'."
            );
        }
        _meta.CustomCreateHandler = (sp, obj, ctx, ct) => handler(sp, (T)obj, ctx, ct);
        return this;
    }

    /// <summary>
    /// Register an opt-in custom update handler. When set, the bridge bypasses the
    /// data provider's <c>UpdateAsync</c> path entirely on the entity-edit page:
    /// the bridge loads the existing row from the data provider (passed as
    /// <em>original</em>), materialises the patched instance from the submitted
    /// form values (passed as <em>patched</em>), and dispatches to this delegate.
    /// <para>
    /// Return <see cref="UpdateResult.Ok"/> on success; the bridge emits an
    /// <c>AuditAction.Update</c> event with a before/after diff and the page
    /// navigates back to the entity view. Return <see cref="UpdateResult.Error(string)"/>
    /// to reject the submission — no audit is emitted and the message is surfaced
    /// inline on the edit form (the bridge wraps the failure in an
    /// <see cref="EntityUpdateFailedException"/>).
    /// </para>
    /// <para>
    /// The handler runs inside a fresh DI scope so it can resolve scoped services
    /// (e.g. its own <c>DbContext</c>) without entangling with the bridge's request
    /// scope. The before-snapshot for audit is captured from <em>original</em>
    /// before the handler runs; the after-snapshot is captured from <em>patched</em>.
    /// </para>
    /// </summary>
    public EntityBuilder<T> OnUpdate(
        Func<
            IServiceProvider,
            T /*original*/
            ,
            T /*patched*/
            ,
            IActionContext,
            CancellationToken,
            Task<UpdateResult>
        > handler
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_meta.CustomUpdateHandler is not null)
        {
            throw new InvalidOperationException(
                $"A custom update handler is already registered on entity '{typeof(T).Name}'."
            );
        }
        _meta.CustomUpdateHandler = (sp, original, patched, ctx, ct) =>
            handler(sp, (T)original, (T)patched, ctx, ct);
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
