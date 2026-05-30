using System.Security.Claims;
using AdminForge.Core.Contracts;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Default <see cref="IAdminAuthorizationPolicy"/> implementation that permits every
/// action. Acts purely as a hook — hosts that want row-level or context-aware checks
/// register their own implementation via DI (overrides this default).
/// </summary>
public sealed class AllowAllAuthorizationPolicy : IAdminAuthorizationPolicy
{
    public Task<bool> IsAuthorizedAsync(
        string entityName,
        AdminAction action,
        ClaimsPrincipal user,
        object? instance = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(true);
}
