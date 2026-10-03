using AdminForge.Core.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AdminForge.Auth;

public static class AuthEndpointRouteBuilderExtensions
{
    /// <summary>The login page, its POST and sign-out. Call before <c>MapAdminForge()</c> or on its own under MCP.</summary>
    public static IEndpointRouteBuilder MapAdminForgeAuth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Form posts validate antiforgery; the middleware must be in the pipeline for that.
        if (endpoints is IApplicationBuilder app)
            app.UseAntiforgery();

        endpoints.MapGet(AdminForgeAuth.LoginPath, ShowLogin).AllowAnonymous();
        endpoints.MapPost(AdminForgeAuth.LoginPath, LoginAsync).AllowAnonymous();
        endpoints
            .MapPost(AdminForgeAuth.LogoutPath, Logout)
            .RequireAuthorization(AdminForgeAuth.Policy);
        return endpoints;
    }

    private static IResult ShowLogin(
        IAntiforgery antiforgery,
        AdminForgeOptions options,
        HttpContext http,
        [FromQuery] string? returnUrl
    ) =>
        LoginPage.Render(
            options.Title,
            antiforgery.GetAndStoreTokens(http),
            returnUrl,
            message: null
        );

    private static async Task<IResult> LoginAsync(
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string? returnUrl,
        IAdminCredentialValidator validator,
        IAntiforgery antiforgery,
        AdminForgeOptions options,
        HttpContext http,
        CancellationToken cancellationToken
    )
    {
        var principal = await validator.ValidateAsync(username, password, cancellationToken);
        if (principal is null)
        {
            return LoginPage.Render(
                options.Title,
                antiforgery.GetAndStoreTokens(http),
                returnUrl,
                "Wrong username or password.",
                StatusCodes.Status401Unauthorized
            );
        }

        await http.SignInAsync(AdminForgeAuth.Scheme, principal);
        var target =
            returnUrl is { Length: > 0 } && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl
                : AdminForgeAuth.Root;
        return Results.LocalRedirect(target);
    }

    private static IResult Logout() =>
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = AdminForgeAuth.LoginPath },
            [AdminForgeAuth.Scheme]
        );
}
