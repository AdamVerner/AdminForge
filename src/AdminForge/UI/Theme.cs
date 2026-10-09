using System.Globalization;
using System.Net;
using System.Text;

namespace AdminForge.UI;

/// <summary>The tokens a panel computes from its configuration, and the head every page of it shares.</summary>
internal static class Theme
{
    public const string Stylesheet = "_content/AdminForge/adminforge.css";
    public const string Script = "_content/AdminForge/adminforge.js";

    // Each takes white text at 4.5:1 or better; production is red.
    public static string EnvironmentColor(AdminEnvironment kind) =>
        kind switch
        {
            AdminEnvironment.Development => "#2457C5",
            AdminEnvironment.Staging => "#B45309",
            AdminEnvironment.Production => "#B42318",
            _ => "#64748B",
        };

    /// <summary>The environment colour and the host's accent as token overrides, the accent fitted to each theme.</summary>
    public static string Tokens(PanelModel panel)
    {
        var css = new StringBuilder(":root,:root[data-theme]{");
        if (panel.Environment is { } env)
            css.Append($"--af-env:{EnvironmentColor(env.Kind)};");
        css.Append('}');
        if (panel.Theme.PrimaryColor is { } accent)
        {
            css.Append(":root{").Append(AccentTokens(Parse(accent), dark: false)).Append('}');
            css.Append(":root[data-theme=dark]{")
                .Append(AccentTokens(Parse(accent), dark: true))
                .Append('}');
        }
        return css.ToString();
    }

    // The surfaces accent text sits on in each theme: surface, page, sunken; they mirror adminforge.css.
    private static readonly Rgb[] LightGrounds =
    [
        new(255, 255, 255),
        new(246, 247, 249),
        new(239, 241, 244),
    ];
    private static readonly Rgb[] DarkGrounds = [new(22, 26, 32), new(14, 16, 20), new(28, 33, 41)];

    /// <summary>The accent, darkened for light or lightened for dark until text in it reads at 4.5:1 on every surface.</summary>
    public static Rgb Fit(Rgb accent, bool dark)
    {
        var grounds = dark ? DarkGrounds : LightGrounds;
        var toward = dark ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
        // Bounded: rounding can stall a channel a step short of black or white.
        for (
            var i = 0;
            i < 64 && !grounds.Append(Subtle(accent, dark)).All(g => Contrast(accent, g) >= 4.5);
            i++
        )
            accent = Mix(accent, toward, 0.92);
        return accent;
    }

    private static string AccentTokens(Rgb host, bool dark)
    {
        var a = Fit(host, dark);
        var hover = dark ? Mix(a, new Rgb(255, 255, 255), 0.8) : Mix(a, new Rgb(0, 0, 0), 0.82);
        return $"--af-accent:{a};--af-accent-rgb:{a.R}, {a.G}, {a.B};--af-on-accent:{(dark ? "#0E1014" : "#FFFFFF")};"
            + $"--af-accent-hover:{hover};--af-accent-subtle:{Subtle(a, dark)};--af-focus:{a};";
    }

    private static Rgb Subtle(Rgb accent, bool dark) =>
        dark ? Mix(accent, DarkGrounds[0], 0.18) : Mix(accent, LightGrounds[0], 0.14);

    /// <summary>A page served without Blazor, in the sign-in layout, with <paramref name="main"/> as its content.</summary>
    public static string PlainPage(PanelModel panel, string title, string main)
    {
        var custom = panel.Theme.StylesheetUrl is { } url
            ? $"""<link rel="stylesheet" href="{WebUtility.HtmlEncode(url)}">"""
            : "";
        var rail = panel.Environment is null
            ? ""
            : """<div class="af-rail" aria-hidden="true"></div>""";
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(title)}</title>
            <link rel="stylesheet" href="/{Stylesheet}">
            <script src="/{Script}"></script>
            <style>{Tokens(panel)}</style>{custom}
            </head>
            <body class="af-auth">
            {rail}
            <main>
            {main}
            </main>
            </body>
            </html>
            """;
    }

    public static string EnvironmentBadge(PanelModel panel) =>
        panel.Environment is { } env
            ? $"""<span class="af-env-badge" data-af="environment">{WebUtility.HtmlEncode(env.Label)}</span>"""
            : "";

    /// <summary>Whether <paramref name="color"/> is a <c>#RRGGBB</c> colour.</summary>
    public static bool IsHex(string color) =>
        color.Length == 7 && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit);

    internal readonly record struct Rgb(int R, int G, int B)
    {
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
    }

    private static Rgb Parse(string hex) => new(Channel(hex, 1), Channel(hex, 3), Channel(hex, 5));

    private static Rgb Mix(Rgb a, Rgb b, double weight) =>
        new(
            (int)Math.Round(a.R * weight + b.R * (1 - weight)),
            (int)Math.Round(a.G * weight + b.G * (1 - weight)),
            (int)Math.Round(a.B * weight + b.B * (1 - weight))
        );

    public static double Contrast(Rgb a, Rgb b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static int Channel(string hex, int at) =>
        int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // WCAG relative luminance.
    private static double Luminance(Rgb c)
    {
        static double Linear(int c) =>
            c / 255.0 <= 0.03928 ? c / 255.0 / 12.92 : Math.Pow((c / 255.0 + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }
}
