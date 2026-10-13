using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static AdminForge.Tests.PanelHost;

namespace AdminForge.Tests;

public sealed record AssignInput(int AssigneeId, string Reason);

public sealed record NoteInput(string Text);

/// <summary>The panel as MCP tools behind its own OAuth: a connection holds what its admin ticked, only while the admin lets it.</summary>
public class McpTests
{
    private static Action<PanelBuilder> Panel(string title = "Tickets", string assign = "Assign") =>
        p =>
            p.Table<Ticket>(t =>
                    t.Title(title)
                        .Columns(x => x.Title, x => x.Status)
                        .Action(
                            "assign",
                            assign,
                            a =>
                                a.Confirm("Reassigns the ticket.")
                                    .Input<AssignInput>()
                                    .Field(i => i.Reason, f => f.Options("holiday", "load"))
                                    .Run<TestDb>(
                                        async (db, ctx) =>
                                        {
                                            ctx.Row.AssigneeId = ctx.Input.AssigneeId;
                                            await db.SaveChangesAsync(ctx.CancellationToken);
                                            ctx.Success($"Assigned for {ctx.Input.Reason}.");
                                        }
                                    )
                        )
                )
                .Form<NoteInput>(
                    "note",
                    f =>
                        f.Run(ctx =>
                        {
                            ctx.Success($"Noted: {ctx.Input.Text}");
                            return Task.CompletedTask;
                        })
                )
                .Dashboard(
                    "ops",
                    d =>
                        d.Row(r =>
                            r.Stat<TestDb, int>("Open", (db, ct) => db.Tickets.CountAsync(ct))
                        )
                );

    [Fact]
    public async Task Tools_read_write_act_submit_and_load_a_dashboard_through_the_panel()
    {
        await using var host = await PanelHost.StartAsync(Panel());
        await using var mcp = await host.ConnectMcpAsync();

        var assign = (await mcp.ListToolsAsync()).Single(t => t.Name == "ticket_assign");
        var reason = assign.JsonSchema.GetProperty("properties").GetProperty("Reason");
        Assert.Equal(
            ["holiday", "load"],
            reason.GetProperty("enum").EnumerateArray().Select(e => e.GetString())
        );
        Assert.Equal(
            ["key", "AssigneeId", "Reason"],
            assign.JsonSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
        );

        var key = Json(
                await mcp.CallToolAsync(
                    "create_ticket",
                    Args(("Title", "From MCP"), ("Status", "Open"))
                )
            )
            .GetProperty("key")
            .GetString()!;
        var page = Json(
            await mcp.CallToolAsync("list_ticket", Args(("search", "from"), ("page_size", 5)))
        );
        var listed = Assert.Single(page.GetProperty("rows").EnumerateArray());
        Assert.Equal(key, listed.GetProperty("key").GetString());
        Assert.False(listed.TryGetProperty("Notes", out _));

        Assert.Equal(
            "Updated.",
            Text(
                await mcp.CallToolAsync("update_ticket", Args(("key", key), ("Notes", "via agent")))
            )
        );
        // A confirmed action runs over MCP without asking: the call is the confirmation.
        Assert.Equal(
            "Assigned for load.",
            Text(
                await mcp.CallToolAsync(
                    "ticket_assign",
                    Args(("key", key), ("AssigneeId", 4), ("Reason", "load"))
                )
            )
        );
        var row = Json(await mcp.CallToolAsync("get_ticket", Args(("key", key))));
        Assert.Equal(
            ("From MCP", "via agent", 4),
            (
                row.GetProperty("Title").GetString(),
                row.GetProperty("Notes").GetString(),
                row.GetProperty("AssigneeId").GetInt32()
            )
        );
        Assert.Equal(
            "Noted: hello",
            Text(await mcp.CallToolAsync("form_note", Args(("Text", "hello"))))
        );
        var dashboard = Json(await mcp.CallToolAsync("dashboard_ops", Args()));
        Assert.Equal(
            1,
            dashboard.GetProperty("widgets").GetProperty("open").GetProperty("value").GetInt32()
        );

        Assert.Contains(
            "not a valid key",
            Text(await mcp.CallToolAsync("get_ticket", Args(("key", "abc"))))
        );
        Assert.Equal(
            "Deleted.",
            Text(await mcp.CallToolAsync("delete_ticket", Args(("key", key))))
        );
        Assert.Equal(
            $"Tickets has no row with the key '{key}'.",
            Text(await mcp.CallToolAsync("get_ticket", Args(("key", key))))
        );
        Assert.Contains(
            $"Tickets has no row with the key '{key}'.",
            Text(await mcp.CallToolAsync("delete_ticket", Args(("key", key))))
        );

        Assert.All(host.Audit, e => Assert.Equal(AdminSurface.Mcp, e.Surface));
        Assert.Equal(
            [
                AdminAuditOperation.Create,
                AdminAuditOperation.Update,
                AdminAuditOperation.Action,
                AdminAuditOperation.Form,
                AdminAuditOperation.Delete,
            ],
            host.Audit.Select(e => e.Operation)
        );
    }

