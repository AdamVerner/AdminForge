using System.Net;
using AdminForge.Core.Metadata;
using Microsoft.Extensions.DependencyInjection;
using TodoApp;

namespace AdminForge.IntegrationTests;

/// <summary>
/// A type off the DbContext is served by the provider the host registers, and a read-only one
/// offers nothing to create or edit.
/// </summary>
public class NonEfEntityTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _todo;

    public NonEfEntityTests(TodoAppFactory todo) => _todo = todo;

    [Fact]
    public async Task A_Read_Only_Entity_Has_No_Create_Or_Edit_Surface()
    {
        _todo
            .Services.GetRequiredService<AuditLogStore>()
            .Record(
                new AuditEvent
                {
                    EntityType = "Probe",
                    Action = AuditAction.Custom,
                    User = "tester",
                }
            );
        var client = await _todo.CreateSignedInClientAsync();

        var listBody = await client.GetStringAsync("/admin/entities/AuditLogEntry");
        Assert.Contains("Probe", listBody);
        Assert.DoesNotContain("New Audit Log", listBody);

        var viewBody = await client.GetStringAsync("/admin/entities/AuditLogEntry/1");
        Assert.Contains("tester", viewBody);
        Assert.DoesNotContain(">Edit<", viewBody);

        Assert.Contains(
            "is read-only",
            await client.GetStringAsync("/admin/entities/AuditLogEntry/1/edit")
        );
        Assert.Contains(
            "is read-only",
            await client.GetStringAsync("/admin/entities/AuditLogEntry/new")
        );
    }
}
