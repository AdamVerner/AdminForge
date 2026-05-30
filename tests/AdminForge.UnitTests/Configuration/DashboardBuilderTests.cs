using AdminForge.Core.Configuration;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Configuration;

public class DashboardBuilderTests
{
    private static IReadOnlyList<EntityMeta> Scan()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new EfCoreReflectionScanner().Scan(ctx);
    }

    [Fact]
    public void AddDashboard_Registers_With_Title_And_Widgets_In_Order()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder
            .AddTable<Todo>()
            .AddDashboard("ops", d =>
                d.WithTitle("Ops")
                    .AddStatCard("Count", () => Task.FromResult<object?>(1))
                    .AddTable<Todo>(t => t.WithTitle("All Todos"))
            );

        var dash = Assert.Single(builder.Build().Dashboards);
        Assert.Equal("ops", dash.RouteName);
        Assert.Equal("Ops", dash.Title);
        Assert.Collection(
            dash.Widgets,
            w => Assert.IsType<StatCardMeta>(w),
            w => Assert.IsType<TableWidgetMeta>(w)
        );
    }

    [Fact]
    public void AddDashboard_Throws_On_Duplicate_RouteName()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddDashboard("a", _ => { });
        Assert.Throws<InvalidOperationException>(() => builder.AddDashboard("A", _ => { }));
    }

    [Fact]
    public void Layout_Captures_Rows_Cells_And_Widths()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddDashboard("ops", d =>
            d.AddStatCard("A", () => Task.FromResult<object?>(1))
                .AddStatCard("B", () => Task.FromResult<object?>(2))
                .AddStatCard("C", () => Task.FromResult<object?>(3))
                .Layout(layout => layout
                    .Row(r => r.Add("A").Add("B", width: 2))
                    .Row(r => r.Add("C", fullWidth: true))
                )
        );

        var dash = builder.Build().Dashboards.Single();
        Assert.NotNull(dash.Layout);
        Assert.Equal(2, dash.Layout!.Rows.Count);

        var row0 = dash.Layout.Rows[0];
        Assert.Collection(
            row0.Cells,
            c => { Assert.Equal(1, c.Width); Assert.False(c.FullWidth); },
            c => { Assert.Equal(2, c.Width); Assert.False(c.FullWidth); }
        );
        var row1 = dash.Layout.Rows[1];
        var only = Assert.Single(row1.Cells);
        Assert.True(only.FullWidth);
    }

    [Fact]
    public void Layout_Add_Unknown_Widget_Throws()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddDashboard("ops", d =>
        {
            d.AddStatCard("Known", () => Task.FromResult<object?>(1));
            Assert.Throws<InvalidOperationException>(
                () => d.Layout(layout => layout.Row(r => r.Add("Unknown")))
            );
        });
    }

    [Fact]
    public async Task StatCard_Fetch_Is_Invoked_When_Materialised()
    {
        var builder = new AdminForgeBuilder(Scan());
        var calls = 0;
        builder.AddDashboard("ops", d =>
            d.AddStatCard("Count", () => { calls++; return Task.FromResult<object?>(42); })
        );

        var dash = builder.Build().Dashboards.Single();
        var stat = (StatCardMeta)dash.Widgets.Single();
        var result = await stat.Fetch(null!, default);

        Assert.Equal(1, calls);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task LineChart_Selectors_Project_Points()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddDashboard("ops", d =>
            d.AddLineChart<(int x, int y)>(
                "Series",
                () => Task.FromResult<IReadOnlyList<(int x, int y)>>(new[]
                {
                    (1, 10), (2, 20), (3, 30),
                }),
                xAxis: p => p.x,
                yAxis: p => p.y
            )
        );

        var chart = (LineChartMeta)builder.Build().Dashboards.Single().Widgets.Single();
        var points = await chart.Fetch(null!, default);
        Assert.Equal(3, points.Count);
        Assert.Equal(2, chart.XSelector(points[1]));
        Assert.Equal(20, chart.YSelector(points[1]));
    }

    [Fact]
    public void TableWidget_Captures_Visible_Columns_And_Order()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder
            .AddTable<Todo>()
            .AddDashboard("ops", d =>
                d.AddTable<Todo>(t => t
                    .WithTitle("Recent")
                    .WithColumns(x => x.Title, x => x.Status)
                    .Take(5)
                    .OrderBy(x => x.CreatedAt, descending: true)
                )
            );

        var table = (TableWidgetMeta)builder.Build().Dashboards.Single().Widgets.Single();
        Assert.Equal("Recent", table.Title);
        Assert.Equal(5, table.MaxRows);
        Assert.Equal("CreatedAt", table.SortBy);
        Assert.True(table.SortDescending);
        Assert.NotNull(table.VisibleColumns);
        Assert.Equal(new[] { "Title", "Status" }, table.VisibleColumns!);
    }
}
