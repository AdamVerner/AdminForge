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
}
