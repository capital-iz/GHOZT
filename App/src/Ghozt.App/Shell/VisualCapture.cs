using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ghozt.App.Theming;
using Ghozt.Theming;

namespace Ghozt.App.Shell;

/// <summary>
/// Renders the shell to PNG, for the visual-regression coverage the blueprint requires.
/// </summary>
/// <remarks>
/// <para>
/// This is a product feature, not a test scaffold. GZT-UX-001 §20 requires reference screenshots
/// across core workspaces, modes, palettes, scaling and density, and §19 requires a theme digest
/// recorded alongside them so an image can be reproduced later. Both are provided here: every
/// capture writes the digest into its own filename.
/// </para>
/// <para>
/// It renders the live visual tree rather than a mock, so what is captured is what an operator would
/// see. A harness that drew its own approximation of the shell would pass while the shell was broken
/// - which is the failure mode this whole product exists to eliminate.
/// </para>
/// </remarks>
public static class VisualCapture
{
    /// <summary>Renders one element at a device scale factor.</summary>
    public static void Save(FrameworkElement element, string path, double scale = 1.0)
    {
        var width = element.ActualWidth;
        var height = element.ActualHeight;

        if (width < 1 || height < 1)
        {
            // Measuring an unrealised element would produce a 0x0 bitmap and a green test. Better to
            // say why nothing was captured.
            throw new InvalidOperationException(
                $"{element.GetType().Name} has no layout yet ({width}x{height}); it cannot be captured.");
        }

        var target = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);

        // Force a layout pass, and let the dispatcher finish the queued work behind it.
        //
        // UpdateLayout alone is not enough: a workspace populates itself from Loaded and
        // DataContextChanged, both of which are queued, so rendering immediately captures a frame in
        // which the shell header already knows about two findings and the workspace still shows its
        // empty state. Draining to ContextIdle means the image is a frame an operator could actually
        // have seen.
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        element.UpdateLayout();
        target.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// Captures the shell in every palette, in the requested modes.
    /// </summary>
    /// <remarks>
    /// Each capture waits for the dispatcher to drain before rendering. Without that, a theme change
    /// and its capture race, and the images silently come back one palette behind - a failure that
    /// looks exactly like the generator being wrong.
    /// </remarks>
    public static IReadOnlyList<string> CaptureCatalogue(
        Window window,
        string directory,
        IEnumerable<ThemeMode>? modes = null,
        WorkspaceKind? workspace = null)
    {
        var theme = ThemeManager.Current;
        var written = new List<string>();

        var originalPalette = theme.PaletteKey;
        var originalMode = theme.Mode;
        var originalOpacity = theme.SurfaceOpacity;
        var originalEffects = theme.Effects;

        try
        {
            foreach (var mode in modes ?? [ThemeMode.Dark, ThemeMode.Light])
            {
                foreach (var palette in GhoztCatalog.All)
                {
                    theme.Mode = mode;
                    theme.ApplyPaletteWithItsDefaults(palette.Key);

                    Drain(window);

                    var name = $"{palette.Key}.{mode.ToString().ToLowerInvariant()}.png";
                    var path = Path.Combine(directory, name);
                    Save(window, path);
                    written.Add(path);
                }
            }
        }
        finally
        {
            // Restore whatever the operator had. A capture run that left the shell in the last
            // palette it rendered would be a side effect nobody asked for.
            theme.Mode = originalMode;
            theme.PaletteKey = originalPalette;
            theme.SurfaceOpacity = originalOpacity;
            theme.Effects = originalEffects;
            Drain(window);
        }

        return written;
    }

    /// <summary>Lets the dispatcher finish layout and rendering before the next capture.</summary>
    private static void Drain(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
