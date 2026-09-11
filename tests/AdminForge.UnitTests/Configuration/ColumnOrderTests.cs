using AdminForge.Core.Configuration;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Configuration;

/// <summary>Columns render in the order <c>AddColumn</c> named them, not the order reflection found them.</summary>
public class ColumnOrderTests
{
    private static IReadOnlyList<EntityMeta> Scan()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new EfCoreReflectionScanner().Scan(ctx);
    }

    [Fact]
    public void OrderedColumns_Follows_AddColumn_Then_Reflection()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e =>
            e.Column(t => t.Status).Column(t => t.Title).Column<int>("Age", c => c.From(t => t.Id))
        );
        var todo = builder.Build().Entities.Single();

        var listed = todo
            .OrderedColumns.Where(c => c.ShowInList)
            .Select(c => c.PropertyName)
            .ToList();
        Assert.Equal(["Status", "Title", "Age"], listed);

        // Columns nobody added keep reflection order, behind every column that was.
        var rest = todo.OrderedColumns.Where(c => !c.ShowInList).Select(c => c.PropertyName);
        Assert.Equal(todo.Columns.Where(c => !c.ShowInList).Select(c => c.PropertyName), rest);
    }

    [Fact]
    public void A_Second_AddColumn_Keeps_The_First_Position()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e =>
            e.Column(t => t.Title).Column(t => t.Status).Column(t => t.Title)
        );
        var todo = builder.Build().Entities.Single();
        Assert.Equal(
            ["Title", "Status"],
            todo.OrderedColumns.Where(c => c.ShowInList).Select(c => c.PropertyName)
        );
    }
}
