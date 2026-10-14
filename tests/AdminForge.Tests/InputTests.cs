using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using static AdminForge.Tests.PanelHost;

namespace AdminForge.Tests;

public sealed record UploadInput([Range(1, 3)] int Copies, AdminFile Document, string? Note);

public sealed record ReassignInput([Range(1, 5)] int Priority, string Reason, string? Note);

public sealed record NoticeInput(string Channel, IReadOnlyList<string>? Recipients);

public sealed record PickInput(int Number, Guid Ref, TicketStatus Status);

public sealed record RetitleInput(string Title, string Team);

public sealed record AlertInput(string Message, bool Urgent);

/// <summary>Input is converted and checked on the server, whichever surface sent it; a refusal is shown once and never audited.</summary>
public class InputTests
{
    private static void Panel(PanelBuilder p) =>
        p.Table<Ticket>(t =>
                t.Column(
                        x => x.Title,
                        c => c.Validate(s => s.Length >= 3, "At least 3 characters.")
                    )
                    .Action(
                        "refuse",
                        "Refuse",
                        a => a.Run(_ => throw new AdminRejection("Nope, it is locked."))
                    )
                    .Action(
                        "crash",
                        "Crash",
                        a => a.Run(_ => throw new InvalidOperationException("secret detail"))
                    )
                    .Action(
                        "timeout",
                        "Time out",
                        a => a.Run(_ => throw new TaskCanceledException("HttpClient timed out"))
                    )
            )
            .Form<UploadInput>(
                "upload",
                f =>
                    f.Field(i => i.Document, x => x.Accept(".txt").MaxBytes(10))
                        .Field(i => i.Note, x => x.Markdown())
                        .Run(ctx =>
                        {
                            using var reader = new StreamReader(ctx.Input.Document.OpenRead());
                            ctx.Success(
                                $"{ctx.Input.Copies} x {ctx.Input.Document.Name}: {reader.ReadToEnd()}"
                            );
                        })
            )
            .Form<NoticeInput>(
                "notice",
                f =>
                    f.Field(i => i.Channel, x => x.Options("inbox", "email"))
                        .Field(
                            i => i.Recipients,
                            x =>
                                x.Options<TestDb>(
                                    async (db, ct) =>
                                        await db
                                            .People.Select(p => new AdminOption(
                                                p.Id.ToString(),
                                                p.Name
                                            ))
                                            .ToListAsync(ct)
                                )
                        )
                        .Run(ctx =>
                            ctx.Success(
                                $"{ctx.Input.Channel} to {string.Join(",", ctx.Input.Recipients ?? [])}"
                            )
                        )
            );

