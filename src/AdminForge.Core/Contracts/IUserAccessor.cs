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

/// <summary>
/// The caller of one panel operation. The bridge opens a DI scope per operation and sets this in
/// it, so a data provider or handler resolving <see cref="IUserAccessor"/> there sees the user the
/// circuit was opened for — a circuit has no request of its own to read the user from.
/// </summary>
public sealed class OperationUserAccessor : IUserAccessor
{
    private ClaimsPrincipal? _user;

    public bool IsSet => _user is not null;

    public void Set(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _user = user;
    }

    public string? GetUserId() => UserIdentity.IdOf(GetUser());

    public ClaimsPrincipal GetUser() => _user ?? new ClaimsPrincipal();
}

public static class UserIdentity
{
    /// <summary>The <c>sub</c> or name-identifier claim, else the identity's name; null when anonymous.</summary>
    public static string? IdOf(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst("nameidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.Identity.Name;
    }
}
