using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static AdminForge.Tests.PanelHost;

namespace AdminForge.Tests;

/// <summary>
/// One decision, three surfaces: what the panel shows, what a direct call does, what MCP does. Each
/// operation that has a row is asked with that row, so a rule about the row holds wherever it is reached.
/// </summary>
public class AuthorizationTests
{
    private static PanelBuilder Tickets(PanelBuilder p, Func<RowOp, bool>? tableRule = null) =>
        p.Table<Ticket>(t =>
        {
            t.Columns(x => x.Title, x => x.Status)
                .Action(
                    "close",
                    "Close",
                    a =>
                        a.Run<TestDb>(
                            async (db, ctx) =>
                            {
                                ctx.Row.Status = TicketStatus.Done;
                                await db.SaveChangesAsync(ctx.CancellationToken);
                            }
                        )
                );
            if (tableRule is not null)
                t.Authorize((user, op, row) => tableRule(new(op, row)));
        });

    public sealed record RowOp(AdminOp Op, Ticket? Row);

    private static bool Frozen(object? row) => row is Ticket { Status: TicketStatus.Done };

    public static TheoryData<string> Operations => ["update", "delete", "action", "table rule"];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_rule_about_the_row_holds_on_every_surface(string operation)
    {
        await using var host = await PanelHost.StartAsync(p =>
            Tickets(
                p,
                operation == "table rule" ? r => r.Op != AdminOp.Update || !Frozen(r.Row) : null
            )
        );
        host.Policy.Allows = operation switch
        {
            "update" => r =>
                r is not TableRequest<Ticket> { Op: AdminOp.Update } t || !Frozen(t.Row),
            "delete" => r =>
                r is not TableRequest<Ticket> { Op: AdminOp.Delete } t || !Frozen(t.Row),
            "action" => r => r is not ActionRequest<Ticket> a || !Frozen(a.Row),
            _ => _ => true,
        };
        var tickets = await host.Db(Seed.OpenAndDoneAsync);
        var (open, done) = (tickets[0].Id.ToString(), tickets[1].Id.ToString());
        var hook = operation switch
        {
            "delete" => "delete",
            "action" => "action:close",
            _ => "edit",
        };
        var tool = operation switch
        {
            "delete" => "delete_ticket",
            "action" => "ticket_close",
            _ => "update_ticket",
        };

        // The panel offers it on the open row and not on the done one.
        if (operation == "action")
        {
            Assert.True((await host.PageAsync($"/admin/entities/ticket/{open}")).Has(hook));
            Assert.False((await host.PageAsync($"/admin/entities/ticket/{done}")).Has(hook));
        }
        else
            Assert.Equal(1, (await host.PageAsync("/admin/entities/ticket")).Count(hook));

        // A direct call on the done row is refused, though nothing in the UI stood in its way.
        using var admin = host.As();
        Task Call(string key) =>
            operation switch
            {
                "delete" => admin.Panel.DeleteAsync("ticket", key),
                "action" => admin.Panel.RunActionAsync("ticket", key, "close", Args()),
                _ => admin.Panel.UpdateAsync("ticket", key, Args(("Title", "Renamed"))),
            };
        await Assert.ThrowsAsync<AdminForbiddenException>(() => Call(done));

        // Only the detail page shows actions, so a list does not ask about them per row.
        Assert.All(
            (await admin.Panel.ListAsync("ticket", new ListRequest())).Rows,
            r => Assert.Empty(r.Allowed.Actions)
        );

        // MCP lists the tool, since some rows allow it, and refuses it on the done row.
        await using var mcp = await host.ConnectMcpAsync();
        Assert.Contains(tool, (await mcp.ListToolsAsync()).Select(t => t.Name));
        Dictionary<string, object?> On(string key) =>
            operation is "update" or "table rule"
                ? Args(("key", key), ("Title", "Renamed"))
                : Args(("key", key));
        var refused = await mcp.CallToolAsync(tool, On(done));
        Assert.True(refused.IsError);
        Assert.StartsWith("Forbidden", Text(refused));

        // Positive control: the open row goes through on both.
        var allowed = await mcp.CallToolAsync(tool, On(open));
        Assert.NotEqual(true, allowed.IsError);
        var stored = await host.Db(db => db.Tickets.AsNoTracking().ToListAsync());
        var doneRow = stored.Single(t => t.Id.ToString() == done);
        Assert.Equal("Done one", doneRow.Title);
        Assert.Single(host.Audit);

        // The edit form shows the row's values, so a Read rule about the row holds there too.
        if (operation == "update")
        {
            host.Policy.Allows = r =>
                r is not TableRequest<Ticket> { Op: AdminOp.Read } t || !Frozen(t.Row);
            await Assert.ThrowsAsync<AdminForbiddenException>(() =>
                admin.Panel.EditAsync("ticket", done)
            );
            Assert.NotNull(await admin.Panel.EditAsync("ticket", open));
        }
    }

