# AdminForge

AdminForge builds an admin panel for an ASP.NET Core app. You describe tables, actions, forms and
dashboards in C#. The panel renders them with Blazor Server and MudBlazor at `/admin`. It can also
serve the same operations as MCP tools for AI agents.

Every operation from every surface goes through one pipeline. First the pipeline checks the
panel's policy and loads the row. Then it asks your policy with that row and checks the input.
Last it runs the work and records an audit event. A rule you write once holds in the browser and
over MCP.

**Status: preview.** The API changes between minor versions until 1.0.

## Install

```
dotnet add package AdminForge
dotnet add package AdminForge.EntityFrameworkCore   # tables over a DbContext
dotnet add package AdminForge.Mcp                   # MCP tools
```

## Quick start

```csharp
using AdminForge;

builder.Services.AddAdminForge(panel => panel
    .Title("Todo Admin")
    .RequireAuthorization("Admins")
    .UseEntityFramework<AppDbContext>()
    .Table<Todo>()
    .Table<User>());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAdminForge();
app.Run();
```

`UseAuthentication()` and `UseAuthorization()` come before `MapAdminForge()`. The panel reads the
signed-in user on every operation.

`MapAdminForge()` builds the model and checks it. A configuration the panel cannot serve fails
here, at startup, and the message names the fix. For example, a panel with neither
`RequireAuthorization(...)`, `UseLogin<T>()` nor `AllowAnonymous()` does not start.

`examples/TodoApp` uses every feature in this file except `RequireAuthorization(...)` and
`AllowAnonymous()`, which cannot run beside its `UseLogin<T>()`. Run it with `task example:todo` and
sign in as `admin` / `admin`.

## Tables and sources

A table lists rows of one type. Its id is the kebab-case type name, so `Table<TodoList>()` is
`todo-list`. The id is the route, the policy key, the audit name and the MCP tool name. Set another
one with `t.Id("lists")`. `t.Empty(text)` shows one sentence under the list while it has no rows.

The source decides what the table offers. A source that only lists has a list page. A source that
also finds by key has a detail page, and actions. A source that writes gets create and edit. A
source that deletes gets delete.

**EF Core.** `UseEntityFramework<TDb>()` serves every table whose type is an entity of `TDb`. It
reads the key from the model, hides navigations, and makes generated columns read-only. It filters,
sorts and searches in SQL. Subclass `EfSource<TDb, T>` to add a rule:

```csharp
public sealed class UserSource(AppDbContext db) : EfSource<AppDbContext, User>(db)
{
    public override async Task<User> CreateAsync(User row, CancellationToken ct)
    {
        if (await Db.Users.AnyAsync(u => u.Email == row.Email, ct))
            throw new AdminRejection(new Dictionary<string, string> { ["Email"] = "Already registered." });
        return await base.CreateAsync(row, ct);
    }
}

panel.Table<User>(t => t.Source<UserSource>());
```

**Your own service.** Pass delegates over any service from DI. Declare the search, filters and
sorts the service supports. The panel offers only those.

```csharp
panel.Table<Company>(t => t.Source<CrmStore, int>(
    list: (store, q, ct) => store.ListAsync(q.Search, q.Filter(c => c.Plan), q.Page, ct),
    find: (store, id, ct) => store.FindAsync(id, ct),
    s => s.Search(findsKey: true).Filters(c => c.Plan).Sorts(c => c.Name)));
```

When the admin pastes a key into the search box, `Search(findsKey: true)` narrows the list to that row.

**A source class.** Implement `IAdminSource<T, TKey>`, and `IAdminWritable<T>` and
`IAdminDeletable<T>` for writes. The panel resolves it from the operation's scope.

```csharp
public sealed class ContactSource(CrmStore store)
    : IAdminSource<Contact, int>, IAdminWritable<Contact>, IAdminDeletable<Contact>
{
    public Task<AdminPage<Contact>> ListAsync(AdminQuery<Contact> query, CancellationToken ct) =>
        Task.FromResult(AdminPage.From(store.Contacts, query));
    public Task<Contact?> FindAsync(int key, CancellationToken ct) => /* ... */;
    public Task<Contact> CreateAsync(Contact row, CancellationToken ct) => /* ... */;
    public Task<Contact> UpdateAsync(Contact row, IReadOnlySet<string> changed, CancellationToken ct) => /* ... */;
    public Task DeleteAsync(Contact row, CancellationToken ct) => /* ... */;
}

panel.Table<Contact>(t => t.Source<ContactSource>(s => s.Search().Filters(c => c.CompanyId)));
```

