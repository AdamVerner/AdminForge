using System.Linq.Expressions;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Configuration;

/// <summary>
/// Builder-shape tests for the column/action/link power-ups (HideColumn / AddColumn /
/// AddAction / HideRelatedLink / RelatedLink / LinkText). These exercise only the
/// metadata produced by <c>EntityBuilder&lt;T&gt;</c>; runtime behaviour is covered
/// by the provider + bridge integration tests.
/// </summary>
public class EntityBuilderPowerupTests
{
    private static IReadOnlyList<EntityMeta> Scan()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new EfCoreReflectionScanner().Scan(ctx);
    }

    [Fact]
    public void HideColumn_Clears_ShowInList_And_Sets_HiddenInEdit()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e => e.HideColumn(t => t.CreatedAt));
        var todo = builder.Build().Entities.Single();
        var col = todo.Columns.Single(c => c.PropertyName == nameof(Todo.CreatedAt));
        Assert.False(col.ShowInList);
        Assert.True(col.HiddenInEdit);
    }

    [Fact]
    public void AddColumn_Stores_Custom_Selector_And_Defaults_Are_Computed()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Tag>(e =>
            e.AddColumn<int>(
                "TodoCount",
                c => c.Label("# Todos").From(t => t.Todos.Count()).Sortable()
            )
        );

        var tag = builder.Build().Entities.Single();
        var col = tag.Columns.Single(c => c.PropertyName == "TodoCount");
        Assert.True(col.IsCustom);
        Assert.NotNull(col.CustomValueSelector);
        Assert.Equal("# Todos", col.Label);
        Assert.True(col.IsSortable);
        Assert.False(col.IsFilterable);
        Assert.True(col.HiddenInEdit); // computed columns are read-only
    }

    [Fact]
    public void AddColumn_Throws_When_From_Is_Missing()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddTable<Tag>(e => e.AddColumn<int>("X", _ => { }))
        );
    }

    [Fact]
    public void AddColumn_Throws_On_Duplicate_Name()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddTable<Tag>(e => e.AddColumn<string>("Name", c => c.From(t => t.Name)))
        );
    }

    [Fact]
    public void AddAction_Captures_Handler_And_Configure_Options()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>(e =>
            e.AddAction(
                "Ping",
                (_, _, _) => Task.CompletedTask,
                cfg => cfg.RequireConfirmation("Sure?").Icon("Email").Color("Primary")
            )
        );

        var user = builder.Build().Entities.Single();
        var action = Assert.Single(user.Actions);
        Assert.Equal("Ping", action.Name);
        Assert.Equal("Sure?", action.ConfirmationMessage);
        Assert.Equal("Email", action.Icon);
        Assert.Equal("Primary", action.Color);
    }

    [Fact]
    public void AddAction_Rejects_Duplicate_Names()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddTable<User>(e =>
                e.AddAction("Ping", (_, _, _) => Task.CompletedTask)
                    .AddAction("Ping", (_, _, _) => Task.CompletedTask)
            )
        );
    }

    [Fact]
    public void HideRelatedLink_Adds_Nav_Name_To_Hidden_Set()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>(e => e.HideRelatedLink(u => u.TodoLists));
        var meta = builder.Build().Entities.Single();
        Assert.Contains(nameof(User.TodoLists), meta.HiddenRelatedNavigations);
    }

    [Fact]
    public void RelatedLink_Override_Captures_Source_Nav_And_Label()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<TodoList>(e =>
            e.RelatedLink(l => l.Todos, link => link.Label("View All Tasks"))
        );
        var meta = builder.Build().Entities.Single();
        var link = Assert.Single(meta.RelatedLinks);
        Assert.Equal(nameof(TodoList.Todos), link.SourceNavigationName);
        Assert.Equal("View All Tasks", link.Label);
    }

    [Fact]
    public void RelatedLink_CrossEntity_Decomposes_Equality_Into_Filter_Dictionary()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>(e =>
            e.RelatedLink<Todo>("Active", source => target => target.AssigneeId == source.Id)
        );

        var meta = builder.Build().Entities.Single();
        var link = Assert.Single(meta.RelatedLinks);
        var filter = link.FilterBuilder(
            new User
            {
                Id = 42,
                DisplayName = "x",
                Email = "x@y.z",
            }
        );
        Assert.Single(filter);
        Assert.Equal(42, filter["AssigneeId"]);
    }

    [Fact]
    public void RelatedLink_CrossEntity_Throws_On_NonEquality_Clause()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<ArgumentException>(() =>
            builder.AddTable<User>(e =>
                e.RelatedLink<Todo>(
                    "Bad",
                    source =>
                        target => target.AssigneeId == source.Id && target.Status != TodoStatus.Done
                )
            )
        );
    }

    [Fact]
    public void LinkText_Compiles_Expression_Into_Resolver()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e =>
            e.Column(
                t => t.Assignee,
                c => c.LinkText(u => "Owned by " + (u == null ? "?" : u.DisplayName))
            )
        );

        var meta = builder.Build().Entities.Single();
        var col = meta.Columns.Single(c => c.PropertyName == nameof(Todo.Assignee));
        Assert.NotNull(col.LinkTextExpression);
        Assert.NotNull(col.LinkTextResolver);
        Assert.Equal(
            "Owned by Alice",
            col.LinkTextResolver!(new User { DisplayName = "Alice", Email = "a@x.y" })
        );
    }

    [Fact]
    public void LinkText_Rejects_Non_Navigation_Column()
    {
        // Typed LinkText: the column's CLR type for Todo.Title is string, so the
        // expression below is well-typed; the kind check at runtime trips it.
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddTable<Todo>(e =>
                e.Column(t => t.Title, c => c.LinkText(s => s ?? string.Empty))
            )
        );
    }

    [Fact]
    public void AddColumn_With_Selector_Opts_Column_Into_List()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e => e.AddColumn(t => t.Title));
        var meta = builder.Build().Entities.Single();
        var col = meta.Columns.Single(c => c.PropertyName == nameof(Todo.Title));
        Assert.True(col.ShowInList);
    }

    [Fact]
    public void AddColumn_With_Selector_Honours_Configure_Callback()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e => e.AddColumn(t => t.Title, c => c.Label("Headline")));
        var meta = builder.Build().Entities.Single();
        var col = meta.Columns.Single(c => c.PropertyName == nameof(Todo.Title));
        Assert.True(col.ShowInList);
        Assert.Equal("Headline", col.Label);
    }

    [Fact]
    public void AddColumn_With_Selector_Throws_On_Nav_Collection()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddTable<Tag>(e => e.AddColumn(t => t.Todos))
        );
    }

    [Fact]
    public void Default_AutoDiscovered_Columns_Are_Not_ShowInList()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>();
        var meta = builder.Build().Entities.Single();
        Assert.All(meta.Columns.Where(c => !c.IsCustom), c => Assert.False(c.ShowInList));
    }
}
