namespace TodoApp.Crm;

/// <summary>
/// Read models as a host service hands them back: flat records with no navigations. A list the
/// service already carries (domains, offices) shows on the detail page as it is.
/// </summary>
public sealed record Company(
    int Id,
    string Name,
    Plan Plan,
    int Seats,
    DateTime CreatedAt,
    string? SuspendedReason,
    IReadOnlyList<string> Domains,
    IReadOnlyList<Office> Offices,
    string Notes,
    string Profile
);

public sealed record Office(string City, string Country, bool Headquarters);

public enum Plan
{
    Free,
    Team,
    Enterprise,
}

/// <summary>A positional record: the panel creates and edits it through its constructor.</summary>
public sealed record Contact(int Id, string Name, string Email, int CompanyId, string? Signature);
