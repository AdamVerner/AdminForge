using AdminForge.Mcp;
using AdminForge.UI;
using static AdminForge.Tests.PanelHost;

namespace AdminForge.Tests;

/// <summary>Case tables for the formats and conversions every surface depends on.</summary>
public class UnitTests
{
    private sealed record Pair(string Code, int N);

    private sealed record Membership(int OrgId, Guid Id);

    [Fact]
    public void A_key_round_trips_through_a_route_segment()
    {
        var single = new KeyCodec([typeof(Ticket).GetProperty(nameof(Ticket.Id))!]);
        Assert.Equal("42", single.Encode([42]));
        Assert.Equal([42], single.Decode("42"));
        Assert.Null(single.TryDecode("abc"));
        Assert.Null(single.TryDecode("1,2"));
        Assert.Throws<AdminRejection>(() => single.Decode("abc"));

        // Each part is escaped, so a separator inside a part never splits it.
        var pair = new KeyCodec([
            typeof(Pair).GetProperty("Code")!,
            typeof(Pair).GetProperty("N")!,
        ]);
        var encoded = pair.Encode(new Pair("a,b-c", 5));
        Assert.Equal(["a,b-c", 5], pair.Decode(encoded));
        var membership = new KeyCodec([
            typeof(Membership).GetProperty("OrgId")!,
            typeof(Membership).GetProperty("Id")!,
        ]);
        var id = Guid.Parse("1e7537b9-b007-4679-87b9-595410157120");
        Assert.Equal(
            "5,1e7537b9-b007-4679-87b9-595410157120",
            membership.Encode(new Membership(5, id))
        );
        Assert.Equal([5, id], membership.Decode("5,1e7537b9-b007-4679-87b9-595410157120"));
    }

    public static TheoryData<object?, Type, object?> Coercions =>
        new()
        {
            { "3", typeof(int), 3 },
            { 3L, typeof(int), 3 },
            { 3L, typeof(int?), 3 },
            { "", typeof(int?), null },
            { "done", typeof(TicketStatus), TicketStatus.Done },
            { 1L, typeof(TicketStatus), TicketStatus.Done },
            {
                "2026-01-02T03:04:05Z",
                typeof(DateTime),
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            },
            { 2.5, typeof(decimal), 2.5m },
            { "2026-01-02", typeof(DateOnly), new DateOnly(2026, 1, 2) },
            { new DateTime(2026, 1, 2), typeof(DateOnly), new DateOnly(2026, 1, 2) },
            { "01:30:00", typeof(TimeSpan), TimeSpan.FromMinutes(90) },
            {
                "1e7537b9-b007-4679-87b9-595410157120",
                typeof(Guid),
                Guid.Parse("1e7537b9-b007-4679-87b9-595410157120")
            },
            { "1.5", typeof(double), 1.5 },
            { 7, typeof(string), "7" },
            { "", typeof(string), "" },
        };

    [Theory]
    [MemberData(nameof(Coercions))]
    public void A_submitted_value_converts_to_its_column_type(
        object? value,
        Type type,
        object? expected
    ) => Assert.Equal(expected, ScalarTypes.Coerce(value, type));

    [Fact]
    public void A_value_that_does_not_convert_throws_what_the_validator_turns_into_a_field_error()
    {
        Assert.Throws<FormatException>(() => ScalarTypes.Coerce("x", typeof(int)));
        Assert.Throws<ArgumentException>(() =>
            ScalarTypes.Coerce("Sideways", typeof(TicketStatus))
        );
        Assert.Equal(
            ["a", "b"],
            (IEnumerable<string>)
                ScalarTypes.Coerce(new List<object> { "a", "b" }, typeof(IReadOnlyList<string>))!
        );
    }

    [Theory]
    [InlineData("tomorrow", typeof(DateOnly))]
    [InlineData("x", typeof(TimeSpan))]
    [InlineData("x", typeof(OrderId))]
    public void A_parsable_type_that_does_not_parse_does_not_coerce(string text, Type type) =>
        Assert.False(ScalarTypes.TryCoerce(text, type, out _));

    public readonly record struct OrderId(int Value) : IParsable<OrderId>
    {
        public static OrderId Parse(string s, IFormatProvider? provider) =>
            new(int.Parse(s, provider));

        public static bool TryParse(string? s, IFormatProvider? provider, out OrderId result)
        {
            result = default;
            return false;
        }
    }

