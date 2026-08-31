namespace Ghozt.Theming;

/// <summary>
/// A colour with straight (non-premultiplied) alpha.
/// </summary>
/// <remarks>
/// Deliberately not <c>System.Windows.Media.Color</c>: the theme engine is a plain library so the
/// whole contrast guarantee can be tested without a WPF binding, exactly as the suite's engine is.
/// </remarks>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>The 32-bit ARGB value a UI layer wants, so no view has to know how a token is stored.</summary>
    public uint Argb => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    public double Alpha => A / 255.0;

    public Rgba WithAlpha(double alpha) =>
        this with { A = (byte)ColorMath.RoundHalfEven(Math.Clamp(alpha, 0.0, 1.0) * 255.0) };

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public string ToHexA() => A == 255 ? ToHex() : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHexA();
}