    [Fact]
    public async Task A_connection_holds_its_grant_while_its_admin_may_and_until_it_is_ended()
    {
        await using var host = await PanelHost.StartAsync(Panel());
        // The SDK re-authorizes on a 401, so the consent page is answered once and refused after.
        var consents = 0;
        await using var client = await host.ConnectMcpAsync(offered =>
            ++consents == 1
                ? offered.Where(t => t is "list_ticket" or "get_ticket")
                : throw new InvalidOperationException("Ended; no new consent.")
        );

        Assert.Equal(["get_ticket", "list_ticket"], await ToolNamesAsync(client));
        var refused = await client.CallToolAsync("create_ticket", Args(("Title", "x")));
        Assert.Contains("not granted create_ticket", Text(refused));

        // The umbrella policy is asked on every call: while the admin is out, so is every tool.
        host.Revoked["admin"] = true;
        Assert.Empty(await ToolNamesAsync(client));
        Assert.StartsWith("Forbidden", Text(await client.CallToolAsync("list_ticket", Args())));
        host.Revoked.Clear();
        Assert.Equal(["get_ticket", "list_ticket"], await ToolNamesAsync(client));

        // Another admin sees it among all connections, with its admin, and cannot reach it as their own.
        using (var bob = host.As("bob"))
        {
            Assert.Empty((await bob.Panel.ListAsync("my-mcp-connections", new ListRequest())).Rows);
            var all = Assert.Single(
                (await bob.Panel.ListAsync("mcp-connections", new ListRequest())).Rows
            );
            Assert.Equal(
                ("admin", "Test agent"),
                (all.Values["AdminName"], all.Values["ClientName"])
            );
            await Assert.ThrowsAsync<AdminRejection>(() =>
                bob.Panel.RunActionAsync("my-mcp-connections", all.Key, "end", Args())
            );
        }

        using var admin = host.As();
        var mine = Assert.Single(
            (await admin.Panel.ListAsync("my-mcp-connections", new ListRequest())).Rows
        );
        Assert.False(mine.Values.ContainsKey("AdminName"));
        var ended = await admin.Panel.RunActionAsync("my-mcp-connections", mine.Key, "end", Args());
        Assert.StartsWith("Ended the connection", ended.Message);
        var audited = Assert.Single(host.Audit);
        Assert.Equal(
            ("my-mcp-connections", "end", mine.Key, AdminSurface.Panel),
            (audited.Table, audited.Action, audited.Key, audited.Surface)
        );

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await client.CallToolAsync("list_ticket", Args())
        );
        Assert.Equal(2, consents);
        Assert.True(
            (
                await host.Services.GetRequiredService<IMcpConnectionStore>().FindAsync(mine.Key)
            )!.Revoked
        );
    }

    [Fact]
    public async Task Search_related_rows_and_dashboard_tables_show_only_granted_tables()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Person>(t => t.Related<Ticket>(x => x.AssigneeId))
                .Table<Ticket>()
                .Dashboard("ops", d => d.Row(r => r.Table<Ticket>("Recent")))
                .Form<NoteInput>(
                    "broken",
                    f =>
                        f.Field(
                                i => i.Text,
                                x =>
                                    x.Options<TestDb>(
                                        (_, _) =>
                                            throw new InvalidOperationException("options down")
                                    )
                            )
                            .Run(_ => { })
                )
        );
        await host.Db(async db =>
        {
            db.People.Add(new Person { Name = "Ada", Email = "ada@example.com" });
            db.Tickets.Add(new Ticket { Title = "Ada's printer", AssigneeId = 1 });
            return await db.SaveChangesAsync();
        });
        await using var mcp = await host.ConnectMcpAsync(offered =>
            offered.Where(t => t is "search" or "get_person" or "dashboard_ops" or "form_broken")
        );
        // A tool whose choices fail to load is still listed, and so are the others.
        Assert.Equal(
            ["dashboard_ops", "form_broken", "get_person", "search"],
            (await mcp.ListToolsAsync()).Select(t => t.Name).Order()
        );

        var hits = Json(await mcp.CallToolAsync("search", Args(("text", "Ada"))));
        Assert.Equal(["person"], hits.EnumerateObject().Select(p => p.Name));
        var person = Json(await mcp.CallToolAsync("get_person", Args(("key", "1"))));
        Assert.False(
            person.TryGetProperty("related", out var related) && related.EnumerateObject().Any()
        );
        var widget = Json(await mcp.CallToolAsync("dashboard_ops", Args()))
            .GetProperty("widgets")
            .EnumerateObject()
            .Single()
            .Value;
        Assert.False(widget.TryGetProperty("rows", out _));
        Assert.Contains("not granted ticket", widget.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_grant_names_stable_ids_so_it_survives_a_retitled_table_and_action()
    {
        var store = new InMemoryMcpConnectionStore();
        var keys = new EphemeralDataProtectionProvider();
        void Shared(IServiceCollection s)
        {
            s.AddSingleton<IMcpConnectionStore>(store);
            s.AddSingleton<IDataProtectionProvider>(keys);
        }

        string refresh;
        await using (var before = await PanelHost.StartAsync(Panel(), Shared))
        {
            var granted = await AuthorizeAsync(before, t => t is "list_ticket" or "ticket_assign");
            refresh = (await TokensAsync(before, Exchange(granted)))
                .GetProperty("refresh_token")
                .GetString()!;
        }

        await using var after = await PanelHost.StartAsync(
            Panel(title: "Support tickets", assign: "Hand over"),
            Shared
        );
        var access = (await TokensAsync(after, Refresh(refresh)))
            .GetProperty("access_token")
            .GetString()!;
        var http = after.Http(null);
        http.DefaultRequestHeaders.Authorization = new("Bearer", access);
        await using var mcp = await McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions { Endpoint = new Uri("http://localhost/mcp") },
                http
            )
        );
        var tools = await mcp.ListToolsAsync();
        Assert.Equal(["list_ticket", "ticket_assign"], tools.Select(t => t.Name).Order());
        Assert.Contains("Hand over", tools.Single(t => t.Name == "ticket_assign").Description);
    }

    [Theory]
    [InlineData("wrong verifier", "invalid_grant")]
    [InlineData("foreign redirect", "invalid_grant")]
    [InlineData("other client", "invalid_client")]
    [InlineData("expired code", "invalid_grant")]
    [InlineData("expired connection", "invalid_grant")]
    [InlineData("forged refresh token", "invalid_grant")]
    public async Task The_token_endpoint_refuses_what_does_not_fit(string tamper, string error)
    {
        var clock = new ManualClock();
        await using var host = await PanelHost.StartAsync(
            Panel(),
            s => s.AddSingleton<TimeProvider>(clock)
        );
        var form = Exchange(await AuthorizeAsync(host));
        switch (tamper)
        {
            case "wrong verifier":
                form["code_verifier"] = "bad-verifier";
                break;
            case "foreign redirect":
                form["redirect_uri"] = "http://localhost/other";
                break;
            case "other client":
                form["client_id"] = "someone-else";
                break;
            case "expired code":
                clock.Now += TimeSpan.FromMinutes(2);
                break;
            case "expired connection":
                var refresh = (await TokensAsync(host, form))
                    .GetProperty("refresh_token")
                    .GetString()!;
                clock.Now += TimeSpan.FromHours(2);
                form = Refresh(refresh);
                break;
            case "forged refresh token":
                form = Refresh("not-a-token");
                break;
        }
        var response = await Token(host, form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            error,
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("error")
                .GetString()
        );
    }

    [Fact]
    public async Task The_consent_page_names_a_remote_redirect_whatever_the_client_calls_itself()
    {
        await using var host = await PanelHost.StartAsync(Panel());
        var clientId = await RegisterAsync(host, "Claude Desktop", "https://evil.example/cb");
        var page = await host.Http()
            .GetStringAsync(AuthorizeUrl(clientId, "https://evil.example/cb", "x"));
        Assert.Contains("""data-af="redirect-host">evil.example</strong>""", page);
        Assert.Contains("data-af=\"remote-redirect\"", page);
    }

    [Fact]
    public async Task A_replayed_code_or_refresh_token_ends_the_connection()
    {
        var clock = new ManualClock();
        await using var host = await PanelHost.StartAsync(
            Panel(),
            s => s.AddSingleton<TimeProvider>(clock)
        );
        var first = await TokensAsync(host, Exchange(await AuthorizeAsync(host)));
        var refresh = first.GetProperty("refresh_token").GetString()!;
        var access = first.GetProperty("access_token").GetString()!;

        var rotated = await TokensAsync(host, Refresh(refresh));
        Assert.NotEqual(refresh, rotated.GetProperty("refresh_token").GetString());
        Assert.True(await Authorized(host, rotated.GetProperty("access_token").GetString()!));

        // A client that refreshed from two requests at once reuses the token within seconds: a race, not a replay.
        var raced = await TokensAsync(host, Refresh(refresh));
        Assert.True(await Authorized(host, raced.GetProperty("access_token").GetString()!));
        clock.Now += TimeSpan.FromSeconds(11);
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(host, Refresh(refresh))).StatusCode);
        Assert.False(await Authorized(host, access));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await Token(host, Refresh(rotated.GetProperty("refresh_token").GetString()!))
            ).StatusCode
        );

        var second = Exchange(await AuthorizeAsync(host));
        var token = (await TokensAsync(host, second)).GetProperty("access_token").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(host, second)).StatusCode);
        Assert.False(await Authorized(host, token));
    }

    [Fact]
    public async Task The_store_advances_a_refresh_once_however_many_race_for_it()
    {
        var store = new InMemoryMcpConnectionStore();
        var now = new DateTime(2026, 1, 1);
        McpConnection Connection(string id, DateTime expires) =>
            new()
            {
                Id = id,
                AdminId = "admin",
                AdminName = "admin",
                ClientName = "agent",
                Grant = [],
                Claims = [],
                CreatedAt = now,
                ExpiresAt = expires,
            };
        await store.AddAsync(Connection("live", now.AddDays(1)));
        await store.AddAsync(Connection("old", now.AddDays(-1)));

        var won = await Task.WhenAll(
            Enumerable
                .Range(0, 32)
                .Select(_ => Task.Run(() => store.TryAdvanceRefreshAsync("live", 0)))
        );
        Assert.Single(won, true);
        Assert.Equal(1, (await store.FindAsync("live"))!.RefreshCounter);

        await store.RevokeAsync("live");
        Assert.False(await store.TryAdvanceRefreshAsync("live", 1));

        await store.PurgeExpiredAsync(now);
        Assert.Equal(["live"], (await store.ListAsync(null)).Select(c => c.Id));
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<IReadOnlyList<string>> ToolNamesAsync(McpClient client) =>
        (await client.ListToolsAsync()).Select(t => t.Name).Order().ToList();

    /// <summary>Registers a client and consents by hand to what <paramref name="grant"/> picks; the code, its verifier and the client id.</summary>
    private static async Task<(string Code, string Verifier, string Client)> AuthorizeAsync(
        PanelHost host,
        Func<string, bool>? grant = null
    )
    {
        var clientId = await RegisterAsync(host, "By hand", "http://localhost/callback");
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier))
        );
        var query = await PanelHost.ConsentAsync(
            host.Http(),
            new Uri(AuthorizeUrl(clientId, "http://localhost/callback", challenge)),
            all => all.Where(grant ?? (_ => true)),
            CancellationToken.None
        );
        return (query["code"].ToString(), verifier, clientId);
    }

    private static async Task<string> RegisterAsync(PanelHost host, string name, string redirect)
    {
        var registered = await host.Http(null)
            .PostAsJsonAsync(
                "/mcp/oauth/register",
                new { client_name = name, redirect_uris = new[] { redirect } }
            );
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return (await registered.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("client_id")
            .GetString()!;
    }

    private static string AuthorizeUrl(string clientId, string redirect, string challenge) =>
        QueryHelpers.AddQueryString(
            "http://localhost/mcp/oauth/authorize",
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirect,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "s",
            }
        );

    private static Dictionary<string, string> Exchange(
        (string Code, string Verifier, string Client) a
    ) =>
        new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = a.Code,
            ["code_verifier"] = a.Verifier,
            ["redirect_uri"] = "http://localhost/callback",
            ["client_id"] = a.Client,
        };

    private static Dictionary<string, string> Refresh(string token) =>
        new() { ["grant_type"] = "refresh_token", ["refresh_token"] = token };

    private static Task<HttpResponseMessage> Token(
        PanelHost host,
        Dictionary<string, string> form
    ) => host.Http(null).PostAsync("/mcp/oauth/token", new FormUrlEncodedContent(form));

    private static async Task<JsonElement> TokensAsync(
        PanelHost host,
        Dictionary<string, string> form
    )
    {
        var response = await Token(host, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<bool> Authorized(PanelHost host, string accessToken)
    {
        var http = host.Http(null);
        http.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        var response = await http.PostAsync(
            "/mcp",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );
        return response.StatusCode != HttpStatusCode.Unauthorized;
    }

    private static JsonElement Json(CallToolResult result)
    {
        Assert.False(result.IsError == true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement;
    }
}
