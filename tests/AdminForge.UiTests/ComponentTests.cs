using AdminForge.UI.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using MudBlazor;
using MudBlazor.Services;

namespace AdminForge.UiTests;

public enum Stage
{
    Open,
    Done,
}

public sealed record Item(int Id, string Title, Stage Stage);

public sealed record Postpone(int Days);

public sealed class Items : IAdminSource<Item, int>, IAdminWritable<Item>
{
    public List<Item> Rows { get; } = [new(1, "First", Stage.Open), new(2, "Second", Stage.Done)];
    public IReadOnlySet<string>? LastChanged { get; private set; }

    public Task<AdminPage<Item>> ListAsync(AdminQuery<Item> query, CancellationToken ct) =>
        Task.FromResult(AdminPage.From(Rows, query));

    public Task<Item?> FindAsync(int key, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.Id == key));

    public Task<Item> CreateAsync(Item row, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Item> UpdateAsync(Item row, IReadOnlySet<string> changed, CancellationToken ct)
    {
        LastChanged = changed;
        Rows[Rows.FindIndex(r => r.Id == row.Id)] = row;
        return Task.FromResult(row);
    }
}

public sealed class TestHost : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "AdminForge.UiTests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public string EnvironmentName { get; set; } = "Testing";
}

/// <summary>The components a browser would otherwise be needed for, rendered over a real panel.</summary>
// xUnit disposes an IAsyncDisposable class asynchronously, which MudBlazor's services need.
public sealed class ComponentTests : BunitContext
{
    public ComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddAuthorization();
        Services.AddLogging();
        // AddAdminForge registers the server render mode, whose options read the host.
        Services.AddSingleton<IWebHostEnvironment, TestHost>();
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton<Items>();
        Services.AddAdminForge(p =>
            p.AllowAnonymous()
                .Table<Item>(t =>
                    t.Source<Items>(o => o.Filters(x => x.Stage))
                        .Live<Items, Item>(
                            "Echo",
                            (_, item, ct) => Echo(item, ct),
                            w => w.Feed(i => i.Title)
                        )
                        .Action(
                            "postpone",
                            "Postpone",
                            a =>
                                a.Input<Postpone>()
                                    .Run(ctx =>
                                    {
                                        if (ctx.Input.Days > 7)
                                            throw new AdminRejection(
                                                new Dictionary<string, string>
                                                {
                                                    ["Days"] = "A week at most.",
                                                }
                                            );
                                        ctx.Success($"Postponed {ctx.Input.Days} days.");
                                    })
                        )
                        .Action("issue", "Issue key", a => a.Run(ctx => ctx.Result("```\nak_1\n```")))
                )
        );
        Services.AddMudServices();
        // The server render mode replaces bUnit's navigation and JS with the circuit's own.
        Services.AddSingleton<NavigationManager>(sp =>
            sp.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>()
        );
        Services.AddSingleton(JSInterop.JSRuntime);
    }

    private static async IAsyncEnumerable<Item> Echo(
        Item item,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        yield return item;
        await Task.Delay(Timeout.Infinite, ct);
    }

    [Fact]
    public void A_filter_applies_when_picked_and_its_chip_removes_it()
    {
        FilterBar.State? sent = null;
        var popovers = Render<MudPopoverProvider>();
        var bar = Render<FilterBar>(ps =>
            ps.Add(b => b.TableId, "item").Add(b => b.OnChange, (FilterBar.State s) => sent = s)
        );

        bar.Find(".af-add-filter").Click();
        popovers.Find("#af-filter-Stage").Change("Done");
        Assert.Equal("Done", sent!.Filters["Stage"]);

        // The list hands the filter back; the bar shows it as a chip whose button clears it.
        bar.Render(ps => ps.Add(b => b.Filters, sent.Filters));
        Assert.Contains("Stage: Done", bar.Find(".af-filter-chip").TextContent.Replace("\n", " "));
        bar.Find(".af-filter-chip button").Click();
        Assert.Empty(sent.Filters);
    }

    [Fact]
    public async Task An_action_dialog_keeps_a_refusal_beside_its_field_and_closes_with_the_outcome()
    {
        Render<MudPopoverProvider>();
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var dialog = await provider.InvokeAsync(() =>
            dialogs.ShowAsync<ActionInputDialog>(
                "Postpone",
                new DialogParameters<ActionInputDialog>
                {
                    { d => d.TableId, "item" },
                    { d => d.RowKey, "1" },
                    { d => d.ActionId, "postpone" },
                    { d => d.Title, "Postpone" },
                }
            )
        );

        provider.WaitForElement("#af-f-Days").Input("9");
        provider.Find("form").Submit();
        provider.WaitForAssertion(() =>
            Assert.Equal("A week at most.", provider.Find(".af-field-error").TextContent)
        );
        Assert.Contains("1 field needs attention", provider.Find("[data-af=error]").TextContent);

        provider.Find("#af-f-Days").Input("3");
        provider.Find("form").Submit();
        var result = await dialog.Result;
        Assert.Equal("Postponed 3 days.", Assert.IsType<Outcome>(result!.Data).Message);
    }

    [Fact]
    public void An_action_that_shows_a_result_raises_no_toast_beside_it()
    {
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var actions = Render<RowActions>(ps =>
            ps.Add(r => r.TableId, "item")
                .Add(r => r.RowKey, "1")
                .Add(r => r.RowLabel, "First")
                .Add(r => r.Actions, new HashSet<string> { "issue" })
        );

        actions.Find("[data-af='action:issue']").Click();
        dialogs.WaitForAssertion(() => Assert.Contains("ak_1", dialogs.Markup));
        Assert.Empty(Services.GetRequiredService<ISnackbar>().ShownSnackbars);
    }

    [Fact]
    public void Html_content_renders_in_a_sandboxed_frame_never_into_the_panel()
    {
        Render<MudPopoverProvider>();
        var provider = Render<MudDialogProvider>();
        provider.InvokeAsync(() =>
            ContentDialog.ShowAsync(
                Services.GetRequiredService<IDialogService>(),
                "Body",
                "<script>steal()</script><b>hi</b>",
                Content.Html
            )
        );

        var frame = provider.Find("iframe");
        Assert.Equal("", frame.GetAttribute("sandbox"));
        Assert.EndsWith("<script>steal()</script><b>hi</b>", frame.GetAttribute("srcdoc"));
        Assert.StartsWith(
            """<meta http-equiv="Content-Security-Policy" content="default-src 'none';""",
            frame.GetAttribute("srcdoc")
        );
        Assert.Empty(provider.FindAll("script"));
        Assert.Empty(provider.FindAll("b"));
    }

    [Fact]
    public void A_live_widget_follows_the_row_when_the_page_moves_to_another_key()
    {
        Render<MudPopoverProvider>();
        var page = Render<AdminForge.UI.Pages.EntityViewPage>(ps =>
            ps.Add(p => p.TableId, "item").Add(p => p.Key, "1")
        );
        page.WaitForAssertion(() =>
            Assert.Equal("First", page.Find("[data-af='live:echo'] td").TextContent)
        );

        page.Render(ps => ps.Add(p => p.TableId, "item").Add(p => p.Key, "2"));
        page.WaitForAssertion(() =>
            Assert.Equal("Second", page.Find("[data-af='live:echo'] td").TextContent)
        );
    }

    [Fact]
    public void Saving_an_edit_writes_only_the_fields_the_admin_changed()
    {
        Render<MudPopoverProvider>();
        var items = Services.GetRequiredService<Items>();
        var form = Render<AdminForge.UI.Pages.EntityFormPage>(ps =>
            ps.Add(p => p.TableId, "item").Add(p => p.Key, "1")
        );
        form.WaitForElement("#af-f-Title").Input("Renamed");

        // Another admin closes the item while this form is open.
        items.Rows[0] = items.Rows[0] with
        {
            Stage = Stage.Done,
        };
        form.Find("form").Submit();

        form.WaitForAssertion(() =>
            Assert.Equal(new Item(1, "Renamed", Stage.Done), items.Rows[0])
        );
        Assert.Equal(["Title"], items.LastChanged!);
    }
}

