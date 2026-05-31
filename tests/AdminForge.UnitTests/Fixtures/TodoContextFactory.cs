using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Fixtures;

/// <summary>
/// Helpers to spin up <see cref="AppDbContext"/> instances against either
/// EF Core's in-memory provider or a fresh SQLite ":memory:" database. Lets each
/// test isolate its data without bringing in the WebApplicationFactory machinery.
/// </summary>
internal static class TodoContextFactory
{
    public static AppDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Creates a fresh SQLite-backed context. The returned tuple owns the open
    /// connection — dispose both context and connection when the test ends.
    /// </summary>
    public static (AppDbContext Context, SqliteConnection Connection) CreateSqlite()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    /// <summary>Seeds a minimal but cyclic-ish dataset for provider tests.</summary>
    public static async Task SeedAsync(AppDbContext context)
    {
        var alice = new User
        {
            DisplayName = "Alice",
            Email = "alice@example.com",
            Role = UserRole.Admin,
        };
        var bob = new User
        {
            DisplayName = "Bob",
            Email = "bob@example.com",
            Role = UserRole.Member,
        };
        context.Users.AddRange(alice, bob);
        await context.SaveChangesAsync();

        var inbox = new TodoList { Name = "Inbox", OwnerId = alice.Id };
        var shopping = new TodoList { Name = "Shopping", OwnerId = bob.Id };
        context.TodoLists.AddRange(inbox, shopping);
        await context.SaveChangesAsync();

        var urgent = new Tag { Name = "urgent", Color = "#ff0000" };
        var blocked = new Tag { Name = "blocked", Color = "#888888" };
        context.Tags.AddRange(urgent, blocked);
        await context.SaveChangesAsync();

        context.Todos.AddRange(
            new Todo
            {
                Title = "Pay rent",
                TodoListId = inbox.Id,
                AssigneeId = alice.Id,
                Priority = TodoPriority.High,
                Status = TodoStatus.Open,
            },
            new Todo
            {
                Title = "Buy milk",
                TodoListId = shopping.Id,
                AssigneeId = bob.Id,
                Priority = TodoPriority.Low,
                Status = TodoStatus.Open,
            },
            new Todo
            {
                Title = "File taxes",
                TodoListId = inbox.Id,
                AssigneeId = alice.Id,
                Priority = TodoPriority.Critical,
                Status = TodoStatus.InProgress,
            }
        );
        await context.SaveChangesAsync();
    }
}
