using System.ComponentModel.DataAnnotations;

namespace TodoApp.Entities;

public enum UserRole
{
    Member = 0,
    Admin = 1,
    Owner = 2,
}

public sealed class User
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(254)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Member;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TodoList> TodoLists { get; set; } = [];
    public ICollection<Todo> AssignedTodos { get; set; } = [];
}
