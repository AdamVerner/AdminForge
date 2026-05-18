using AdminForge;
using TodoApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAdminForge(options =>
{
    options.Title = "Todo Admin";
    options.RoutePrefix = "admin";
});

builder.Services.AddSingleton<TodoRepository>();

var app = builder.Build();

app.UseAdminForge();

app.MapGet("/todos", (TodoRepository repo) => repo.GetAll());
app.MapGet("/todos/{id:int}", (int id, TodoRepository repo) =>
    repo.Find(id) is { } todo ? Results.Ok(todo) : Results.NotFound());
app.MapPost("/todos", (CreateTodoRequest req, TodoRepository repo) =>
{
    var todo = repo.Create(req.Title);
    return Results.Created($"/todos/{todo.Id}", todo);
});
app.MapPut("/todos/{id:int}/complete", (int id, TodoRepository repo) =>
    repo.Complete(id) ? Results.NoContent() : Results.NotFound());
app.MapDelete("/todos/{id:int}", (int id, TodoRepository repo) =>
    repo.Delete(id) ? Results.NoContent() : Results.NotFound());

app.Run();
