using System.ComponentModel.DataAnnotations;

namespace TodoApp.Entities;

public sealed class Tag
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>A hex colour such as "#a1b2c3".</summary>
    [MaxLength(9)]
    public string? Color { get; set; }

    public ICollection<Todo> Todos { get; set; } = [];
}
