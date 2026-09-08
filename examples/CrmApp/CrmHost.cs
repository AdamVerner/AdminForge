using AdminForge;
using AdminForge.Core.Configuration;

namespace CrmApp;

/// <summary>
/// The whole panel: a host with no <c>DbContext</c>, four read models, one provider each.
/// Lives apart from <c>Program</c> so the integration tests mount the same wiring.
/// </summary>
public static class CrmHost
{
    public static WebApplication Create(
        string[] args,
        Action<WebApplicationBuilder>? configure = null
    )
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        builder.Services.AddSingleton<CrmStore>();
        builder.Services.AddAdminForgeDataProvider<Organization, OrganizationProvider>();
        builder.Services.AddAdminForgeDataProvider<Account, AccountProvider>();
        builder.Services.AddAdminForgeDataProvider<Member, MemberProvider>();
        builder.Services.AddAdminForgeDataProvider<ApiKey, ApiKeyProvider>();

        builder.Services.AddAdminForge(forge =>
            forge
                .WithTitle("CRM Admin")
                .WithWelcomeMessage("A panel with no DbContext: every table is a read model.")
                .WithEnvironment("local", "#2e7d32")
                .AllowAnonymousAccess()
                .AddTable<Organization>(e =>
                    e.ReadOnly()
                        .Nav(n => n.Group("Customers").Order(0))
                        .Searchable()
                        .DisplayMember(o => o.Name)
                        // Column order follows these calls, not the record's property order.
                        .AddColumn(o => o.Name, c => c.Sortable().Filterable())
                        .AddColumn(o => o.Plan, c => c.Filterable())
                        .AddColumn(o => o.Seats, c => c.Sortable())
                        .AddColumn(o => o.SuspendedReason)
                        .AddColumn(o => o.CreatedAt, c => c.Sortable().Format("yyyy-MM-dd"))
                        .AddColumn(o => o.Id)
                        // The members of this organization, rendered on its detail page.
                        .RelatedLink<Member>(
                            "Members",
                            org => member => member.OrgId == org.Id,
                            link =>
                                link.Inline()
                                    .Columns(
                                        m => m.AccountId,
                                        m => m.Role,
                                        m => m.JoinedAt,
                                        m => m.IsActive
                                    )
                        )
                        .RelatedLink<ApiKey>(
                            "API keys",
                            org => key => key.OrgId == org.Id,
                            link =>
                                link.Inline()
                                    .Columns(k => k.Name, k => k.CreatedAt, k => k.IsRevoked)
                        )
                )
                .AddTable<Account>(e =>
                    e.ReadOnly()
                        .Nav(n => n.Group("Customers").Order(1))
                        .Searchable()
                        .DisplayMember(a => a.DisplayName)
                        .AddColumn(a => a.DisplayName, c => c.Sortable())
                        .AddColumn(a => a.Email, c => c.Sortable().Filterable())
                        .AddColumn(a => a.CreatedAt, c => c.Sortable())
                        .AddColumn(a => a.Id)
                )
                .AddTable<Member>(e =>
                    e.Label("Members")
                        .ReadOnly()
                        .Nav(n => n.Group("Customers").Order(2))
                        .Searchable()
                        .AddColumn(m => m.AccountId, c => c.Label("Account").LinksTo<Account>())
                        .AddColumn(
                            m => m.OrgId,
                            c => c.Label("Organization").Filterable().LinksTo<Organization>()
                        )
                        .AddColumn(m => m.Role, c => c.Filterable())
                        .AddColumn(m => m.JoinedAt, c => c.Sortable())
                        .AddColumn(m => m.IsActive)
                )
                // No Searchable(): this provider ignores ListQuery.Search, so no search box appears.
                .AddTable<ApiKey>(e =>
                    e.Label("API keys")
                        .ReadOnly()
                        .Nav(n => n.Group("Customers").Order(3))
                        .AddColumn(k => k.Name, c => c.Sortable())
                        .AddColumn(
                            k => k.OrgId,
                            c => c.Label("Organization").Filterable().LinksTo<Organization>()
                        )
                        .AddColumn(k => k.CreatedAt, c => c.Sortable().Format("yyyy-MM-dd"))
                        .AddColumn(k => k.ExpiresAt, c => c.Format("yyyy-MM-dd"))
                        .AddColumn(k => k.IsRevoked)
                )
        );

        var app = builder.Build();
        app.MapAdminForge();
        app.MapGet("/", () => Results.Redirect("/admin"));
        return app;
    }
}
