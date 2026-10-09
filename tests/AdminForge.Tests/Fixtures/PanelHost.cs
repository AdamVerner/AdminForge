using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AdminForge.Tests;

/// <summary>
/// A real host over <c>AddAdminForge</c> and <c>MapAdminForge</c>, on a test server, with an
/// in-memory SQLite <see cref="TestDb"/>, a capturing audit sink, a lambda policy, and callers named
/// by a request header.
/// </summary>
public sealed partial class PanelHost : IAsyncDisposable
{
    /// <summary>The umbrella policy: a signed-in caller whose name is not revoked.</summary>
    public const string Umbrella = "Admins";

    private readonly WebApplication _app;
    private readonly SqliteConnection _connection;

    private PanelHost(WebApplication app, SqliteConnection connection)
    {
        _app = app;
        _connection = connection;
    }

    public IServiceProvider Services => _app.Services;
    public IReadOnlyList<AdminAuditEvent> Audit =>
        Services.GetRequiredService<AuditLog>().Events.ToList();

    /// <summary>The policy's rule; allows everything until a test says otherwise.</summary>
    public Rule Policy => Services.GetRequiredService<Rule>();

    /// <summary>Names the umbrella policy refuses from now on.</summary>
    public ConcurrentDictionary<string, bool> Revoked =>
        Services.GetRequiredService<Revocations>().Names;

    public ContactStore Contacts => Services.GetRequiredService<ContactStore>();

    public static async Task<PanelHost> StartAsync(
        Action<PanelBuilder>? panel = null,
        Action<IServiceCollection>? services = null,
        bool login = false
    )
    {
        var app = Build(
            p =>
            {
                p.UseEntityFramework<TestDb>().UseMcp().Audit<CaptureSink>().Policy<RulePolicy>();
                if (!login)
                    p.RequireAuthorization(Umbrella);
                panel?.Invoke(p);
            },
            services,
            out var connection
        );
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TestDb>().Database.EnsureCreatedAsync();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAdminForge();
        await app.StartAsync();
        return new PanelHost(app, connection);
    }

