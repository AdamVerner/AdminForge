using System.Security.Claims;

namespace AdminForge.Core.Contracts;

/// <summary>
/// Lets renderer- and data-access-layer code reach the current user without
/// taking a direct dependency on <c>HttpContext</c>. The host wires an
/// implementation via <c>IHttpContextAccessor</c>; tests can stub this directly.
/// </summary>
public interface IUserAccessor
{
    /// <summary>Stable identifier (claim or name) for the acting user, or null when anonymous.</summary>
    string? GetUserId();

    /// <summary>
    /// The full <see cref="ClaimsPrincipal"/> for the current request. Never returns
    /// null — anonymous requests yield a principal with no identity, matching
    /// ASP.NET Core conventions.
    /// </summary>
    ClaimsPrincipal GetUser();
}

/// <summary>
/// Trivial null user accessor used when nothing is wired in DI. Returns null/empty
/// values so callers surface anonymous activity rather than throwing.
/// </summary>
public sealed class NullUserAccessor : IUserAccessor
{
    public string? GetUserId() => null;

    public ClaimsPrincipal GetUser() => new();
}
