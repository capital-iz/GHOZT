namespace Ghozt.App.Shell;

/// <summary>The lifecycle workspaces, in the order the operations rail lists them.</summary>
/// <remarks>
/// The order is the examination lifecycle - register, plan, explore, triage, reconstruct, preserve,
/// validate, guard, report, decide - so moving down the rail is moving forward through the work.
/// Settings sits at the foot because it is configuration, not a stage.
/// </remarks>
public enum WorkspaceKind
{
    Targets,
    Sweep,
    StateMap,
    Faults,
    Replay,
    Evidence,
    Algorithms,
    Regressions,
    Reports,
    Clearance,
    Settings,
}

/// <summary>One entry in the operations rail.</summary>
public sealed record WorkspaceDefinition(
    WorkspaceKind Kind,
    string Title,
    string Glyph,
    string Purpose,
    string Shortcut)
{
    /// <summary>
    /// The eleven workspaces.
    /// </summary>
    /// <remarks>
    /// Glyphs are Segoe Fluent Icons code points. Each is paired with a label in the rail's expanded
    /// mode and an accessible name in its compact mode - an icon alone is never the only carrier for
    /// navigation, which is the same rule the status layer follows for colour.
    /// </remarks>
    public static readonly IReadOnlyList<WorkspaceDefinition> All =
    [
        new(WorkspaceKind.Targets, "Targets", "",
            "Register apps, builds, environments and the authority to examine them.", "Alt+1"),
        new(WorkspaceKind.Sweep, "Sweep", "",
            "Plan, budget, approve and run autonomous examinations.", "Alt+2"),
        new(WorkspaceKind.StateMap, "State Map", "",
            "Explore the connected graph of features, states, endpoints and evidence.", "Alt+3"),
        new(WorkspaceKind.Faults, "Faults", "",
            "Triage confirmed deviations and unstable Phantom findings.", "Alt+4"),
        new(WorkspaceKind.Replay, "Replay", "",
            "Reconstruct exact sequences and compare runs.", "Alt+5"),
        new(WorkspaceKind.Evidence, "Evidence", "",
            "Search, inspect, seal, redact and export raw artefacts.", "Alt+6"),
        new(WorkspaceKind.Algorithms, "Algorithms", "",
            "Test deterministic algorithms, datasets, invariants and expected outputs.", "Alt+7"),
        new(WorkspaceKind.Regressions, "Regressions", "",
            "Manage guards generated from features and confirmed faults.", "Alt+8"),
        new(WorkspaceKind.Reports, "Reports", "",
            "Create technical, executive and machine-readable outputs.", "Alt+9"),
        new(WorkspaceKind.Clearance, "Clearance", "",
            "Evaluate release policy and issue a signed decision.", "Alt+0"),
        new(WorkspaceKind.Settings, "Settings", "",
            "Roles, policies, adapters, agents, themes and retention.", "Ctrl+,"),
    ];

    public static WorkspaceDefinition For(WorkspaceKind kind) => All.First(w => w.Kind == kind);
}
