using AdminForge.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using static AdminForge.Tests.PanelHost;

namespace AdminForge.Tests;

/// <summary>Tables through the pipeline: what each kind of source gets, and what every write leaves in the audit.</summary>
public class TableTests
{
    private static void Tickets(PanelBuilder p) =>
        p.Table<Ticket>(t =>
            t.Columns(x => x.Title, x => x.Status, x => x.Notes)
                .Column(x => x.Title, c => c.Validate(s => s.Length >= 3, "At least 3 characters."))
        );

    [Fact]
    public async Task Delete_is_offered_and_allowed_only_on_the_rows_it_applies_to()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Ticket>(t => t.DeleteWhen(x => x.Status != TicketStatus.Done))
        );
        using var admin = host.As();
        var open = await admin.Panel.CreateAsync("ticket", Args(("Title", "Still open"), ("Status", "Open")));
        var done = await admin.Panel.CreateAsync("ticket", Args(("Title", "All done"), ("Status", "Done")));

        var offered = (await admin.Panel.ListAsync("ticket", new ListRequest())).Rows
            .ToDictionary(r => r.Key, r => r.Allowed.Delete);
        Assert.Equal((true, false), (offered[open], offered[done]));
        Assert.False((await admin.Panel.GetAsync("ticket", done))!.Row.Allowed.Delete);

        var refused = await Assert.ThrowsAsync<AdminRejection>(() => admin.Panel.DeleteAsync("ticket", done));
        Assert.Equal("Delete does not apply to All done.", refused.Message);
        await admin.Panel.DeleteAsync("ticket", open);
        Assert.Equal(["All done"], await host.Db(db => db.Tickets.Select(t => t.Title).ToListAsync()));

        // Who may not delete hears that first, not whether Delete applies to a row they may not touch.
        host.Policy.Allows = r => r is not TableRequest<Ticket> { Op: AdminOp.Delete };
        var forbidden = await Assert.ThrowsAnyAsync<AdminRejection>(() => admin.Panel.DeleteAsync("ticket", done));
        Assert.Equal("You may not delete from Ticket.", forbidden.Message);
    }

    [Fact]
    public async Task An_EF_table_edits_a_constant_default_never_queries_an_unmapped_property_and_searches_no_hidden_text()
    {
        await using var host = await PanelHost.StartAsync(p => p.Table<Gadget>(t => t.Hide(g => g.Secret)));
        var table = host.Services.GetRequiredService<PanelModel>().Table("gadget")!;
        Assert.NotNull(table.Column("Tier")!.Edit);
        var shout = table.Column("Shout")!;
        Assert.Equal((false, false, false), (shout.Sortable, shout.Filterable, shout.Edit is not null));

        using var admin = host.As();
        await admin.Panel.CreateAsync("gadget", Args(("Name", "Lamp"), ("Tier", "pro")));
        await host.Db(async db =>
        {
            var gadget = await db.Gadgets.SingleAsync();
            gadget.Secret = "hunter2";
            return await db.SaveChangesAsync();
        });
        Assert.Equal("pro", await host.Db(db => db.Gadgets.Select(g => g.Tier).SingleAsync()));
        Assert.Empty((await admin.Panel.ListAsync("gadget", new ListRequest(Search: "hunter"))).Rows);
        Assert.Single((await admin.Panel.ListAsync("gadget", new ListRequest(Search: "lamp"))).Rows);
    }

    [Fact]
    public async Task An_EF_source_given_options_still_sorts_and_filters_every_column()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Ticket>(t => t.Source<EfSource<TestDb, Ticket>>(s => s.Search(findsKey: true)))
        );
        var title = host.Services.GetRequiredService<PanelModel>().Table("ticket")!.Column("Title")!;
        Assert.True(title.Sortable && title.Filterable);

        using var admin = host.As();
        var key = await admin.Panel.CreateAsync("ticket", Args(("Title", "Printer jam"), ("Status", "Open")));
        await admin.Panel.CreateAsync("ticket", Args(("Title", $"Ticket {key}0"), ("Status", "Open")));
        Assert.Equal(key, Assert.Single((await admin.Panel.ListAsync("ticket", new ListRequest(Search: key))).Rows).Key);
    }

    [Fact]
    public async Task No_list_asks_a_source_for_more_than_the_panel_s_page_size()
    {
        await using var host = await PanelHost.StartAsync(p => p.MaxPageSize(2).Table<Ticket>());
        using var admin = host.As();
        foreach (var title in new[] { "One", "Two", "Three" })
            await admin.Panel.CreateAsync("ticket", Args(("Title", title), ("Status", "Open")));

        var first = await admin.Panel.ListAsync("ticket", new ListRequest(PageSize: 10));
        Assert.Equal((2, 3), (first.Rows.Count, first.Total));
        // The next page starts where the clamped one ended, not where the asked-for size would.
        Assert.Equal(["Three"], (await admin.Panel.ListAsync("ticket", new ListRequest(Page: 1, PageSize: 10))).Rows.Select(r => r.Label));

        var failure = Record.Exception(() =>
        {
            var app = PanelHost.Build(
                p => p.AllowAnonymous().MaxPageSize(2).UseEntityFramework<TestDb>().Table<Ticket>()
                    .Dashboard("ops", d => d.Row(r => r.Table<Ticket>("Recent", w => w.Take(5)))),
                null,
                out var db
            );
            using (db)
                app.MapAdminForge();
        });
        Assert.Contains("more than the panel's MaxPageSize of 2", failure?.Message);
    }

    [Fact]
    public async Task A_list_link_with_a_sort_the_table_lacks_opens_in_the_default_order()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Ticket>(t => t.Computed("Shout", x => x.Title.ToUpperInvariant()))
        );
        using var admin = host.As();
        await admin.Panel.CreateAsync("ticket", Args(("Title", "Printer jam"), ("Status", "Open")));

        foreach (var sort in new[] { "Shout", "Credits" })
        {
            var page = await host.PageAsync($"/admin/entities/ticket?sort={sort}&desc=1");
            Assert.False(page.Has("error"), sort);
            Assert.Equal(1, page.Count("row"));
        }

        // A page past the end, from a stale link or after a delete, lands on the last one.
        var past = await host.Http().GetAsync("/admin/entities/ticket?page=9");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, past.StatusCode);
        Assert.Equal("/admin/entities/ticket", past.Headers.Location!.AbsolutePath + past.Headers.Location.Query);
    }

    [Fact]
    public async Task An_EF_table_creates_edits_and_deletes_with_an_audit_diff()
    {
        await using var host = await PanelHost.StartAsync(Tickets);
        using var admin = host.As();

        var key = await admin.Panel.CreateAsync(
            "ticket",
            Args(("Title", "Printer jam"), ("Status", "Open"))
        );
        var created = Assert.Single(host.Audit);
        Assert.Equal(AdminAuditOperation.Create, created.Operation);
        Assert.Equal(("ticket", key), (created.Table, created.Key));
        Assert.Equal("Printer jam", created.Changes["Title"].After);
        Assert.Equal("admin", created.User.Identity!.Name);

        // The detail page names the row; the edit form keeps the configured order and offers
        // neither the key nor the column the database generates.
        Assert.Equal(
            "Printer jam",
            (await host.PageAsync($"/admin/entities/ticket/{key}")).Text("title")
        );
        Assert.Equal(
            ["Title", "Status", "Notes", "AssigneeId"],
            (await host.PageAsync($"/admin/entities/ticket/{key}/edit")).Hooks("field:")
        );

        // An edit writes the changed column only; the generated one keeps what the database stored.
        await admin.Panel.UpdateAsync(
            "ticket",
            key,
            Args(("Title", "Printer jam, tray 2"), ("Status", "Open"))
        );
        var update = host.Audit[^1];
        Assert.Equal(AdminAuditOperation.Update, update.Operation);
        Assert.Equal(["Title"], update.Changes.Keys);
        Assert.Equal(
            new AdminChange("Printer jam", "Printer jam, tray 2"),
            update.Changes["Title"]
        );
        var stored = await host.Db(db => db.Tickets.AsNoTracking().SingleAsync());
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), stored.CreatedAt);

        // A column rule runs on the server: nothing is written and nothing audited.
        var refused = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.UpdateAsync("ticket", key, Args(("Title", "ab")))
        );
        Assert.Equal("At least 3 characters.", refused.FieldErrors["Title"]);
        Assert.Equal(2, host.Audit.Count);

        // A value type has no empty value: clearing one is refused, not stored as its default.
        var cleared = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.UpdateAsync("ticket", key, Args(("Status", null)))
        );
        Assert.Equal("Status is required.", cleared.FieldErrors["Status"]);

        // Search and filters are translated to SQL; a filter value arrives as text and is converted.
        var found = await admin.Panel.ListAsync(
            "ticket",
            new ListRequest(Search: "TRAY", Filters: Args(("Status", "Open")))
        );
        Assert.Equal(key, Assert.Single(found.Rows).Key);

        await admin.Panel.DeleteAsync("ticket", key);
        var deleted = host.Audit[^1];
        Assert.Equal(AdminAuditOperation.Delete, deleted.Operation);
        Assert.Equal("Printer jam, tray 2", deleted.Changes["Title"].Before);
        Assert.Empty(await host.Db(db => db.Tickets.ToListAsync()));

        // A stored write is reported stored even when its audit record fails; the failure is logged.
        var audit = host.Services.GetRequiredService<AuditLog>();
        audit.Broken = new InvalidOperationException("audit store down");
        await admin.Panel.CreateAsync("ticket", Args(("Title", "Toner"), ("Status", "Open")));
        Assert.Single(await host.Db(db => db.Tickets.ToListAsync()));
        Assert.Contains(LogCapture.Errors, e => e.Contains("audit store down"));

        // A sink's own timeout is a failed record too, not a cancelled write.
        audit.Broken = new TaskCanceledException("audit timed out");
        await admin.Panel.CreateAsync("ticket", Args(("Title", "Paper"), ("Status", "Open")));
        Assert.Equal(2, await host.Db(db => db.Tickets.CountAsync()));
    }

    [Fact]
    public async Task A_writable_source_of_positional_records_creates_patches_and_deletes()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Contact>(t => t.Source<ContactStore>())
        );
        using var admin = host.As();

        var list = await host.PageAsync("/admin/entities/contact");
        Assert.True(list.Has("create"));
        Assert.Equal(2, list.Count("edit"));
        Assert.Equal(["Id", "Name", "Email", "CompanyId", "Origin"], list.Hooks("column:"));

        var key = await admin.Panel.CreateAsync(
            "contact",
            Args(("Name", "Cleo"), ("CompanyId", "9"))
        );
        Assert.Equal(new Contact(3, "Cleo", null, 9), host.Contacts.Rows.Single(c => c.Id == 3));
        Assert.Equal("3", key);

        // The form submits every column; the source hears which one changed.
        await admin.Panel.UpdateAsync(
            "contact",
            "1",
            Args(("Name", "Ada L."), ("Email", "ada@example.com"), ("CompanyId", 7))
        );
        Assert.Equal(["Name"], host.Contacts.Changed.Single());
        // The patched copy keeps what only the row itself may set.
        Assert.Equal(
            new Contact(1, "Ada L.", "ada@example.com", 7).Imported("crm"),
            host.Contacts.Rows[0]
        );

        await admin.Panel.DeleteAsync("contact", "2");
        Assert.DoesNotContain(host.Contacts.Rows, c => c.Id == 2);
        Assert.Equal(
            [AdminAuditOperation.Create, AdminAuditOperation.Update, AdminAuditOperation.Delete],
            host.Audit.Select(e => e.Operation)
        );
    }

    [Fact]
    public async Task A_source_that_only_reads_offers_no_writes_where_a_writable_one_does()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Event>(t =>
                    t.Source<EventLog, int>(
                        (log, q, _) => Task.FromResult(AdminPage.From(log.Rows, q)),
                        (log, id, _) => Task.FromResult(log.Rows.FirstOrDefault(e => e.Id == id))
                    )
                )
                .Table<Contact>(t => t.Source<ContactStore>())
                .Table<Topic>(t =>
                    t.Source<EventLog, string>(
                        (log, q, _) => Task.FromResult(AdminPage.From(log.Topics, q)),
                        (log, name, _) =>
                            Task.FromResult(log.Topics.FirstOrDefault(x => x.Name == name))
                    )
                )
        );

        // The key "a%2Cb" escaped once more into the path, as the panel links it; the route decodes it once.
        Assert.Equal("a,b", (await host.PageAsync("/admin/entities/topic/a%252Cb")).Text("title"));

        var events = await host.PageAsync("/admin/entities/event");
        var contacts = await host.PageAsync("/admin/entities/contact");
        Assert.Equal(2, events.Count("row"));
        foreach (var hook in new[] { "create", "edit", "delete" })
        {
            Assert.True(contacts.Has(hook), hook);
            Assert.False(events.Has(hook), hook);
        }

        using var admin = host.As();
        await Assert.ThrowsAsync<AdminRejection>(() => admin.Panel.NewAsync("event"));
        await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.UpdateAsync("event", "1", Args(("Text", "x")))
        );
        Assert.Empty(host.Audit);
    }

    [Fact]
    public async Task An_inline_related_table_is_pinned_to_the_row_and_hides_its_key()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Person>(t =>
                    t.Related<Ticket>(x => x.AssigneeId, r => r.Title("Assigned").Inline())
                )
                .Table<Ticket>(t => t.Columns(x => x.Title, x => x.AssigneeId))
        );
        await host.Db(async db =>
        {
            db.People.Add(new Person { Name = "Ada", Email = "ada@example.com" });
            db.Tickets.AddRange(
                new Ticket { Title = "Hers", AssigneeId = 1 },
                new Ticket { Title = "Nobody's" }
            );
            return await db.SaveChangesAsync();
        });

        var page = await host.PageAsync("/admin/entities/person/1");
        var related = page.Text("related:ticket");
        Assert.Contains("Hers", related);
        Assert.DoesNotContain("Nobody's", related);
        Assert.Equal(["Title"], page.Within("related:ticket").Hooks("column:"));
    }

    [Fact]
    public async Task A_related_table_on_a_text_column_pins_by_equality_not_contains()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Person>(t => t.Related<Ticket>(x => x.Notes)).Table<Ticket>()
        );
        await host.Db(async db =>
        {
            db.People.Add(new Person { Name = "Ada", Email = "ada@example.com" });
            db.Tickets.AddRange(
                new Ticket { Title = "Hers", Notes = "1" },
                new Ticket { Title = "Person ten's", Notes = "10" }
            );
            return await db.SaveChangesAsync();
        });
        using var admin = host.As();
        var link = Assert.Single((await admin.Panel.GetAsync("person", "1"))!.Related);
        var rows = (
            await admin.Panel.ListAsync("ticket", new ListRequest(Filters: link.Filter))
        ).Rows;
        Assert.Equal(["Hers"], rows.Select(r => r.Label));

        // MCP tells an agent the same: the pinned text column equals, other text contains.
        await using var mcp = await host.ConnectMcpAsync();
        var filters = (await mcp.ListToolsAsync())
            .Single(t => t.Name == "list_ticket")
            .JsonSchema.GetProperty("properties");
        Assert.EndsWith(
            "equals.",
            filters.GetProperty("Notes").GetProperty("description").GetString()
        );
        Assert.EndsWith(
            "contains.",
            filters.GetProperty("Title").GetProperty("description").GetString()
        );

        // An in-memory source pins the same way.
        var query = new AdminQuery<Ticket>(
            new(0, 10),
            filters: Args(("Notes", "1")),
            exact: new HashSet<string> { "Notes" }
        );
        var tickets = new[]
        {
            new Ticket { Notes = "1" },
            new Ticket { Notes = "10" },
        };
        Assert.Equal(["1"], AdminPage.From(tickets, query).Rows.Select(t => t.Notes));
    }

    [Fact]
    public async Task A_host_source_table_shows_its_lists_links_content_and_computed_values()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Format<DateTime>("dd.MM.yyyy")
                .Table<Company>(t =>
                    t.Source<CompanyList, int>(
                            (list, q, _) => list.ListAsync(q),
                            (list, id, _) => list.FindAsync(id),
                            s => s.Search(findsKey: true).Sorts(c => c.Name).Filters(c => c.Name)
                        )
                        .Columns(c => c.Name, c => c.JoinedAt)
                        .Column(c => c.Notes, c => c.Content(Content.Markdown))
                        .Computed("domain-count", c => c.Domains.Count, c => c.InList())
                        .Computed("initial", c => c.Name[..1])
                        .Computed("domain-list", c => c.Domains.ToList())
                        .Related<Contact>(
                            c => c.CompanyId,
                            r => r.Inline().Columns("Name", "Email", "initial")
                        )
                )
                .Table<Contact>(t =>
                    t.Title("People")
                        .Source<ContactStore>(s => s.Filters(c => c.CompanyId))
                        .Column(c => c.CompanyId, c => c.LinksTo<Company>())
                        .Computed("initial", c => c.Name[..1])
                )
                .Table<Event>(t =>
                    t.Source<EventLog>(
                        (_, _, _) => throw new InvalidOperationException("event store down"),
                        s => s.Search()
                    )
                )
        );

        // The list keeps the configured order, appends the listed computed column, and offers
        // search because the source declares it; the contacts' source does not.
        var list = await host.PageAsync("/admin/entities/company");
        Assert.Equal(["Name", "JoinedAt", "domain-count"], list.Hooks("column:"));
        Assert.Contains("04.03.2025", list.Text("row"));
        Assert.True(list.Has("search"));
        Assert.False((await host.PageAsync("/admin/entities/contact")).Has("search"));

        // The detail page shows every column, the list column, the detail-only computed value,
        // the panel's one timestamp format, and the contacts pinned to this company.
        var detail = await host.PageAsync("/admin/entities/company/7");
        Assert.Equal("Northwind", detail.Text("title"));
        Assert.Contains("nw.test", detail.Text("value:Domains"));
        Assert.Equal("N", detail.Text("value:initial").Split(' ')[^1]);
        Assert.Contains("nw.test", detail.Text("value:domain-list"));
        Assert.DoesNotContain("System.Collections", detail.Text("value:domain-list"));
        Assert.Contains("04.03.2025", detail.Text("value:JoinedAt"));
        Assert.Contains("Pays late", detail.Text("value:Notes"));
        Assert.StartsWith("People", detail.Text("tab:contact"));
        Assert.Contains("Brian", detail.Text("related:contact"));
        // An inline table can show a computed column the child's own list leaves out.
        var related = detail.Within("related:contact");
        Assert.Equal(["Name", "Email", "initial"], related.Hooks("column:"));
        Assert.Equal("Adaada@example.comA", related.Text("row"));

        // A foreign key cell links to the row it names.
        Assert.Contains(
            "href=\"/admin/entities/company/7\"",
            (await host.PageAsync("/admin/entities/contact")).Html
        );
        Assert.Equal(
            "Company id: Northwind",
            (await host.PageAsync("/admin/entities/contact?filter:CompanyId=7")).Text(
                "filter:CompanyId"
            )
        );

        // A first cell with no text still links its row, by the row's label.
        host.Contacts.Rows.Add(new Contact(3, "", "nameless@example.com", 8));
        Assert.Contains(
            "href=\"/admin/entities/contact/3\">3</a>",
            (await host.PageAsync("/admin/entities/contact")).Html
        );

        // A pasted key opens its row; text searches the source; computed values follow the list rule.
        using var admin = host.As();
        var byKey = await admin.Panel.ListAsync("company", new ListRequest(Search: "8"));
        Assert.Equal("Globex", Assert.Single(byKey.Rows).Label);
        // A pasted key keeps the list's filters and asks the policy with the row it found.
        Assert.Empty(
            (
                await admin.Panel.ListAsync(
                    "company",
                    new ListRequest(Search: "8", Filters: Args(("Name", "North")))
                )
            ).Rows
        );
        host.Policy.Allows = r => r is not TableRequest<Company> { Row.Id: 8 };
        Assert.Empty((await admin.Panel.ListAsync("company", new ListRequest(Search: "8"))).Rows);
        Assert.Empty(await admin.Panel.SearchAsync("8"));
        host.Policy.Allows = _ => true;
        var byName = await admin.Panel.ListAsync("company", new ListRequest(Search: "north"));
        var row = Assert.Single(byName.Rows);
        Assert.Equal(2, row.Values["domain-count"]);
        Assert.False(row.Values.ContainsKey("initial"));
        Assert.False(row.Values.ContainsKey("Notes"));

        // A table whose source fails drops out of the global search; the others still answer.
        Assert.Equal("Northwind", Assert.Single(await admin.Panel.SearchAsync("north")).Label);
    }

    [Fact]
    public async Task A_row_with_a_composite_key_opens_where_create_sends_the_admin()
    {
        await using var host = await PanelHost.StartAsync(
            p => p.Table<Grant>(t => t.Source<GrantStore>()),
            s => s.AddSingleton<GrantStore>()
        );
        using var admin = host.As();

        var key = await admin.Panel.CreateAsync("grant", Args(("Role", "owner")));

        var detail = await host.PageAsync(Links.Row("grant", key));
        Assert.Contains("owner", detail.Text("value:Role"));
        Assert.Contains("north,eu/1", detail.Text("value:Org"));
    }

    [Fact]
    public async Task Every_operation_runs_in_a_scope_of_its_own_that_carries_the_caller()
    {
        await using var host = await PanelHost.StartAsync(
            p =>
                p.Table<Event>(t =>
                    t.Source<ScopedEvents, int>(
                        (s, q, _) => s.ListAsync(q),
                        (s, id, _) => Task.FromResult<Event?>(null)
                    )
                ),
            s => s.AddScoped<ScopedEvents>()
        );
        using var circuit = host.As("ada");

        // One circuit, two calls: each gets a fresh service, and each sees the circuit's user.
        var first = await circuit.Panel.ListAsync("event", new ListRequest());
        var second = await circuit.Panel.ListAsync("event", new ListRequest());
        var (a, b) = (
            (string)first.Rows[0].Values["Text"]!,
            (string)second.Rows[0].Values["Text"]!
        );
        Assert.StartsWith("ada@", a);
        Assert.StartsWith("ada@", b);
        Assert.NotEqual(a, b);
    }

    /// <summary>Lists one event naming the caller and this instance, so a test sees which scope served it.</summary>
    public sealed class ScopedEvents(IUserAccessor user)
    {
        private readonly Guid _instance = Guid.NewGuid();

        public Task<AdminPage<Event>> ListAsync(AdminQuery<Event> q) =>
            Task.FromResult(new AdminPage<Event>([new(1, $"{user.GetUserId()}@{_instance}")], 1));
    }

    public sealed class EventLog
    {
        public IReadOnlyList<Event> Rows { get; } = [new(1, "Started"), new(2, "Stopped")];
        public IReadOnlyList<Topic> Topics { get; } = [new("a,b"), new("a")];
    }
}

public sealed record Topic([property: System.ComponentModel.DataAnnotations.Key] string Name);