What a source receives:

| `AdminQuery<T>` member | Meaning |
|---|---|
| `Page` | Zero-based `Index` and `Size`. `Skip` is `Index * Size`. |
| `Search` | The trimmed search text. If the table is not searchable or the box is empty, it is null. |
| `SortBy`, `SortDescending`, `SortedBy(x => x.Prop, out desc)` | The sorted column. Null keeps the source's own order. |
| `Filters`, `Filter(x => x.Prop)` | Filter values, already converted to the column's type. |
| `Exact` | Filters that must match exactly even on text: a related table's pin to its parent, and link columns. |

`AdminPage.From(rows, query)` filters, sorts and pages a list in memory. Text filters match a
substring in any case, and other filters and those in `Exact` match exactly.

A row can be a class with setters or a positional record. The panel builds a record through its
constructor. `UpdateAsync` receives the whole row and the names of the properties that changed.

## Columns

Every scalar property is a column, in declaration order. A list property, such as
`IReadOnlyList<string>` or a list of records, shows on the detail page only.

```csharp
panel.Table<Todo>(t => t
    .Columns(x => x.Title, x => x.Status, x => x.AssigneeId, x => x.DueAt)   // the list page, in this order
    .Column(x => x.Title, c => c.Validate(s => s.Length >= 3, "At least 3 characters."))
    .Column(x => x.Status, c => c.Status(s => s.Tone(TodoStatus.Done, Tone.Success)))
    .Column(x => x.AssigneeId, c => c.Title("Assignee").LinksTo<User>())
    .Column(x => x.Description, c => c.Content(Content.Markdown))
    .Column(x => x.CreatedAt, c => c.ReadOnly().Format("yyyy-MM-dd"))
    .Hide(x => x.UpdatedAt)
    .Computed("due", x => DuePhrase(x.DueAt))
    .Computed<AuditLogStore, int>("edits", (log, x, ct) => log.CountAsync(x.Id, ct), c => c.InList())
    .DisplayMember(x => x.Title));
```

| Call | Effect |
|---|---|
| `Columns(...)` | Picks the list page's columns and their order. The detail page and the forms follow the same order. |
| `Validate(rule, message)` | A rule checked on the server for every surface, MCP included. Data annotations such as `[Required]` and `[MaxLength]` apply too. |
| `Status(...)` | Shows the value as a coloured chip. |
| `LinksTo<T>()` | The value is a key of another table. The cell links to that row, and the column is filterable. |
| `Content(kind)` | A long value. The cell shows its first line and opens the whole value as `Text`, `Markdown`, `Json` or `Html`. HTML opens in a sandboxed frame with no scripts. |
| `Format(...)` | A .NET format string for this column. `panel.Format<DateTime>(...)` sets one for every column of a type. Numbers group their thousands in cells and stat cards alike, except keys, links and columns named `...Id`. `Format("0")` shows plain digits, such as for a year. |
| `ReadOnly()` | Shown, never written. |
| `Hide(...)` | Removed from every surface. Writes keep the stored value. |
| `Computed(...)` | A value computed from the row, on the detail page. `InList()` adds it to the list page too, with one computation per row shown. |
| `DisplayMember(...)` | The row's label in titles, links and search. The default is its `Name`, `Title`, `Label`, `DisplayName` or `Email`, else its key. |

## Related tables

`Related<TChild>(child => child.ForeignKey)` shows the child rows that point at this row on its
detail page. `Inline()` renders them as a table, pinned to the row, without the foreign key column.
The child's source must declare that foreign key as a filter. EF sources filter on anything.

```csharp
panel.Table<Company>(t => t
    .Related<Contact>(c => c.CompanyId, r => r.Title("People").Inline().Columns(c => c.Name, c => c.Email)));
```

`Columns("Name", "open-tickets")` names the inline table's columns as strings. Use it to show a
computed column that the child's own list leaves out.

## Actions and input records

An action is a button on a row. The panel loads the row, asks the policy with it, and then runs the
handler in the operation's scope. With an EF source, the row is tracked by the scope's `DbContext`.

