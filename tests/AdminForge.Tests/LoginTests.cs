using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static AdminForge.Tests.PanelHost;

namespace AdminForge.Tests;

/// <summary>admin / admin signs in; "locked" is locked out for 90 s; anything else is refused.</summary>
public sealed class LockingLogin : IAdminCredentialValidator
{
    public Task<LoginResult> ValidateAsync(
        string username,
        string password,
        CancellationToken ct
    ) =>
        Task.FromResult(
            username == "locked" ? LoginResult.Locked(TimeSpan.FromSeconds(90))
            : username == "admin" && password == "admin"
                ? LoginResult.Ok(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")]))
                )
            : LoginResult.Rejected
        );
}

public class LoginTests
{
    [Fact]
    public async Task The_host_adds_its_own_conventions_to_the_login_endpoints()
    {
        await using var host = await PanelHost.StartAsync(
            p =>
                p.UseLogin<LockingLogin>(
                    endpoints: (which, e) =>
                        e.AddEndpointFilter(
                            async (context, next) =>
                            {
                                context.HttpContext.Response.Headers["X-Host-Filter"] = which.ToString();
                                return await next(context);
                            }
                        )
                ),
            login: true
        );
        var browser = host.Http(null, new CookieContainer());

        Assert.Equal("Page", (await browser.GetAsync("/admin/login")).Headers.GetValues("X-Host-Filter").Single());
        Assert.Equal(
            "SignIn",
            (await SignInAsync(browser, "admin", "nope")).Headers.GetValues("X-Host-Filter").Single()
        );

        // The panel's own endpoints stay out of the host's OpenAPI document.
        var described = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<MethodInfo>() is not null)
            .Where(e => e.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription != true)
            .Select(e => e.RoutePattern.RawText);
        Assert.Empty(described);
    }

    [Fact]
    public async Task Sign_in_refuses_locks_out_redirects_only_locally_and_signs_out()
    {
        await using var host = await PanelHost.StartAsync(
            p => p.UseLogin<LockingLogin>(),
            login: true
        );
        var cookies = new CookieContainer();
        var browser = host.Http(null, cookies);

        var anonymous = await browser.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("http://localhost/admin/login", anonymous.Headers.Location!.ToString());

        PanelHost.AssertUnframeable(await browser.GetAsync("/admin/login"));
        var wrong = await SignInAsync(browser, "admin", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        PanelHost.AssertUnframeable(wrong);
        Assert.Contains("Wrong username or password", await wrong.Content.ReadAsStringAsync());

        var locked = await SignInAsync(browser, "locked", "x");
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("90", locked.Headers.GetValues("Retry-After").Single());
        Assert.Contains("Try again in 2 minute(s)", await locked.Content.ReadAsStringAsync());

        // Another host falls back to the panel; UnitTests pins IsLocalUrl case by case.
        Assert.Equal(
            "/admin",
            (
                await SignInAsync(host.Http(null), "admin", "admin", "//evil.example")
            ).Headers.Location!.ToString()
        );
        var back = await SignInAsync(browser, "admin", "admin", "/admin/entities/ticket?x=1");
        Assert.Equal("/admin/entities/ticket?x=1", back.Headers.Location!.ToString());

        var panel = await browser.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.True(new Page(await panel.Content.ReadAsStringAsync()).Has("nav:home"));

        // Signing out takes an antiforgery token; a cross-site POST without one is refused.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await browser.PostAsync("/admin/logout", null)).StatusCode
        );
        // The token the account menu renders for the signed-in admin, over the browser's cookie.
        var menu = new DefaultHttpContext
        {
            RequestServices = host.Services,
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "admin")],
                    Login.Scheme,
                    ClaimTypes.Name,
                    ClaimTypes.Role
                )
            ),
        };
        menu.Request.Headers.Cookie = cookies.GetCookieHeader(new Uri("http://localhost/admin"));
        var tokens = host.Services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(menu);
        Assert.Equal(
            HttpStatusCode.Redirect,
            (
                await browser.PostAsync(
                    "/admin/logout",
                    new FormUrlEncodedContent([new(tokens.FormFieldName, tokens.RequestToken)])
                )
            ).StatusCode
        );
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/admin")).StatusCode);
    }
}
