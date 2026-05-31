namespace AdminForge.Core.Configuration;

/// <summary>
/// Optional visual theming applied to the admin shell. Every property is
/// nullable — leave any of them unset to keep the renderer's defaults.
/// </summary>
/// <remarks>
/// Configured via <see cref="AdminForgeBuilder.WithTheme(System.Action{ThemeOptions})"/>.
/// The renderer interprets these values; <see cref="AdminForge.Core"/> stays
/// presentation-agnostic and treats this as a plain DTO.
/// </remarks>
public sealed class ThemeOptions
{
    /// <summary>
    /// URL of a logo image rendered in the admin shell's app bar next to the title.
    /// Supports absolute URLs, app-relative paths (e.g. <c>/images/logo.png</c>), and
    /// inline <c>data:</c> URIs (handy for shipping a single-file SVG without an asset file).
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Alt text rendered on the logo image. Defaults to "Logo" when a logo URL is set
    /// and no alt is supplied.
    /// </summary>
    public string? LogoAlt { get; set; }

    /// <summary>
    /// Hex string (e.g. <c>"#7e57c2"</c>) used as the renderer's primary palette colour.
    /// When unset, the renderer's default theme palette is used unchanged.
    /// </summary>
    public string? PrimaryColor { get; set; }

    /// <summary>
    /// Hex string used as the renderer's secondary palette colour. When unset, the
    /// renderer's default palette is used unchanged.
    /// </summary>
    public string? SecondaryColor { get; set; }
}