```csharp
public sealed record SnoozeInput([Range(1, 30)] int Days, string? Reason);
public sealed record CommentInput([Required] string Body, AdminFile? Screenshot);

panel.Table<Todo>(t => t
    .Action("snooze", "Snooze", a => a
        .Confirm("Pushes the due date out.")
        .Input<SnoozeInput>()
        .Field(i => i.Reason, f => f.Options("waiting on someone", "low priority"))
        .Run<AppDbContext>(async (db, ctx) =>
        {
            ctx.Row.DueAt = ctx.Row.DueAt?.AddDays(ctx.Input.Days);
            await db.SaveChangesAsync(ctx.CancellationToken);
            ctx.Success($"Snoozed {ctx.Input.Days} days.");
        }))
    .Action("comment", "Add comment", a => a
        .Input<CommentInput>()
        .Field(i => i.Body, f => f.Markdown())
        .Field(i => i.Screenshot, f => f.Accept(".png", ".jpg").MaxBytes(2_000_000))
        .Run(ctx => ctx.Result($"Saved **{ctx.Input.Body.Length}** characters."))));
```

An input record's properties are the dialog's fields. The property type picks the input. Data
annotations set the rules. `Field(...)` adds what a type cannot say:

| `Field` call | Effect |
|---|---|
| `Options("a", "b")`, `Options<TService>((svc, ct) => ...)` | A select. The panel loads the choices to show the field, and loads them again on submit. A list-of-strings property takes several. |
| `Markdown()` | A Markdown editor with a preview. |
| `Multiline()` | A text area. |
| `Accept(...)`, `MaxBytes(...)` | Rules for an `AdminFile` property, which is an upload. |
| `Title`, `Description`, `Validate` | Label, help text and a server-side rule. |

`ctx.Success(message)` shows a toast. `ctx.Result(markdown)` shows a Markdown document. An uploaded
file is held in memory. Its audit entry records its name, type and size, never its bytes.

An action can also take its shape from the row:

```csharp
panel.Table<Role>(t => t
    .Action("edit-permissions", "Edit permissions", a => a
        .Input<PermissionsInput>()
        .Prefill(role => new PermissionsInput(role.Permissions))
        .Options<PermissionCatalog>(i => i.Permissions, (catalog, role, ct) => catalog.ForOrgAsync(role.OrgId, ct))
        .Run<RoleStore>((store, ctx) => store.SetPermissionsAsync(ctx.Row, ctx.Input.Permissions, ctx.CancellationToken)))
    .Action("duplicate", "Duplicate", a => a
        .When(role => !role.BuiltIn)
        .Run<RoleStore>(async (store, ctx) => ctx.Open(await store.CopyAsync(ctx.Row, ctx.CancellationToken)))));
```

| Call | Effect |
|---|---|
| `Prefill(row => input)`, `Prefill<TService>((svc, row, ct) => ...)` | The dialog opens with this input instead of the input record's defaults. |
| `Options<TService>(i => i.Field, (svc, row, ct) => ...)` | Choices that depend on the row. They are loaded when the dialog opens and again on submit. MCP cannot list them before the row is known, but it checks a call against them. |
| `When(row => ...)` | The action shows only on rows that pass. A direct call on another row is refused with "does not apply". This is separate from authorization. |
| `ctx.Open(row)` | After the action, the panel opens that row of the same table. Over MCP, the result names its key. |

## Forms

A form is a page of its own at `/admin/forms/{id}`, with an input record and a handler.

```csharp
public sealed record DecodeInput([Required] string Id);

panel.Form<DecodeInput>("decode-id", f => f
    .Title("Decode an id")
    .Run<IdCodec>((codec, ctx) =>
    {
        ctx.Result($"`{codec.Decode(ctx.Input.Id)}`");
        return Task.CompletedTask;
    }));
```

## Dashboards and streaming

A dashboard is a page of widgets in rows. `Home()` makes it the home page.

```csharp
panel.Dashboard("operations", d => d
    .Home()
    .Row(r => r
        .Stat<AppDbContext, int>("Open tasks", (db, ct) => db.Todos.CountAsync(x => x.Status != TodoStatus.Done, ct))
        .Chart<DailyCompletion>("Completed per day", c => c
            .Points<AppDbContext>(LastFourteenDaysAsync).X(p => p.Day).Y(p => p.Count)
            .Poll(TimeSpan.FromSeconds(10)))
        .Chart<MetricsTick>("Live metrics", c => c
            .Stream<MetricsTickStream>((s, ct) => s.Ticks(0, ct)).X(p => p.At).Y(p => p.Value).Window(40)))
    .Row(r => r.Table<Todo>("Recent tasks", w => w.Sort(x => x.CreatedAt, descending: true).Take(10))));
```

