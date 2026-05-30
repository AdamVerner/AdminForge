using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Reflection;

public class EfCoreReflectionScannerTests
{
    private static IReadOnlyList<EntityMeta> ScanTodoApp()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        var scanner = new EfCoreReflectionScanner();
        return scanner.Scan(ctx);
    }

    [Fact]
    public void Scans_All_Four_Entities()
    {
        var scanned = ScanTodoApp();
        var nonJoin = scanned.Where(m => !m.IsJoinEntity).Select(m => m.ClrType).ToHashSet();
        Assert.Contains(typeof(User), nonJoin);
        Assert.Contains(typeof(TodoList), nonJoin);
        Assert.Contains(typeof(Todo), nonJoin);
        Assert.Contains(typeof(Tag), nonJoin);
    }

    [Fact]
    public void Marks_Primary_Key_Property()
    {
        var todo = ScanTodoApp().Single(m => m.ClrType == typeof(Todo));
        var id = todo.Columns.Single(c => c.PropertyName == "Id");
        Assert.True(id.IsPrimaryKey);
        Assert.Contains("Id", todo.PrimaryKeyPropertyNames);
    }

    [Fact]
    public void Marks_Enum_Columns_With_Underlying_Type()
    {
        var todo = ScanTodoApp().Single(m => m.ClrType == typeof(Todo));

        var priority = todo.Columns.Single(c => c.PropertyName == nameof(Todo.Priority));
        Assert.Equal(ColumnKind.Enum, priority.Kind);
        Assert.Equal(typeof(TodoPriority), priority.EnumType);

        var status = todo.Columns.Single(c => c.PropertyName == nameof(Todo.Status));
        Assert.Equal(ColumnKind.Enum, status.Kind);
        Assert.Equal(typeof(TodoStatus), status.EnumType);
    }

    [Fact]
    public void Marks_Foreign_Key_Scalar_And_Links_To_Navigation()
    {
        var todo = ScanTodoApp().Single(m => m.ClrType == typeof(Todo));

        var listFk = todo.Columns.Single(c => c.PropertyName == nameof(Todo.TodoListId));
        Assert.True(listFk.IsForeignKey);
        Assert.Equal(nameof(Todo.TodoList), listFk.ForeignKeyNavigation);

        var assigneeFk = todo.Columns.Single(c => c.PropertyName == nameof(Todo.AssigneeId));
        Assert.True(assigneeFk.IsForeignKey);
        Assert.Equal(nameof(Todo.Assignee), assigneeFk.ForeignKeyNavigation);
    }

    [Fact]
    public void Marks_Reference_Navigations()
    {
        var todo = ScanTodoApp().Single(m => m.ClrType == typeof(Todo));

        var list = todo.Columns.Single(c => c.PropertyName == nameof(Todo.TodoList));
        Assert.Equal(ColumnKind.NavigationReference, list.Kind);
        Assert.Equal(typeof(TodoList), list.RelatedEntityType);

        var assignee = todo.Columns.Single(c => c.PropertyName == nameof(Todo.Assignee));
        Assert.Equal(ColumnKind.NavigationReference, assignee.Kind);
        Assert.Equal(typeof(User), assignee.RelatedEntityType);
    }

    [Fact]
    public void Marks_Collection_Navigations()
    {
        var list = ScanTodoApp().Single(m => m.ClrType == typeof(TodoList));
        var todos = list.Columns.Single(c => c.PropertyName == nameof(TodoList.Todos));
        Assert.Equal(ColumnKind.NavigationCollection, todos.Kind);
        Assert.Equal(typeof(Todo), todos.RelatedEntityType);
    }

    [Fact]
    public void Marks_Many_To_Many_Skip_Navigation()
    {
        var todo = ScanTodoApp().Single(m => m.ClrType == typeof(Todo));
        var tags = todo.Columns.Single(c => c.PropertyName == nameof(Todo.Tags));
        Assert.Equal(ColumnKind.NavigationCollection, tags.Kind);
        Assert.Equal(typeof(Tag), tags.RelatedEntityType);

        var tag = ScanTodoApp().Single(m => m.ClrType == typeof(Tag));
        var todos = tag.Columns.Single(c => c.PropertyName == nameof(Tag.Todos));
        Assert.Equal(ColumnKind.NavigationCollection, todos.Kind);
    }

    [Fact]
    public void Captures_Nullability_And_MaxLength()
    {
        var todo = ScanTodoApp().Single(m => m.ClrType == typeof(Todo));

        var title = todo.Columns.Single(c => c.PropertyName == nameof(Todo.Title));
        Assert.False(title.IsNullable);
        Assert.Equal(200, title.MaxLength);
        Assert.True(title.IsRequired);

        var description = todo.Columns.Single(c => c.PropertyName == nameof(Todo.Description));
        Assert.True(description.IsNullable);
        Assert.Equal(4000, description.MaxLength);
    }

    [Fact]
    public void Humanises_PascalCase_Labels()
    {
        var todo = ScanTodoApp().Single(m => m.ClrType == typeof(Todo));
        var dueAt = todo.Columns.Single(c => c.PropertyName == nameof(Todo.DueAt));
        Assert.Equal("Due At", dueAt.Label);
    }
}
