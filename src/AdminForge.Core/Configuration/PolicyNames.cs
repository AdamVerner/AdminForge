namespace AdminForge.Core.Configuration;

/// <summary>
/// Centralised generator of AdminForge authorization policy names.
/// Format: <c>"AdminForge:{Entity}:{Action}"</c>. Policy names are stable —
/// host applications can register them up-front against ASP.NET's
/// <c>IAuthorizationOptions</c>.
/// </summary>
public static class PolicyNames
{
    /// <summary>Shared prefix on every AdminForge policy name.</summary>
    public const string Prefix = "AdminForge";

    /// <summary>Builds the policy name for the given entity + action pair.</summary>
    public static string For(string entityName, AdminAction action)
    {
        if (string.IsNullOrWhiteSpace(entityName))
            throw new ArgumentException("Entity name must not be empty.", nameof(entityName));
        return $"{Prefix}:{entityName}:{action}";
    }
}
