using System.IO;
using System.Windows.Media.Imaging;
using Xunit;

namespace Ghozt.Tests;

/// <summary>
/// Holds the installer's branding to the sizes and clearances WiX actually requires.
/// </summary>
/// <remarks>
/// <para>
/// WiX does not validate these bitmaps. A banner of the wrong size is stretched or letterboxed
/// rather than rejected, and artwork placed under the dialog title is simply overprinted by it. Both
/// failures ship a wizard that looks broken while every build stays green.
/// </para>
/// <para>
/// The clearance rule is the expensive one: WixUI draws each dialog's title and description over the
/// LEFT of the banner. The Capital-Iz mark sits at the right for that reason, and this measures the
/// left region is genuinely empty rather than trusting that it was laid out correctly.
/// </para>
/// </remarks>
public sealed class BrandingTests
{
    /// <summary>WixUI_InstallDir's fixed expectations.</summary>
    private const int BannerWidth = 493;
    private const int BannerHeight = 58;
    private const int DialogWidth = 493;
    private const int DialogHeight = 312;

    /// <summary>
    /// How far across the banner WixUI's title and description reach.
    /// </summary>
    /// <remarks>
    /// The title starts near the left edge and the description runs beneath it. Sixty per cent is
    /// the width that "End-User License Agreement" - the longest title in this wizard - occupies at
    /// the dialog's font size.
    /// </remarks>
    private const double TitleZoneFraction = 0.60;

    private static DirectoryInfo ProductRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Windows", "Installer", "msi")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Windows/Installer/msi above the test output.");
    }

    private static string AssetPath(string name) =>
        Path.Combine(ProductRoot().FullName, "Windows", "Installer", "msi", "Assets", name);

    private static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    [Theory]
    [InlineData("BannerTop.bmp", BannerWidth, BannerHeight)]
    [InlineData("DialogBackground.bmp", DialogWidth, DialogHeight)]
    public void WizardBitmapsAreExactlyTheSizeWixExpects(string name, int width, int height)
    {
        var path = AssetPath(name);
        Assert.True(File.Exists(path), $"{name} is missing. Run Windows/Installer/brand/build-brand-assets.py.");

        var image = Load(path);
        Assert.Equal(width, image.PixelWidth);
        Assert.Equal(height, image.PixelHeight);
    }

    [Fact]
    public void TheBannersTitleZoneIsClearOfArtwork()
    {
        var image = Load(AssetPath("BannerTop.bmp"));

        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        var converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);

        var zoneWidth = (int)(image.PixelWidth * TitleZoneFraction);

        // The foot carries a deliberate spectral hairline, which is not artwork the title can
        // collide with - the title sits well above it. Measured rows stop short of it.
        var measuredHeight = image.PixelHeight - 4;

        var marked = 0;
        for (var y = 0; y < measuredHeight; y++)
        {
            for (var x = 0; x < zoneWidth; x++)
            {
                var i = (y * stride) + (x * 4);
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];

                // Anything meaningfully darker than paper would compete with the overprinted title.
                if (r < 230 || g < 230 || b < 230)
                {
                    marked++;
                }
            }
        }

        Assert.True(
            marked == 0,
            $"{marked} pixels of artwork sit in the banner's left {TitleZoneFraction:P0}, where WixUI "
            + "overprints the dialog title. Move the mark right.");
    }

    [Fact]
    public void TheApplicationIconExistsAndCarriesEverySizeWindowsAsksFor()
    {
        var path = Path.Combine(
            ProductRoot().FullName, "App", "src", "Ghozt.App", "Assets", "GhoztLogo.ico");

        Assert.True(File.Exists(path), "GhoztLogo.ico is missing. Run build-brand-assets.py.");

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var sizes = decoder.Frames.Select(f => f.PixelWidth).ToHashSet();

        // Explorer, the taskbar, Alt-Tab, Add/Remove Programs and the Start menu each pick a
        // different one. A missing size is upscaled badly rather than refused.
        foreach (var required in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.True(sizes.Contains(required), $"The icon has no {required}px frame. Present: {string.Join(", ", sizes.OrderBy(s => s))}");
        }
    }

    [Fact]
    public void TheInstallerAuthoringNamesTheProductAndPublisherCorrectly()
    {
        // The naming lock in GZT-ID-001 §02: the formal product name carries the descriptor, the
        // publisher is Capital-Iz, and the visible shortcut label is "Ghozt" with nothing appended.
        var wxs = File.ReadAllText(
            Path.Combine(ProductRoot().FullName, "Windows", "Installer", "msi", "Package.wxs"));

        Assert.Contains("Name=\"Ghozt - Dev Tester\"", wxs, StringComparison.Ordinal);
        Assert.Contains("Manufacturer=\"Capital-Iz\"", wxs, StringComparison.Ordinal);
        Assert.Contains("Name=\"Ghozt\"", wxs, StringComparison.Ordinal);

        // A misspelling of the product name is the one branding error that cannot be undone once it
        // is in an installed package's registry footprint.
        Assert.DoesNotContain("Ghost", wxs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheUpgradeCodeIsPresentAndStable()
    {
        // If this ever changes, every installed copy stops being recognised and the next build
        // installs alongside it instead of replacing it.
        var wxs = File.ReadAllText(
            Path.Combine(ProductRoot().FullName, "Windows", "Installer", "msi", "Package.wxs"));

        Assert.Contains("UpgradeCode=\"8F3A6C21-7D94-4E52-9B08-51C4A7E6D310\"", wxs, StringComparison.Ordinal);
    }
}
