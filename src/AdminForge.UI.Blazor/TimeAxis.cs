namespace AdminForge.UI.Blazor;

/// <summary>X-axis label spacing and format for a time series spanning a given range.</summary>
public static class TimeAxis
{
    private static readonly TimeSpan[] Ladder =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(2), TimeSpan.FromDays(3), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
    ];

    public static TimeSpan LabelSpacing(TimeSpan span) =>
        Ladder.FirstOrDefault(s => s >= span / 6, Ladder[^1]);

    public static string LabelFormat(TimeSpan spacing) =>
        spacing < TimeSpan.FromMinutes(1) ? "HH:mm:ss"
        : spacing < TimeSpan.FromDays(1) ? "HH:mm"
        : "MM-dd";
}
