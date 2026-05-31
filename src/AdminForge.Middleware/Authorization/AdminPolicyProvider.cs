using AdminForge.Core.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AdminForge.Middleware.Authorization;

/// <summary>
/// <see cref="IAuthorizationPolicyProvider"/> that materialises per-entity, per-action
/// AdminForge policies (named <c>AdminForge:{Entity}:{Action}</c>) on demand. Default
/// behaviour: each policy succeeds if the user satisfies the umbrella policy from
/// <see cref="AdminForgeOptions.AuthorizationPolicy"/> (or, if that is null, always
/// succeeds for any authenticated request).
///
/// Consumers override granular policies via the standard
/// <c>AddAuthorization(o =&gt; o.AddPolicy("AdminForge:User:Delete", ...))</c> path —
/// those explicit registrations are checked first.
/// </summary>
public sealed class AdminPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;
    private readonly AdminForgeOptions _options;

    public AdminPolicyProvider(
        IOptions<AuthorizationOptions> authzOptions,
        AdminForgeOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(authzOptions);
        ArgumentNullException.ThrowIfNull(options);
        _fallback = new DefaultAuthorizationPolicyProvider(authzOptions);
        _options = options;
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallback.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Explicit registrations win.
        var explicitPolicy = await _fallback.GetPolicyAsync(policyName).ConfigureAwait(false);
        if (explicitPolicy is not null)
            return explicitPolicy;

        if (!policyName.StartsWith(PolicyNames.Prefix + ":", StringComparison.Ordinal))
            return null;

        // Materialise on-demand: defer to the umbrella policy if configured.
        var builder = new AuthorizationPolicyBuilder();
        if (!string.IsNullOrWhiteSpace(_options.AuthorizationPolicy))
        {
            var umbrella = await _fallback
                .GetPolicyAsync(_options.AuthorizationPolicy)
                .ConfigureAwait(false);
            if (umbrella is not null)
            {
                builder.Combine(umbrella);
                return builder.Build();
            }
        }
        // No umbrella: granular policy is permissive (still requires an authenticated user only if
        // the host has a fallback policy demanding it). Use an always-true requirement.
        builder.RequireAssertion(_ => true);
        return builder.Build();
    }
}
