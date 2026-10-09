using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace AdminForge;

/// <summary>The caller of one operation, set in the operation's own scope: a circuit has no request to read the user from.</summary>
internal sealed class OperationUserAccessor
{
    public ClaimsPrincipal? User { get; set; }
}

/// <summary>The operation's user when one runs, else the circuit's, else the request's.</summary>
internal sealed class CurrentUserAccessor(
    OperationUserAccessor operation,
    IHttpContextAccessor http,
    AuthenticationStateProvider circuit
) : IUserAccessor
{
    public ClaimsPrincipal GetUser() =>
        operation.User ?? CircuitUser() ?? http.HttpContext?.User ?? new ClaimsPrincipal();

    public string? GetUserId() => IdOf(GetUser());

    public static string? IdOf(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated != true
            ? null
            : user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.Identity.Name;

    // Outside a render the state is unset and the call throws; inside one it is the circuit's user.
    private ClaimsPrincipal? CircuitUser()
    {
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
