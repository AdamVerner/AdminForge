using System.Linq.Expressions;
using AdminForge.Core.Contracts;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Provider;

/// <summary>
/// Exercises <see cref="EfCoreDataProvider{TContext,TEntity}"/>'s custom-column
/// projection against the real SQLite provider. Sort/filter must compose into the
/// same SQL chain as the native columns; values come back via
/// <c>ListResult.CustomValues</c>.
/// </summary>
public class CustomColumnProjectionTests
{
    private static CustomColumnSpec TagCountSpec(bool sortable = true, bool filterable = false)
    {
        Expression<Func<Tag, int>> selector = t => t.Todos.Count();
        return new CustomColumnSpec(selector, sortable, filterable);
    }

    [Fact]
    public async Task Sqlite_CustomValues_Returned_Per_Row()
    {
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            // Wire a couple of todos onto each tag so counts vary.
            var urgent = ctx.Tags.First(t => t.Name == "urgent");
            var blocked = ctx.Tags.First(t => t.Name == "blocked");
            var todo = ctx.Todos.First();
            todo.Tags = [urgent, blocked];
            await ctx.SaveChangesAsync();

            var provider = new EfCoreDataProvider<TodoApp.Data.AppDbContext, Tag>(ctx);

            var result = await provider.ListAsync(
                new ListQuery
                {
                    PageSize = 10,
                    CustomColumns = new Dictionary<string, CustomColumnSpec>
                    {
                        ["TodoCount"] = TagCountSpec(),
                    },
                }
            );

            Assert.Equal(2, result.TotalCount);
            Assert.Equal(2, result.CustomValues.Count);
            foreach (var dict in result.CustomValues)
            {
                Assert.True(dict.ContainsKey("TodoCount"));
                Assert.IsType<int>(dict["TodoCount"]);
            }
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    [Fact]
    public async Task Sqlite_Custom_Sort_Translates()
    {
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            // urgent tag gets 2 todos, blocked gets 1.
            var urgent = ctx.Tags.First(t => t.Name == "urgent");
            var todoA = ctx.Todos.First();
            var todoB = ctx.Todos.Skip(1).First();
            todoA.Tags = [urgent];
            todoB.Tags = [urgent];
            await ctx.SaveChangesAsync();

            var provider = new EfCoreDataProvider<TodoApp.Data.AppDbContext, Tag>(ctx);

            var resultDesc = await provider.ListAsync(
                new ListQuery
                {
                    PageSize = 10,
                    SortBy = "TodoCount",
                    SortDescending = true,
                    CustomColumns = new Dictionary<string, CustomColumnSpec>
                    {
                        ["TodoCount"] = TagCountSpec(),
                    },
                }
            );

            // urgent (2 todos) should come ahead of blocked (0).
            Assert.Equal("urgent", resultDesc.Items[0].Name);
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    [Fact]
    public async Task Multiple_Custom_Columns_Composite_Projection_Returns_Correct_Values()
    {
        // Regression for the Option A composite-projection rewrite: two custom
        // columns must both arrive on every row, with values matching independent
        // recomputation. The implementation collapses N custom columns into one
        // composite Select projection per page.
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            var urgent = ctx.Tags.First(t => t.Name == "urgent");
            var blocked = ctx.Tags.First(t => t.Name == "blocked");
            var todoA = ctx.Todos.First();
            var todoB = ctx.Todos.Skip(1).First();
            todoA.Tags = [urgent, blocked];
            todoB.Tags = [urgent];
            await ctx.SaveChangesAsync();

            Expression<Func<Tag, int>> todoCountSel = t => t.Todos.Count();
            Expression<Func<Tag, string>> nameUpperSel = t => t.Name.ToUpper();

            var provider = new EfCoreDataProvider<TodoApp.Data.AppDbContext, Tag>(ctx);
            var result = await provider.ListAsync(
                new ListQuery
                {
                    PageSize = 10,
                    CustomColumns = new Dictionary<string, CustomColumnSpec>
                    {
                        ["TodoCount"] = new CustomColumnSpec(todoCountSel, true, false),
                        ["NameUpper"] = new CustomColumnSpec(nameUpperSel, false, false),
                    },
                }
            );

            Assert.Equal(result.Items.Count, result.CustomValues.Count);
            for (var i = 0; i < result.Items.Count; i++)
            {
                var tag = result.Items[i];
                var values = result.CustomValues[i];
                Assert.True(values.ContainsKey("TodoCount"));
                Assert.True(values.ContainsKey("NameUpper"));
                // Recompute independently and compare.
                var expectedCount = ctx.Todos.Count(t => t.Tags.Any(tt => tt.Id == tag.Id));
                Assert.Equal(expectedCount, values["TodoCount"]);
                Assert.Equal(tag.Name.ToUpper(), values["NameUpper"]);
            }
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    [Fact]
    public async Task Sqlite_Custom_Filter_Narrows_Result_Set()
    {
        var (ctx, conn) = TodoContextFactory.CreateSqlite();
        try
        {
            await TodoContextFactory.SeedAsync(ctx);
            var urgent = ctx.Tags.First(t => t.Name == "urgent");
            ctx.Todos.First().Tags = [urgent];
            await ctx.SaveChangesAsync();

            var provider = new EfCoreDataProvider<TodoApp.Data.AppDbContext, Tag>(ctx);

            var result = await provider.ListAsync(
                new ListQuery
                {
                    PageSize = 10,
                    Filters = new Dictionary<string, object?> { ["TodoCount"] = 1 },
                    CustomColumns = new Dictionary<string, CustomColumnSpec>
                    {
                        ["TodoCount"] = TagCountSpec(sortable: true, filterable: true),
                    },
                }
            );

            Assert.Equal(1, result.TotalCount);
            Assert.Equal("urgent", result.Items[0].Name);
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }
}
