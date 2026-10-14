using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

/// <summary>The host's answer to a sign-in attempt.</summary>
public interface IAdminCredentialValidator
{
    Task<LoginResult> ValidateAsync(string username, string password, CancellationToken ct);
}

/// <summary>Signed in, refused, or locked out for a while.</summary>
public abstract record LoginResult
{
    private protected LoginResult() { }

    public static LoginResult Ok(ClaimsPrincipal principal) => new Accepted(principal);

    public static LoginResult Rejected { get; } = new Refused();

    /// <summary>Too many attempts: the page answers 429 with <c>Retry-After</c>.</summary>
    public static LoginResult Locked(TimeSpan retryAfter) => new LockedOut(retryAfter);

    internal sealed record Accepted(ClaimsPrincipal Principal) : LoginResult;

    internal sealed record Refused : LoginResult;

    internal sealed record LockedOut(TimeSpan RetryAfter) : LoginResult;
}

/// <summary>One of the endpoints <c>UseLogin</c> maps.</summary>
public enum LoginEndpoint
{
    /// <summary>GET of the login page.</summary>
    Page,

    /// <summary>POST of the login form.</summary>
    SignIn,

    /// <summary>POST of sign-out.</summary>
    SignOut,
}

/// <summary>The panel's own sign-in: a cookie scheme, a policy over it, a login page and sign-out.</summary>
internal static class Login
{
    public const string Scheme = "AdminForge";
    public const string Policy = "AdminForge.SignedIn";
    public const string LoginPath = PanelModel.Root + "/login";
    public const string LogoutPath = PanelModel.Root + "/logout";

    public static void Register<TValidator>(
        PanelBuilder panel,
        Action<CookieAuthenticationOptions>? configure,
        Action<LoginEndpoint, IEndpointConventionBuilder>? conventions
    )
        where TValidator : class, IAdminCredentialValidator
    {
        var services = panel.Services;
        services.AddScoped<IAdminCredentialValidator, TValidator>();
        services
            .AddAuthentication()
            .AddCookie(
                Scheme,
                o =>
                {
                    o.Cookie.Name = "adminforge";
                    o.LoginPath = LoginPath;
                    o.ExpireTimeSpan = TimeSpan.FromHours(12);
                    o.SlidingExpiration = true;
                    configure?.Invoke(o);
                }
            );
        services
            .AddAuthorizationBuilder()
            .AddPolicy(Policy, p => p.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());
        panel.Endpoints.Add((endpoints, _) => Map(endpoints, conventions));
    }

    private static void Map(
        IEndpointRouteBuilder endpoints,
        Action<LoginEndpoint, IEndpointConventionBuilder>? conventions
    )
    {
        (LoginEndpoint, IEndpointConventionBuilder)[] mapped =
        [
            (LoginEndpoint.Page, endpoints.MapGet(LoginPath, Show).AllowAnonymous().DenyFraming()),
            (LoginEndpoint.SignIn, endpoints.MapPost(LoginPath, SignInAsync).AllowAnonymous().DenyFraming()),
            (LoginEndpoint.SignOut, endpoints.MapPost(LogoutPath, SignOut).RequireAuthorization(Policy)),
        ];
        foreach (var (which, endpoint) in mapped)
            conventions?.Invoke(which, endpoint);
    }

    private static IResult Show(
        IAntiforgery antiforgery,
        PanelModel panel,
        HttpContext http,
        [FromQuery] string? returnUrl
    ) => Page(panel, antiforgery.GetAndStoreTokens(http), returnUrl, null, null);

