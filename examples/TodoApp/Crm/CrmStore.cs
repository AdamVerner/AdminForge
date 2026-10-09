using System.Text.Json;

namespace TodoApp.Crm;

/// <summary>Seeded data standing in for the services a real host calls.</summary>
public sealed class CrmStore
{
    private readonly Lock _lock = new();
    private readonly List<Contact> _contacts = [];

    public CrmStore()
    {
        var start = new DateTime(2025, 1, 6, 9, 30, 0, DateTimeKind.Utc);
        string[] names = ["Northwind", "Acme Industrial", "Globex"];
        Companies =
        [
            .. names.Select(
                (name, i) =>
                    new Company(
                        i + 1,
                        name,
                        (Plan)(i % 3),
                        (i + 1) * 25,
                        start.AddDays(i * 31),
                        i == 2 ? "unpaid invoice" : null,
                        [$"{Slug(name)}.example", $"{Slug(name)}.test"],
                        [new("Prague", "CZ", true), new("Berlin", "DE", false)],
                        $"## {name}\n\nCustomer since **{2019 + i}**. Renews every January.\n\n- Call billing first\n- Escalate to ops after two days",
                        JsonSerializer.Serialize(
                            new
                            {
                                tier = ((Plan)(i % 3)).ToString(),
                                seats = (i + 1) * 25,
                                flags = new { sso = i == 2, beta = true },
                            }
                        )
                    )
            ),
        ];
        foreach (var company in Companies)
            for (var i = 1; i <= 4; i++)
                _contacts.Add(
                    new Contact(
                        _contacts.Count + 1,
                        $"{company.Name.Split(' ')[0]} Person {i}",
                        $"person{i}@{Slug(company.Name)}.example",
                        company.Id,
                        $"<p>Regards,<br><b>Person {i}</b><br><i>{company.Name}</i></p>"
                    )
                );
    }

    public IReadOnlyList<Company> Companies { get; }

    public IReadOnlyList<Contact> Contacts
    {
        get
        {
            lock (_lock)
                return _contacts.ToArray();
        }
    }

    public Contact Add(Contact contact)
    {
        lock (_lock)
        {
            var stored = contact with { Id = _contacts.Max(c => c.Id) + 1 };
            _contacts.Add(stored);
            return stored;
        }
    }

    public void Replace(Contact contact)
    {
        lock (_lock)
            _contacts[_contacts.FindIndex(c => c.Id == contact.Id)] = contact;
    }

    public void Remove(int id)
    {
        lock (_lock)
            _contacts.RemoveAll(c => c.Id == id);
    }

    private static string Slug(string name) => name.Split(' ')[0].ToLowerInvariant();
}
