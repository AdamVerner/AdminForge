using System.Security.Claims;

namespace AdminForge.Auth;

/// <summary>The host's answer to a sign-in attempt: the admin's principal, or null when refused.</summary>
public interface IAdminCredentialValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(
        string username,
        string password,
        CancellationToken cancellationToken
    );
}
