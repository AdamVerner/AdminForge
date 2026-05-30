namespace AdminForge.Core.Configuration;

/// <summary>
/// Thrown when an <see cref="Contracts.IAdminAuthorizationPolicy"/> denies a mutation.
/// Pages catch this and surface the message to the user — distinct from arbitrary
/// data-layer failures so the renderer can present a 403-flavoured message.
/// </summary>
public sealed class AdminForbiddenException : Exception
{
    public AdminForbiddenException(string entityName, AdminAction action)
        : base($"Not authorized to {action} '{entityName}'.")
    {
        EntityName = entityName;
        Action = action;
    }

    public string EntityName { get; }
    public AdminAction Action { get; }
}
