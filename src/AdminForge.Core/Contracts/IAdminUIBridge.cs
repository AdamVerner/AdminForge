using AdminForge.Core.Configuration;
using AdminForge.Core.Metadata;
using AdminForge.Core.ViewModels;

namespace AdminForge.Core.Contracts;

/// <summary>
/// Renderer-agnostic surface between AdminForge.Core and a UI implementation
/// (today: Blazor; tomorrow: React via JSON controllers).
/// <para>
/// The bridge owns reflection-based dispatch over heterogeneous entity types —
/// the renderer talks to it in <em>strings</em> (entity route names, string-encoded
/// primary keys), and the bridge resolves the right <see cref="IAdminDataProvider{T}"/>
/// per request.
/// </para>
/// </summary>
public interface IAdminUIBridge
{
    /// <summary>All registered entities, in registration order.</summary>
    IReadOnlyList<EntityMeta> Entities { get; }

    /// <summary>All registered dashboards, in registration order.</summary>
    IReadOnlyList<DashboardMeta> Dashboards { get; }

    /// <summary>All registered generic forms, in registration order.</summary>
    IReadOnlyList<FormMeta> Forms { get; }

    /// <summary>Find a registered entity by its <see cref="EntityMeta.RouteName"/> (case-insensitive).</summary>
    EntityMeta? FindEntityByRouteName(string routeName);

    /// <summary>Returns a list-page view model for the given entity + query.</summary>
    Task<EntityListVM> ListAsync(
        EntityMeta entity,
        ListQuery query,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns a read-only view of a single entity, or null when missing.</summary>
    Task<EntityViewVM?> FindAsync(
        EntityMeta entity,
        string encodedKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Loads the entity by key and returns an editable VM (raw values keyed by property name),
    /// or null when missing.
    /// </summary>
    Task<EntityEditVM?> LoadForEditAsync(
        EntityMeta entity,
        string encodedKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a fresh edit VM for a new entity (default values).
    /// </summary>
    EntityEditVM NewEditModel(EntityMeta entity);

    /// <summary>Persists a new entity from an edit VM. Returns the encoded key of the new instance.</summary>
    Task<string> CreateAsync(
        EntityMeta entity,
        EntityEditVM model,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists changes from an edit VM. <paramref name="model.Key"/> identifies the row.
    /// </summary>
    Task UpdateAsync(
        EntityMeta entity,
        EntityEditVM model,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes an entity by key. Returns false when nothing was found.</summary>
    Task<bool> DeleteAsync(
        EntityMeta entity,
        string encodedKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Searches a related entity for navigation-picker results. Matches against
    /// every string scalar column via the data provider's <c>Search</c> hook and
    /// projects rows into <see cref="NavigationRef"/> (encoded key + display label).
    /// </summary>
    /// <param name="relatedType">CLR type of the related entity (must be registered).</param>
    /// <param name="search">Free-text query, null/empty returns the top page.</param>
    /// <param name="take">Maximum results to return.</param>
    Task<IReadOnlyList<NavigationRef>> SearchRelatedAsync(
        Type relatedType,
        string? search,
        int take = 25,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the <see cref="NavigationRef"/> for a single related-entity row by its
    /// encoded key — used by the navigation-property picker to display the current
    /// value when editing.
    /// </summary>
    Task<NavigationRef?> FindRelatedAsync(
        Type relatedType,
        string encodedKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Find a registered dashboard by its <see cref="DashboardMeta.RouteName"/> (case-insensitive).</summary>
    DashboardMeta? FindDashboardByRouteName(string routeName);

    /// <summary>
    /// Materialises a dashboard view by invoking every widget's data delegate inside
    /// a scoped <see cref="IServiceProvider"/>.
    /// </summary>
    Task<DashboardVM> LoadDashboardAsync(
        DashboardMeta dashboard,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Invoke a custom action registered via <c>AddAction</c>. Loads the entity by key,
    /// authorizes via <see cref="IAdminAuthorizationPolicy"/> (action name supplied),
    /// runs the handler inside a fresh DI scope, and emits an
    /// <see cref="AuditAction.Custom"/> audit event.
    /// </summary>
    Task InvokeActionAsync(
        string entityRouteName,
        string encodedKey,
        string actionName,
        IActionContext context,
        CancellationToken cancellationToken = default
    );

    /// <summary>Lightweight nav projection of every registered form.</summary>
    IReadOnlyList<FormSummary> ListForms();

    /// <summary>Build a fresh form view model for the given route, or null when missing.</summary>
    FormVM? GetForm(string routeName);

    /// <summary>
    /// Validate and submit a form. Throws <see cref="FormValidationException"/>
    /// when validation fails and <see cref="AdminForbiddenException"/> when the
    /// authorization policy denies the submission. On success the handler runs
    /// in a fresh DI scope and an <see cref="AuditAction.FormSubmit"/> event is
    /// emitted.
    /// </summary>
    Task SubmitFormAsync(
        string routeName,
        FormSubmission submission,
        IActionContext? context,
        CancellationToken cancellationToken = default
    );
}
