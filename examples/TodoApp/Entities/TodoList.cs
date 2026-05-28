using System.ComponentModel.DataAnnotations;

namespace TodoApp.Entities;

public sealed class TodoList
{
    public int Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsArchived { get; set; }

    public int OwnerId { get; set; }
    public User? Owner { get; set; }

    public ICollection<Todo> Todos { get; set; } = [];
}
