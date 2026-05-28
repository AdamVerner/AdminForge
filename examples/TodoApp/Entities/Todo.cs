using System.ComponentModel.DataAnnotations;

namespace TodoApp.Entities;

public enum TodoPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3,
}

public enum TodoStatus
{
    Open = 0,
    InProgress = 1,
    Blocked = 2,
    Done = 3,
    Cancelled = 4,
}

public sealed class Todo
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    public TodoPriority Priority { get; set; } = TodoPriority.Normal;

    public TodoStatus Status { get; set; } = TodoStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DueAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int TodoListId { get; set; }
    public TodoList? TodoList { get; set; }

    public int? AssigneeId { get; set; }
    public User? Assignee { get; set; }

    public ICollection<Tag> Tags { get; set; } = [];
}
