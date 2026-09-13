using System.IO;
using System.Text.RegularExpressions;
using Ghozt.Theming;
using Xunit;

namespace Ghozt.Tests;

/// <summary>
/// Every resource key the shell asks for must exist.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a real defect. The text styles were renamed out of the <c>Text.*</c>
/// namespace to stop them colliding with the colour tokens, and two references inside a ternary were
/// missed. WPF does not complain about an unknown resource key: <c>TryFindResource</c> returns null,
/// the style is simply not applied, and the control renders with whatever it inherits. The forensics
/// inspector therefore shipped with its labels brighter than its values, and nothing failed.
/// </para>
/// <para>
/// A missing key is invisible at runtime, so it has to be caught here. The check reads the actual
/// source files rather than a list somebody maintains by hand, because a list is the thing that goes
/// stale.
/// </para>
/// </remarks>
public sealed class ResourceCoverageTests
{
    private static readonly Regex DynamicOrStatic =
        new(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z][A-Za-z0-9_.]*)\s*\}", RegexOptions.Compiled);

    private static readonly Regex TryFind =
        new(@"TryFindResource\(\s*""([A-Za-z][A-Za-z0-9_.]*)""\s*\)", RegexOptions.Compiled);

    private static readonly Regex XamlKey =
        new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// Keys the shell supplies at runtime rather than from a dictionary file.
    /// </summary>
    /// <remarks>
    /// An exact list, so a key that stops being supplied has to be removed here deliberately rather
    /// than being quietly absorbed.
    /// </remarks>
    private static readonly HashSet<string> RuntimeSupplied = new(StringComparer.Ordinal)
    {
        // Registered on the window before InitializeComponent.
        "BoolToVisibility", "InverseBoolToVisibility", "TokenBrush", "SeverityBrush",
        "StreamTime", "Reproducibility", "IsEmpty", "HasText",

        // Published by ThemeManager.PublishMetrics.
        "Space.2", "Space.4", "Space.8", "Space.12", "Space.16", "Space.24", "Space.32", "Space.48",
        "Space.Card", "Space.CardTight", "Space.Panel", "Space.Row",
        "Metric.RowHeight", "Metric.ControlHeight",
        "Motion.Selection", "Motion.Panel", "Motion.Materialise", "Motion.Pulse", "Motion.Trail",
        "Motion.Enabled",
        "Finish.BlurRadius", "Finish.GlassEnabled", "Finish.GlowEnabled", "Finish.GradientEnabled",
    };

    /// <summary>Walks up to the app source, so the test runs from any output directory.</summary>
    private static DirectoryInfo AppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Ghozt.App");
            if (Directory.Exists(candidate))
            {
                return new DirectoryInfo(candidate);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/Ghozt.App above the test output.");
    }

    private static IEnumerable<FileInfo> SourceFiles(DirectoryInfo root) =>
        root.EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(f => f.Extension is ".xaml" or ".cs")
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Every key any dictionary in the app declares.</summary>
    private static HashSet<string> DeclaredKeys(DirectoryInfo root)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles(root).Where(f => f.Extension == ".xaml"))
        {
            foreach (Match match in XamlKey.Matches(File.ReadAllText(file.FullName)))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    /// <summary>Every colour token the generator publishes, plus the .Color aliases.</summary>
    private static HashSet<string> TokenKeys()
    {
        var tokens = new GhoztTokens(GhoztCatalog.DefaultKey, dark: true, 82);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in tokens.TokenNames)
        {
            keys.Add(name);
            keys.Add(name + ".Color");
        }

        return keys;
    }

    [Fact]
    public void EveryResourceKeyTheShellAsksForExists()
    {
        var root = AppSource();
        var available = DeclaredKeys(root);
        available.UnionWith(TokenKeys());
        available.UnionWith(RuntimeSupplied);

        var missing = new List<string>();

        foreach (var file in SourceFiles(root))
        {
            var text = File.ReadAllText(file.FullName);
            var regex = file.Extension == ".xaml" ? DynamicOrStatic : TryFind;

            foreach (Match match in regex.Matches(text))
            {
                var key = match.Groups[1].Value;
                if (!available.Contains(key))
                {
                    missing.Add($"{file.Name}: {key}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "These resource keys are referenced but never declared. WPF resolves an unknown key to "
            + "null and renders without complaint, so nothing else will catch this:\n"
            + string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void NoStyleSharesAKeyWithAColourToken()
    {
        // The collision that started all of this. A style and a brush under one key are resolved by
        // whichever dictionary merged last, so the shell was handed a colour where it asked for a
        // Style - and refused to open at all.
        var declared = DeclaredKeys(AppSource());
        var tokens = TokenKeys();

        var collisions = declared.Where(tokens.Contains).ToList();

        Assert.True(
            collisions.Count == 0,
            "These keys are declared in a dictionary AND published as colour tokens:\n"
            + string.Join("\n", collisions));
    }

    [Fact]
    public void TheTextNamespaceBelongsToTheTokenTable()
    {
        // Stated as its own rule so the reason survives. Text.* is generated; a style that wants to
        // live there is a style that will one day collide with a token nobody has added yet.
        var declared = DeclaredKeys(AppSource());
        var offenders = declared.Where(k => k.StartsWith("Text.", StringComparison.Ordinal)).ToList();

        Assert.True(
            offenders.Count == 0,
            "These dictionary keys sit in the generated Text.* namespace:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void ButtonsAndTogglesMustBeRoundedRectanglesAndNotOvals()
    {
        // Ghozt design doctrine: buttons must be rounded rectangles, never ovals or capsules.
        var root = AppSource();
        var geometryPath = Path.Combine(root.FullName, "Themes", "Geometry.xaml");
        Assert.True(File.Exists(geometryPath), "Geometry.xaml must exist.");

        var geometryXaml = File.ReadAllText(geometryPath);
        var match = Regex.Match(geometryXaml, @"<CornerRadius\s+x:Key=""Radius\.Pill"">(\d+)</CornerRadius>");
        Assert.True(match.Success, "Radius.Pill must be declared in Geometry.xaml.");

        var radius = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(radius <= 12, $"Radius.Pill must be a rounded rectangle (<= 12), but was {radius}. Ovals (999) are prohibited.");
    }
}
