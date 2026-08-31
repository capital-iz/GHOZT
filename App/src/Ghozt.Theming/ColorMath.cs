using System.Globalization;

namespace Ghozt.Theming;

/// <summary>
/// The colour arithmetic every generated theme is built from.
/// </summary>
/// <remarks>
/// <para>
/// Twelve palettes, each in light and dark, is far too many combinations to hand-tune and keep
/// readable. Instead a palette declares only its identity colours, everything else is derived, and
/// text is then forced to meet a contrast floor against the surface it actually sits on. That makes
/// legibility a property of the system rather than something to be rechecked by eye each time.
/// </para>
/// <para>
/// This mirrors the Capital-Iz suite's <c>ColorMath</c> so that a Ghozt token and a CommanDeck token
/// with the same name resolve the same way. Ghozt extends the suite rather than forking it: the
/// arithmetic here is the suite's arithmetic, and only the palette declarations and the semantic
/// namespace are new.
/// </para>
/// </remarks>
public static class ColorMath
{
    /// <summary>
    /// Round half to even.
    /// </summary>
    /// <remarks>
    /// Named rather than left to <see cref="Math.Round(double)"/> because the tie-breaking rule is
    /// load-bearing: the suite's Dart port had to reimplement this exact behaviour, and a handful of
    /// derived tokens land one unit off if the two disagree. Stating it here keeps the reason
    /// attached to the code.
    /// </remarks>
    public static int RoundHalfEven(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

    private static byte Clamp255(double value) => (byte)Math.Clamp(RoundHalfEven(value), 0, 255);

    /// <summary>Parses <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public static Rgba Parse(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);

        var text = hex.Trim().TrimStart('#');

        if (text.Length == 3)
        {
            text = string.Concat(text[0], text[0], text[1], text[1], text[2], text[2]);
        }

        static byte Byte(string source, int index) =>
            byte.Parse(source.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return text.Length switch
        {
            6 => new Rgba(Byte(text, 0), Byte(text, 2), Byte(text, 4)),
            8 => new Rgba(Byte(text, 2), Byte(text, 4), Byte(text, 6), Byte(text, 0)),
            _ => throw new FormatException($"'{hex}' is not a colour this engine can read."),
        };
    }

    /// <summary>Blends <paramref name="amount"/> of <paramref name="b"/> into <paramref name="a"/>.</summary>
    public static Rgba Mix(Rgba a, Rgba b, double amount)
    {
        var t = Math.Clamp(amount, 0.0, 1.0);
        return new Rgba(
            Clamp255(a.R + ((b.R - a.R) * t)),
            Clamp255(a.G + ((b.G - a.G) * t)),
            Clamp255(a.B + ((b.B - a.B) * t)),
            Clamp255(a.A + ((b.A - a.A) * t)));
    }

    public static Rgba Lighten(Rgba colour, double amount) => Mix(colour, White with { A = colour.A }, amount);

    public static Rgba Darken(Rgba colour, double amount) => Mix(colour, Black with { A = colour.A }, amount);

    /// <summary>Composites a translucent colour over an opaque backdrop.</summary>
    public static Rgba Flatten(Rgba colour, Rgba backdrop)
    {
        var alpha = colour.Alpha;
        if (alpha >= 1.0)
        {
            return colour with { A = 255 };
        }

        return new Rgba(
            Clamp255((colour.R * alpha) + (backdrop.R * (1 - alpha))),
            Clamp255((colour.G * alpha) + (backdrop.G * (1 - alpha))),
            Clamp255((colour.B * alpha) + (backdrop.B * (1 - alpha))));
    }

    /// <summary>WCAG relative luminance.</summary>
    public static double Luminance(Rgba colour)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));
    }

    /// <summary>WCAG contrast ratio, 1:1 to 21:1.</summary>
    public static double ContrastRatio(Rgba foreground, Rgba background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        var (hi, lo) = a >= b ? (a, b) : (b, a);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Pure white and pure black, the only two inks a contrast floor can always be met with.</summary>
    public static readonly Rgba White = new(255, 255, 255);

    public static readonly Rgba Black = new(0, 0, 0);

    /// <summary>
    /// The luminance at which white and black are exactly as legible as each other.
    /// </summary>
    /// <remarks>
    /// Solving (1.05)/(L+0.05) = (L+0.05)/0.05 gives L = 0.1791, not the 0.5 that "looks dark"
    /// suggests. Between the two, a surface still reads as dark while black already out-contrasts
    /// white on it - which is the band where lightening text can never reach AA.
    /// </remarks>
    public const double InkCrossoverLuminance = 0.1791;

    /// <summary>
    /// The floor on the better of white and black against any background at all, reached exactly at
    /// <see cref="InkCrossoverLuminance"/>. Because it exceeds 4.5, choosing the winning ink turns
    /// the AA floor from something attempted into something guaranteed.
    /// </summary>
    public const double GuaranteedContrast = 4.58;

    /// <summary>Whichever of white or black actually contrasts more against <paramref name="background"/>.</summary>
    public static Rgba BestInk(Rgba background) =>
        Luminance(background) <= InkCrossoverLuminance ? White : Black;

    /// <summary>
    /// The ink that leaves the worst of <paramref name="backgrounds"/> as legible as possible.
    /// </summary>
    /// <remarks>
    /// A single text colour lands on several surfaces at once, so the direction has to be decided
    /// for the set rather than per surface - pushing for one surface and then for the next can walk
    /// a colour straight back past where the first needed it.
    /// </remarks>
    public static Rgba BestInkForAll(IReadOnlyList<Rgba> backgrounds)
    {
        if (backgrounds.Count == 0)
        {
            return White;
        }

        var worstWhite = backgrounds.Min(b => ContrastRatio(White, b));
        var worstBlack = backgrounds.Min(b => ContrastRatio(Black, b));
        return worstWhite >= worstBlack ? White : Black;
    }

    /// <summary>
    /// Pushes <paramref name="foreground"/> toward the winning ink until it clears
    /// <paramref name="minimumRatio"/> against every one of <paramref name="backgrounds"/>.
    /// </summary>
    /// <remarks>
    /// Forty steps, then the ink itself. The direction is decided once, for the whole set, by
    /// <see cref="BestInkForAll"/>; stepping per surface is what walks a colour back past where an
    /// earlier surface needed it.
    /// </remarks>
    public static Rgba EnsureContrastAcross(
        Rgba foreground,
        IReadOnlyList<Rgba> backgrounds,
        double minimumRatio)
    {
        if (backgrounds.Count == 0)
        {
            return foreground;
        }

        bool Clears(Rgba candidate) =>
            backgrounds.All(b => ContrastRatio(candidate, b) >= minimumRatio);

        if (Clears(foreground))
        {
            return foreground;
        }

        var ink = BestInkForAll(backgrounds);

        for (var step = 1; step <= 40; step++)
        {
            var candidate = Mix(foreground, ink, step / 40.0);
            if (Clears(candidate))
            {
                return candidate;
            }
        }

        return ink;
    }

    /// <summary>
    /// Straight-line distance in RGB, on a 0-765 scale.
    /// </summary>
    /// <remarks>
    /// Used to keep two colours that must remain tellable apart from converging once both have been
    /// pushed to clear the same contrast floor.
    /// </remarks>
    public static double PerceptualDistance(Rgba a, Rgba b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    /// <summary>
    /// The most saturated colour with this one's character: the channels are stretched so the
    /// weakest reaches zero and the strongest reaches full.
    /// </summary>
    public static Rgba Saturate(Rgba colour)
    {
        var min = Math.Min(colour.R, Math.Min(colour.G, colour.B));
        var max = Math.Max(colour.R, Math.Max(colour.G, colour.B));

        if (max == min)
        {
            return colour;
        }

        var span = (double)(max - min);
        return colour with
        {
            R = Clamp255((colour.R - min) / span * 255.0),
            G = Clamp255((colour.G - min) / span * 255.0),
            B = Clamp255((colour.B - min) / span * 255.0),
        };
    }
}
