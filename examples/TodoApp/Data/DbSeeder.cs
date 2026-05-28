using Microsoft.EntityFrameworkCore;
using TodoApp.Entities;

namespace TodoApp.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var alice = new User
        {
            DisplayName = "Alice Admin",
            Email = "alice@example.com",
            Role = UserRole.Owner,
        };
        var bob = new User
        {
            DisplayName = "Bob Builder",
            Email = "bob@example.com",
            Role = UserRole.Admin,
        };
        var carol = new User
        {
            DisplayName = "Carol Contributor",
            Email = "carol@example.com",
            Role = UserRole.Member,
        };

        db.Users.AddRange(alice, bob, carol);

        var work = new Tag { Name = "work", Color = "#1e88e5" };
        var personal = new Tag { Name = "personal", Color = "#43a047" };
        var urgent = new Tag { Name = "urgent", Color = "#e53935" };

        db.Tags.AddRange(work, personal, urgent);

        var inbox = new TodoList
        {
            Name = "Inbox",
            Description = "Default catch-all list.",
            Owner = alice,
        };
        var release = new TodoList
        {
            Name = "Q3 Release",
            Description = "Things to ship before the release cutoff.",
            Owner = bob,
        };
        db.TodoLists.AddRange(inbox, release);

        db.Todos.AddRange(
            new Todo
            {
                Title = "Write the design doc",
                Description = "Cover the new auth flow.",
                Priority = TodoPriority.High,
                Status = TodoStatus.InProgress,
                TodoList = release,
                Assignee = bob,
                DueAt = DateTime.UtcNow.AddDays(3),
                Tags = [work, urgent],
            },
            new Todo
            {
                Title = "Review PRs",
                Priority = TodoPriority.Normal,
                Status = TodoStatus.Open,
                TodoList = inbox,
                Assignee = alice,
                Tags = [work],
            },
            new Todo
            {
                Title = "Buy groceries",
                Priority = TodoPriority.Low,
                Status = TodoStatus.Open,
                TodoList = inbox,
                Assignee = carol,
                Tags = [personal],
            },
            new Todo
            {
                Title = "Deploy hotfix",
                Description = "Roll out 1.4.2 to staging.",
                Priority = TodoPriority.Critical,
                Status = TodoStatus.Done,
                TodoList = release,
                Assignee = bob,
                CompletedAt = DateTime.UtcNow.AddHours(-2),
                Tags = [work, urgent],
            }
        );

        await db.SaveChangesAsync(ct);
    }
}
