using System.Globalization;

namespace Ghozt.Theming;

/// <summary>
/// The optional finishes laid over a palette: the gradient wash, the glass treatment and the glow.
/// </summary>
/// <remarks>
/// Each is independently switchable because a style has to remain available plain. The generated
/// palettes are the product; the finishes are a preference on top of them, and an operator who wants
/// Spectral Black flat should not have to accept a wash to get it.
/// <para>
/// The ranges mirror the suite's <c>ThemeEffects</c> exactly, so a look configured in one CAPITAL-IZ
/// product transfers to another as the same numbers rather than as an approximation of it.
/// </para>
/// </remarks>
public sealed record ThemeEffects(
    bool Gradient = false,
    int Direction = 135,
    int Intensity = 55,
    int GradientOpacity = 30,
    bool Glass = true,
    int Blur = 18,
    bool Glow = true)
{
    /// <summary>The style ungraded: no wash, no glass, glow only. Every palette must be usable here.</summary>
    public static readonly ThemeEffects Plain = new(Gradient: false, Glass: false, Glow: true);

    /// <summary>What the app ships with: glass surfaces and glows, no full-app wash.</summary>
    public static readonly ThemeEffects Standard = new();

    public const int MinimumDirection = 0;
    public const int MaximumDirection = 360;
    public const int MinimumIntensity = 20;
    public const int MaximumIntensity = 80;
    public const int MinimumGradientOpacity = 25;
    public const int MaximumGradientOpacity = 100;
    public const int MinimumBlur = 0;
    public const int MaximumBlur = 40;

    /// <summary>
    /// Clamps every dial to its published range.
    /// </summary>
    /// <remarks>
    /// Values arrive from persisted settings, which survive across versions and can hold anything.
    /// Normalising on read rather than on write means a setting written by an older build cannot
    /// produce an out-of-range finish in this one.
    /// </remarks>
    public ThemeEffects Normalised() => this with
    {
        Direction = Math.Clamp(Direction, MinimumDirection, MaximumDirection),
        Intensity = Math.Clamp(Intensity, MinimumIntensity, MaximumIntensity),
        GradientOpacity = Math.Clamp(GradientOpacity, MinimumGradientOpacity, MaximumGradientOpacity),
        Blur = Math.Clamp(Blur, MinimumBlur, MaximumBlur),
    };

    /// <summary>
    /// The alpha of each wash stop, floored so the tail of the wash never disappears entirely.
    /// </summary>
    public (double Start, double Mid, double End) StopAlphas()
    {
        var opacity = Math.Clamp(GradientOpacity, MinimumGradientOpacity, MaximumGradientOpacity) / 100.0;
        return (opacity, Math.Max(opacity - 0.05, 0.18), Math.Max(opacity - 0.18, 0.12));
    }

    /// <summary>
    /// Serialised as one compact string, so the finish travels with a profile alongside the palette
    /// key rather than needing a table of its own.
    /// </summary>
    public string Encode() => string.Join(
        ',',
        Gradient ? 1 : 0,
        Direction,
        Intensity,
        GradientOpacity,
        Glass ? 1 : 0,
        Blur,
        Glow ? 1 : 0);

    public static ThemeEffects Decode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return Standard;
        }

        var parts = encoded.Split(',');
        if (parts.Length != 7)
        {
            return Standard;
        }

        int At(int index, int fallback) =>
            int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        return new ThemeEffects(
            Gradient: At(0, 0) != 0,
            Direction: At(1, 135),
            Intensity: At(2, 55),
            GradientOpacity: At(3, 30),
            Glass: At(4, 1) != 0,
            Blur: At(5, 18),
            Glow: At(6, 1) != 0).Normalised();
    }
}