    [Fact]
    public async Task A_table_the_policy_hides_is_hidden_on_every_surface()
    {
        await using var host = await PanelHost.StartAsync(p => Tickets(p));
        host.Policy.Allows = r => r.User.Identity!.Name != "guest" || r is not TableRequest<Ticket>;

        Assert.True((await host.PageAsync("/admin", "admin")).Has("nav:ticket"));
        Assert.False((await host.PageAsync("/admin", "guest")).Has("nav:ticket"));
        Assert.True((await host.PageAsync("/admin", "guest")).Has("nav:home"));

        await host.Db(Seed.OpenAndDoneAsync);
        using (var admin = host.As())
            Assert.Equal(2, (await admin.Panel.SearchAsync("one")).Count);
        using (var guest = host.As("guest"))
        {
            await Assert.ThrowsAsync<AdminForbiddenException>(() =>
                guest.Panel.ListAsync("ticket", new ListRequest())
            );
            Assert.Empty(await guest.Panel.SearchAsync("one"));

            // A policy that throws answers no to a question about access.
            host.Policy.Allows = _ => throw new InvalidOperationException("policy store down");
            Assert.False(await guest.Panel.CanAsync("ticket", AdminOp.Read));
            host.Policy.Allows = r =>
                r.User.Identity!.Name != "guest" || r is not TableRequest<Ticket>;
        }

        await using var mcp = await host.ConnectMcpAsync(user: "guest");
        var tools = (await mcp.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Contains("search", tools);
        Assert.DoesNotContain("list_ticket", tools);
    }

    [Fact]
    public async Task Every_read_a_dashboard_or_a_form_makes_is_asked_too()
    {
        await using var host = await PanelHost.StartAsync(p =>
            Tickets(p)
                .Dashboard(
                    "ops",
                    d =>
                        d.Row(r =>
                            r.Stat<TestDb, int>("Count", (db, ct) => db.Tickets.CountAsync(ct))
                                .Stat<TestDb, int>(
                                    "Slow",
                                    (_, _) => Task.FromException<int>(new TaskCanceledException())
                                )
                                .Table<Ticket>("Recent")
                                .Chart<int>(
                                    "Polled",
                                    c =>
                                        c.Points<TestDb>(
                                                (db, ct) =>
                                                    Task.FromResult<IReadOnlyList<int>>([1, 2])
                                            )
                                            .X(n => n)
                                            .Y(n => n)
                                            .Poll(TimeSpan.FromSeconds(1))
                                )
                        )
                )
                .Form<NoticeInput>(
                    "notice",
                    f =>
                        f.Field(i => i.Channel, x => x.Options("inbox"))
                            .Field(i => i.Recipients, x => x.Options("a"))
                            .Run(_ => { })
                )
        );
        await host.Db(Seed.OpenAndDoneAsync);
        using var guest = host.As("guest");

        // The table widget is a read of its table: refused there, the rest of the dashboard still loads.
        host.Policy.Allows = r => r is not TableRequest<Ticket>;
        var dashboard = await guest.Panel.DashboardAsync("ops");
        Assert.Equal(2, ((StatView)dashboard.Widgets["count"]).Value);
        Assert.StartsWith("You may not read", dashboard.Widgets["recent"].Error);
        Assert.Equal("This widget failed to load.", dashboard.Widgets["slow"].Error);

        // A polled chart and a form's choices are asked for as the dashboard and the form are.
        host.Policy.Allows = r => r is not (DashboardRequest or FormRequest);
        await Assert.ThrowsAsync<AdminForbiddenException>(() =>
            guest.Panel.WidgetAsync("ops", "polled")
        );
        await Assert.ThrowsAsync<AdminForbiddenException>(() => guest.Panel.FormAsync("notice"));
    }

    [Fact]
    public async Task The_umbrella_policy_is_asked_again_on_every_operation()
    {
        await using var host = await PanelHost.StartAsync(p => Tickets(p));
        await host.Db(Seed.OpenAndDoneAsync);
        await using var mcp = await host.ConnectMcpAsync();
        using var circuit = host.As();
        Assert.NotEqual(true, (await mcp.CallToolAsync("list_ticket", Args())).IsError);
        Assert.Equal(2, (await circuit.Panel.ListAsync("ticket", new ListRequest())).Total);

        // The admin loses access: a connection and a circuit opened before stop working at once.
        host.Revoked["admin"] = true;
        var refused = await mcp.CallToolAsync("list_ticket", Args());
        Assert.True(refused.IsError);
        Assert.StartsWith("Forbidden", Text(refused));
        await Assert.ThrowsAsync<AdminForbiddenException>(() =>
            circuit.Panel.ListAsync("ticket", new ListRequest())
        );
    }

    [Fact]
    public async Task The_endpoints_leave_the_challenge_to_the_hosts_scheme_and_serve_the_scripts_to_anyone()
    {
        await using var host = await PanelHost.StartAsync(
            p => Tickets(p),
            s =>
                s.AddAuthorizationBuilder()
                    .SetFallbackPolicy(
                        new AuthorizationPolicyBuilder(TestAuth.Name)
                            .RequireAuthenticatedUser()
                            .Build()
                    )
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await host.Http(null).GetAsync("/admin")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Http(null).GetAsync("/_framework/blazor.web.js")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.Http(null).GetAsync("/_content/AdminForge/adminforge.css")).StatusCode
        );
        // The host's own assets stay under its fallback policy.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await host.Http(null).GetAsync("/todo.css")).StatusCode
        );

        var mcp = await host.Http(null)
            .PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
        Assert.Contains("resource_metadata=", mcp.Headers.WwwAuthenticate.ToString());
    }
}
