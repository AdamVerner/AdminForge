using AdminForge.UI.Blazor;

namespace AdminForge.UnitTests.UI;

public class TimeAxisTests
{
    [Theory]
    [InlineData("13.00:00:00", "3.00:00:00", "MM-dd")]
    [InlineData("00:00:40", "00:00:10", "HH:mm:ss")]
    [InlineData("00:00:00", "00:00:01", "HH:mm:ss")]
    [InlineData("400.00:00:00", "30.00:00:00", "MM-dd")]
    public void Spacing_Keeps_About_Six_Labels(string span, string expected, string format)
    {
        var spacing = TimeAxis.LabelSpacing(TimeSpan.Parse(span));
        Assert.Equal(TimeSpan.Parse(expected), spacing);
        Assert.Equal(format, TimeAxis.LabelFormat(spacing));
    }
}
