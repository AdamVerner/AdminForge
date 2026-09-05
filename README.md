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

- Auto-generated CRUD pages for every EF Core entity (list, view, create, edit, delete) — with filter, sort, pagination, and validation.
- **Dashboards** composed in C# from stat cards, line charts, and table widgets, arranged in a row-based grid layout.
- **Generic forms** with 8 field types (text, number, float, bool, date, datetime, markdown, file upload) and a typed submit handler.
- **Per-entity custom actions** surfaced as buttons on the entity view (with optional confirmation dialogs).
- **Related-table links** auto-generated from collection navigations; cross-entity links are configurable.
- **Custom server-side columns** projected via `Expression<Func<T,TValue>>` — composes with filter/sort/pagination.
- **Audit log hook** — a single delegate receives every create/update/delete/custom-action event.
- **Per-action authorization policies** — `AdminForge:{Entity}:{Action}` policies are materialised on demand.
- **Authorization required at mount** — `MapAdminForge()` throws at startup unless the host set an umbrella policy or registered its own `IAdminAuthorizationPolicy`. An open panel has to say so: `AllowAnonymousAccess()`. The umbrella policy goes on the panel's endpoints, so the host's authentication scheme handles a rejected request — a cookie scheme redirects to its login page.
- **Sign-out button** — `WithSignOut("/admin/logout")` puts a button in the app bar that posts to a host-owned endpoint; the signed-in user's name shows beside it.
- **Live updates** for single-entity views (polling) and dashboard line charts (polling or `IAsyncEnumerable` streaming) — multiple browser tabs share one upstream stream.
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

## Example

A full working sample lives in `examples/TodoApp` (EF Core + SQLite). Run it locally:

```
task example:todo:seed   # one-shot DB seed
task example:todo        # run the host on http://localhost:5xxx/admin
```

## Status

**v0.1.0, preview.** APIs may shift between minor versions. The shape is settled, but expect renames and additions as the library hardens.

## Limitations / non-goals

- **Route prefix is locked to `/admin`** — the Blazor `@page` routes are compile-time. A runtime route-rewriter is on the roadmap.
- **File uploads are in-memory** in this release — a streaming `IFileStorageHandler` is planned.
- **No multi-tenancy**, no custom page builder, no multi-step forms, no i18n in v1.
- **Blazor Server only** for now — the architecture is renderer-agnostic (Core produces view models + `IAdminUIBridge`), but only the Blazor UI is shipped today.

## License

MIT.
