using System.Security.Claims;
using AdminForge.Core.Contracts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace AdminForge.Bridge;

/// <summary>
/// The scope's operation user when the bridge set one, else the circuit's user, else the request's.
/// Inside a circuit <see cref="IHttpContextAccessor"/> is unreliable and the authentication state
/// is what the circuit was opened with; outside a render that state is unset.
/// </summary>
public sealed class CurrentUserAccessor(
    OperationUserAccessor operation,
    IHttpContextAccessor accessor,
    AuthenticationStateProvider? circuit = null
) : IUserAccessor
{
    public string? GetUserId() => UserIdentity.IdOf(GetUser());

    public ClaimsPrincipal GetUser()
    {
        if (operation.IsSet)
            return operation.GetUser();
        if (CircuitUser() is { } user)
            return user;
        return accessor.HttpContext?.User ?? new ClaimsPrincipal();
    }

    private ClaimsPrincipal? CircuitUser()
    {
        if (circuit is null)
            return null;
        try
        {
            var state = circuit.GetAuthenticationStateAsync();
            return state.IsCompletedSuccessfully ? state.Result.User : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