    /// <summary>The host before it maps anything, for the startup guards.</summary>
    public static WebApplication Build(
        Action<PanelBuilder> panel,
        Action<IServiceCollection>? services,
        out SqliteConnection connection
    )
    {
        // The example's name, so MapStaticAssets finds the manifest its build copied here.
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                EnvironmentName = "Development",
            }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LogCapture.Provider());
        var db = new SqliteConnection("DataSource=:memory:");
        db.Open();
        connection = db;
        var revocations = new Revocations();
        builder.Services.AddDbContext<TestDb>(o => o.UseSqlite(db));
        builder.Services.AddSingleton<AuditLog>();
        builder.Services.AddSingleton<Rule>();
        builder.Services.AddSingleton<ContactStore>();
        builder.Services.AddSingleton(revocations);
        builder
            .Services.AddAuthentication(TestAuth.Name)
            .AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Name, null);
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy(
                Umbrella,
                p =>
                    p.AddAuthenticationSchemes(TestAuth.Name)
                        .RequireAuthenticatedUser()
                        .RequireAssertion(c =>
                            !revocations.Names.ContainsKey(c.User.Identity?.Name ?? "")
                        )
            );
        builder.Services.AddAdminForge(panel);
        services?.Invoke(builder.Services);
        return builder.Build();
    }

    /// <summary>A client that keeps cookies, as a browser does, and runs as <paramref name="user"/>; null for an anonymous one.</summary>
    public HttpClient Http(string? user = "admin", CookieContainer? cookies = null)
    {
        var server = _app.GetTestServer();
        var client = new HttpClient(
            new CookieContainerHandler(cookies ?? new()) { InnerHandler = server.CreateHandler() }
        )
        {
            BaseAddress = server.BaseAddress,
        };
        if (user is not null)
            client.DefaultRequestHeaders.Add(TestAuth.Header, user);
        return client;
    }

    /// <summary>A scope standing in for a circuit opened by <paramref name="user"/>: its panel calls run as them.</summary>
    internal Session As(string user = "admin")
    {
        var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<OperationUserAccessor>().User = TestAuth.Principal(
            user
        );
        return new Session(scope);
    }

    internal sealed record Session(IServiceScope Scope) : IDisposable
    {
        public AdminPanel Panel => Scope.ServiceProvider.GetRequiredService<AdminPanel>();

        public void Dispose() => Scope.Dispose();
    }

    /// <summary>The first paint of a panel page, as <paramref name="user"/> sees it.</summary>
    public async Task<Page> PageAsync(string path, string user = "admin")
    {
        var response = await Http(user).GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new Page(await response.Content.ReadAsStringAsync());
    }

    public async Task<T> Db<T>(Func<TestDb, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TestDb>());
    }

    /// <summary>Connects the SDK's client through the panel's OAuth; <paramref name="user"/>'s browser ticks what <paramref name="pick"/> chooses.</summary>
    public async Task<McpClient> ConnectMcpAsync(
        Func<IReadOnlyList<string>, IEnumerable<string>>? pick = null,
        string user = "admin"
    )
    {
        var browser = Http(user);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                OAuth = new ClientOAuthOptions
                {
                    RedirectUri = new Uri("http://localhost/callback"),
                    DynamicClientRegistration = new() { ClientName = "Test agent" },
                    AuthorizationCallbackHandler = async (context, ct) =>
                    {
                        var query = await ConsentAsync(
                            browser,
                            context.AuthorizationUri,
                            pick ?? (all => all),
                            ct
                        );
                        return new AuthorizationResult
                        {
                            Code = query["code"].ToString(),
                            State = query["state"].ToString(),
                            Iss = query["iss"].ToString(),
                        };
                    },
                },
            },
            Http(null)
        );
        return await McpClient.CreateAsync(transport);
    }

    /// <summary>Posts the login form as a browser does, with the antiforgery token its page carries.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(
        HttpClient browser,
        string user,
        string password,
        string? returnUrl = null
    )
    {
        var page = new HtmlParser().ParseDocument(await browser.GetStringAsync("/admin/login"));
        var token = page.QuerySelector("input[name=__RequestVerificationToken]")!;
        return await browser.PostAsync(
            "/admin/login",
            new FormUrlEncodedContent([
                new(token.GetAttribute("name")!, token.GetAttribute("value")!),
                new("username", user),
                new("password", password),
                new("returnUrl", returnUrl ?? ""),
            ])
        );
    }

    public static Dictionary<string, object?> Args(params (string Name, object? Value)[] args) =>
        args.ToDictionary(a => a.Name, a => a.Value);

    /// <summary>A tool result's text blocks, one per line.</summary>
    public static string Text(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    public static void AssertUnframeable(HttpResponseMessage response)
    {
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal(
            "frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single()
        );
    }

    /// <summary>Opens the consent page as the browser's admin, ticks tools, allows, and returns the callback's query.</summary>
    public static async Task<
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
    > ConsentAsync(
        HttpClient browser,
        Uri authorize,
        Func<IReadOnlyList<string>, IEnumerable<string>> pick,
        CancellationToken ct
    )
    {
        var page = await browser.GetAsync(authorize, ct);
        var html = await page.Content.ReadAsStringAsync(ct);
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"{page.StatusCode} {authorize} {html}");
        AssertUnframeable(page);
        Assert.Contains("""data-af="redirect-host">localhost</strong>""", html);
        Assert.DoesNotContain("remote-redirect", html);
        var doc = new HtmlParser().ParseDocument(html);
        var fields = doc.QuerySelectorAll("input[type=hidden]")
            .Select(i => new KeyValuePair<string, string>(
                i.GetAttribute("name")!,
                i.GetAttribute("value") ?? ""
            ))
            .ToList();
        var offered = doc.QuerySelectorAll("input[name=tools]")
            .Select(i => i.GetAttribute("value")!)
            .ToList();
        fields.AddRange(pick(offered).Select(t => new KeyValuePair<string, string>("tools", t)));
        fields.Add(new("expires", "60"));
        fields.Add(new("decision", "allow"));
        var redirect = await browser.PostAsync(
            new Uri(authorize, doc.QuerySelector("form[method=post]")!.GetAttribute("action")!),
            new FormUrlEncodedContent(fields),
            ct
        );
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        return QueryHelpers.ParseQuery(redirect.Headers.Location!.Query);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>Signs a request in as the name its header carries.</summary>
public sealed class TestAuth(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "Test";
    public const string Header = "X-Test-User";

    public static ClaimsPrincipal Principal(string name) =>
        new(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, name)],
                Name
            )
        );

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(
            Request.Headers.TryGetValue(Header, out var name)
                ? AuthenticateResult.Success(new AuthenticationTicket(Principal(name!), Name))
                : AuthenticateResult.NoResult()
        );
}

