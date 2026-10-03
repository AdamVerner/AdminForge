using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdminForge.Core.Contracts;
using AdminForge.Mcp;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace AdminForge.IntegrationTests;

/// <summary>A connection holds what the admin ticked, and only while the admin lets it.</summary>
public class McpOAuthTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _factory;

    public McpOAuthTests(TodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task A_Connection_Holds_Its_Grant_Until_It_Is_Ended()
    {
        // The SDK re-authorizes on a 401, so the consent page is answered once and refused after.
        var consents = 0;
        await using var client = await McpSession.ConnectAsync(
            _factory,
            offered =>
                ++consents == 1
                    ? offered.Where(t => t is "list_todo" or "get_todo")
                    : throw new InvalidOperationException(
                        "The connection was ended; no new consent."
                    ),
            clientName: "Read-only agent"
        );

        // tools/list is the grant; a call outside it is refused by name.
        Assert.Equal(
            ["get_todo", "list_todo"],
            (await client.ListToolsAsync()).Select(t => t.Name).Order()
        );
        var page = await client.CallToolAsync(
            "list_todo",
            new Dictionary<string, object?> { ["page_size"] = 1 }
        );
        Assert.NotEqual(true, page.IsError);
        var refused = await client.CallToolAsync(
            "create_todo",
            new Dictionary<string, object?> { ["Title"] = "x" }
        );
        Assert.True(refused.IsError);
        Assert.Contains("not granted create_todo", Text(refused));

        // Another admin sees it among all connections, never among their own.
        using (var other = SignedInScope("bob"))
        {
            var otherBridge = other.ServiceProvider.GetRequiredService<IAdminUIBridge>();
            Assert.DoesNotContain(
                (
                    await otherBridge.ListAsync(
                        otherBridge.FindEntityByRouteName("McpConnection")!,
                        new ListQuery()
                    )
                ).Rows,
                r => (string)r.Values["ClientName"]! == "Read-only agent"
            );
            var all = (
                await otherBridge.ListAsync(
                    otherBridge.FindEntityByRouteName("McpConnectionEntry")!,
                    new ListQuery()
                )
            ).Rows;
            Assert.Equal(
                "admin",
                Assert
                    .Single(all, r => (string)r.Values["ClientName"]! == "Read-only agent")
                    .Values["AdminName"]
            );
        }

        // The panel lists the connection for its admin and ends it with one action.
        using var scope = SignedInScope("admin");
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var table = bridge.FindEntityByRouteName("McpConnection")!;
        var rows = (await bridge.ListAsync(table, new ListQuery())).Rows;
        var mine = Assert.Single(rows, r => (string)r.Values["ClientName"]! == "Read-only agent");
        Assert.Equal(["get_todo", "list_todo"], ((McpConnection)mine.Instance!).Grant.Order());

        var ended = new McpActionProbe();
        await bridge.InvokeActionAsync("McpConnection", mine.Key, "End connection", ended);
        Assert.Contains("Ended", ended.Message);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await client.CallToolAsync("list_todo", new Dictionary<string, object?>())
        );
        Assert.Equal(2, consents);
    }

    private IServiceScope SignedInScope(string admin)
    {
        var scope = _factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<OperationUserAccessor>()
            .Set(
                new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [new("sub", admin), new(System.Security.Claims.ClaimTypes.Name, admin)],
                        "test"
                    )
                )
            );
        return scope;
    }

    [Theory]
    [InlineData("verifier", "bad-verifier", null, null, "invalid_grant")]
    [InlineData("redirect", null, "http://localhost/other", null, "invalid_grant")]
    [InlineData("client", null, null, "someone-else", "invalid_client")]
    public async Task The_Token_Endpoint_Rejects_A_Code_That_Does_Not_Fit(
        string _,
        string? verifier,
        string? redirectUri,
        string? clientId,
        string error
    )
    {
        var (code, realVerifier, realClient) = await AuthorizeAsync();
        var response = await Token(
            new()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = verifier ?? realVerifier,
                ["redirect_uri"] = redirectUri ?? "http://localhost/callback",
                ["client_id"] = clientId ?? realClient,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            error,
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("error")
                .GetString()
        );
    }

    [Fact]
    public async Task A_Replayed_Code_Or_Refresh_Token_Ends_The_Connection()
    {
        var (code, verifier, clientId) = await AuthorizeAsync();
        Dictionary<string, string> exchange = new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = "http://localhost/callback",
            ["client_id"] = clientId,
        };
        var first = await (await Token(exchange)).Content.ReadFromJsonAsync<JsonElement>();
        var refresh = first.GetProperty("refresh_token").GetString()!;
        var access = first.GetProperty("access_token").GetString()!;

        // Refresh rotates; the old refresh token is dead and using it again kills the connection.
        var rotated = await (
            await Token(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh })
        ).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(refresh, rotated.GetProperty("refresh_token").GetString());
        Assert.True(await Authorized(rotated.GetProperty("access_token").GetString()!));

        var replay = await Token(
            new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }
        );
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.False(await Authorized(access));

        // A second connection: replaying its code ends it too.
        var (code2, verifier2, client2) = await AuthorizeAsync();
        exchange = new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code2,
            ["code_verifier"] = verifier2,
            ["redirect_uri"] = "http://localhost/callback",
            ["client_id"] = client2,
        };
        var token2 = (await (await Token(exchange)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token")
            .GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(exchange)).StatusCode);
        Assert.False(await Authorized(token2));
    }

    [Fact]
    public async Task An_Anonymous_Call_Is_Pointed_At_The_Metadata()
    {
        var http = _factory.CreateClient();
        var response = await http.PostAsync(
            "/mcp",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains(
            "resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"",
            challenge
        );

        var resource = await http.GetFromJsonAsync<JsonElement>(
            "/.well-known/oauth-protected-resource/mcp"
        );
        Assert.Equal(
            "http://localhost/mcp/oauth",
            resource.GetProperty("authorization_servers")[0].GetString()
        );
        var server = await http.GetFromJsonAsync<JsonElement>(
            "/.well-known/oauth-authorization-server/mcp/oauth"
        );
        Assert.Equal(
            "http://localhost/mcp/oauth/token",
            server.GetProperty("token_endpoint").GetString()
        );
        Assert.Equal(
            ["S256"],
            server
                .GetProperty("code_challenge_methods_supported")
                .EnumerateArray()
                .Select(e => e.GetString())
        );
    }

    /// <summary>Registers a client and consents to everything by hand; returns the code, its verifier and the client id.</summary>
    private async Task<(string Code, string Verifier, string ClientId)> AuthorizeAsync()
    {
        var http = _factory.CreateClient();
        var registered = await http.PostAsJsonAsync(
            "/mcp/oauth/register",
            new { client_name = "By hand", redirect_uris = new[] { "http://localhost/callback" } }
        );
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var clientId = (await registered.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("client_id")
            .GetString()!;

        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier))
        );
        var authorize = QueryHelpers.AddQueryString(
            "http://localhost/mcp/oauth/authorize",
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = "http://localhost/callback",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "s",
            }
        );
        var browser = await _factory.CreateSignedInClientAsync(allowAutoRedirect: false);
        var query = await McpSession.ConsentAsync(
            browser,
            new Uri(authorize),
            all => all,
            CancellationToken.None
        );
        return (query["code"].ToString(), verifier, clientId);
    }

    private Task<HttpResponseMessage> Token(Dictionary<string, string> form) =>
        _factory.CreateClient().PostAsync("/mcp/oauth/token", new FormUrlEncodedContent(form));

    private async Task<bool> Authorized(string accessToken)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        var response = await http.PostAsync(
            "/mcp",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );
        return response.StatusCode != HttpStatusCode.Unauthorized;
    }

    private static string Text(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private sealed class McpActionProbe : IActionContext
    {
        public string Message { get; private set; } = "";

        public Task<bool> ConfirmAsync(string message) => Task.FromResult(true);

        public void ShowSuccess(string message) => Message += message;

        public void ShowError(string message) => Message += message;

        public void ShowResult(string markdown) => Message += markdown;

        public void NavigateTo(string url) { }

        public void Refresh() { }
    }
}
