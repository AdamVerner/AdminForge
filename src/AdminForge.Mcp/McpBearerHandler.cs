using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdminForge.Mcp;

/// <summary>
/// Turns a connection's access token into the admin's principal plus the grant. The store is read on
/// every call, so ending a connection takes effect at once and its last call is known.
/// </summary>
internal sealed class McpBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TokenSealer sealer,
    IMcpConnectionStore store,
    AdminForgeMcpOptions mcp,
    TimeProvider clock
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var now = clock.GetUtcNow().UtcDateTime;
        var token = sealer.Unseal<AccessToken>(header["Bearer ".Length..].Trim());
        if (token is null || token.Exp <= now)
            return AuthenticateResult.Fail("Invalid or expired token.");

        var connection = await store
            .FindAsync(token.Cid, Context.RequestAborted)
            .ConfigureAwait(false);
        if (connection is null || !connection.IsLive(now))
            return AuthenticateResult.Fail("Connection ended.");

        await store.TouchAsync(connection.Id, now, Context.RequestAborted).ConfigureAwait(false);
        return AuthenticateResult.Success(
            new AuthenticationTicket(Principal(connection), Scheme.Name)
        );
    }

    private static ClaimsPrincipal Principal(McpConnection connection)
    {
        var claims = connection.Claims.Select(c => new Claim(c.Type, c.Value)).ToList();
        claims.Add(new Claim(AdminForgeMcp.ConnectionClaim, connection.Id));
        claims.AddRange(connection.Grant.Select(t => new Claim(AdminForgeMcp.ToolClaim, t)));
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, AdminForgeMcp.Scheme, ClaimTypes.Name, ClaimTypes.Role)
        );
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var metadata = new Uri(mcp.BaseUrl(Context), OAuthEndpoints.ResourceMetadataPath(mcp));
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{metadata}\"";
        return Task.CompletedTask;
    }
}
