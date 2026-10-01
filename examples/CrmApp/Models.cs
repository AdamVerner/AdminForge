namespace CrmApp;

/// <summary>
/// The read models this panel is built from. Each is flat — one property per column, no
/// navigations — because a provider-backed table is whatever a service hands back, not a row.
/// A list the service already carries (domains, contacts) renders on the detail page as is.
/// </summary>
public sealed record Organization(
    int Id,
    string Name,
    Plan Plan,
    int Seats,
    DateTime CreatedAt,
    string? SuspendedReason,
    IReadOnlyList<string> Domains,
    IReadOnlyList<Contact> Contacts
);

public sealed record Contact(string Name, string Email, bool Primary);

public enum Plan
{
    Free,
    Team,
    Enterprise,
}

public sealed record Member(
    int Id,
    int OrgId,
    int AccountId,
    string Role,
    DateTime JoinedAt,
    bool IsActive
);

public sealed record ApiKey(
    int Id,
    int OrgId,
    string Name,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    bool IsRevoked
);

public sealed record Account(int Id, string Email, string DisplayName, DateTime CreatedAt);
