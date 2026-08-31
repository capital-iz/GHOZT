namespace Ghozt.Theming;

/// <summary>How dense the interface is. Density reflows mandatory information; it never removes it.</summary>
public enum Density
{
    Compact,
    Standard,
    Comfortable,
}

/// <summary>How much motion is permitted.</summary>
public enum MotionLevel
{
    Full,
    Reduced,
    Off,
}

/// <summary>
/// The resolved token table for one palette, mode, opacity and finish.
/// </summary>
/// <remarks>
/// A value object rather than a service: two tables built from the same four inputs are equal, so a
/// rebuild that changes nothing does not look like a theme change and does not re-theme the tree.
/// </remarks>
public sealed class GhoztTokens : IEquatable<GhoztTokens>
{
    private readonly IReadOnlyDictionary<string, Rgba> _raw;
    private static readonly HashSet<string> ReportedMissing = new(StringComparer.Ordinal);

    public GhoztTokens(
        string paletteKey,
        bool dark,
        int surfaceOpacityPercent,
        ThemeEffects? effects = null,
        Density density = Density.Standard,
        MotionLevel motion = MotionLevel.Full)
    {
        PaletteKey = paletteKey;
        Palette = GhoztCatalog.Find(paletteKey);
        Dark = dark;
        SurfaceOpacityPercent = Math.Clamp(surfaceOpacityPercent, 35, 100);
        Effects = (effects ?? Palette.DefaultEffects).Normalised();
        Density = density;
        Motion = motion;
        _raw = Palette.BuildTokens(dark, SurfaceOpacityPercent, Effects);
    }

    public string PaletteKey { get; }

    public GhoztPalette Palette { get; }

    public bool Dark { get; }

    public int SurfaceOpacityPercent { get; }

    public ThemeEffects Effects { get; }

    public Density Density { get; }

    public MotionLevel Motion { get; }

    /// <summary>The raw table, for tests and for surfaces that need the value rather than a brush.</summary>
    public IReadOnlyDictionary<string, Rgba> Raw => _raw;

    public IEnumerable<string> TokenNames => _raw.Keys;

    /// <summary>
    /// A colour by token name.
    /// </summary>
    /// <remarks>
    /// Loud in development, survivable in an operator's hands - and the difference matters more than
    /// it looks. A missing token throws in a debug build, which is where a typo should be caught and
    /// where it fails every build. In release it falls back to <c>Surface.Deep</c>, the app's own
    /// base surface: visible, themed and never transparent, reported once so it reaches a log rather
    /// than vanishing.
    /// <para>
    /// A control that is the wrong shade is a blemish; a screen that will not open is a broken
    /// product. A fallback that were merely transparent would reproduce the invisible-control failure
    /// this guard exists to avoid, which is why the fallback is a real surface.
    /// </para>
    /// </remarks>
    public Rgba Get(string token)
    {
        if (_raw.TryGetValue(token, out var value))
        {
            return value;
        }

#if DEBUG
        throw new ArgumentException(
            $"Unknown theme token: {token}. Add it to GhoztPalette.BuildTokens, or fix the name.",
            nameof(token));
#else
        lock (ReportedMissing)
        {
            if (ReportedMissing.Add(token))
            {
                System.Diagnostics.Debug.WriteLine($"Ghozt theme: unknown token \"{token}\"; falling back to Surface.Deep.");
            }
        }

        return _raw.TryGetValue("Surface.Deep", out var fallback) ? fallback : new Rgba(5, 7, 10);
#endif
    }

    public Rgba this[string token] => Get(token);

    /// <summary>The opaque colour a translucent surface is actually seen as.</summary>
    public Rgba Flattened(string token) => ColorMath.Flatten(Get(token), Get("Surface.Deep"));

    /// <summary>The blur sigma the glass treatment frosts with; zero when glass is off.</summary>
    /// <remarks>
    /// Zero is the caller's signal to drop the effect entirely rather than apply a zero-radius blur.
    /// A zero blur is not free: it is a full-surface offscreen composite that changes nothing.
    /// </remarks>
    public double GlassBlurSigma => Effects.Glass ? Effects.Blur : 0.0;

    public bool GlowEnabled => Effects.Glow && Motion != MotionLevel.Off;

    /// <summary>The spacing step for the current density, in device-independent pixels.</summary>
    public double Space(int step) => step * Density switch
    {
        Theming.Density.Compact => 0.82,
        Theming.Density.Comfortable => 1.22,
        _ => 1.0,
    };

    /// <summary>
    /// A digest of the exact look, recorded alongside a screenshot or a report so that an image can
    /// be reproduced later.
    /// </summary>
    /// <remarks>
    /// The blueprint requires a theme digest for reproducible screenshots. It carries the inputs, not
    /// the output: a table can be regenerated from these five values, and a digest that carried the
    /// table would go stale the moment the generator improved.
    /// </remarks>
    public string Digest() =>
        $"{PaletteKey}/{(Dark ? "dark" : "light")}/{SurfaceOpacityPercent}/{Effects.Encode()}/{Density}";

    public bool Equals(GhoztTokens? other) =>
        other is not null
        && other.PaletteKey == PaletteKey
        && other.Dark == Dark
        && other.SurfaceOpacityPercent == SurfaceOpacityPercent
        && other.Effects == Effects
        && other.Density == Density
        && other.Motion == Motion;

    public override bool Equals(object? obj) => Equals(obj as GhoztTokens);

    public override int GetHashCode() =>
        HashCode.Combine(PaletteKey, Dark, SurfaceOpacityPercent, Effects, Density, Motion);
}
