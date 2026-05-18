namespace TodoApp;

public sealed record Todo(int Id, string Title, bool IsComplete);

public sealed record CreateTodoRequest(string Title);

public sealed class TodoRepository
{
    private readonly List<Todo> _todos = [];
    private int _nextId = 1;

    public IReadOnlyList<Todo> GetAll() => _todos.AsReadOnly();

    public Todo? Find(int id) => _todos.FirstOrDefault(t => t.Id == id);

    public Todo Create(string title)
    {
        var todo = new Todo(_nextId++, title, false);
        _todos.Add(todo);
        return todo;
    }

    public bool Complete(int id)
    {
        var index = _todos.FindIndex(t => t.Id == id);
        if (index < 0) return false;
        _todos[index] = _todos[index] with { IsComplete = true };
        return true;
    }

    public bool Delete(int id) => _todos.RemoveAll(t => t.Id == id) > 0;
}
