using AdminForge;
using Microsoft.EntityFrameworkCore;
using TodoApp;
using TodoApp.Crm;
using TodoApp.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=todoapp.db")
);
builder.Services.AddSingleton<AuditLogStore>();
builder.Services.AddSingleton<CrmStore>();
builder.Services.AddSingleton<MetricsTickStream>();

builder.Services.AddAdminForge(panel =>
    panel
        .Title("Todo Admin")
        .Welcome(
            "Tasks and lists live in SQLite through EF Core; companies and contacts in a service of the host's own."
        )
        .Environment("local", AdminEnvironment.Local)
        .Theme(t =>
        {
            t.PrimaryColor = "#00897b";
            t.LogoUrl = Logo;
            t.LogoAlt = "Todo";
            t.StylesheetUrl = "/todo.css";
        })
        .Render<TodoApp.Pages.TaskBanner>(AdminSlot.AboveDetail)
        .UseLogin<DemoCredentials>()
        .UseMcp(o =>
        {
            o.Path = "/mcp";
            // Behind a proxy the Host header cannot be trusted to build the OAuth documents.
            if (builder.Configuration["PublicBaseUrl"] is { } url)
                o.PublicBaseUrl = new Uri(url);
        })
        .UseEntityFramework<AppDbContext>()
        .Policy<FrozenWhenDonePolicy>()
        .Audit<AuditLogSink>()
        .Format<DateTime>("yyyy-MM-dd HH:mm")
        .Group("People", g => g.Users())
        .Group("Work", g => g.Lists().Tasks().Tags())
        .Group("Customers", g => g.Companies().Contacts().Directory())
        .Group("Tools", g => g.Notifications())
        .Operations()
        .Settings()
        .Account()
);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

// The panel reads the signed-in admin, so both run before it is mapped.
app.UseAuthentication();
app.UseAuthorization();
app.MapAdminForge();
app.MapGet("/", () => Results.Redirect("/admin"));
app.Run();

public partial class Program
{
    private const string Logo =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E%3Cpath fill='%2300897b' d='M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z'/%3E%3C/svg%3E";
}
