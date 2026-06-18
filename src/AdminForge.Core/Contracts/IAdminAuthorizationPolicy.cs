using System.Security.Claims;
using AdminForge.Core.Configuration;

namespace AdminForge.Core.Contracts;

/// <summary>
/// Per-action authorization hook. The default implementation delegates to ASP.NET's
/// <c>IAuthorizationService</c> with policies named
/// <c>"AdminForge:{Entity}:{Action}"</c> (see <see cref="PolicyNames"/>). Consumers
/// can swap in a custom implementation when they need richer logic (per-entity
/// row access, ownership checks, etc.).
/// </summary>
public interface IAdminAuthorizationPolicy
{
    /// <summary>
    /// Returns true if <paramref name="user"/> is permitted to perform
    /// <paramref name="action"/> on the entity identified by <paramref name="entityName"/>.
    /// </summary>
    /// <param name="entityName">Logical entity name (matches <c>EntityMeta.Name</c>).</param>
    /// <param name="action">Action being attempted.</param>
    /// <param name="user">Authenticated principal; never null (anonymous users are represented as <see cref="ClaimsPrincipal"/> with no identity).</param>
    /// <param name="instance">When available, the resolved entity instance — for row-level checks. Null for list/create.</param>
    /// <param name="actionName">When <paramref name="action"/> is <see cref="AdminAction.Custom"/>, the user-supplied action name (e.g. <c>"Send Welcome Email"</c>). Null otherwise.</param>
    Task<bool> IsAuthorizedAsync(
        string entityName,
        AdminAction action,
        ClaimsPrincipal user,
        object? instance = null,
        string? actionName = null,
        CancellationToken cancellationToken = default
    );
}
