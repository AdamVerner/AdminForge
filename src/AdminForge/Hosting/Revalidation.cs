using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AdminForge;

/// <summary>How often an open session asks again whether its user may stay, and the host's answer.</summary>
internal sealed record RevalidationModel(
    TimeSpan Every,
    Func<IServiceProvider, ClaimsPrincipal, CancellationToken, Task<bool>>? StillValid
);

/// <summary>Sessions signed out with <c>UseLogin</c>: their other open circuits and copies of their cookie end too.</summary>
/// <remarks>Held per process; behind several instances, another instance learns of a sign-out only from its own.</remarks>
internal sealed class SignedOutSessions(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _forgetAt = new();

    /// <summary>Remembers <paramref name="session"/> as ended until no cookie of it can be valid any more.</summary>
    public void End(string session, TimeSpan cookieLifetime)
    {
        var now = clock.GetUtcNow();
        foreach (var (old, at) in _forgetAt)
            if (at <= now)
                _forgetAt.TryRemove(old, out _);
        _forgetAt[session] = now + cookieLifetime;
    }

    public bool Ended(ClaimsPrincipal user) =>
        user.FindFirst(Revalidation.SessionClaim)?.Value is { } session && _forgetAt.ContainsKey(session);
}

internal static class Revalidation
{
    /// <summary>One sign-in's id, so signing out ends that session everywhere it is open.</summary>
    public const string SessionClaim = "adminforge:session";

    /// <summary>The standard claim for when the user signed in, in Unix seconds, for a host that caps a session.</summary>
    public const string AuthTimeClaim = "auth_time";

    public static async Task<bool> StillValidAsync(
        IServiceProvider services,
        ClaimsPrincipal user,
        CancellationToken ct
    )
    {
        if (services.GetService<SignedOutSessions>()?.Ended(user) == true)
            return false;
        return services.GetRequiredService<PanelModel>().Revalidation?.StillValid is not { } check
            || await check(services, user, ct).ConfigureAwait(false);
    }
}

/// <summary>A circuit's user, asked again every interval; a user who may no longer stay is signed out of it.</summary>
internal sealed class RevalidatingUser(
    ILoggerFactory logs,
    IServiceScopeFactory scopes,
    PanelModel panel
) : RevalidatingServerAuthenticationStateProvider(logs)
{
    protected override TimeSpan RevalidationInterval => panel.Revalidation!.Every;

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState state,
        CancellationToken ct
    )
    {
        if (state.User.Identity?.IsAuthenticated != true)
            return true;
        await using var scope = scopes.CreateAsyncScope();
        return await Revalidation.StillValidAsync(scope.ServiceProvider, state.User, ct);
    }
}
