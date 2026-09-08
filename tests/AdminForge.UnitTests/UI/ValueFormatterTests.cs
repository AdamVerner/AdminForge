using AdminForge.Core.Metadata;
using AdminForge.UI.Blazor;

namespace AdminForge.UnitTests.UI;

/// <summary>
/// One rendering per value, whichever surface asks. A column that names its own format gets it.
/// </summary>
public class ValueFormatterTests
{
    private static ColumnMeta Column(Type clrType, string? format = null) =>
        new()
        {
            PropertyName = "X",
            Label = "X",
            ClrType = clrType,
            IsNullable = false,
            Kind = ColumnKind.Scalar,
            Format = format,
        };

    [Fact]
    public void Null_Is_The_Callers_To_Render()
    {
        Assert.Null(ValueFormatter.Format(null, Column(typeof(string))));
    }

    [Theory]
    [InlineData(null, "2025-03-04 17:08")]
    [InlineData("yyyy-MM-dd", "2025-03-04")]
    [InlineData("HH:mm:ss", "17:08:09")]
    public void A_Timestamp_Uses_The_Columns_Format_Or_The_Default(string? format, string expected)
    {
        var at = new DateTime(2025, 3, 4, 17, 8, 9, DateTimeKind.Utc);
        Assert.Equal(expected, ValueFormatter.Format(at, Column(typeof(DateTime), format)));
        Assert.Equal(
            expected,
            ValueFormatter.Format(new DateTimeOffset(at), Column(typeof(DateTimeOffset), format))
        );
    }

    [Fact]
    public void A_Bool_Reads_As_Yes_Or_No()
    {
        Assert.Equal("yes", ValueFormatter.Format(true, Column(typeof(bool))));
        Assert.Equal("no", ValueFormatter.Format(false, Column(typeof(bool))));
    }

    [Fact]
    public void A_Number_Takes_A_Format_When_The_Column_Names_One()
    {
        Assert.Equal("42", ValueFormatter.Format(42, Column(typeof(int))));
        Assert.Equal("42.00", ValueFormatter.Format(42m, Column(typeof(decimal), "0.00")));
    }
}
