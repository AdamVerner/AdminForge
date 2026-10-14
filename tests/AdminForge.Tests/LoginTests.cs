using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        Assert.Equal(HttpStatusCode.Redirect, (await SignOutAsync(host, browser, cookies)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task A_session_ends_when_signed_out_anywhere_or_when_the_host_says_so()
    {
        var allowed = true;
        await using var host = await PanelHost.StartAsync(
            p =>
                p.UseLogin<LockingLogin>()
                    .Revalidate(TimeSpan.FromMilliseconds(100), (_, _, _) => Task.FromResult(allowed)),
            login: true
        );
        var cookies = new CookieContainer();
        var browser = host.Http(null, cookies);
        await SignInAsync(browser, "admin", "admin");
        Assert.NotNull(SignedIn(host, cookies).FindFirst("auth_time"));

        // A copy of the cookie, as another tab or a stolen one holds it, ends with the sign-out.
        var copy = new CookieContainer();
        foreach (Cookie cookie in cookies.GetCookies(new Uri("http://localhost/admin")))
            copy.Add(cookie);
        var other = host.Http(null, copy);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/admin")).StatusCode);
        await SignOutAsync(host, browser, cookies);
        Assert.Equal(HttpStatusCode.Redirect, (await other.GetAsync("/admin")).StatusCode);

        // The host's answer is asked on every request, and every interval in an open circuit.
        await SignInAsync(browser, "admin", "admin");
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/admin")).StatusCode);
        await using var scope = host.Services.CreateAsyncScope();
        var circuit = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        ((IHostEnvironmentAuthenticationStateProvider)circuit).SetAuthenticationState(
            Task.FromResult(new AuthenticationState(SignedIn(host, cookies)))
        );
        allowed = false;
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/admin")).StatusCode);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((await circuit.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.False((await circuit.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
    }

    /// <summary>Signs out with the token the account menu renders for the signed-in admin.</summary>
    private static async Task<HttpResponseMessage> SignOutAsync(PanelHost host, HttpClient browser, CookieContainer cookies)
    {
        var menu = new DefaultHttpContext { RequestServices = host.Services, User = SignedIn(host, cookies) };
        menu.Request.Headers.Cookie = cookies.GetCookieHeader(new Uri("http://localhost/admin"));
        var tokens = host.Services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(menu);
        return await browser.PostAsync(
            "/admin/logout",
            new FormUrlEncodedContent([new(tokens.FormFieldName, tokens.RequestToken)])
        );
    }

    /// <summary>The principal the browser's sign-in cookie carries, as the cookie scheme reads it.</summary>
    private static ClaimsPrincipal SignedIn(PanelHost host, CookieContainer cookies) =>
        host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(Login.Scheme)
            .TicketDataFormat.Unprotect(cookies.GetCookies(new Uri("http://localhost/admin"))["adminforge"]!.Value)!
            .Principal;
}
