namespace Ghozt.Theming;

/// <summary>
/// The eight fixed colours that carry forensic meaning, identical in every palette and every mode.
/// </summary>
/// <remarks>
/// <para>
/// These are not themed. A palette may tint ordinary surfaces and actions, but it must never make a
/// fault look verified, a simulation look observed, or an authority decision look like ordinary
/// selection. A product whose evidence colours moved with the operator's taste would not be usable
/// as evidence at all.
/// </para>
/// <para>
/// They still go through the contrast guarantee before being published as ink: a fixed anchor is a
/// promise about <em>meaning</em>, not a promise that the raw value is readable on every surface.
/// The anchor is what the eye must recognise; the published token is the nearest legible form of it.
/// </para>
/// </remarks>
public static class GhoztSemantics
{
    /// <summary>Current agent path, observation, connected state and primary live activity.</summary>
    public const string LiveHex = "#6EF2D6";

    /// <summary>Network, API, service and streaming information.</summary>
    public const string TelemetryHex = "#55CFF5";

    /// <summary>Hypotheses, simulated branches, model reasoning and Phantom states.</summary>
    public const string CounterfactualHex = "#8B79FF";

    /// <summary>Approval, evidence seal, trusted verdict, release clearance, formal certification.</summary>
    public const string AuthorityHex = "#CDA661";

    /// <summary>Expected behaviour proven with evidence.</summary>
    public const string VerifiedHex = "#42C98A";

    /// <summary>Uncertainty, blocked scope, degraded operation or review required.</summary>
    public const string WarningHex = "#FFB44A";

    /// <summary>Confirmed failure under a declared condition.</summary>
    public const string FaultHex = "#FF5968";

    /// <summary>Immediate risk, destructive failure or containment action.</summary>
    public const string CriticalHex = "#D92F4C";

    public static readonly Rgba Live = ColorMath.Parse(LiveHex);
    public static readonly Rgba Telemetry = ColorMath.Parse(TelemetryHex);
    public static readonly Rgba Counterfactual = ColorMath.Parse(CounterfactualHex);
    public static readonly Rgba Authority = ColorMath.Parse(AuthorityHex);
    public static readonly Rgba Verified = ColorMath.Parse(VerifiedHex);
    public static readonly Rgba Warning = ColorMath.Parse(WarningHex);
    public static readonly Rgba Fault = ColorMath.Parse(FaultHex);
    public static readonly Rgba Critical = ColorMath.Parse(CriticalHex);

    /// <summary>The anchors, by the token name each is published under.</summary>
    public static readonly IReadOnlyDictionary<string, Rgba> Anchors = new Dictionary<string, Rgba>(StringComparer.Ordinal)
    {
        ["Ghozt.Live"] = Live,
        ["Ghozt.Telemetry"] = Telemetry,
        ["Ghozt.Counterfactual"] = Counterfactual,
        ["Authority.Clearance"] = Authority,
        ["State.Verified"] = Verified,
        ["State.Warning"] = Warning,
        ["State.Fault"] = Fault,
        ["State.Critical"] = Critical,
    };
}

/// <summary>
/// The observation status of a lattice node, a finding or a claim.
/// </summary>
/// <remarks>
/// Named rather than expressed as a colour, because colour is never the sole carrier of status: a
/// surface binds the label, the icon and the colour from the same value, and the label is what
/// survives high-contrast mode, a colour-blind operator and a printed report.
/// </remarks>
public enum GhoztState
{
    /// <summary>No observation has been made. Not a failure - an absence.</summary>
    Untested,

    /// <summary>Visited, but nothing has been proven about it.</summary>
    Observed,

    /// <summary>Expected behaviour proven with evidence.</summary>
    Verified,

    /// <summary>Uncertain, blocked, degraded or awaiting review.</summary>
    Warning,

    /// <summary>A difference from expectation that is not yet a confirmed fault.</summary>
    Deviation,