    [Fact]
    public void A_positional_record_is_built_and_copied_through_its_constructor()
    {
        var factory = new RowFactory(typeof(Contact));
        Assert.Equal(new Contact(0, "Ada", null, 0), factory.Create(Args(("Name", "Ada"))));
        Assert.Equal(
            new Contact(1, "Ada", "a@x", 9),
            factory.Copy(new Contact(1, "Ada", "a@x", 7), Args(("CompanyId", 9)))
        );
        Assert.Equal("", new RowFactory(typeof(Ticket)).Defaults()["Title"]);
    }

    [Fact]
    public void A_page_from_memory_filters_sorts_and_pages()
    {
        Contact[] rows =
        [
            new(1, "Ada", null, 7),
            new(2, "adam", null, 8),
            new(3, "Brian", null, 7),
        ];
        var query = new AdminQuery<Contact>(
            new AdminPageRequest(1, 1),
            sortBy: "Id",
            sortDescending: true,
            filters: Args(("Name", "AD"))
        );
        var page = AdminPage.From(rows, query);
        Assert.Equal(2, page.Total);
        Assert.Equal(1, Assert.Single(page.Rows).Id);
        var byCompany = new AdminQuery<Contact>(new(0, 10), filters: Args(("CompanyId", 7)));
        Assert.Equal([1, 3], AdminPage.From(rows, byCompany).Rows.Select(r => r.Id));

        // A filter comes back typed, nullable for a value type, null when not given.
        Assert.Equal(7, byCompany.Filter(c => c.CompanyId));
        Assert.Null(byCompany.Filter(c => c.Email));
        Assert.Equal("AD", query.Filter(c => c.Name));
        Assert.True(query.SortedBy(c => c.Id, out var descending) && descending);
    }