public sealed class AuditLog
{
    public ConcurrentQueue<AdminAuditEvent> Events { get; } = new();
    public Exception? Broken { get; set; }
}

public sealed class CaptureSink(AuditLog log) : IAdminAuditSink
{
    public Task RecordAsync(AdminAuditEvent auditEvent, CancellationToken ct)
    {
        if (log.Broken is { } broken)
            throw broken;
        log.Events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }
}

/// <summary>The host's policy as one lambda a test sets.</summary>
public sealed class Rule
{
    public Func<AdminRequest, bool> Allows { get; set; } = _ => true;
}

public sealed class RulePolicy(Rule rule) : IAdminPolicy
{
    public ValueTask<bool> AllowsAsync(AdminRequest request, CancellationToken ct) =>
        new(rule.Allows(request));
}

public sealed class Revocations
{
    public ConcurrentDictionary<string, bool> Names { get; } = new();
}

/// <summary>The host's error log, so a test can see a failure was logged.</summary>
public sealed class LogCapture
{
    public static readonly ConcurrentQueue<string> Errors = new();

    public sealed class Provider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger();

        public void Dispose() { }
    }

    private sealed class Logger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (IsEnabled(logLevel))
                Errors.Enqueue($"{formatter(state, exception)} {exception?.Message}");
        }
    }
}

/// <summary>A first-paint page read through its <c>data-af</c> hooks, never its CSS classes.</summary>
public sealed partial class Page(string html)
{
    private readonly IHtmlDocument _doc = new HtmlParser().ParseDocument(html);

    public string Html { get; } = html;

    public int Count(string hook) => _doc.QuerySelectorAll($"[data-af='{hook}']").Length;

    public bool Has(string hook) => Count(hook) > 0;

    /// <summary>Every hook starting with <paramref name="prefix"/>, in page order, without the prefix.</summary>
    public IReadOnlyList<string> Hooks(string prefix) =>
        _doc.QuerySelectorAll("[data-af]")
            .Select(e => e.GetAttribute("data-af")!)
            .Where(h => h.StartsWith(prefix, StringComparison.Ordinal))
            .Select(h => h[prefix.Length..])
            .ToList();

    /// <summary>The first element carrying <paramref name="hook"/>, as a page of its own.</summary>
    public Page Within(string hook) =>
        new(
            _doc.QuerySelector($"[data-af='{hook}']")?.OuterHtml
                ?? throw new InvalidOperationException($"No data-af=\"{hook}\" on the page.")
        );

    /// <summary>The href of every element <paramref name="selector"/> matches, in page order.</summary>
    public IReadOnlyList<string> Links(string selector) =>
        _doc.QuerySelectorAll(selector).Select(e => e.GetAttribute("href")!).ToList();

    /// <summary>The text inside the first element carrying <paramref name="hook"/>, its whitespace collapsed.</summary>
    public string Text(string hook)
    {
        var element = _doc.QuerySelector($"[data-af='{hook}']");
        Assert.True(element is not null, $"No data-af=\"{hook}\" on the page.");
        return Spaces().Replace(element.TextContent, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