    /// <summary>A confirmed failure under recorded conditions.</summary>
    Fault,

    /// <summary>Intermittent or condition-dependent; not yet reliably reproduced.</summary>
    Phantom,

    /// <summary>Immediate risk, destructive failure or an active containment action.</summary>
    Critical,

    /// <summary>A controlled variation of observed history. Never presented as observed fact.</summary>
    Counterfactual,
}

/// <summary>
/// How a claim came to be known. Kept separate from <see cref="GhoztState"/> because the blueprint
/// requires observed fact, computed metric, AI inference and human judgement to be distinguishable
/// on the surface - a confidence figure alone does not tell an operator which of the four it is.
/// </summary>
public enum Provenance
{
    /// <summary>Captured from the target. The strongest form.</summary>
    Observed,

    /// <summary>Derived arithmetically from observations.</summary>
    Computed,

    /// <summary>Produced by a model. Always labelled, never presented as observation.</summary>
    Inferred,

    /// <summary>Entered or decided by a person, with that person recorded.</summary>
    Declared,

    /// <summary>Produced by varying a factor under control. Hypothetical by construction.</summary>
    Simulated,
}

public static class GhoztStateExtensions
{
    /// <summary>The token this state paints with. One place, so a state cannot be coloured two ways.</summary>
    public static string TokenName(this GhoztState state) => state switch
    {
        GhoztState.Untested => "Text.Muted",
        GhoztState.Observed => "Ghozt.Live",
        GhoztState.Verified => "State.Verified",
        GhoztState.Warning => "State.Warning",
        GhoztState.Deviation => "State.Warning",
        GhoztState.Fault => "State.Fault",
        GhoztState.Phantom => "Ghozt.Counterfactual",
        GhoztState.Critical => "State.Critical",
        GhoztState.Counterfactual => "Ghozt.Counterfactual",
        _ => "Text.Body",
    };

    /// <summary>The label. Always rendered alongside the colour - never replaced by it.</summary>
    public static string Label(this GhoztState state) => state switch
    {
        GhoztState.Untested => "Untested",
        GhoztState.Observed => "Observed",
        GhoztState.Verified => "Verified",
        GhoztState.Warning => "Review",
        GhoztState.Deviation => "Deviation",
        GhoztState.Fault => "Fault",
        GhoztState.Phantom => "Phantom",
        GhoztState.Critical => "Critical",
        GhoztState.Counterfactual => "Counterfactual",
        _ => "Unknown",
    };

    /// <summary>
    /// A Segoe Fluent Icons glyph. A second, non-colour channel for the same information, which is
    /// what keeps status legible in high contrast and for a colour-blind operator.
    /// </summary>
    public static string Glyph(this GhoztState state) => state switch
    {
        GhoztState.Untested => "",       // circle, hollow
        GhoztState.Observed => "",       // view
        GhoztState.Verified => "",       // check mark
        GhoztState.Warning => "",        // warning
        GhoztState.Deviation => "",      // diff
        GhoztState.Fault => "",          // error badge
        GhoztState.Phantom => "",        // intermittent
        GhoztState.Critical => "",       // shield / containment
        GhoztState.Counterfactual => "", // branch
        _ => "",
    };

    public static string Label(this Provenance provenance) => provenance switch
    {
        Provenance.Observed => "Observed",
        Provenance.Computed => "Computed",
        Provenance.Inferred => "AI inference",
        Provenance.Declared => "Human decision",
        Provenance.Simulated => "Simulated",
        _ => "Unknown",
    };

    public static string TokenName(this Provenance provenance) => provenance switch
    {
        Provenance.Observed => "Ghozt.Live",
        Provenance.Computed => "Ghozt.Telemetry",
        Provenance.Inferred => "Ghozt.Counterfactual",
        Provenance.Declared => "Authority.Clearance",
        Provenance.Simulated => "Ghozt.Counterfactual",
        _ => "Text.Body",
    };
}
