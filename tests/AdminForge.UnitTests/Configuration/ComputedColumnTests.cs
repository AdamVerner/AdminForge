using AdminForge.Core.Configuration;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Configuration;

/// <summary>
/// A computed column names one value source, and which one it names decides where it renders
/// and whether the database can be asked to sort on it.
/// </summary>
public class ComputedColumnTests
{
    private static AdminForgeBuilder Builder()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new AdminForgeBuilder(new EfCoreReflectionScanner().Scan(ctx));
    }

    private static ColumnMeta Register(Action<CustomColumnBuilder<Todo, int>> configure)
    {
        var builder = Builder();
        builder.AddTable<Todo>(e => e.Column("Computed", configure));
        return builder.Build().Entities.Single().Columns.Single(c => c.PropertyName == "Computed");
    }

    [Fact]
    public void A_Projection_Lists_By_Default_And_A_Resolver_Does_Not()
    {
        var projected = Register(c => c.From(t => t.Id));
        Assert.True(projected.ShowInList);
        Assert.NotNull(projected.CustomValueSelector);
        Assert.Null(projected.ValueResolver);

        var resolved = Register(c => c.Resolve((sp, t, ct) => Task.FromResult(1)));
        Assert.False(resolved.ShowInList);
        Assert.NotNull(resolved.ValueResolver);
        Assert.Null(resolved.CustomValueSelector);
    }

    // The opt-in must hold whichever side of Resolve() it is written on.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShownInList_Survives_Call_Order(bool optInFirst)
    {
        var column = Register(c =>
        {
            if (optInFirst)
                c.ShownInList().Resolve((sp, t, ct) => Task.FromResult(1));
            else
                c.Resolve((sp, t, ct) => Task.FromResult(1)).ShownInList();
        });
        Assert.True(column.ShowInList);
    }

    [Fact]
    public async Task A_Resolver_Receives_The_Instance_And_The_Scope()
    {
        var column = Register(c =>
            c.Resolve(
                (sp, todo, ct) =>
                    Task.FromResult(todo.Title.Length + (int)sp.GetService(typeof(int))!)
            )
        );
        var services = new StubServices(40);

        var value = await column.ValueResolver!(services, new Todo { Title = "ab" }, default);

        Assert.Equal(42, value);
    }

    [Theory]
    [InlineData("neither")]
    [InlineData("both")]
    public void A_Computed_Column_Names_Exactly_One_Value_Source(string how)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Register(c =>
            {
                if (how == "both")
                    c.From(t => t.Id).Resolve((sp, t, ct) => Task.FromResult(1));
            })
        );
        Assert.Contains("exactly one value source", ex.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_Resolved_Column_Cannot_Be_Sorted_Or_Filtered(bool sortable, bool filterable)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Register(c =>
                c.Resolve((sp, t, ct) => Task.FromResult(1))
                    .Sortable(sortable)
                    .Filterable(filterable)
            )
        );
        Assert.Contains("cannot sort or filter", ex.Message);
    }

    [Fact]
    public void Per_Surface_Visibility_Is_Independent()
    {
        var builder = Builder();
        builder.AddTable<Todo>(e =>
            e.Column(t => t.Title, c => c.HiddenInView())
                .Column(t => t.Status)
                .HideColumn(t => t.Description)
        );
        var entity = builder.Build().Entities.Single();

        var title = entity.Columns.Single(c => c.PropertyName == nameof(Todo.Title));
        Assert.True(title.ShowInList);
        Assert.True(title.HiddenInView);
        Assert.False(title.HiddenInEdit);

        // HideColumn means every surface, and a configured column is ordered ahead of the rest.
        var description = entity.Columns.Single(c => c.PropertyName == nameof(Todo.Description));
        Assert.False(description.ShowInList);
        Assert.True(description.HiddenInView);
        Assert.True(description.HiddenInEdit);
        Assert.Equal(
            [nameof(Todo.Title), nameof(Todo.Status)],
            entity.OrderedColumns.Where(c => c.DisplayOrder is not null).Select(c => c.PropertyName)
        );
    }

    private sealed class StubServices(int value) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(int) ? value : null;
    }
}
