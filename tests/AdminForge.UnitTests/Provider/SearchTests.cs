using AdminForge.Core.Contracts;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Provider;

/// <summary>
/// <see cref="ListQuery.Search"/> applies a case-insensitive Contains over every
/// string scalar column. The provider builds the predicate at runtime; SQLite must
/// translate it cleanly.
/// </summary>
public class SearchTests
{
    [Fact]
    public async Task Search_Matches_Title_Case_Insensitive()
    {
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            var provider = new EfCoreDataProvider<AppDbContext, Todo>(ctx);

            var result = await provider.ListAsync(new ListQuery { PageSize = 50, Search = "milk" });

            Assert.Equal(1, result.TotalCount);
            Assert.Equal("Buy milk", result.Items.Single().Title);
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    [Fact]
    public async Task Empty_Search_Returns_All()
    {
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            var provider = new EfCoreDataProvider<AppDbContext, Todo>(ctx);

            var result = await provider.ListAsync(new ListQuery { PageSize = 50, Search = "" });

            Assert.Equal(3, result.TotalCount);
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    [Fact]
    public async Task Search_Across_Multiple_String_Columns_With_OR()
    {
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            // Add a description that matches but a title that does not.
            ctx.Todos.Add(
                new Todo
                {
                    Title = "Unrelated",
                    Description = "milk-run",
                    TodoListId = ctx.TodoLists.First().Id,
                }
            );
            await ctx.SaveChangesAsync();

            var provider = new EfCoreDataProvider<AppDbContext, Todo>(ctx);
            var result = await provider.ListAsync(new ListQuery { PageSize = 50, Search = "milk" });

            // "Buy milk" (title hit) + "Unrelated" (description hit)
            Assert.Equal(2, result.TotalCount);
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }
}
