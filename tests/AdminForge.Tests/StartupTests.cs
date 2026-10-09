using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.TestHost;

namespace AdminForge.Tests;

[Route("/elsewhere")]
public sealed class OutsidePage : ComponentBase;

public sealed class UnroutedPage : ComponentBase;

public sealed record Keyless(string Name);

/// <summary>A configuration the panel cannot serve fails at startup, and the message names the fix.</summary>
public class StartupTests
{
    public static TheoryData<string, Action<PanelBuilder>, string> Misconfigurations =>
        new()
        {
            {
                "no authorization",
                p => p.UseEntityFramework<TestDb>().Table<Ticket>(),
                "refuses to mount without authorization"
            },
            {
                "both",
                p => p.RequireAuthorization(PanelHost.Umbrella).AllowAnonymous(),
                "pick one"
            },
            { "page outside", p => p.AllowAnonymous().Page<OutsidePage>(), "outside the panel" },
            { "no source", p => p.AllowAnonymous().Table<Contact>(), "has no source" },
            {
                "unknown column",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t => t.Source<ContactStore>().Hide(c => c.Name.Length)),
                "has no column 'Length'"
            },
            {
                "key type",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t =>
                            t.Source<ContactStore, string>(
                                (_, q, _) => Task.FromResult(new AdminPage<Contact>([], 0)),
                                (_, _, _) => Task.FromResult<Contact?>(null)
                            )
                        ),
                "finds by String"
            },
            {
                "related unfiltered",
                p =>
                    p.AllowAnonymous()
                        .UseEntityFramework<TestDb>()
                        .Table<Person>(t => t.Related<Contact>(c => c.CompanyId))
                        .Table<Contact>(t => t.Source<ContactStore>()),
                "does not filter on"
            },
            {
                "related key hidden",
                p =>
                    p.AllowAnonymous()
                        .UseEntityFramework<TestDb>()
                        .Table<Person>(t => t.Related<Contact>(c => c.CompanyId))
                        .Table<Contact>(t =>
                            t.Source<ContactStore>(s => s.Filters(c => c.CompanyId))
                                .Hide(c => c.CompanyId)
                        ),
                "which 'contact' hides"
            },
            {
                "related table of another type",
                p =>
                    p.AllowAnonymous()
                        .UseEntityFramework<TestDb>()
                        .Table<Person>(t =>
                            t.Related<Contact>(c => c.CompanyId, r => r.Table("ticket"))
                        )
                        .Table<Ticket>(),
                "names table 'ticket', which lists Ticket rows, not Contact"
            },
            {
                "two homes",
                p => p.AllowAnonymous().Dashboard("a", d => d.Home()).Dashboard("b", d => d.Home()),
                "one home page"
            },
            {
                "duplicate id",
                p =>
                    p.AllowAnonymous()
                        .UseEntityFramework<TestDb>()
                        .Table<Ticket>()
                        .Table<Person>(t => t.Id("ticket")),
                "have the id 'ticket'"
            },
            {
                "links nowhere",
                p =>
                    p.AllowAnonymous()
                        .UseEntityFramework<TestDb>()
                        .Table<Ticket>(t => t.Column(x => x.AssigneeId, c => c.LinksTo<Event>())),
                "Event, which has no table"
            },
            { "mcp without a policy", p => p.AllowAnonymous().UseMcp(), "needs a signed-in panel" },
            {
                "two MCP tools with one name",
                p =>
                    p.RequireAuthorization(PanelHost.Umbrella)
                        .UseMcp()
                        .Form<NoteInput>("note-send", f => f.Run(_ => { }))
                        .Form<NoteInput>("note_send", f => f.Run(_ => { })),
                "Two MCP tools are named 'form_note_send'"
            },
            {
                "two policies",
                p => p.RequireAuthorization("a").RequireAuthorization("b"),
                "cannot also require 'b'"
            },
            {
                "page not routable",
                p => p.AllowAnonymous().Page<UnroutedPage>(),
                "is not routable"
            },
            {
                "nested groups",
                p => p.Group("a", g => g.Group("b", _ => { })),
                "groups do not nest"
            },
            {
                "no key",
                p =>
                    p.AllowAnonymous()
                        .Table<Keyless>(t =>
                            t.Source<ContactStore>(
                                (_, _, _) => Task.FromResult(new AdminPage<Keyless>([], 0))
                            )
                        ),
                "has no key"
            },
            {
                "source of another type",
                p => p.AllowAnonymous().Table<Event>(t => t.Source<ContactStore>()),
                "does not implement IAdminSource<Event>"
            },
            {
                "action without find",
                p =>
                    p.AllowAnonymous()
                        .Table<Event>(t =>
                            t.Source<ContactStore>(
                                    (_, _, _) => Task.FromResult(new AdminPage<Event>([], 0))
                                )
                                .Action("x", "X", a => a.Run(_ => { }))
                        ),
                "cannot find a row by key"
            },
            {
                "two actions",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t =>
                            t.Source<ContactStore>()
                                .Action("ping", "Ping", a => a.Run(_ => { }))
                                .Action("Ping", "Again", a => a.Run(_ => { }))
                        ),
                "two actions with id 'Ping'"
            },
            {
                "action without run",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t => t.Source<ContactStore>().Action("x", "X", _ => { })),
                "Action 'x' on table 'contact' has no Run"
            },
            {
                "computed clashes",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t => t.Source<ContactStore>().Computed("Name", c => c.Id)),
                "two columns named 'Name'"
            },
            {
                "live without a view",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t =>
                            t.Source<ContactStore>()
                                .Live<Counter, int>(
                                    "Count",
                                    (c, row, ct) => c.CountAsync(1, ct),
                                    _ => { }
                                )
                        ),
                "needs Chart(...) or Feed(...)"
            },
            {
                "two live widgets with one id",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t =>
                            t.Source<ContactStore>()
                                .Live<Counter, int>(
                                    "Count",
                                    (c, row, ct) => c.CountAsync(1, ct),
                                    l => l.Feed(x => x)
                                )
                                .Live<Counter, int>(
                                    "count",
                                    (c, row, ct) => c.CountAsync(2, ct),
                                    l => l.Feed(x => x)
                                )
                        ),
                "two live widgets titled so their id is 'count'"
            },
            {
                "form without run",
                p => p.AllowAnonymous().Form<NoteInput>("note", _ => { }),
                "Form 'note' has no Run"
            },
            {
                "list input without options",
                p => p.AllowAnonymous().Form<NoticeInput>("notice", f => f.Run(_ => { })),
                "needs Options(...)"
            },
            {
                "chart without points",
                p =>
                    p.AllowAnonymous()
                        .Dashboard(
                            "d",
                            d => d.Row(r => r.Chart<int>("c", c => c.X(n => n).Y(n => n)))
                        ),
                "needs Points(...) or Stream(...)"
            },
            {
                "streamed chart polls",
                p =>
                    p.AllowAnonymous()
                        .Dashboard(
                            "d",
                            d =>
                                d.Row(r =>
                                    r.Chart<int>(
                                        "c",
                                        c =>
                                            c.Stream<Counter>((s, ct) => s.CountAsync(1, ct))
                                                .X(n => n)
                                                .Y(n => n)
                                                .Poll(TimeSpan.FromSeconds(1))
                                    )
                                )
                        ),
                "polls, which needs Points"
            },
            {
                "chart without y",
                p =>
                    p.AllowAnonymous()
                        .Dashboard(
                            "d",
                            d =>
                                d.Row(r =>
                                    r.Chart<int>(
                                        "c",
                                        c =>
                                            c.Stream<Counter>((s, ct) => s.CountAsync(1, ct))
                                                .X(n => n)
                                    )
                                )
                        ),
                "needs Y(...)"
            },
            {
                "two widgets with one id",
                p =>
                    p.AllowAnonymous()
                        .Dashboard(
                            "d",
                            d =>
                                d.Row(r =>
                                    r.Stat<Counter, int>("Total", (_, _) => Task.FromResult(1))
                                        .Stat<Counter, int>("total", (_, _) => Task.FromResult(2))
                                )
                        ),
                "two widgets titled so their id is 'total'"
            },
            {
                "non-positive poll",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t => t.Source<ContactStore>().Poll(TimeSpan.Zero)),
                "interval"
            },
            {
                "ambiguous link",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t => t.Source<ContactStore>())
                        .Table<Contact>(t => t.Id("people").Source<ContactStore>())
                        .Dashboard("d", d => d.Row(r => r.Table<Contact>("Recent"))),
                "which has 2 tables (contact, people). Name one by its id: r.Table(id)"
            },
            {
                "link to an unknown table",
                p =>
                    p.AllowAnonymous()
                        .Table<Contact>(t =>
                            t.Source<ContactStore>()
                                .Column(c => c.CompanyId, c => c.LinksTo("nope"))
                        ),
                "names table 'nope', which is not registered"
            },
        };

    [Theory]
    [MemberData(nameof(Misconfigurations))]
    public void A_misconfigured_panel_does_not_start(
        string _,
        Action<PanelBuilder> panel,
        string message
    )
    {
        var failure = Record.Exception(() =>
        {
            var app = PanelHost.Build(panel, null, out var db);
            using (db)
                app.MapAdminForge();
        });
        Assert.NotNull(failure);
        Assert.Contains(message, failure.Message);
    }

    [Fact]
    public async Task An_open_panel_says_so_and_serves_anyone()
    {
        var app = PanelHost.Build(
            p => p.AllowAnonymous().UseEntityFramework<TestDb>().Table<Person>(),
            null,
            out var db
        );
        using (db)
        {
            app.MapAdminForge();
            await app.StartAsync();
            var client = app.GetTestClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
            await app.DisposeAsync();
        }
    }
}
