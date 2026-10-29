namespace Ghozt.Theming;

/// <summary>
/// The twelve Ghozt palettes, in the order the blueprint lists them.
/// </summary>
/// <remarks>
/// <para>
/// Each is a coherent design identity with Dark, Light and System modes, semantic-token derivation
/// and optional surface finishes. They are not superficial colour swaps: they preserve contrast,
/// hierarchy, state meaning and component behaviour, because the same generator produces all of them
/// and the same guarantee constrains all of them.
/// </para>
/// <para>
/// The values are the blueprint's values verbatim (GZT-UX-001 §03). Changing one here changes the
/// product's identity, which is why they are declared once, in one file, rather than assembled from
/// defaults at call sites.
/// </para>
/// </remarks>
public static class GhoztCatalog
{
    /// <summary>The style used when nothing has been chosen: the default restricted-forensics identity.</summary>
    public const string DefaultKey = "spectral_black";

    private static readonly GhoztPalette[] PaletteList =
    [
        new()
        {
            Key = "spectral_black",
            Label = "Spectral Black",
            Description = "Default restricted-forensics identity; balanced and unmistakably Ghozt.",
            SuitedTo = "Daily operation, State Map, forensic runs",
            Primary = "#6EF2D6",
            Secondary = "#55CFF5",
            Accent = "#8B79FF",
            AccentSoft = "#101A22",
            DarkBase = "#05070A",
            DarkPanel = "#091017",
            DarkElevated = "#101A22",
            LightBase = "#EEF6F5",
            DefaultOpacity = 82,
            DefaultEffects = new ThemeEffects(Gradient: true, GradientOpacity: 28, Glass: true, Blur: 18, Glow: true),
        },
        new()
        {
            Key = "blacksite",
            Label = "Blacksite",
            Description = "Severe, covert and executive; gold carries authority.",
            SuitedTo = "Clearance, controlled environments, client demos",
            Primary = "#B6C4CF",
            Secondary = "#CDA661",
            Accent = "#7CD4C4",
            AccentSoft = "#14191D",
            DarkBase = "#040506",
            DarkPanel = "#0B0E11",
            DarkElevated = "#14191D",
            LightBase = "#F1F3F4",
            DefaultOpacity = 88,
            DefaultEffects = new ThemeEffects(Gradient: false, Glass: true, Blur: 10, Glow: true),
        },
        new()
        {
            Key = "ghostwire",
            Label = "Ghostwire",
            Description = "High-energy telemetry and live traversal.",
            SuitedTo = "Live sweeps, network and API inspection",
            Primary = "#45D9FF",
            Secondary = "#2F7CF6",
            Accent = "#63F5C8",
            AccentSoft = "#0E2638",
            DarkBase = "#030811",
            DarkPanel = "#071523",
            DarkElevated = "#0E2638",
            LightBase = "#EDF8FC",
            DefaultOpacity = 78,
            DefaultEffects = new ThemeEffects(Gradient: true, GradientOpacity: 34, Glass: true, Blur: 16, Glow: true),
        },
        new()
        {
            Key = "phantom_violet",
            Label = "Phantom Violet",
            Description = "Counterfactual reasoning, AI hypotheses and intermittent faults.",
            SuitedTo = "Counterfactual Lab, AI analysis, Phantom faults",
            Primary = "#A78BFA",
            Secondary = "#7C4DFF",
            Accent = "#5EEAD4",
            AccentSoft = "#211A3A",
            DarkBase = "#080612",
            DarkPanel = "#141027",
            DarkElevated = "#211A3A",
            LightBase = "#F5F1FF",
            DefaultOpacity = 80,
            DefaultEffects = new ThemeEffects(Gradient: true, GradientOpacity: 30, Glass: true, Blur: 18, Glow: true),
        },
        new()
        {
            Key = "operator_green",
            Label = "Operator Green",
            Description = "Focused long-session terminal and operations posture.",
            SuitedTo = "Black-box logs, queues, local test operation",
            Primary = "#4FD18B",
            Secondary = "#2E9E6B",
            Accent = "#B5F26E",
            AccentSoft = "#16251D",
            DarkBase = "#060A08",
            DarkPanel = "#0D1511",
            DarkElevated = "#16251D",
            LightBase = "#F0F8F3",
            DefaultOpacity = 84,
            DefaultEffects = new ThemeEffects(Gradient: false, Glass: false, Blur: 0, Glow: true),
        },
        new()
        {
            Key = "institutional_gold",
            Label = "Institutional Gold",
            Description = "Premium CAPITAL-IZ authority and formal review.",
            SuitedTo = "Reports, approvals, certificates, executive review",
            Primary = "#CDA661",
            Secondary = "#B08D55",
            Accent = "#6EF2D6",
            AccentSoft = "#202227",
            DarkBase = "#0B0C0F",
            DarkPanel = "#16171A",
            DarkElevated = "#202227",
            LightBase = "#F7F4EE",
            DefaultOpacity = 90,
            DefaultEffects = new ThemeEffects(Gradient: false, Glass: true, Blur: 8, Glow: false),
        },
        new()
        {
            Key = "cold_archive",
            Label = "Cold Archive",
            Description = "Calm evidence review and long-form forensic reading.",
            SuitedTo = "Evidence Vault, report inspection, audit",
            Primary = "#86B7D8",
            Secondary = "#5A7FA3",
            Accent = "#A3D5E8",
            AccentSoft = "#172B39",
            DarkBase = "#071018",
            DarkPanel = "#0E1C27",
            DarkElevated = "#172B39",
            LightBase = "#F2F7FA",
            DefaultOpacity = 92,
            DefaultEffects = new ThemeEffects(Gradient: false, Glass: false, Blur: 0, Glow: false),
        },
        new()
        {
            Key = "redacted_mono",
            Label = "Redacted Mono",
            Description = "Near-monochrome restricted-document and compliance character.",
            SuitedTo = "Sensitive reviews, redaction, accessibility",
            Primary = "#D9DEE2",
            Secondary = "#8B959E",
            Accent = "#CDA661",
            AccentSoft = "#1B1D1F",
            DarkBase = "#050505",
            DarkPanel = "#101112",
            DarkElevated = "#1B1D1F",
            LightBase = "#F4F4F3",
            DefaultOpacity = 94,
            DefaultEffects = new ThemeEffects(Gradient: false, Glass: false, Blur: 0, Glow: false),
        },
        new()
        {
            Key = "deep_ocean",
            Label = "Deep Ocean",
            Description = "Technical calm with subtle teal intelligence.",
            SuitedTo = "Multi-service architecture and data flows",
            Primary = "#39C6C1",
            Secondary = "#2A7FA3",
            Accent = "#8DD8D1",
            AccentSoft = "#103A43",
            DarkBase = "#041014",
            DarkPanel = "#09232A",
            DarkElevated = "#103A43",
            LightBase = "#EDF8F7",
            DefaultOpacity = 83,
            DefaultEffects = new ThemeEffects(Gradient: true, GradientOpacity: 30, Glass: true, Blur: 14, Glow: true),
        },
        new()
        {
            Key = "containment_amber",
            Label = "Containment Amber",
            Description = "Risk analysis, unstable environments and active containment.",
            SuitedTo = "Risk Control, destructive tests, incident modes",
            Primary = "#E8A33D",
            Secondary = "#B4661E",
            Accent = "#55CFF5",
            AccentSoft = "#231A15",
            DarkBase = "#0D0A07",
            DarkPanel = "#181310",
            DarkElevated = "#231A15",
            LightBase = "#FBF5EA",
            DefaultOpacity = 88,
            DefaultEffects = new ThemeEffects(Gradient: false, Glass: false, Blur: 0, Glow: true),
        },
        new()
        {
            Key = "frost_glass",
            Label = "Frost Glass",
            Description = "Premium luminous lab interface with maximum depth.",
            SuitedTo = "Presentation, exploration, multi-monitor labs",
            Primary = "#7BB7FF",
            Secondary = "#9F7AEA",
            Accent = "#6EF2D6",
            AccentSoft = "#1E2D49",
            DarkBase = "#0A1020",
            DarkPanel = "#121C31",
            DarkElevated = "#1E2D49",
            LightBase = "#F4F7FC",
            DefaultOpacity = 72,
            DefaultEffects = new ThemeEffects(Gradient: true, GradientOpacity: 36, Glass: true, Blur: 22, Glow: true),
        },
        new()
        {
            Key = "daylight_evidence",
            Label = "Daylight Evidence",
            Description = "High-readability light-first forensic and reporting environment.",
            SuitedTo = "Day work, print review, client-facing analysis",
            Primary = "#1F7A8C",
            Secondary = "#3F6F8F",
            Accent = "#7559C7",
            AccentSoft = "#18262C",
            DarkBase = "#081015",
            DarkPanel = "#111B20",
            DarkElevated = "#18262C",
            LightBase = "#F7FAFB",
            DefaultOpacity = 100,
            DefaultEffects = new ThemeEffects(Gradient: false, Glass: false, Blur: 0, Glow: false),
        },
    ];

    public static IReadOnlyList<GhoztPalette> All => PaletteList;

    /// <summary>
    /// The palette for a key, falling back to the default.
    /// </summary>
    /// <remarks>
    /// Falls back rather than throwing: a persisted setting can name a palette that a later build
    /// renamed, and a shell that refuses to open because of a stale preference is a worse failure
    /// than a shell that opens in the default identity and says so.
    /// </remarks>
    public static GhoztPalette Find(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            foreach (var palette in PaletteList)
            {
                if (string.Equals(palette.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return palette;
                }
            }
        }

        return PaletteList.First(p => p.Key == DefaultKey);
    }

    public static bool Exists(string? key) =>
        key is not null && PaletteList.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
