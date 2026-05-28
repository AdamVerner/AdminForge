using Microsoft.EntityFrameworkCore;
using TodoApp.Entities;

namespace TodoApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TodoList> TodoLists => Set<TodoList>();
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<TodoList>(b =>
        {
            b.HasOne(l => l.Owner)
                .WithMany(u => u.TodoLists)
                .HasForeignKey(l => l.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Todo>(b =>
        {
            b.HasOne(t => t.TodoList)
                .WithMany(l => l.Todos)
                .HasForeignKey(t => t.TodoListId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(t => t.Assignee)
                .WithMany(u => u.AssignedTodos)
                .HasForeignKey(t => t.AssigneeId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasMany(t => t.Tags)
                .WithMany(t => t.Todos)
                .UsingEntity(j => j.ToTable("TodoTags"));
        });

        modelBuilder.Entity<Tag>(b =>
        {
            b.HasIndex(t => t.Name).IsUnique();
        });
    }
}