    private static async Task<IResult> SignInAsync(
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string? returnUrl,
        IAdminCredentialValidator validator,
        IAntiforgery antiforgery,
        PanelModel panel,
        HttpContext http,
        CancellationToken ct
    )
    {
        var result = await validator.ValidateAsync(username, password, ct);
        var tokens = antiforgery.GetAndStoreTokens(http);
        switch (result)
        {
            case LoginResult.Accepted ok:
                // The scheme's own identity, so the principal counts as signed in whatever the validator built.
                var identity = new ClaimsIdentity(
                    ok.Principal.Claims,
                    Scheme,
                    ClaimTypes.Name,
                    ClaimTypes.Role
                );
                await http.SignInAsync(Scheme, new ClaimsPrincipal(identity));
                return Results.LocalRedirect(IsLocalUrl(returnUrl) ? returnUrl! : PanelModel.Root);
            case LoginResult.LockedOut locked:
                var seconds = (int)Math.Ceiling(locked.RetryAfter.TotalSeconds);
                http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                return Page(
                    panel,
                    tokens,
                    returnUrl,
                    username,
                    $"Too many attempts. Try again in {Math.Max(1, (int)Math.Ceiling(locked.RetryAfter.TotalMinutes))} minute(s).",
                    StatusCodes.Status429TooManyRequests
                );
            default:
                return Page(
                    panel,
                    tokens,
                    returnUrl,
                    username,
                    "Wrong username or password.",
                    StatusCodes.Status401Unauthorized
                );
        }
    }

    /// <summary>Refuses to be shown inside another site's frame, where a click could be steered.</summary>
    public static TBuilder DenyFraming<TBuilder>(this TBuilder endpoint)
        where TBuilder : IEndpointConventionBuilder =>
        endpoint.AddEndpointFilter(
            (context, next) =>
            {
                var headers = context.HttpContext.Response.Headers;
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = "frame-ancestors 'none'";
                return next(context);
            }
        );

    // Without the token any site could sign the admin out with a cross-site POST.
    private static async Task<IResult> SignOut(IAntiforgery antiforgery, HttpContext http) =>
        await antiforgery.IsRequestValidAsync(http)
            ? Results.SignOut(new AuthenticationProperties { RedirectUri = LoginPath }, [Scheme])
            : Results.BadRequest();

    /// <summary>A path on this site: one leading slash and no second one or backslash, which a browser reads as another host.</summary>
    internal static bool IsLocalUrl(string? url) =>
        url is { Length: > 0 }
        && (
            url[0] == '/' && (url.Length == 1 || url[1] is not ('/' or '\\'))
            || url.Length > 1 && url[0] == '~' && url[1] == '/'
        )
        && !url.Any(char.IsControl);

    // Plain HTML: the Blazor shell only starts once a session exists.
    private static IResult Page(
        PanelModel panel,
        AntiforgeryTokenSet tokens,
        string? returnUrl,
        string? username,
        string? message,
        int statusCode = StatusCodes.Status200OK
    )
    {
        var error = message is null
            ? ""
            : $"""<div class="af-alert af-alert-danger" role="alert" data-af="error"><strong>{E(message)}</strong></div>""";
        var logo = panel.Theme.LogoUrl is { Length: > 0 } url
            ? $"""<img src="{E(url)}" alt="{E(panel.Theme.LogoAlt ?? "")}">"""
            : "";
        var form = $$"""
                <form class="af-panel af-auth-card" method="post" action="{{LoginPath}}">
                  <div class="af-auth-brand">{{logo}}<h1>{{E(panel.Title)}}</h1>{{UI.Theme.EnvironmentBadge(panel)}}</div>
                  <p>Sign in to continue.</p>
                  {{error}}
                  <input type="hidden" name="{{tokens.FormFieldName}}" value="{{tokens.RequestToken}}">
                  <input type="hidden" name="returnUrl" value="{{E(returnUrl ?? "")}}">
                  <div class="af-field">
                    <label class="af-label" for="username">Username</label>
                    <input class="af-input" id="username" name="username" value="{{E(
                username ?? ""
            )}}" autocomplete="username" required{{(username is null ? " autofocus" : "")}}>
                  </div>
                  <div class="af-field">
                    <label class="af-label" for="password">Password</label>
                    <input class="af-input" id="password" name="password" type="password" autocomplete="current-password" required{{(
                username is null ? "" : " autofocus"
            )}}>
                  </div>
                  <button class="af-btn af-btn-primary af-btn-block" type="submit">Sign in</button>
                </form>
            """;
        return Results.Content(
            UI.Theme.PlainPage(panel, $"Sign in · {panel.Title}", form),
            "text/html; charset=utf-8",
            statusCode: statusCode
        );
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
