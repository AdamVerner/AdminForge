using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;

namespace AdminForge.Core.Metadata;

/// <summary>
/// A registered generic form. Lives outside the entity model — the consumer
/// supplies the fields and a submit handler. Built fluently via
/// <c>FormBuilder</c> and registered through <c>AdminForgeBuilder.AddForm</c>.
/// </summary>
public sealed class FormMeta
{
    /// <summary>
    /// URL-safe identifier for this form. Used to resolve
    /// <c>/admin/forms/{RouteName}</c> and as the key in the audit
    /// <c>EntityType</c> field (<c>"Form:{RouteName}"</c>).
    /// </summary>
    public required string RouteName { get; init; }

    /// <summary>Display title rendered above the form.</summary>
    public required string Title { get; set; }

    /// <summary>Optional helper text rendered below the title.</summary>
    public string? Description { get; set; }

    /// <summary>Fields in declaration order.</summary>
    public List<FieldMeta> Fields { get; } = [];

    /// <summary>Sidebar nav entry.</summary>
    public NavMeta Nav { get; set; } = new();

    /// <summary>
    /// Handler invoked when the form is submitted and passes validation.
    /// Runs inside a freshly created DI scope so it can resolve scoped
    /// services (e.g. a DbContext) without entangling with the request scope.
    /// </summary>
    public Func<IServiceProvider, FormSubmission, IActionContext, Task>? Submit { get; set; }
}
