namespace AdminForge.Core.Configuration;

/// <summary>
/// The four CRUD operations gated by per-entity authorization policies.
/// </summary>
public enum AdminAction
{
    Read,
    Create,
    Update,
    Delete,

    /// <summary>Invocation of a user-registered custom action (<c>AddAction</c>).</summary>
    Custom,

    /// <summary>Submission of a generic form registered via <c>AddForm</c>.</summary>
    FormSubmit,
}
