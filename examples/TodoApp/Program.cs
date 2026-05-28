using System.Text.Json.Serialization;
using AdminForge;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    // Entity graphs are cyclic (Todo.Tags <-> Tag.Todos, etc.); avoid serializer blowing up.
    o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

var connectionString =
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=todoapp.db";

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

builder.Services.AddAdminForge(options =>
{
    options.Title = "Todo Admin";
    options.RoutePrefix = "admin";
});

var app = builder.Build();

// Support `dotnet run --project examples/TodoApp -- seed` for one-shot DB setup.
if (args.Length > 0 && string.Equals(args[0], "seed", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
    Console.WriteLine("Database seeded.");
    return;
}

// Make sure the DB exists for normal runs too (no migrations in Phase 0).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapAdminForge();

app.MapGet(
    "/todos",
    async (AppDbContext db) =>
        await db
            .Todos.Include(t => t.Assignee)
            .Include(t => t.TodoList)
            .Include(t => t.Tags)
            .AsNoTracking()
            .ToListAsync()
);

app.MapGet(
    "/todos/{id:int}",
    async (int id, AppDbContext db) =>
    {
        var todo = await db
            .Todos.Include(t => t.Assignee)
            .Include(t => t.TodoList)
            .Include(t => t.Tags)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
        return todo is null ? Results.NotFound() : Results.Ok(todo);
    }
);

app.MapPost(
    "/todos",
    async (CreateTodoRequest req, AppDbContext db) =>
    {
        var list = await db.TodoLists.FindAsync(req.TodoListId);
        if (list is null)
            return Results.BadRequest($"TodoList {req.TodoListId} not found.");

        var todo = new Todo
        {
            Title = req.Title,
            TodoListId = req.TodoListId,
            Priority = req.Priority ?? TodoPriority.Normal,
        };
        db.Todos.Add(todo);
        await db.SaveChangesAsync();
        return Results.Created($"/todos/{todo.Id}", todo);
    }
);

app.MapPut(
    "/todos/{id:int}/complete",
    async (int id, AppDbContext db) =>
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null)
            return Results.NotFound();
        todo.Status = TodoStatus.Done;
        todo.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
);

app.MapDelete(
    "/todos/{id:int}",
    async (int id, AppDbContext db) =>
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null)
            return Results.NotFound();
        db.Todos.Remove(todo);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
);

app.Run();

public sealed record CreateTodoRequest(string Title, int TodoListId, TodoPriority? Priority);
