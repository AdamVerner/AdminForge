using AdminForge.Core.Contracts;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Provider;

public class EfCoreDataProviderTests
{
    private static EfCoreDataProvider<AppDbContext, T> NewProvider<T>(AppDbContext ctx)
        where T : class => new(ctx);

    // -- InMemory provider variant -------------------------------------------------

    [Fact]
    public async Task InMemory_List_Returns_All_Items_With_Total()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var provider = NewProvider<Todo>(ctx);

        var result = await provider.ListAsync(new ListQuery { Page = 0, PageSize = 50 });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task InMemory_List_Paginates_Correctly()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var provider = NewProvider<Todo>(ctx);

        var page0 = await provider.ListAsync(
            new ListQuery
            {
                Page = 0,
                PageSize = 2,
                SortBy = nameof(Todo.Id),
            }
        );
        var page1 = await provider.ListAsync(
            new ListQuery
            {
                Page = 1,
                PageSize = 2,
                SortBy = nameof(Todo.Id),
            }
        );

        Assert.Equal(3, page0.TotalCount);
        Assert.Equal(2, page0.Items.Count);
        Assert.Single(page1.Items);
        Assert.NotEqual(page0.Items[0].Id, page1.Items[0].Id);
    }

    [Fact]
    public async Task InMemory_List_Sorts_Descending()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var provider = NewProvider<Todo>(ctx);

        var result = await provider.ListAsync(
            new ListQuery
            {
                Page = 0,
                PageSize = 10,
                SortBy = nameof(Todo.Priority),
                SortDescending = true,
            }
        );

        Assert.Equal(TodoPriority.Critical, result.Items[0].Priority);
    }

    [Fact]
    public async Task InMemory_List_Filters_By_Property()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var provider = NewProvider<Todo>(ctx);

        var result = await provider.ListAsync(
            new ListQuery
            {
                Filters = new Dictionary<string, object?>
                {
                    [nameof(Todo.Status)] = TodoStatus.Open,
                },
            }
        );

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, t => Assert.Equal(TodoStatus.Open, t.Status));
    }

    [Fact]
    public async Task InMemory_Find_Returns_Entity_For_Existing_Key()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var existing = ctx.Todos.First();

        var provider = NewProvider<Todo>(ctx);
        var found = await provider.FindAsync([existing.Id]);

        Assert.NotNull(found);
        Assert.Equal(existing.Title, found!.Title);
    }

    [Fact]
    public async Task InMemory_Find_Returns_Null_For_Missing_Key()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var provider = NewProvider<Todo>(ctx);

        var found = await provider.FindAsync([99_999]);

        Assert.Null(found);
    }

    [Fact]
    public async Task InMemory_Create_Persists_New_Entity()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var list = ctx.TodoLists.First();

        var provider = NewProvider<Todo>(ctx);
        var created = await provider.CreateAsync(
            new Todo { Title = "New Task", TodoListId = list.Id, Priority = TodoPriority.Normal }
        );

        Assert.NotEqual(0, created.Id);
        var roundtrip = await provider.FindAsync([created.Id]);
        Assert.NotNull(roundtrip);
        Assert.Equal("New Task", roundtrip!.Title);
    }

    [Fact]
    public async Task InMemory_Update_Applies_Changes()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var existing = ctx.Todos.AsNoTracking().First();

        // Detach + mutate to simulate a posted edit form.
        var provider = NewProvider<Todo>(ctx);
        existing.Title = "Updated Title";
        existing.Status = TodoStatus.Done;
        await provider.UpdateAsync(existing);

        var roundtrip = await provider.FindAsync([existing.Id]);
        Assert.Equal("Updated Title", roundtrip!.Title);
        Assert.Equal(TodoStatus.Done, roundtrip.Status);
    }

    [Fact]
    public async Task InMemory_Delete_Removes_Entity_And_Returns_True()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var existing = ctx.Todos.AsNoTracking().First();

        var provider = NewProvider<Todo>(ctx);
        var deleted = await provider.DeleteAsync([existing.Id]);

        Assert.True(deleted);
        Assert.Null(await provider.FindAsync([existing.Id]));
    }

    [Fact]
    public async Task InMemory_Delete_Returns_False_For_Missing_Key()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        await TodoContextFactory.SeedAsync(ctx);
        var provider = NewProvider<Todo>(ctx);

        Assert.False(await provider.DeleteAsync([99_999]));
    }

    // -- SQLite (:memory:) variant — same shapes against a real relational provider ---

    [Fact]
    public async Task Sqlite_Full_Crud_Roundtrip()
    {
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            var provider = NewProvider<Todo>(ctx);

            var list = await provider.ListAsync(new ListQuery { PageSize = 10 });
            Assert.Equal(3, list.TotalCount);

            var first = list.Items[0];
            first.Title = "Edited";
            await provider.UpdateAsync(first);
            var reread = await provider.FindAsync([first.Id]);
            Assert.Equal("Edited", reread!.Title);

            var listFk = ctx.TodoLists.AsNoTracking().First().Id;
            var created = await provider.CreateAsync(
                new Todo { Title = "Fresh", TodoListId = listFk }
            );
            Assert.NotEqual(0, created.Id);

            Assert.True(await provider.DeleteAsync([created.Id]));
            Assert.Null(await provider.FindAsync([created.Id]));
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    [Fact]
    public async Task Sqlite_Filter_And_Sort_Translate()
    {
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            var provider = NewProvider<Todo>(ctx);

            var result = await provider.ListAsync(
                new ListQuery
                {
                    Filters = new Dictionary<string, object?>
                    {
                        [nameof(Todo.Status)] = TodoStatus.Open,
                    },
                    SortBy = nameof(Todo.Title),
                    SortDescending = false,
                }
            );

            Assert.Equal(2, result.TotalCount);
            Assert.Equal("Buy milk", result.Items[0].Title);
            Assert.Equal("Pay rent", result.Items[1].Title);
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }
}
