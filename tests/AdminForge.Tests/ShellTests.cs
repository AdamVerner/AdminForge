using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AdminForge.Tests;

/// <summary>A host page that names the user the panel resolves for it.</summary>
[Route("/admin/whoami")]
public sealed class WhoAmIPage : ComponentBase
{
    [Inject]
    public IUserAccessor User { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "data-af", "host-page");
        builder.AddContent(2, User.GetUserId());
        builder.CloseElement();
    }
}

/// <summary>Routable from the tests assembly but never registered with Page&lt;T&gt;.</summary>
[Route("/admin/stray")]
public sealed class StrayPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        builder.AddMarkupContent(0, "<p data-af=\"host-page\">stray</p>");
}

/// <summary>What the shell lists and where: the home dashboard, host pages, the nav areas, hidden entries.</summary>
public class ShellTests
{
    [Fact]
    public async Task The_shell_lists_each_entry_in_its_area_and_opens_home_and_host_pages_inside_it()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Environment("Staging", AdminEnvironment.Staging)
                .Group(
                    "Work",
                    g =>
                        g.Table<Ticket>()
                            .Table<Person>(t =>
                                t.Hidden().Empty("Invite someone to see them here.")
                            )
                            .Dashboard(
                                "ops",
                                d =>
                                    d.Row(r =>
                                        r.Stat<TestDb, int>(
                                            "Open",
                                            (db, ct) => db.Tickets.CountAsync(ct)
                                        )
                                    )
                            )
                )
                .Dashboard(
                    "home",
                    d =>
                        d.Home()
                            .Row(r =>
                                r.Stat<TestDb, int>(
                                        "Tickets",
                                        (db, ct) => db.Tickets.CountAsync(ct)
                                    )
                                    .Table<Ticket>("Latest", w => w.Columns(x => x.Title).Take(1))
                            )
                )
                .Page<WhoAmIPage>(x => x.Title("Who am I").Area(NavArea.Settings))
                .Form<NoteInput>("my-notices", f => f.Area(NavArea.Account).Run(_ => { }))
        );
        // No site may frame the shell, its own included.
        Assert.Equal(
            "frame-ancestors 'none'",
            (await host.Http().GetAsync("/admin"))
                .Headers.GetValues("Content-Security-Policy")
                .Single()
        );
        await host.Db(Seed.OpenAndDoneAsync);

        // Home is the dashboard; its widgets load in the first paint.
        var home = await host.PageAsync("/admin");
        Assert.Contains("2", home.Text("widget:tickets"));
        Assert.Contains("Open one", home.Text("widget:latest"));
        Assert.Equal("Staging", home.Text("environment"));

        // The sidebar: hidden entries and the home dashboard stay out, Settings comes last, and
        // Account entries go to the account menu instead.
        // MCP's own Settings tables are its tests' business.
        var sidebar = home.Hooks("nav:").Where(h => !h.StartsWith("mcp", StringComparison.Ordinal));
        Assert.Equal(["home", "ticket", "ops", "who-am-i"], sidebar);
        using (var admin = host.As())
        {
            var nav = await admin.Panel.NavAsync();
            Assert.Equal(NavArea.Account, Assert.Single(nav, i => i.Id == "my-notices").Nav.Area);
            Assert.DoesNotContain(nav, i => i.Id == "person");
        }

        // A hidden table is still routable, and its empty state carries the table's own text.
        var people = await host.PageAsync("/admin/entities/person");
        Assert.Equal(1, people.Count("title"));
        Assert.Contains("Invite someone to see them here.", people.Text("empty"));

        // A host page renders inside the shell as the caller.
        var page = await host.PageAsync("/admin/whoami", "ada");
        Assert.Equal("ada", page.Text("host-page"));
        Assert.True(page.Has("nav:ticket"));

        // A refused host page leaves the sidebar and renders the refused state instead of itself.
        host.Policy.Allows = r => r is not HostPageRequest { PageId: "who-am-i" };
        var refused = await host.PageAsync("/admin/whoami", "ada");
        Assert.True(refused.Has("refused"));
        Assert.False(refused.Has("host-page"));
        Assert.False(refused.Has("nav:who-am-i"));
        Assert.True(refused.Has("nav:ticket"));

        // A routable component nobody registered has no page policy to ask, so it is refused.
        var stray = await host.PageAsync("/admin/stray");
        Assert.True(stray.Has("refused"));
        Assert.False(stray.Has("host-page"));

        // An unknown panel path answers 404 inside the shell; the OAuth discovery paths stay the host's.
        var missing = await host.Http().GetAsync("/admin/no-such-page");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
        var shell = new Page(await missing.Content.ReadAsStringAsync());
        Assert.Contains("This page doesn't exist", shell.Html.Replace("&#x27;", "'"));
        Assert.True(shell.Has("nav:ticket"));

        // The circuit, which carries every write, runs under the panel's root, under its policy.
        Assert.Contains("withUrl(\"admin/_blazor\")", shell.Html);
        Assert.Equal(
            System.Net.HttpStatusCode.OK,
            (await host.Http().PostAsync("/admin/_blazor/negotiate?negotiateVersion=1", null)).StatusCode
        );
        Assert.NotEqual(
            System.Net.HttpStatusCode.OK,
            (await host.Http(null).PostAsync("/admin/_blazor/negotiate?negotiateVersion=1", null)).StatusCode
        );

        // A page that finds nothing at its address answers 404 too, with its own message.
        foreach (var (path, message) in new[]
        {
            ("/admin/entities/nope", "There is no table 'nope'."),
            ("/admin/entities/ticket/999", "Ticket has no row with the key '999'."),
            ("/admin/entities/ticket/999/edit", "Ticket has no row with the key '999'."),
            ("/admin/entities/ticket/x", "'x' is not a valid key for Ticket."),
            ("/admin/forms/nope", "There is no form 'nope'."),
            ("/admin/dashboards/nope", "There is no dashboard 'nope'."),
        })
        {
            var response = await host.Http().GetAsync(path);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            var body = new Page(await response.Content.ReadAsStringAsync());
            Assert.Contains(message, body.Text("not-found").Replace("&#x27;", "'"));
            Assert.True(body.Has("nav:ticket"));
        }
        Assert.Equal(
            System.Net.HttpStatusCode.OK,
            (await host.Http(null).GetAsync("/.well-known/oauth-protected-resource/mcp")).StatusCode
        );
    }
}
