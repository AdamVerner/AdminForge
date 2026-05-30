using System.Security.Claims;
using AdminForge.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace AdminForge.Middleware;

/// <summary>
/// Default <see cref="IUserAccessor"/> that pulls the current user identifier from
/// <c>HttpContext.User</c> via <see cref="IHttpContextAccessor"/>.
/// </summary>
public sealed class HttpContextUserAccessor : IUserAccessor
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextUserAccessor(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    public string? GetUserId()
    {
        var user = _accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return null;
        return user.FindFirst("sub")?.Value
               ?? user.FindFirst("nameidentifier")?.Value
               ?? user.Identity.Name;
    }

    public ClaimsPrincipal GetUser() => _accessor.HttpContext?.User ?? new ClaimsPrincipal();
}