    [Theory]
    [InlineData("/admin", true)]
    [InlineData("/admin/entities/ticket?x=1", true)]
    [InlineData("~/admin", true)]
    [InlineData("/", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("admin", false)]
    [InlineData("/admin\r\nSet-Cookie:x", false)]
    [InlineData("", false)]
    public void A_return_url_must_stay_on_this_site(string url, bool local) =>
        Assert.Equal(local, Login.IsLocalUrl(url));

    [Fact]
    public void A_value_renders_by_its_column_then_the_panel_then_the_default()
    {
        var at = new DateTime(2025, 3, 4, 17, 8, 9);
        var column = new ColumnModel
        {
            Name = "At",
            Title = "At",
            ClrType = typeof(DateTime),
        };
        PanelModel Panel(string? format) =>
            new()
            {
                Title = "",
                Theme = new(),
                Tables = [],
                Forms = [],
                Dashboards = [],
                Pages = [],
                Formats = format is null
                    ? new Dictionary<Type, string>()
                    : new() { [typeof(DateTime)] = format },
            };
        Assert.Equal("2025-03-04 17:08", ValueFormatter.Format(at, column, Panel(null)));
        Assert.Equal("04.03.2025", ValueFormatter.Format(at, column, Panel("dd.MM.yyyy")));
        Assert.Equal(
            "17:08:09",
            ValueFormatter.Format(at, column with { Format = "HH:mm:ss" }, Panel("dd.MM.yyyy"))
        );
        Assert.Equal("Yes", ValueFormatter.Format(true));
        Assert.Null(ValueFormatter.Format(null));
    }

    public static TheoryData<object, string> Defaults =>
        new()
        {
            { new DateTimeOffset(2025, 3, 4, 17, 8, 9, TimeSpan.FromHours(2)), "2025-03-04 17:08" },
            { new DateOnly(2025, 3, 4), "2025-03-04" },
            { new TimeOnly(7, 5, 30), "07:05" },
            { false, "No" },
            { 1234.5m, "1234.5" },
            { 0.25, "0.25" },
            { 63.42237800215959, "63.4224" },
            { 0.00004, "4E-05" },
            { -0.00001, "-1E-05" },
            { TicketStatus.Done, "Done" },
            { Guid.Empty, "00000000-0000-0000-0000-000000000000" },
        };

    [Theory]
    [InlineData("DueAt", "Due at")]
    [InlineData("Id", "Id")]
    [InlineData("SentOverMQTT", "Sent over MQTT")]
    [InlineData("sales overview", "Sales overview")]
    [InlineData("name", "Name")]
    public void A_member_name_becomes_a_sentence_case_label(string name, string label) =>
        Assert.Equal(label, Members.Humanize(name));

    // Invariant whatever the server's culture: a Czech host must not render 1234,5.
    [Theory]
    [MemberData(nameof(Defaults))]
    public void A_value_renders_the_same_on_any_server(object value, string expected)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new("cs-CZ");
        try
        {
            Assert.Equal(expected, ValueFormatter.Format(value));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(0, 1, "HH:mm:ss")]
    [InlineData(30, 5, "HH:mm:ss")]
    [InlineData(120, 30, "HH:mm:ss")]
    [InlineData(3600, 900, "HH:mm")]
    [InlineData(86400, 14400 * 1.5, "HH:mm")]
    [InlineData(14 * 86400, 3 * 86400, "MM-dd")]
    [InlineData(365 * 86400, 30 * 86400, "MM-dd")]
    public void A_time_axis_labels_about_six_steps_of_the_span(
        double spanSeconds,
        double spacingSeconds,
        string format
    )
    {
        var spacing = TimeAxis.LabelSpacing(TimeSpan.FromSeconds(spanSeconds));
        Assert.Equal(TimeSpan.FromSeconds(spacingSeconds), spacing);
        Assert.Equal(format, TimeAxis.LabelFormat(spacing));
    }

    [Fact]
    public void The_sidebar_keeps_registration_order_with_settings_and_account_last()
    {
        NavModel Nav(string? group, NavArea area, int order) => new(group, area, false, order);
        var entries = new[]
        {
            ("Audit", Nav(null, NavArea.Settings, 0)),
            ("Tasks", Nav("Work", NavArea.Main, 1)),
            ("Home", Nav(null, NavArea.Main, 2)),
            ("People", Nav("Crm", NavArea.Main, 3)),
            ("Lists", Nav("Work", NavArea.Main, 4)),
            ("Tokens", Nav(null, NavArea.Account, 5)),
        };
        var groups = NavGrouping.Arrange(entries, e => e.Item2);
        Assert.Equal([null, "Work", "Crm", "Settings", "Account"], groups.Select(g => g.Key));
        Assert.Equal(["Tasks", "Lists"], groups[1].Entries.Select(e => e.Item1));
    }

    [Theory]
    [InlineData("ticket", "ticket")]
    [InlineData("todo-list", "todo_list")]
    [InlineData("SiteSettings", "site_settings")]
    [InlineData("grant-experimental", "grant_experimental")]
    public void A_tool_name_is_the_id_in_snake_case(string id, string slug) =>
        Assert.Equal(slug, PanelTool.Snake(id));

    [Theory]
    [InlineData(typeof(int?), "integer", null)]
    [InlineData(typeof(decimal), "number", null)]
    [InlineData(typeof(bool), "boolean", null)]
    [InlineData(typeof(DateTime), "string", "date-time")]
    [InlineData(typeof(DateOnly), "string", "date")]
    [InlineData(typeof(Guid), "string", null)]
    public void A_column_type_maps_to_json_schema(Type type, string json, string? format)
    {
        var schema = JsonSchema.ForClrType(type, null);
        Assert.Equal((json, format), ((string)schema["type"]!, (string?)schema["format"]));
    }

    [Fact]
    public void A_Markdown_code_block_carries_a_copy_button()
    {
        var html = MarkdownRenderer.ToHtml("Your key:\n\n```\nak_<secret>\n```\n\nand `inline`.");
        Assert.Matches(
            "<div class=\"af-code\"><button [^>]*data-af=\"copy-code\">.*</button>\n<pre><code>ak_&lt;secret&gt;\n</code></pre>\n</div>",
            html
        );
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "copy-code"));
    }

    [Fact]
    public void Markdown_renders_tables_and_escapes_raw_html()
    {
        var html = MarkdownRenderer.ToHtml("| a | b |\n|---|---|\n| 1 | <script>x()</script> |");
        Assert.Contains("<table>", html);
        Assert.DoesNotContain("<script>", html);

        var links = MarkdownRenderer.ToHtml(
            "[a](javascript:alert(1)) <javascript:alert(2)> ![i](data:text/html,x) [b](JAVASCRIPT:x) [r](&#106;avascript:x) "
                + "[ok](https://x.test/p) [rel](/admin/x?y=1:2) <mailto:a@x.test>"
        );
        var urls = System
            .Text.RegularExpressions.Regex.Matches(links, "(?:href|src)=\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value);
        Assert.Equal(
            ["", "", "", "", "", "https://x.test/p", "/admin/x?y=1:2", "mailto:a@x.test"],
            urls
        );
    }
}