public sealed class FormatterTests
{
    [Theory]
    [InlineData(0.00004, "4E-05")]
    [InlineData(-0.00001, "-1E-05")]
    [InlineData(2.5, "2.5")]
    [InlineData(0.0, "0")]
    public void A_small_double_never_reads_as_zero(double value, string text) =>
        Assert.Equal(text, AdminForge.UI.ValueFormatter.Format(value));
}

public sealed class ThemeTests
{
    // Light: white, page, sunken. Dark: surface, page, sunken.
    [Theory]
    [InlineData(0, 137, 123, false, "#FFFFFF", "#F6F7F9", "#EFF1F4")]
    [InlineData(0, 137, 123, true, "#161A20", "#0E1014", "#1C2129")]
    [InlineData(255, 235, 59, false, "#FFFFFF", "#F6F7F9", "#EFF1F4")]
    [InlineData(20, 20, 120, true, "#161A20", "#0E1014", "#1C2129")]
    public void A_host_accent_is_fitted_until_its_text_reads_at_AA(
        int r,
        int g,
        int b,
        bool dark,
        params string[] grounds
    )
    {
        var fitted = AdminForge.UI.Theme.Fit(new(r, g, b), dark);
        foreach (var ground in grounds)
            Assert.True(
                AdminForge.UI.Theme.Contrast(fitted, Hex(ground)) >= 4.5,
                $"{fitted} on {ground}"
            );
    }

    [Fact]
    public void An_accent_that_already_reads_is_kept() =>
        Assert.Equal(
            "#2457C5",
            AdminForge.UI.Theme.Fit(new(0x24, 0x57, 0xC5), dark: false).ToString()
        );

    private static AdminForge.UI.Theme.Rgb Hex(string h) =>
        new(
            Convert.ToInt32(h[1..3], 16),
            Convert.ToInt32(h[3..5], 16),
            Convert.ToInt32(h[5..7], 16)
        );
}
