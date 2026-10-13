using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace AdminForge.Mcp;

/// <summary>
/// The MCP authorization flow: metadata, dynamic registration, authorize with the consent page,
/// token with rotating refresh tokens, revoke. Clients are public and prove themselves with PKCE.
/// </summary>
internal static class OAuthEndpoints
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>What the consent page offers for a connection's lifetime; the first is the default.</summary>
    private static readonly TimeSpan[] ConnectionLifetimes =
    [
        TimeSpan.FromHours(24),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(8),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    /// <summary>How long an ended or expired connection stays listed before the next consent purges it.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static readonly string[] OAuthRequestKeys =
    [
        "client_id",
        "redirect_uri",
        "state",
        "code_challenge",
        "code_challenge_method",
        "resource",
        "scope",
        "response_type",
    ];

    public static string ResourceMetadataPath(AdminForgeMcpOptions o) =>
        "/.well-known/oauth-protected-resource" + o.Path;

    public static string ServerMetadataPath(AdminForgeMcpOptions o) =>
        "/.well-known/oauth-authorization-server" + o.Path + "/oauth";

    public static void Map(
        IEndpointRouteBuilder endpoints,
        AdminForgeMcpOptions o,
        string panelPolicy
    )
    {
        var oauth = o.Path + "/oauth";
        endpoints.MapGet(ResourceMetadataPath(o), ResourceMetadata).AllowAnonymous();
        endpoints.MapGet(ServerMetadataPath(o), ServerMetadata).AllowAnonymous();
        endpoints
            .MapGet("/.well-known/oauth-authorization-server", ServerMetadata)
            .AllowAnonymous();
        endpoints.MapPost(oauth + "/register", Register).AllowAnonymous().DisableAntiforgery();
        endpoints
            .MapGet(oauth + "/authorize", AuthorizeAsync)
            .RequireAuthorization(panelPolicy)
            .DenyFraming();
        endpoints
            .MapPost(oauth + "/authorize", ConsentAsync)
            .RequireAuthorization(panelPolicy)
            .DenyFraming();
        endpoints.MapPost(oauth + "/token", TokenAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapPost(oauth + "/revoke", RevokeAsync).AllowAnonymous().DisableAntiforgery();
    }

    private static Uri Issuer(AdminForgeMcpOptions o, HttpContext http) =>
        new(o.BaseUrl(http), o.Path.TrimStart('/') + "/oauth");

    private static Uri Resource(AdminForgeMcpOptions o, HttpContext http) =>
        new(o.BaseUrl(http), o.Path.TrimStart('/'));

    private static IResult ResourceMetadata(AdminForgeMcpOptions o, HttpContext http) =>
        Results.Json(
            new
            {
                resource = Resource(o, http).ToString(),
                authorization_servers = new[] { Issuer(o, http).ToString() },
                bearer_methods_supported = new[] { "header" },
                scopes_supported = Array.Empty<string>(),
            }
        );

    private static IResult ServerMetadata(AdminForgeMcpOptions o, HttpContext http)
    {
        var issuer = Issuer(o, http).ToString().TrimEnd('/');
        return Results.Json(
            new
            {
                issuer,
                authorization_endpoint = issuer + "/authorize",
                token_endpoint = issuer + "/token",
                registration_endpoint = issuer + "/register",
                revocation_endpoint = issuer + "/revoke",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "none" },
                revocation_endpoint_auth_methods_supported = new[] { "none" },
                scopes_supported = Array.Empty<string>(),
            }
        );
    }

    /// <summary>RFC 7591: the client id is the registration itself, sealed, so nothing is stored.</summary>
    private static IResult Register([FromBody] JsonObject body, TokenSealer sealer)
    {
        var name = body["client_name"]?.GetValue<string>() ?? "MCP client";
        var uris =
            body["redirect_uris"]?.AsArray().Select(u => u!.GetValue<string>()).ToArray() ?? [];
        if (uris.Length == 0 || !uris.All(IsAcceptableRedirect))
            return OAuthError(
                "invalid_redirect_uri",
                "redirect_uris must be https or loopback http."
            );
        var clientId = sealer.Seal(new ClientRegistration(name, uris));
        return Results.Json(
            new
            {
                client_id = clientId,
                client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                client_name = name,
                redirect_uris = uris,
                token_endpoint_auth_method = "none",
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
            },
            statusCode: StatusCodes.Status201Created
        );
    }

    private static bool IsAcceptableRedirect(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && (u.Scheme == "https" || (u.Scheme == "http" && (u.IsLoopback || u.Host == "localhost")));

    private static async Task<IResult> AuthorizeAsync(
        HttpContext http,
        AdminForgeMcpOptions o,
        PanelModel forge,
        TokenSealer sealer,
        IAntiforgery antiforgery,
        PanelTools tools
    )
    {
        var request = http.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        var (client, error) = Validate(request, o, http, sealer);
        if (client is null)
            return error!;

        var offered = await AllowedToolsAsync(tools, http.RequestServices, http.RequestAborted)
            .ConfigureAwait(false);
        return ConsentPage.Render(
            forge,
            client.Name,
            new Uri(request["redirect_uri"]),
            offered,
            ConnectionLifetimes,
            request.Where(kv => OAuthRequestKeys.Contains(kv.Key)).ToDictionary(),
            antiforgery.GetAndStoreTokens(http),
            http.Request.Path
        );
    }

    private static async Task<IResult> ConsentAsync(
        HttpContext http,
        AdminForgeMcpOptions o,
        TokenSealer sealer,
        IMcpConnectionStore store,
        PanelTools panelTools,
        TimeProvider clock,
        IAntiforgery antiforgery
    )
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        var decision = form["decision"].ToString();
        var tools = form["tools"].ToArray();
        _ = int.TryParse(form["expires"], out var expires);
        var request = form.Where(kv => OAuthRequestKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        var (client, error) = Validate(request, o, http, sealer);
        if (client is null)
            return error!;

        var redirect = request["redirect_uri"];
        var state = request.GetValueOrDefault("state");
        if (decision != "allow")
            return Results.Redirect(
                Callback(redirect, new() { ["error"] = "access_denied", ["state"] = state })
            );

        var allowed = (
            await AllowedToolsAsync(panelTools, http.RequestServices, http.RequestAborted)
                .ConfigureAwait(false)
        )
            .Select(t => t.ProtocolTool.Name)
            .ToHashSet(StringComparer.Ordinal);
        var grant = tools
            .Where(t => t is not null && allowed.Contains(t))
            .Select(t => t!)
            .Distinct()
            .ToList();
        var lifetime = ConnectionLifetimes.Contains(TimeSpan.FromMinutes(expires))
            ? TimeSpan.FromMinutes(expires)
            : ConnectionLifetimes[0];

        var user = http.User;
        var now = clock.GetUtcNow().UtcDateTime;
        var connection = new McpConnection
        {
            Id = Guid.NewGuid().ToString("N"),
            AdminId = CurrentUserAccessor.IdOf(user) ?? "",
            AdminName = user.Identity?.Name ?? CurrentUserAccessor.IdOf(user) ?? "",
            ClientName = client.Name,
            Grant = grant,
            Claims = user.Claims.Select(c => new McpClaim(c.Type, c.Value)).ToList(),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
        await store.PurgeExpiredAsync(now - Retention, http.RequestAborted).ConfigureAwait(false);
        await store.AddAsync(connection, http.RequestAborted).ConfigureAwait(false);

        var code = sealer.Seal(
            new AuthCode(
                connection.Id,
                request["client_id"],
                redirect,
                request["code_challenge"],
                now.AddMinutes(1)
            )
        );
        return Results.Redirect(
            Callback(
                redirect,
                new()
                {
                    ["code"] = code,
                    ["state"] = state,
                    ["iss"] = Issuer(o, http).ToString().TrimEnd('/'),
                }
            )
        );
    }

    private static async Task<IResult> TokenAsync(
        HttpContext http,
        AdminForgeMcpOptions o,
        TokenSealer sealer,
        IMcpConnectionStore store,
        RefreshGrace grace,
        TimeProvider clock
    )
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var refreshing = false;
        string? cid;
        int counter;
        switch (form["grant_type"].ToString())
        {
            case "authorization_code":
            {
                var code = sealer.Unseal<AuthCode>(form["code"]);
                if (code is null || code.Exp <= now)
                    return OAuthError("invalid_grant", "The code is invalid or expired.");
                if (code.ClientId != form["client_id"].ToString())
                    return OAuthError("invalid_client", "The code was issued to another client.");
                if (code.RedirectUri != form["redirect_uri"].ToString())
                    return OAuthError(
                        "invalid_grant",
                        "redirect_uri does not match the authorization request."
                    );
                var resource = form["resource"].ToString();
                if (resource.Length > 0 && resource != Resource(o, http).ToString())
                    return OAuthError("invalid_target", "resource is not this server.");
                if (TokenSealer.Challenge(form["code_verifier"].ToString()) != code.Challenge)
                    return OAuthError(
                        "invalid_grant",
                        "code_verifier does not match the challenge."
                    );
                (cid, counter) = (code.Cid, 0);
                break;
            }
            case "refresh_token":
            {
                var refresh = sealer.Unseal<RefreshToken>(form["refresh_token"]);
                if (refresh is null)
                    return OAuthError("invalid_grant", "The refresh token is invalid.");
                (cid, counter, refreshing) = (refresh.Cid, refresh.Counter, true);
                break;
            }
            default:
                return OAuthError(
                    "unsupported_grant_type",
                    "Use authorization_code or refresh_token."
                );
        }

        var connection = await store.FindAsync(cid, http.RequestAborted).ConfigureAwait(false);
        if (connection is null || !connection.IsLive(now))
            return OAuthError("invalid_grant", "The connection has ended.");
        // The code is counter 0, so a replayed code and a replayed refresh token fail alike, and a leak ends the connection.
        // Only the newest token starts a window; one presented after it was replaced never does.
        if (refreshing && connection.RefreshCounter == counter)
            grace.Presented(cid, counter, now);
        var advanced = await store
            .TryAdvanceRefreshAsync(cid, counter, http.RequestAborted)
            .ConfigureAwait(false);
        // A reuse is let through only while it is fresh and the token that replaced it is still the newest.
        var raced =
            !advanced
            && refreshing
            && grace.Fresh(cid, counter, now)
            && await store.FindAsync(cid, http.RequestAborted).ConfigureAwait(false) is
                { RefreshCounter: var stored } current
            && stored == counter + 1
            && current.IsLive(now);
        if (!advanced && !raced)
        {
            await store.RevokeAsync(cid, http.RequestAborted).ConfigureAwait(false);
            return OAuthError(
                "invalid_grant",
                "The code or refresh token was already used; the connection is revoked."
            );
        }

        var expiresAt = now + AccessTokenLifetime;
        if (expiresAt > connection.ExpiresAt)
            expiresAt = connection.ExpiresAt;
        return Results.Json(
            new
            {
                access_token = sealer.Seal(new AccessToken(connection.Id, expiresAt)),
                token_type = "Bearer",
                expires_in = (long)(expiresAt - now).TotalSeconds,
                refresh_token = sealer.Seal(new RefreshToken(connection.Id, counter + 1)),
                scope = "",
            }
        );
    }

    /// <summary>RFC 7009: either token ends its connection; an unknown token is a success, as the RFC says.</summary>
    private static async Task<IResult> RevokeAsync(
        HttpContext http,
        TokenSealer sealer,
        IMcpConnectionStore store
    )
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        var token = form["token"].ToString();
        var cid = sealer.Unseal<AccessToken>(token)?.Cid ?? sealer.Unseal<RefreshToken>(token)?.Cid;
        if (cid is not null)
            await store.RevokeAsync(cid, http.RequestAborted).ConfigureAwait(false);
        return Results.Ok();
    }

    private static (ClientRegistration? Client, IResult? Error) Validate(
        IReadOnlyDictionary<string, string> r,
        AdminForgeMcpOptions o,
        HttpContext http,
        TokenSealer sealer
    )
    {
        var client = sealer.Unseal<ClientRegistration>(r.GetValueOrDefault("client_id"));
        if (client is null)
            return (null, OAuthError("invalid_client", "Unknown client_id; register first."));
        if (r.GetValueOrDefault("response_type") != "code")
            return (null, OAuthError("unsupported_response_type", "Only code."));
        if (!client.RedirectUris.Contains(r.GetValueOrDefault("redirect_uri") ?? ""))
            return (null, OAuthError("invalid_request", "redirect_uri is not registered."));
        if (
            r.GetValueOrDefault("code_challenge_method") != "S256"
            || string.IsNullOrEmpty(r.GetValueOrDefault("code_challenge"))
        )
            return (null, OAuthError("invalid_request", "PKCE with S256 is required."));
        var resource = r.GetValueOrDefault("resource");
        if (!string.IsNullOrEmpty(resource) && resource != Resource(o, http).ToString())
            return (null, OAuthError("invalid_target", "resource is not this server."));
        return (client, null);
    }

    /// <summary>The tools this admin may run today; the consent page offers nothing else.</summary>
    private static async Task<IReadOnlyList<PanelTool>> AllowedToolsAsync(
        PanelTools tools,
        IServiceProvider services,
        CancellationToken ct
    )
    {
        var bridge = PanelTool.Panel(services);
        var allowed = new List<PanelTool>();
        foreach (var tool in tools.All)
            if (await tool.IsAllowedAsync(bridge, ct).ConfigureAwait(false))
                allowed.Add(tool);
        return allowed;
    }

    private static string Callback(string redirect, Dictionary<string, string?> query) =>
        QueryHelpers.AddQueryString(redirect, query.Where(kv => kv.Value is not null)!);

    private static IResult OAuthError(string error, string description) =>
        Results.Json(
            new { error, error_description = description },
            statusCode: StatusCodes.Status400BadRequest
        );
}
