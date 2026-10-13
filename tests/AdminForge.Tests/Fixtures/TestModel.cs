using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AdminForge.Tests;

public enum TicketStatus
{
    Open,
    Done,
}

public sealed class Ticket
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Title { get; set; } = "";
    public TicketStatus Status { get; set; }
    public int? AssigneeId { get; set; }
    public string? Notes { get; set; }

    /// <summary>The database stamps it; the panel must never write it.</summary>
    public DateTime CreatedAt { get; set; }
}

public sealed class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Person> People => Set<Person>();

    protected override void OnModelCreating(ModelBuilder model) =>
        model
            .Entity<Ticket>()
            .Property(t => t.CreatedAt)
            .HasDefaultValueSql("'2026-01-02 03:04:05'");
}

/// <summary>A provider-backed read model, a positional record: no parameterless constructor, no setters.</summary>
public sealed record Contact(int Id, string Name, string? Email, int CompanyId)
{
    public string? Origin { get; private set; }

    public Contact Imported(string origin) => this with { Origin = origin };
}

/// <summary>An in-memory source that writes and deletes; it records what it was told changed.</summary>
public sealed class ContactStore
    : IAdminSource<Contact, int>,
        IAdminWritable<Contact>,
        IAdminDeletable<Contact>
{
    public List<Contact> Rows { get; } =
    [new Contact(1, "Ada", "ada@example.com", 7).Imported("crm"), new(2, "Brian", null, 7)];
    public List<IReadOnlySet<string>> Changed { get; } = [];

    public Task<AdminPage<Contact>> ListAsync(AdminQuery<Contact> query, CancellationToken ct) =>
        Task.FromResult(AdminPage.From(Rows, query));

    public Task<Contact?> FindAsync(int key, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.Id == key));

    public Task<Contact> CreateAsync(Contact row, CancellationToken ct)
    {
        var stored = row with { Id = Rows.Max(r => r.Id) + 1 };
        Rows.Add(stored);
        return Task.FromResult(stored);
    }

    public Task<Contact> UpdateAsync(
        Contact row,
        IReadOnlySet<string> changed,
        CancellationToken ct
    )
    {
        Changed.Add(changed);
        Rows[Rows.FindIndex(r => r.Id == row.Id)] = row;
        return Task.FromResult(row);
    }

    public Task DeleteAsync(Contact row, CancellationToken ct)
    {
        Rows.Remove(row);
        return Task.CompletedTask;
    }
}

/// <summary>A read-only table's rows.</summary>
public sealed record Event(int Id, string Text);

/// <summary>A read model with a list, a long text and a timestamp: what a host service hands back.</summary>
public sealed record Company(
    int Id,
    string Name,
    DateTime JoinedAt,
    IReadOnlyList<string> Domains,
    string Notes
);

/// <summary>Contact <see cref="ContactStore"/>'s rows belong to company 7.</summary>
public sealed class CompanyList
{
    public IReadOnlyList<Company> Rows { get; } =
    [
        new(
            7,
            "Northwind",
            new DateTime(2025, 3, 4, 17, 8, 0),
            ["north.example", "nw.test"],
            "# Notes\n\nPays late."
        ),
        new(8, "Globex", new DateTime(2025, 5, 6, 9, 0, 0), [], "Quiet."),
    ];

    public Task<AdminPage<Company>> ListAsync(AdminQuery<Company> q) =>
        Task.FromResult(
            AdminPage.From(
                q.Search is { } s
                    ? Rows.Where(c => c.Name.Contains(s, StringComparison.OrdinalIgnoreCase))
                    : Rows,
                q
            )
        );

    public Task<Company?> FindAsync(int id) =>
        Task.FromResult(Rows.FirstOrDefault(c => c.Id == id));
}

public static class Seed
{
    /// <summary>One open and one done ticket.</summary>
    public static async Task<Ticket[]> OpenAndDoneAsync(TestDb db)
    {
        Ticket[] rows =
        [
            new() { Title = "Open one", Status = TicketStatus.Open },
            new() { Title = "Done one", Status = TicketStatus.Done },
        ];
        db.Tickets.AddRange(rows);
        await db.SaveChangesAsync();
        return rows;
    }
}

/// <summary>A row keyed by two parts its store assigns; the organization holds the separators a route key uses.</summary>
public sealed record Grant([property: Key] string Org, [property: Key] string User, string Role);

public sealed class GrantStore : IAdminSource<Grant, object?[]>, IAdminWritable<Grant>
{
    public List<Grant> Rows { get; } = [];

    public Task<AdminPage<Grant>> ListAsync(AdminQuery<Grant> query, CancellationToken ct) =>
        Task.FromResult(AdminPage.From(Rows, query));

    public Task<Grant?> FindAsync(object?[] key, CancellationToken ct) =>
        Task.FromResult(
            Rows.FirstOrDefault(r => r.Org == (string?)key[0] && r.User == (string?)key[1])
        );

    public Task<Grant> CreateAsync(Grant row, CancellationToken ct)
    {
        var stored = row with { Org = "north,eu/1", User = $"u{Rows.Count + 1}" };
        Rows.Add(stored);
        return Task.FromResult(stored);
    }

    public Task<Grant> UpdateAsync(Grant row, IReadOnlySet<string> changed, CancellationToken ct)
    {
        Rows[Rows.FindIndex(r => r.Org == row.Org && r.User == row.User)] = row;
        return Task.FromResult(row);
    }
}