A chart loads its points once, polls them with `Poll(...)`, or streams them with `Stream(...)`. A
table widget reads its table the way the list page does, so the same policy applies.

A table can also show a live widget on each row's detail page. The stream receives the row:

```csharp
panel.Table<Device>(t => t
    .Live<DeviceStream, Reading>("Power", (s, device, ct) => s.ReadingsAsync(device.Id, ct),
        w => w.Chart(p => p.At, p => p.Watts).Window(120))
    .Live<DeviceStream, Message>("Messages", (s, device, ct) => s.MessagesAsync(device.Id, ct),
        w => w.Feed(m => m.At, m => m.Topic, m => m.Payload).Keep(200))
    .Poll(TimeSpan.FromSeconds(5)));
```

Each viewer enumerates the stream on its own, in an operation scope of its own. The pipeline checks
the subscription as a read of the row or of the dashboard. A stream that throws ends with an error
in its widget, and the cause goes to the log. `Poll(...)` on a table re-reads the detail page's row.

## Host pages and the sidebar

A host page is a Razor component of your own inside the panel's shell. Its route starts with
`/admin/`. It can inject `IUserAccessor` for the signed-in admin.

```razor
@page "/admin/status"
@inject IUserAccessor User
<h1 data-af="title">Status</h1>
<p>Signed in as @User.GetUser().Identity?.Name.</p>
```

```csharp
panel
    .Group("Work", g => g.Table<Todo>().Table<TodoList>())
    .Page<StatusPage>(p => p.Area(NavArea.Settings))
    .Table<AuditEntry>(t => t.Id("my-activity").Area(NavArea.Account))
    .Table<Tag>(t => t.Hidden());
```

The sidebar lists entries in registration order. `Group(...)` files entries under a heading.
`NavArea.Settings` pins an entry to the bottom of the sidebar. `NavArea.Account` puts it in the
account menu. `Hidden()` leaves a table out of the sidebar, and its URL still works.

## Authorization

The panel has three layers.

1. **The umbrella policy.** `RequireAuthorization("Admins")` names an ASP.NET Core policy. The
   endpoints require it, so your scheme decides the challenge: a cookie scheme redirects to its
   login page, a bearer scheme answers 401. The pipeline evaluates it again on every operation, so
   an admin who loses access is stopped at once. `UseLogin<T>()` sets the panel's own policy.
   `AllowAnonymous()` opens the panel on purpose, for demos and tests.
2. **`IAdminPolicy`.** Your per-operation decision, resolved from the operation's scope:

   ```csharp
   public sealed class FrozenWhenDonePolicy : IAdminPolicy
   {
       public ValueTask<bool> AllowsAsync(AdminRequest request, CancellationToken ct) => new(request switch
       {
           TableRequest<Todo> { Op: AdminOp.Update or AdminOp.Delete, Row: { } todo } => todo.Status != TodoStatus.Done,
           ActionRequest<Todo> { Row: { } todo } => todo.Status != TodoStatus.Done,
           _ => true,
       });
   }

   panel.Policy<FrozenWhenDonePolicy>();
   ```

   The request is a `TableRequest<T>`, an `ActionRequest<T>`, a `FormRequest`, a
   `DashboardRequest` or a `HostPageRequest`, each with the `User`. `Row` is the stored row whenever
   the operation has one. For a list, a create, or a check whether to offer a button, it is null.
   A refused `HostPageRequest` drops the host page from the sidebar and renders a refusal in its place.

   **A Read rule about the row does not filter lists or search.** They ask Read with a null row,
   so every row the source returns is listed. Your source must leave out the rows the caller may
   not read.
3. **`t.Authorize(...)`.** A rule for one table, asked with the row in the same places:

   ```csharp
   panel.Table<TodoList>(t => t.Authorize((user, op, list) =>
       op is not (AdminOp.Update or AdminOp.Delete) || list?.IsArchived != true));
   ```

