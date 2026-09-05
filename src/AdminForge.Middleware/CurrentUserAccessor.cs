using System.Security.Claims;
using AdminForge.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace AdminForge.Middleware;

/// <summary>
/// The scope's operation user when the bridge set one, else the request's user. Inside a circuit
/// <see cref="IHttpContextAccessor"/> is unreliable, which is why operations carry their own.
/// </summary>
public sealed class CurrentUserAccessor(
    OperationUserAccessor operation,
    IHttpContextAccessor accessor
) : IUserAccessor
{
    public string? GetUserId() => UserIdentity.IdOf(GetUser());

    public ClaimsPrincipal GetUser() =>
        operation.IsSet ? operation.GetUser() : accessor.HttpContext?.User ?? new ClaimsPrincipal();
}
