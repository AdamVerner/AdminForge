using AdminForge.Core.Metadata;

namespace AdminForge.UnitTests;

public class NavGroupingTests
{
    private sealed record Entry(string Label, NavMeta Nav);

    [Fact]
    public void Main_Groups_By_Name_Then_Settings_Then_Account_Each_By_Order_Then_Label()
    {
        var entries = new[]
        {
            new Entry("Zeta", new NavMeta { Group = "Ops" }),
            new Entry("Alpha", new NavMeta { Group = "Ops", Order = 2 }),
            new Entry("Beta", new NavMeta { Group = "Ops", Order = 1 }),
            new Entry("Home", new NavMeta()),
            new Entry("Accounts", new NavMeta { Group = "Admin" }),
            new Entry("Tokens", new NavMeta { Area = NavArea.Account }),
            new Entry("Audit", new NavMeta { Area = NavArea.Settings, Group = "Ops", Order = 2 }),
            new Entry("Site", new NavMeta { Area = NavArea.Settings, Order = 1 }),
        };

        var groups = NavGrouping.Arrange(entries, e => e.Nav, e => e.Label);

        Assert.Equal([null, "Admin", "Ops", "Settings", "Account"], groups.Select(g => g.Key));
        Assert.Equal(["Beta", "Alpha", "Zeta"], groups[2].Entries.Select(e => e.Label));
        Assert.Equal(["Site", "Audit"], groups[3].Entries.Select(e => e.Label));
    }
}
