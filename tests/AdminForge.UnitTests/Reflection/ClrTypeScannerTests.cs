using System.ComponentModel.DataAnnotations;
using AdminForge.Core.Metadata;

namespace AdminForge.UnitTests.Reflection;

public class ClrTypeScannerTests
{
    private enum Tier
    {
        Free,
        Paid,
    }

    private sealed record Account(
        int Id,
        string Email,
        string? DisplayName,
        Tier? Tier,
        DateTime CreatedAt,
        IReadOnlyList<string> Tags
    );

    private sealed record Membership([property: Key] int OrgId, [property: Key] int UserId);

    private sealed class Keyless
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Describes_Scalars_Keys_And_Nullability_Of_A_Read_Model()
    {
        var meta = ClrTypeScanner.Scan(typeof(Account));

        Assert.Equal("Account", meta.RouteName);
        Assert.Equal(["Id"], meta.PrimaryKeyPropertyNames);
        // Collections are not columns: a read model carries them for its own consumers.
        Assert.Equal(
            ["Id", "Email", "DisplayName", "Tier", "CreatedAt"],
            meta.Columns.Select(c => c.PropertyName)
        );

        var id = meta.Columns.Single(c => c.PropertyName == "Id");
        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsGenerated);

        var email = meta.Columns.Single(c => c.PropertyName == "Email");
        Assert.False(email.IsNullable);
        Assert.True(email.IsRequired);

        var name = meta.Columns.Single(c => c.PropertyName == "DisplayName");
        Assert.True(name.IsNullable);
        Assert.Equal("Display Name", name.Label);

        var tier = meta.Columns.Single(c => c.PropertyName == "Tier");
        Assert.Equal(ColumnKind.Enum, tier.Kind);
        Assert.Equal(typeof(Tier), tier.EnumType);
        Assert.True(tier.IsNullable);
    }

    [Fact]
    public void Key_Attributes_Win_Over_The_Id_Convention_And_A_Keyless_Type_Is_Refused()
    {
        Assert.Equal(
            ["OrgId", "UserId"],
            ClrTypeScanner.Scan(typeof(Membership)).PrimaryKeyPropertyNames
        );
        Assert.Throws<InvalidOperationException>(() => ClrTypeScanner.Scan(typeof(Keyless)));
    }
}