    [Fact]
    public async Task An_action_asks_for_its_input_checks_it_and_records_it_with_the_row()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Ticket>(t =>
                t.Action(
                    "reassign",
                    "Reassign",
                    a =>
                        a.Input<ReassignInput>()
                            .Field(i => i.Reason, f => f.Options("holiday", "overload"))
                            .Field(i => i.Note, f => f.Markdown())
                            .Run<TestDb>(
                                async (db, ctx) =>
                                {
                                    ctx.Row.Notes = $"{ctx.Input.Reason}: {ctx.Input.Note}";
                                    await db.SaveChangesAsync(ctx.CancellationToken);
                                    ctx.Success($"Reassigned {ctx.Row.Title}.");
                                    ctx.Result($"**Priority** {ctx.Input.Priority}");
                                }
                            )
                )
            )
        );
        var key = (await host.Db(Seed.OpenAndDoneAsync))[0].Id.ToString();
        using var admin = host.As();

        var form = await admin.Panel.ActionFormAsync("ticket", key, "reassign");
        Assert.Equal(["Priority", "Reason", "Note"], form.Fields.Select(f => f.Name));
        Assert.Equal(["holiday", "overload"], form.Options["Reason"].Select(o => o.Value));

        // Out of range and not a choice: both named, the row untouched, nothing audited.
        var refused = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.RunActionAsync(
                "ticket",
                key,
                "reassign",
                Args(("Priority", 9), ("Reason", "boredom"))
            )
        );
        Assert.Equal(["Priority", "Reason"], refused.FieldErrors.Keys.Order());
        Assert.Empty(host.Audit);

        var outcome = await admin.Panel.RunActionAsync(
            "ticket",
            key,
            "reassign",
            Args(("Priority", "2"), ("Reason", "holiday"), ("Note", "Back *Monday*"))
        );
        Assert.Equal(new Outcome("Reassigned Open one.", "**Priority** 2"), outcome);
        Assert.Equal(
            "holiday: Back *Monday*",
            (await host.Db(db => db.Tickets.AsNoTracking().FirstAsync())).Notes
        );
        var audit = Assert.Single(host.Audit);
        Assert.Equal(
            (AdminAuditOperation.Action, "ticket", key, "reassign"),
            (audit.Operation, audit.Table, audit.Key, audit.Action)
        );
        Assert.Equal(2, audit.Input["Priority"]);
    }

    [Fact]
    public async Task An_action_takes_its_input_choices_and_presence_from_the_row_and_can_open_another()
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Table<Ticket>(t =>
                t.Action(
                        "retitle",
                        "Retitle",
                        a =>
                            a.Input<RetitleInput>()
                                .When(x => x.Status == TicketStatus.Open)
                                .Prefill(x => new RetitleInput(x.Title, ""))
                                .Options<TestDb>(
                                    i => i.Team,
                                    (_, x, _) =>
                                        Task.FromResult<IReadOnlyList<AdminOption>>(
                                            [new($"{x.Status}-team", "Team")]
                                        )
                                )
                                .Run(ctx => ctx.Success($"{ctx.Input.Title} for {ctx.Input.Team}"))
                    )
                    .Action(
                        "duplicate",
                        "Duplicate",
                        a =>
                            a.Run<TestDb>(
                                async (db, ctx) =>
                                {
                                    var copy = new Ticket { Title = ctx.Row.Title + " (copy)" };
                                    db.Tickets.Add(copy);
                                    await db.SaveChangesAsync();
                                    ctx.Open(copy);
                                }
                            )
                    )
            )
        );
        var rows = await host.Db(Seed.OpenAndDoneAsync);
        var (open, done) = (rows[0].Id.ToString(), rows[1].Id.ToString());
        using var admin = host.As();

        // The dialog opens with the row's values and the row's choices; submit checks the same choices.
        var form = await admin.Panel.ActionFormAsync("ticket", open, "retitle");
        Assert.Equal("Open one", form.Values["Title"]);
        Assert.Equal(["Open-team"], form.Options["Team"].Select(o => o.Value));
        var refused = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.RunActionAsync(
                "ticket",
                open,
                "retitle",
                Args(("Title", "Renamed"), ("Team", "Done-team"))
            )
        );
        Assert.Equal("Team: 'Done-team' is not a choice.", refused.FieldErrors["Team"]);
        Assert.Equal(
            "Renamed for Open-team",
            (
                await admin.Panel.RunActionAsync(
                    "ticket",
                    open,
                    "retitle",
                    Args(("Title", "Renamed"), ("Team", "Open-team"))
                )
            ).Message
        );

        // A row the action does not apply to does not offer it, and a direct call says why.
        Assert.DoesNotContain("retitle", (await admin.Panel.GetAsync("ticket", done))!.Row.Allowed.Actions);
        Assert.Equal(
            "Retitle does not apply to Done one.",
            (
                await Assert.ThrowsAsync<AdminRejection>(() =>
                    admin.Panel.ActionFormAsync("ticket", done, "retitle")
                )
            ).Message
        );

        // Over MCP the row's choices cannot be listed up front; the tool names the row an action opened.
        Assert.False((await admin.Panel.ActionFormAsync("ticket", null, "retitle")).Options.ContainsKey("Team"));
        var outcome = await admin.Panel.RunActionAsync("ticket", open, "duplicate", Args());
        Assert.Equal("Open one (copy)", (await admin.Panel.GetAsync("ticket", outcome.Open!))!.Row.Label);
        await using var mcp = await host.ConnectMcpAsync();
        var duplicated = Text(await mcp.CallToolAsync("ticket_duplicate", Args(("key", open))));
        var last = await host.Db(db => db.Tickets.MaxAsync(x => x.Id));
        Assert.Equal($"Opened the row with key {last}.", duplicated);
    }

    [Fact]
    public async Task A_rejection_is_shown_once_and_any_other_failure_is_logged_and_kept_from_the_caller()
    {
        await using var host = await PanelHost.StartAsync(Panel);
        var key = (await host.Db(Seed.OpenAndDoneAsync))[0].Id.ToString();
        using var admin = host.As();
        await using var mcp = await host.ConnectMcpAsync();

        var refused = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.RunActionAsync("ticket", key, "refuse", Args())
        );
        Assert.Equal("Nope, it is locked.", refused.Message);
        var overMcp = await mcp.CallToolAsync("ticket_refuse", Args(("key", key)));
        Assert.True(overMcp.IsError);
        Assert.Equal("Nope, it is locked.", Text(overMcp));

        var crashed = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.RunActionAsync("ticket", key, "crash", Args())
        );
        Assert.DoesNotContain("secret", crashed.Message);
        Assert.Contains("server log", crashed.Message);
        Assert.Contains(LogCapture.Errors, e => e.Contains("secret detail"));
        // A cancellation the caller did not ask for is a failure, not an escape past the UI's catch.
        await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.RunActionAsync("ticket", key, "timeout", Args())
        );
        Assert.DoesNotContain(
            "secret",
            Text(await mcp.CallToolAsync("ticket_crash", Args(("key", key))))
        );

        Assert.Empty(host.Audit);
    }

    [Fact]
    public async Task An_MCP_write_meets_the_same_rules_as_the_form()
    {
        await using var host = await PanelHost.StartAsync(Panel);
        await host.Db(async db =>
        {
            db.People.Add(new Person { Name = "Ada" });
            return await db.SaveChangesAsync();
        });
        await using var mcp = await host.ConnectMcpAsync();

        // Required, an attribute's limit, a column rule and a value of the wrong type, each named.
        var missing = await mcp.CallToolAsync("create_ticket", Args(("Status", "Open")));
        Assert.Contains("- Title: Title is required.", Text(missing));
        var tooLong = await mcp.CallToolAsync(
            "create_ticket",
            Args(("Title", new string('x', 81)))
        );
        Assert.Contains("- Title:", Text(tooLong));
        var created = await mcp.CallToolAsync(
            "create_ticket",
            Args(("Title", "Valid"), ("Status", "Done"))
        );
        Assert.NotEqual(true, created.IsError);
        var key = (await host.Db(db => db.Tickets.SingleAsync())).Id.ToString();
        var rule = await mcp.CallToolAsync("update_ticket", Args(("key", key), ("Title", "ab")));
        Assert.Contains("- Title: At least 3 characters.", Text(rule));
        var wrongType = await mcp.CallToolAsync(
            "update_ticket",
            Args(("key", key), ("Status", "Sideways"))
        );
        Assert.Contains("- Status:", Text(wrongType));
        var notWritable = await mcp.CallToolAsync(
            "update_ticket",
            Args(("key", key), ("CreatedAt", "2020-01-01"))
        );
        Assert.True(notWritable.IsError);

        // A choice is checked against the options as they are when submitted.
        var pigeon = await mcp.CallToolAsync("form_notice", Args(("Channel", "pigeon")));
        Assert.Contains("'pigeon' is not a choice", Text(pigeon));
        var stranger = await mcp.CallToolAsync(
            "form_notice",
            Args(("Channel", "inbox"), ("Recipients", new[] { "1", "99" }))
        );
        Assert.Contains("'99' is not a choice", Text(stranger));
        var sent = await mcp.CallToolAsync(
            "form_notice",
            Args(("Channel", "inbox"), ("Recipients", new[] { "1" }))
        );
        Assert.Equal("inbox to 1", Text(sent));

        var stored = await host.Db(db => db.Tickets.AsNoTracking().SingleAsync());
        Assert.Equal(("Valid", TicketStatus.Done), (stored.Title, stored.Status));
        Assert.Equal(
            [AdminAuditOperation.Create, AdminAuditOperation.Form],
            host.Audit.Select(e => e.Operation)
        );
    }

    private const string ChosenRef = "00000000-0000-0000-0000-000000000001";

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("true", true)]
    public async Task An_unticked_box_is_false_not_missing(string? urgent, bool expected)
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Form<AlertInput>("alert", f => f.Run(ctx => ctx.Success($"Urgent: {ctx.Input.Urgent}.")))
        );
        using var admin = host.As();
        var outcome = await admin.Panel.SubmitFormAsync("alert", Args(("Message", "Hi"), ("Urgent", urgent)));
        Assert.Equal($"Urgent: {expected}.", outcome.Message);
    }

    [Theory]
    [InlineData("Number", "2", true)]
    [InlineData("Number", "3", false)]
    [InlineData("Ref", ChosenRef, true)]
    [InlineData("Ref", "00000000-0000-0000-0000-000000000002", false)]
    [InlineData("Status", "Open", true)]
    [InlineData("Status", "Done", false)]
    public async Task A_select_over_a_non_text_type_checks_the_choice(
        string field,
        string value,
        bool chosen
    )
    {
        await using var host = await PanelHost.StartAsync(p =>
            p.Form<PickInput>(
                "pick",
                f =>
                    f.Field(i => i.Number, x => x.Options("1", "2"))
                        .Field(i => i.Ref, x => x.Options(ChosenRef))
                        .Field(i => i.Status, x => x.Options("Open"))
                        .Run(ctx => ctx.Success("Picked."))
            )
        );
        using var admin = host.As();
        var input = new Dictionary<string, object?>
        {
            ["Number"] = "1",
            ["Ref"] = ChosenRef,
            ["Status"] = "Open",
            [field] = value,
        };
        if (chosen)
            Assert.Equal("Picked.", (await admin.Panel.SubmitFormAsync("pick", input)).Message);
        else
            Assert.Equal(
                $"{field}: '{value}' is not a choice.",
                (
                    await Assert.ThrowsAsync<AdminRejection>(() =>
                        admin.Panel.SubmitFormAsync("pick", input)
                    )
                ).FieldErrors[field]
            );
    }

    [Fact]
    public async Task An_upload_field_checks_its_file_and_the_audit_names_it_without_its_bytes()
    {
        await using var host = await PanelHost.StartAsync(Panel);
        using var admin = host.As();
        AdminFile File(string name, string content) =>
            new(name, "text/plain", System.Text.Encoding.UTF8.GetBytes(content));

        var page = await host.PageAsync("/admin/forms/upload");
        Assert.Equal(["Copies", "Document", "Note"], page.Hooks("field:"));

        var refused = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.SubmitFormAsync(
                "upload",
                Args(("Copies", 9), ("Document", File("run.exe", "MZ")))
            )
        );
        Assert.Contains("'.exe' files are not accepted", refused.FieldErrors["Document"]);
        Assert.True(refused.FieldErrors.ContainsKey("Copies"));
        var tooLarge = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.SubmitFormAsync(
                "upload",
                Args(("Copies", 1), ("Document", File("big.txt", "far too long")))
            )
        );
        Assert.Contains("larger than 10 bytes", tooLarge.FieldErrors["Document"]);
        var missing = await Assert.ThrowsAsync<AdminRejection>(() =>
            admin.Panel.SubmitFormAsync("upload", Args(("Copies", 1)))
        );
        Assert.Equal("Document is required.", missing.FieldErrors["Document"]);

        var done = await admin.Panel.SubmitFormAsync(
            "upload",
            Args(("Copies", "2"), ("Document", File("notes.txt", "hello")))
        );
        Assert.Equal("2 x notes.txt: hello", done.Message);
        var audit = Assert.Single(host.Audit);
        Assert.Equal(
            ("upload", "notes.txt (text/plain, 5 bytes)"),
            (audit.Action, audit.Input["Document"])
        );

        // A file cannot travel in a tool call, so a form that needs one is no tool.
        await using var mcp = await host.ConnectMcpAsync();
        var tools = (await mcp.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Contains("form_notice", tools);
        Assert.DoesNotContain("form_upload", tools);
    }
}