The panel hides what the policy refuses. A refused call that reaches the pipeline anyway, from an
old page or over MCP, fails with a "Forbidden" error.

## Audit

`panel.Audit<TSink>()` registers an `IAdminAuditSink`. It receives every successful create, update,
delete, action and form submission, from the panel and from MCP. A refused or failed operation
records nothing.

```csharp
public sealed class AuditLogSink(AuditLogStore store) : IAdminAuditSink
{
    public Task RecordAsync(AdminAuditEvent e, CancellationToken ct)
    {
        store.Add(e.At, e.Operation, e.Table, e.Key, e.Action, e.User.Identity?.Name, e.Surface, e.Changes);
        return Task.CompletedTask;
    }
}
```

`Changes` holds every stored property on a create and a delete, and the changed ones, before and
after, on an update. `Input` holds an action's or a form's input.

## Errors

Throw `AdminRejection` from a source or a handler for an expected refusal. The panel shows the
message once. MCP returns it as the tool's error. Nothing is audited.

```csharp
throw new AdminRejection("The invoice is already paid.");
throw new AdminRejection(new Dictionary<string, string> { ["Email"] = "Already registered." });
```

The second form puts each message beside its field. Any other exception is logged, and the admin
sees a generic failure without its details.

## Login

`UseLogin<TValidator>()` adds the panel's own sign-in: a login page at `/admin/login`, a cookie,
and sign-out. It also sets the umbrella policy. The validator checks the credentials:

```csharp
public sealed class Credentials(AdminStore admins) : IAdminCredentialValidator
{
    public async Task<LoginResult> ValidateAsync(string username, string password, CancellationToken ct)
    {
        var admin = await admins.FindAsync(username, ct);
        if (admin is { LockedUntil: { } until } && until > DateTimeOffset.UtcNow)
            return LoginResult.Locked(until - DateTimeOffset.UtcNow);
        if (admin is null || !admin.Verify(password))
            return LoginResult.Rejected;
        return LoginResult.Ok(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, username)])));
    }
}
```

`Locked` answers 429 with a `Retry-After` header. If the `returnUrl` is a path on this site,
sign-in redirects there. Otherwise it opens the panel.

`UseLogin<TValidator>(endpoints: e => ...)` runs on the login page, sign-in and sign-out endpoints.
Use it to add an endpoint filter, such as one that pads sign-in to a constant time, or a rate limit.

## MCP

`AdminForge.Mcp` serves the panel as MCP tools for AI agents:

```csharp
panel
    .UseLogin<Credentials>()
    .UseMcp(o =>
    {
        o.Path = "/mcp";
        o.PublicBaseUrl = new Uri("https://admin.example/");
    });
```

An MCP client signs in through the panel. The admin picks the tools the connection gets on a
consent page, and every call runs as that admin through the same pipeline. MCP needs a signed-in
panel, so use it with `RequireAuthorization(...)` or `UseLogin<T>()`.

- Set `PublicBaseUrl` behind a proxy. Without it, the OAuth documents take their URLs from the
  request's Host header.
- Connections live in memory and end on a restart. To keep them, register your own
  `IMcpConnectionStore` that advances a refresh token atomically, and persist the Data Protection
  keys. Without persisted keys, every token dies on a restart.
- A refresh token that is used again ends its connection as a replay. The exception is reuse within
  10 seconds of its first use, while the token that replaced it is still the newest. That is a
  client refreshing from two requests at once. The grace is held per process.
- The pipeline checks every tool call against the claims captured at consent. To cut off an admin
  at once, make your umbrella policy check live state, not only claims.
- Grants use the stable ids of tables and actions, so a renamed title keeps existing grants.
- An action with `Confirm(...)` runs over MCP without a question. The tool's description includes
  that text.
- "My MCP connections" in the account menu and "All MCP connections" in the `NavArea.Settings`
  area list the connections. Each has an audited action that ends a connection.

## Theming

```csharp
panel
    .Environment("staging", AdminEnvironment.Staging)
    .Theme(t =>
    {
        t.PrimaryColor = "#00897b";
        t.LogoUrl = "/logo.svg";
        t.LogoAlt = "Acme";
    });
```

`Environment(...)` shows a badge in a colour per environment, so staging and production look
different. The panel's look comes from CSS custom properties. A host can override those tokens and
add a stylesheet of its own.
