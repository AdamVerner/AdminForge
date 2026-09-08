using AdminForge.Core.Contracts;
using CrmApp;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.IntegrationTests;

/// <summary>
/// The provider-backed panel, walked the way an admin walks it: an organization's detail page
/// carries its members and its keys, each table filtered to that organization and paged on its own.
/// </summary>
public class CrmPanelTests : IClassFixture<CrmPanelFixture>
{
    private readonly CrmPanelFixture _panel;

    public CrmPanelTests(CrmPanelFixture panel) => _panel = panel;

    [Fact]
    public async Task An_Organization_Carries_Its_Members_And_Keys_On_Its_Detail_Page()
    {
        using var scope = _panel.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var orgs = bridge.FindEntityByRouteName("Organization")!;

        var view = (await bridge.FindAsync(orgs, "1"))!;

        var members = Assert.Single(view.RelatedLinks, l => l.Label == "Members");
        Assert.True(members.Inline);
        Assert.Equal(1, members.Filter["OrgId"]);
        Assert.Equal(["AccountId", "Role", "JoinedAt", "IsActive"], members.Columns);

        // The embedded table is the real one: filtered to this organization, and paged.
        var memberMeta = bridge.FindEntityByRouteName(members.RouteName)!;
        var page = await bridge.ListAsync(
            memberMeta,
            new ListQuery { PageSize = 25, Filters = members.Filter }
        );
        Assert.Equal(32, page.TotalCount);
        Assert.Equal(25, page.Rows.Count);

        // ...and it renders there, with a link out of the row to the account.
        var html = await _panel.Client.GetStringAsync("/admin/entities/Organization/1");
        Assert.Contains("Members", html);
        Assert.Contains("API keys", html);
        Assert.Contains("/admin/entities/Account/1", html);
        Assert.Contains("northwind-key-1", html);
        Assert.DoesNotContain("acme-key-1", html); // another organization's key
        // The embedded filter bar stays behind its button until asked for.
        Assert.DoesNotContain("adminforge-apply", html);
        Assert.Contains("adminforge-filters-toggle", html);
    }

    [Fact]
    public async Task Only_A_Table_Whose_Provider_Searches_Offers_A_Search_Box()
    {
        Assert.Contains(
            "adminforge-search",
            await _panel.Client.GetStringAsync("/admin/entities/Member")
        );
        Assert.DoesNotContain(
            "adminforge-search",
            await _panel.Client.GetStringAsync("/admin/entities/ApiKey")
        );
    }

    [Fact]
    public async Task Columns_Render_In_The_Order_AddColumn_Named_Them()
    {
        var html = await _panel.Client.GetStringAsync("/admin/entities/Organization");
        string[] headers = ["Name", "Plan", "Seats", "Suspended Reason", "Created At", "Id"];
        var positions = headers
            .Select(h => html.IndexOf($">{h}", StringComparison.Ordinal))
            .ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public async Task A_Timestamp_Reads_The_Same_In_The_List_And_On_The_Detail_Page()
    {
        var list = await _panel.Client.GetStringAsync("/admin/entities/Member");
        var detail = await _panel.Client.GetStringAsync("/admin/entities/Member/1");
        var store = _panel.Services.GetRequiredService<CrmStore>();
        var joined = store.Members[0].JoinedAt.ToString("yyyy-MM-dd HH:mm");

        Assert.Contains(joined, list);
        Assert.Contains(joined, detail);

        // A column that names its own format uses it on both surfaces.
        var created = store.Organizations[0].CreatedAt.ToString("yyyy-MM-dd");
        Assert.Contains(
            created,
            await _panel.Client.GetStringAsync("/admin/entities/Organization")
        );
        Assert.Contains(
            created,
            await _panel.Client.GetStringAsync("/admin/entities/Organization/1")
        );
    }
}
