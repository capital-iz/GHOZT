using Ghozt.Theming;
using Xunit;

namespace Ghozt.Tests;

/// <summary>
/// The legibility guarantee, proven rather than asserted.
/// </summary>
/// <remarks>
/// These sweep every palette, both modes, the whole opacity range and a finish matrix. That breadth
/// is the point: a theme that reads well at the default opacity and fails at 35% has failed for the
/// operator who moved the slider, and nothing but a sweep finds it.
/// </remarks>
public sealed class ThemeReadingSurfaceTests
{
    /// <summary>The opacity values swept. Ends, the default, and the two thirds between.</summary>
    private static readonly int[] Opacities = [35, 50, 72, 82, 90, 100];

    private static readonly ThemeEffects[] Finishes =
    [
        ThemeEffects.Plain,
        ThemeEffects.Standard,
        new(Gradient: true, GradientOpacity: 25),
        new(Gradient: true, GradientOpacity: 100),
        new(Gradient: true, GradientOpacity: 60, Glass: true, Blur: 40, Glow: true),
    ];

    /// <summary>Every token that carries running text and must clear WCAG AA.</summary>
    private static readonly string[] TextTokens =
    [
        "Text.Primary", "Text.Body", "Text.Secondary", "Text.Muted", "Text.Faint",
        "Accent.Color", "Accent.Text", "Accent.Bright", "Accent.Muted",
        "Ghozt.Live", "Ghozt.Telemetry", "Ghozt.Counterfactual",
        "State.Verified", "State.Warning", "State.Fault", "State.Critical",
        "Authority.Clearance", "Evidence.Seal", "Danger.Text", "Action.Secondary",
    ];

    /// <summary>
    /// Rebuilds the seven surfaces a text token can land on, exactly as the generator names them.
    /// </summary>
    /// <remarks>
    /// Recomputed here from the published tokens rather than imported from the generator, so the test
    /// measures what the app will actually paint. A test that reused the generator's own private list
    /// would agree with it by construction and prove nothing.
    /// </remarks>
    private static List<Rgba> ReadingSurfaces(GhoztTokens t)
    {
        var deep = t["Surface.Deep"];
        var surfaces = new List<Rgba>
        {
            ColorMath.Flatten(t["Surface.Panel"], deep),
            ColorMath.Flatten(t["Surface.Card"], deep),
            t["Surface.CardSolid"],
            ColorMath.Flatten(t["Surface.Input"], deep),
            ColorMath.Flatten(t["Surface.Selected"], deep),
            ColorMath.Flatten(t["Surface.CardStrong"], deep),
            ColorMath.Flatten(t["Surface.Dialog"], deep),
        };

        if (t.Effects.Gradient)
        {
            var stops = new[] { t["Wash.Start"], t["Wash.Mid"], t["Wash.End"] };
            foreach (var surface in surfaces.ToArray())
            {
                foreach (var stop in stops)
                {
                    surfaces.Add(ColorMath.Flatten(stop, surface));
                }
            }
        }

        return surfaces;
    }

    public static TheoryData<string, bool> PaletteModes()
    {
        var data = new TheoryData<string, bool>();
        foreach (var palette in GhoztCatalog.All)
        {
            data.Add(palette.Key, true);
            data.Add(palette.Key, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PaletteModes))]
    public void EveryTextTokenClearsAaOnEverySurfaceItCanLandOn(string key, bool dark)
    {
        var failures = new List<string>();

        foreach (var opacity in Opacities)
        {
            foreach (var finish in Finishes)
            {
                var tokens = new GhoztTokens(key, dark, opacity, finish);
                var surfaces = ReadingSurfaces(tokens);

                foreach (var name in TextTokens)
                {
                    var ink = tokens[name];
                    foreach (var surface in surfaces)
                    {
                        var ratio = ColorMath.ContrastRatio(ink, surface);
                        if (ratio < GhoztPalette.TextContrast)
                        {
                            failures.Add(
                                $"{key}/{(dark ? "dark" : "light")}/op{opacity}/{finish.Encode()}: " +
                                $"{name} {ink} on {surface} is {ratio:F2}:1");
                        }
                    }
                }
            }
        }

        // Every failure at once, not the first: the value of this check is telling you the whole
        // extent of a regression, not the token that happened to sort earliest.
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(40)));
    }

    [Theory]
    [MemberData(nameof(PaletteModes))]
    public void EverySurfaceThatCarriesTextStaysOnItsSideOfTheInkCrossover(string key, bool dark)
    {
        var failures = new List<string>();

        foreach (var opacity in Opacities)
        {
            var tokens = new GhoztTokens(key, dark, opacity, ThemeEffects.Standard);
            foreach (var surface in ReadingSurfaces(tokens))
            {
                var luminance = ColorMath.Luminance(surface);
                var onSide = dark
                    ? luminance <= ColorMath.InkCrossoverLuminance
                    : luminance >= ColorMath.InkCrossoverLuminance;

                if (!onSide)
                {
                    failures.Add($"{key}/{(dark ? "dark" : "light")}/op{opacity}: {surface} has luminance {luminance:F4}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    [Theory]
    [MemberData(nameof(PaletteModes))]
    public void AccentAndAuthorityFillsCarryTheirOwnInk(string key, bool dark)
    {
        var tokens = new GhoztTokens(key, dark, 82, ThemeEffects.Standard);

        (string Fill, string Ink)[] pairs =
        [
            ("Accent.Fill", "Text.OnAccent"),
            ("Authority.Fill", "Authority.OnFill"),
            ("State.CriticalFill", "State.OnCritical"),
            ("State.FaultFill", "State.OnFault"),
        ];

        foreach (var (fill, ink) in pairs)
        {
            var ratio = ColorMath.ContrastRatio(tokens[ink], tokens[fill]);
            Assert.True(
                ratio >= GhoztPalette.TextContrast,
                $"{key}/{(dark ? "dark" : "light")}: {ink} on {fill} is only {ratio:F2}:1");
        }
    }

    [Fact]
    public void DisabledTextIsBelowAaOnPurpose()
    {
        // Text.Disabled is held at a 3.0 floor rather than 4.5. Unavailable must look unavailable;
        // a disabled control that reads exactly as strongly as an enabled one is a lie about state.
        var tokens = new GhoztTokens(GhoztCatalog.DefaultKey, dark: true, 82);
        var panel = ColorMath.Flatten(tokens["Surface.Panel"], tokens["Surface.Deep"]);

        var disabled = ColorMath.ContrastRatio(tokens["Text.Disabled"], panel);
        var body = ColorMath.ContrastRatio(tokens["Text.Body"], panel);

        Assert.True(disabled >= 3.0, $"Text.Disabled fell below its 3.0 floor at {disabled:F2}:1");
        Assert.True(disabled < body, "Text.Disabled must be quieter than Text.Body");
    }
}
