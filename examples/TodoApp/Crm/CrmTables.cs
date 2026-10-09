using AdminForge;

namespace TodoApp.Crm;

/// <summary>Tables over a host service instead of a DbContext, the shape most real panels take.</summary>
public static class CrmTables
{
    /// <summary>Read-only: inline list and find delegates, and only the search, filters and sorts the service supports.</summary>
    public static PanelBuilder Companies(this PanelBuilder panel) =>
        panel.Table<Company>(t =>
            t.Title("Companies")
                .Source<CrmStore, int>(
                    (store, q, _) =>
                        Task.FromResult(AdminPage.From(Search(store.Companies, q.Search), q)),
                    (store, id, _) =>
                        Task.FromResult(store.Companies.FirstOrDefault(c => c.Id == id)),
                    s =>
                        s.Search(findsKey: true)
                            .Filters(c => c.Plan)
                            .Sorts(c => c.Name, c => c.Seats, c => c.CreatedAt)
                )
                .Columns(
                    c => c.Name,
                    c => c.Plan,
                    c => c.Seats,
                    c => c.SuspendedReason,
                    c => c.CreatedAt
                )
                .Column(c => c.CreatedAt, c => c.Format("yyyy-MM-dd"))
                .Column(
                    c => c.SuspendedReason,
                    c => c.Title("Suspended").Status(s => s.Tone("unpaid invoice", Tone.Warning))
                )
                .Column(c => c.Notes, c => c.Content(Content.Markdown))
                .Column(c => c.Profile, c => c.Content(Content.Json))
                // Contact has two tables, so the related one is named by its id.
                .Related<Contact>(
                    c => c.CompanyId,
                    r => r.Table("contact").Inline().Columns(c => c.Name, c => c.Email)
                )
        );

    /// <summary>Writable: a source class that implements create, update and delete.</summary>
    public static PanelBuilder Contacts(this PanelBuilder panel) =>
        panel.Table<Contact>(t =>
            t.Title("Contacts")
                .Source<ContactSource>(s =>
                    s.Search().Filters(c => c.CompanyId, c => c.Email).Sorts(c => c.Name)
                )
                .DisplayMember(c => $"{c.Name} <{c.Email}>")
                .Column(c => c.CompanyId, c => c.Title("Company").LinksTo<Company>())
                .Column(c => c.Email, c => c.Validate(e => e.Contains('@'), "An email has an @."))
                .Column(c => c.Signature, c => c.Content(Content.Html))
        );

    /// <summary>A second, read-only table of the same row type: a list-only source, no detail page.</summary>
    public static PanelBuilder Directory(this PanelBuilder panel) =>
        panel.Table<Contact>(t =>
            t.Id("contact-directory")
                .Title("Directory")
                .Source<CrmStore>(
                    (store, q, _) => Task.FromResult(AdminPage.From(store.Contacts, q)),
                    s => s.Sorts(c => c.Name)
                )
                .Columns(c => c.Name, c => c.Email, c => c.CompanyId)
                .Column(c => c.CompanyId, c => c.Title("Company").LinksTo("company"))
        );

    private static IEnumerable<Company> Search(IEnumerable<Company> rows, string? search) =>
        search is null
            ? rows
            : rows.Where(c =>
                c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || c.Domains.Any(d => d.Contains(search, StringComparison.OrdinalIgnoreCase))
            );
}

/// <summary>Contacts over <see cref="CrmStore"/>. The query's search, filters and sort are the ones the table declares.</summary>
public sealed class ContactSource(CrmStore store)
    : IAdminSource<Contact, int>,
        IAdminWritable<Contact>,
        IAdminDeletable<Contact>
{
    /// <summary>Reads the query itself, as a service that queries its own store would.</summary>
    public Task<AdminPage<Contact>> ListAsync(AdminQuery<Contact> query, CancellationToken ct)
    {
        var rows = store.Contacts.AsEnumerable();
        if (query.Search is { } s)
            rows = rows.Where(c =>
                c.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                || c.Email.Contains(s, StringComparison.OrdinalIgnoreCase)
            );
        if (query.Filter(c => c.CompanyId) is { } company)
            rows = rows.Where(c => c.CompanyId == company);
        if (query.Filter(c => c.Email) is { } email)
            rows = rows.Where(c => c.Email.Contains(email, StringComparison.OrdinalIgnoreCase));
        if (query.SortedBy(c => c.Name, out var descending))
            rows = descending ? rows.OrderByDescending(c => c.Name) : rows.OrderBy(c => c.Name);
        var all = rows.ToList();
        return Task.FromResult(
            new AdminPage<Contact>(
                all.Skip(query.Page.Skip).Take(query.Page.Size).ToList(),
                all.Count
            )
        );
    }

    public Task<Contact?> FindAsync(int key, CancellationToken ct) =>
        Task.FromResult(store.Contacts.FirstOrDefault(c => c.Id == key));

    public Task<Contact> CreateAsync(Contact row, CancellationToken ct)
    {
        if (store.Companies.All(c => c.Id != row.CompanyId))
            throw new AdminRejection(
                new Dictionary<string, string>
                {
                    ["CompanyId"] = $"There is no company {row.CompanyId}.",
                }
            );
        return Task.FromResult(store.Add(row));
    }

    public Task<Contact> UpdateAsync(
        Contact row,
        IReadOnlySet<string> changed,
        CancellationToken ct
    )
    {
        store.Replace(row);
        return Task.FromResult(row);
    }

    public Task DeleteAsync(Contact row, CancellationToken ct)
    {
        store.Remove(row.Id);
        return Task.CompletedTask;
    }
}
