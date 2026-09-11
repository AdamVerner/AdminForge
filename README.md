# AdminForge

**AdminForge auto-generates an admin panel for ASP.NET Core apps.** Point it at your `DbContext`, register a few dashboards or forms if you want them, mount it — done.

## Quick start

```csharp
builder.Services.AddAdminForge<AppDbContext>(forge => forge
    .RequireAuthorizationPolicy("Admins") // or .AllowAnonymousAccess() for an open panel
    /* ... */);
app.MapAdminForge(); // mounts at /admin
```

Install:

```
dotnet add package AdminForge
```

That's it. No JS toolchain, no separate admin host — Blazor Server components shipped inside the package render against MudBlazor.

## What you get

- Auto-generated CRUD pages for every EF Core entity (list, view, create, edit, delete) — with filter, sort, pagination, and validation. Text filters match substrings, case-insensitively.
- **Provider-backed tables** — `AddTable<T>` on any keyed class describes it from its properties and serves it through the `IAdminDataProvider<T>` you register; `ReadOnly()` drops the create and edit surface, and a column offers a sort or filter control only once `Sortable()` / `Filterable()` says the provider honours it, as the table offers a search box only once `Searchable()` does. A host with no DbContext at all calls `AddAdminForge(forge => ...)` and registers a provider per table.
- **Every table has a provider at boot** — `MapAdminForge()` resolves one per registered table and names the ones it cannot serve, rather than failing on the first page load.
- **One DI scope per operation** — every list, find, save, action and widget resolves its provider and handler in a fresh scope, so a scoped `DbContext` or service lives for one call, not for the hours a Blazor circuit stays open. The scope's `IUserAccessor` names the user the circuit was opened for.
- **Dashboards** composed in C# from stat cards, line charts, and table widgets, arranged in a row-based grid layout.
- **Generic forms** with 8 field types (text, number, float, bool, date, datetime, markdown, file upload) and a typed submit handler.
- **Per-entity custom actions** surfaced as buttons on the entity view (with optional confirmation dialogs).
- **Related tables** auto-generated from collection navigations; cross-entity links are configurable. `Inline()` renders the related table *on* the detail page — its own sort, paging and filters, pinned to the parent row, with its filter bar tucked behind one button — and `Columns(...)` picks which columns it shows.
- **Cell links** — `LinksTo<TTarget>()` turns a column carrying another table's id into a link to that row, for read models that have no navigation to follow.
- **Computed columns**, in two kinds. `From(expr)` projects server-side, so it sorts, filters and pages with everything else; `Resolve((sp, row, ct) => …)` computes in-process — another service, another database, an expensive call — and is therefore detail-only until `ShownInList()` says one call per row is acceptable. Both render on the list and the detail page.
- **Per-surface visibility** — `HiddenInList()`, `HiddenInView()` and `HiddenInEdit()` each hide a column from one surface; `HideColumn(...)` hides it from all three.
- **Columns render in `Column` order**, and one value formatter serves the list and the detail page; `Format("yyyy-MM-dd")` overrides the pattern per column.
- **Audit log hook** — a single delegate receives every create/update/delete/custom-action event.
- **Per-action authorization policies** — `AdminForge:{Entity}:{Action}` policies are materialised on demand by a provider that wraps the host's own, so the host's policies keep resolving. `IAdminAuthorizationPolicy` is asked before every read and write the bridge performs.
- **Authorization required at mount** — `MapAdminForge()` throws at startup unless the host set an umbrella policy or registered its own `IAdminAuthorizationPolicy`. An open panel has to say so: `AllowAnonymousAccess()`. The umbrella policy goes on the panel's endpoints, so the host's authentication scheme handles a rejected request — a cookie scheme redirects to its login page. The panel's scripts and styles are served anonymously.
- **Sign-out button** — `WithSignOut("/admin/logout")` puts a button in the app bar that posts to a host-owned endpoint; the signed-in user's name shows beside it.
- **Live updates** for single-entity views (polling) and dashboard line charts (polling or `IAsyncEnumerable` streaming) — multiple browser tabs share one upstream stream.
- **Environment badge** — `WithEnvironment("staging", "#ef6c00")` colours the app bar and labels it, so nobody edits production thinking it is staging.
- **Theming hook** — set a logo and primary / secondary palette colour via `WithTheme(...)`; defaults render MudBlazor's stock palette.

## Configuration sketch

```csharp
builder.Services.AddAdminForge<AppDbContext>(forge => forge
    .WithTitle("My App Admin")
    .WithWelcomeMessage("Pick a table from the sidebar.")
    .RequireAuthorizationPolicy("Admins")
    .WithSignOut("/admin/logout")
    .WithAuditLog((evt, ct) => audit.RecordAsync(evt, ct))
    .WithTheme(t => { t.PrimaryColor = "#00897b"; t.LogoUrl = "/logo.svg"; })

    .AddTable<User>(e => e
        .Nav(n => n.Group("People"))
        .DisplayMember(u => u.DisplayName)
        .AddAction("Reset password", async (sp, user, ctx) =>
        {
            if (!await ctx.ConfirmAsync($"Reset {user.Email}?")) return;
            await sp.GetRequiredService<IUserService>().ResetAsync(user.Id);
            ctx.ShowSuccess("Password reset email sent.");
        }))

    .AddTable<Order>()

    // Not on the DbContext: served by services.AddAdminForgeDataProvider<AuditEntry, AuditProvider>()
    .AddTable<AuditEntry>(e => e
        .ReadOnly()
        .Column(a => a.At, c => c.Sortable())
        .Column(a => a.Action)
        .Column<int>("Retries", c => c
            .Resolve((sp, entry, ct) => sp.GetRequiredService<IRetryLog>().CountAsync(entry.Id, ct))))

    .AddDashboard("ops", d => d
        .WithTitle("Operations")
        .AddStatCard("Open orders", async (sp, ct) =>
            await sp.GetRequiredService<AppDbContext>().Orders.CountAsync(ct))
        .AddLineChart<Snapshot>("Throughput",
            xAxis: p => p.At, yAxis: p => p.Count,
            configure: c => c.WithStreaming(metricsStream)))

    .AddForm("notify", form => form
        .WithTitle("Send Notification")
        .AddField(f => f.Text("Title").Required())
        .AddField(f => f.Markdown("Body"))
        .OnSubmit((sp, values, ctx) => SendAsync(values))));
```

## Examples

`examples/TodoApp` — EF Core + SQLite, the DbContext path:

```
task example:todo:seed   # one-shot DB seed
task example:todo        # run the host on http://localhost:5xxx/admin
```

`examples/CrmApp` — no DbContext at all: four flat read models, one provider each, with the
nested tables, cell links and per-table search that path needs:

```
task example:crm
```

## Status

**Preview.** APIs may shift between minor versions. The shape is settled, but expect renames and additions as the library hardens.

## Releasing

Push a `v*` tag; CI packs and publishes it to nuget.org. Steps in [docs/releasing.md](docs/releasing.md).

## Limitations / non-goals

- **Route prefix is locked to `/admin`** — the Blazor `@page` routes are compile-time. A runtime route-rewriter is on the roadmap.
- **File uploads are in-memory** in this release — a streaming `IFileStorageHandler` is planned.
- **No multi-tenancy**, no custom page builder, no multi-step forms, no i18n in v1.
- **Blazor Server only** for now — the architecture is renderer-agnostic (Core produces view models + `IAdminUIBridge`), but only the Blazor UI is shipped today.

## License

MIT.
