namespace Ghozt.Core;

/// <summary>What Ghozt is permitted to do against a target. Visible at all times in the shell.</summary>
public enum AccessClass
{
    /// <summary>Read state only. No interaction that changes anything.</summary>
    ObserveOnly,

    /// <summary>May drive the target through its ordinary surfaces.</summary>
    Interact,

    /// <summary>May change persistent state, with reset or rollback declared.</summary>
    StateChanging,

    /// <summary>May run destructive tests. Requires typed confirmation and an approver.</summary>
    Destructive,
}

/// <summary>
/// Execution isolation level enforced against targets and sweep runners (Patent Table 70).
/// </summary>
public enum ExecutionIsolationLevel
{
    /// <summary>L0: Read-only source/runtime discovery. No target mutation; limited evidence collection.</summary>
    L0_Observe = 0,

    /// <summary>L1: UI/API actions in local/test environment. Reset/cleanup required; no destructive external side effects.</summary>
    L1_ControlledInteraction = 1,

    /// <summary>L2: Create/update controlled data, restart services, apply fixtures. Approval according to policy; snapshots/rollback.</summary>
    L2_StateChanging = 2,

    /// <summary>L3: Delete/corrupt/fail resources in disposable environment. Explicit approval, isolated blast radius and containment.</summary>
    L3_DestructiveChaos = 3,

    /// <summary>L4: Branch-bounded AI/developer repair. Allowed paths/commands, diff review and tests.</summary>
    L4_CodeModification = 4,

    /// <summary>L5: Approved production diagnostics or carefully bounded test. Strongest approval, minimised capture and immediate stop/containment.</summary>
    L5_ProductionObservation = 5,
}

/// <summary>
/// Classification of data sensitivity for target interaction and evidence storage (Patent §35).
/// </summary>
public enum DataSensitivityTier
{
    Public,
    Internal,
    Confidential,
    Restricted,
}

/// <summary>Where a target runs. Drives the persistent environment band and the production gate.</summary>
public enum TargetEnvironment
{
    Local,
    Development,
    Staging,
    Production,
}

/// <summary>How a sweep is currently proceeding. Partial, blocked and failed are preserved, never collapsed.</summary>
public enum RunState
{
    Draft,
    AwaitingApproval,
    PreFlight,
    Running,
    Paused,
    Stopping,
    Completed,
    Partial,
    Blocked,
    Failed,
    Cancelled,
}

/// <summary>The severity ladder. Stable across every palette and mode.</summary>
public enum Severity
{
    Info,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>Where a finding sits in its lifecycle (Patent Table 45). Maturity is not severity.</summary>
public enum FindingMaturity
{
    New,
    Phantom,
    Confirming,
    Reproduced,
    CauseSupported,
    FixReady,
    ReTest,
    Verified,
    Waived,
    Rejected,
}

/// <summary>Duplicate decision classifier outcome (Patent Table 47).</summary>
public enum DuplicateOutcome
{
    SameFault,
    Variant,
    Related,
    Independent,
}

/// <summary>Semantic category for difference classification (Patent Table 46).</summary>
public enum SemanticCategory
{
    StatusCodeMismatch,
    UiStateCorruption,
    SchemaViolation,
    ContractViolation,
    ResourceDrift,
    CrashFatalExit,
    UnresponsiveWindow,
    AuthenticationDenied,
    InvariantBroken,
    Other,
}

/// <summary>The verdict a release policy can issue.</summary>
public enum ClearanceOutcome
{
    Cleared,
    ConditionallyCleared,
    Denied,
    Blocked,
    Expired,
}

/// <summary>What kind of thing a lattice node is. Drives its shape in the map.</summary>
public enum NodeKind
{
    System,
    Feature,
    Action,
    Endpoint,
    DataState,
    Algorithm,
    Fault,
    Evidence,
}

/// <summary>A registered application, service, algorithm or system under examination.</summary>
public sealed record Target
{
    public required GhoztId Id { get; init; }

    public required string Name { get; init; }

    public required string Kind { get; init; }

    public required TargetEnvironment Environment { get; init; }

    public required AccessClass Access { get; init; }

    public string Owner { get; init; } = "";

    public string RepositoryPath { get; init; } = "";

    public string LaunchCommand { get; init; } = "";

    public string BaseAddress { get; init; } = "";

    /// <summary>The build currently registered against this target, e.g. a commit or a version.</summary>
    public string BuildLabel { get; init; } = "";

    /// <summary>Who authorised what, against which environment, for how long. Never inferred.</summary>
    public string AuthorityNote { get; init; } = "";

    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset? LastSweepAt { get; init; }

    /// <summary>Whether the last capability probe found the target reachable.</summary>
    public bool Reachable { get; init; }

    public string Notes { get; init; } = "";
}

/// <summary>A governed examination plan and its run.</summary>
public sealed record Sweep
{
    public required GhoztId Id { get; init; }

    public required GhoztId TargetId { get; init; }

    public required string Name { get; init; }

    /// <summary>Smoke, changed-area, full sweep, deep forensic, and so on.</summary>
    public required string Mode { get; init; }

    public RunState State { get; init; } = RunState.Draft;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Budgets. A sweep stops when scope, budget, policy or evidence stops it.</summary>
    public int MaxStates { get; init; } = 200;

    public int MaxSeconds { get; init; } = 300;

    public int MaxDepth { get; init; } = 8;

    public bool AllowDestructive { get; init; }

    public int StatesVisited { get; init; }

    public int DeviationsFound { get; init; }

    public int EvidenceCaptured { get; init; }

    /// <summary>Why the run ended, in the operator's terms. Empty while running.</summary>
    public string Outcome { get; init; } = "";
}

/// <summary>A node in the State Lattice: a real feature, action, endpoint, state or artefact.</summary>
public sealed record LatticeNode
{
    public required string Id { get; init; }

    public required GhoztId TargetId { get; init; }

    public required string Label { get; init; }

    public required NodeKind Kind { get; init; }

    /// <summary>Observation status. Untested is an absence, not a failure.</summary>
    public required string State { get; init; }

    /// <summary>How this node came to be known: observed, computed, inferred, declared or simulated.</summary>
    public required string Provenance { get; init; }

    public string ParentId { get; init; } = "";

    public double X { get; init; }

    public double Y { get; init; }

    public int VisitCount { get; init; }

    public string Detail { get; init; } = "";

    /// <summary>Evidence bound to this node. A node with none cannot claim Verified.</summary>
    public int EvidenceCount { get; init; }

    public DateTimeOffset? LastSeenAt { get; init; }
}

/// <summary>A directed relationship between two lattice nodes.</summary>
public sealed record LatticeEdge
{
    public required string FromId { get; init; }

    public required string ToId { get; init; }

    public required GhoztId TargetId { get; init; }

    public string Label { get; init; } = "";

    /// <summary>Observed, expected, or counterfactual. A dotted violet branch is never observed.</summary>
    public string Relationship { get; init; } = "observed";

    public int TraversalCount { get; init; }
}

/// <summary>A confirmed or suspected deviation from expected behaviour.</summary>
public sealed record Finding
{
    public required GhoztId Id { get; init; }

    public required GhoztId TargetId { get; init; }

    public GhoztId? SweepId { get; init; }

    public required string Title { get; init; }

    public required Severity Severity { get; init; }

    public FindingMaturity Maturity { get; init; } = FindingMaturity.New;

    /// <summary>What was seen.</summary>
    public string Actual { get; init; } = "";

    /// <summary>What the governing contract or oracle required.</summary>
    public string Expected { get; init; } = "";

    /// <summary>A normalised signature, used to cluster equivalent failures rather than duplicate them.</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>Attempts and successes, from which reproducibility is computed rather than asserted.</summary>
    public int ReproAttempts { get; init; }

    public int ReproSuccesses { get; init; }

    public string NodeId { get; init; } = "";

    public string Owner { get; init; } = "";

    public DateTimeOffset FirstSeenAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset LastSeenAt { get; init; } = DateTimeOffset.Now;

    public int EvidenceCount { get; init; }

    /// <summary>
    /// The share of reproduction attempts that reproduced the fault.
    /// </summary>
    /// <remarks>
    /// Returns null rather than zero when nothing has been attempted. Zero would read as "never
    /// reproduces", which is a claim; no attempts is an absence of evidence, and the two must not
    /// look alike on a triage queue.
    /// </remarks>
    public double? Reproducibility => ReproAttempts == 0 ? null : (double)ReproSuccesses / ReproAttempts;

    /// <summary>
    /// Bayesian Laplace-smoothed reproducibility estimate under a Beta(1,1) uniform prior: (k + 1) / (n + 2).
    /// </summary>
    public double BayesianReproducibility => StateSpaceMath.LaplaceSmoothedProbability(ReproSuccesses, ReproAttempts);

    /// <summary>
    /// 95% Wilson score confidence interval for the true defect reproducibility rate.
    /// </summary>
    public ConfidenceInterval WilsonConfidence => StateSpaceMath.WilsonScoreInterval(ReproSuccesses, ReproAttempts);

    /// <summary>
    /// A Phantom is a finding that has been seen but does not reliably return, validated through Wilson score intervals.
    /// </summary>
    public bool IsPhantom => StateSpaceMath.IsPhantomDefect(ReproSuccesses, ReproAttempts);

    /// <summary>Optional reference to a parent finding if this finding is a variant (Patent Table 47).</summary>
    public GhoztId? VariantOf { get; init; }

    /// <summary>Optional reference to a related candidate finding (Patent Table 47).</summary>
    public GhoztId? RelatedCandidateId { get; init; }

    /// <summary>Optional semantic difference category (Patent Table 46).</summary>
    public SemanticCategory? SemanticCategory { get; init; }
}

/// <summary>A captured artefact, bound to the finding or event it proves something about.</summary>
public sealed record Evidence
{
    public required GhoztId Id { get; init; }

    public required GhoztId TargetId { get; init; }

    public required string Kind { get; init; }

    public required string Summary { get; init; }

    /// <summary>SHA-256 over the payload. The seal is meaningless without it.</summary>
    public required string Sha256 { get; init; }

    public long SizeBytes { get; init; }

    public string Payload { get; init; } = "";

    public string SourceAdapter { get; init; } = "";

    public GhoztId? FindingId { get; init; }

    public GhoztId? SweepId { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Whether a reviewer has verified the artefact and its chain. Gold only when true.</summary>
    public bool Sealed { get; init; }

    public string SealedBy { get; init; } = "";

    public bool Redacted { get; init; }
}

/// <summary>One line of the black-box stream: a timestamped material observation.</summary>
public sealed record LedgerEvent
{
    public required GhoztId Id { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>A dotted code such as UI.ACTION or HTTP.RESPONSE. Filterable, and never only a colour.</summary>
    public required string Code { get; init; }

    public required string Message { get; init; }

    public Severity Severity { get; init; } = Severity.Info;

    public GhoztId? TargetId { get; init; }

    public GhoztId? SweepId { get; init; }

    /// <summary>The event, finding or evidence this line points at, if any.</summary>
    public string RelatedId { get; init; } = "";

    /// <summary>Who or what issued it: an operator, an agent, an adapter.</summary>
    public string Actor { get; init; } = "";

    /// <summary>The structured payload, expandable under the human-scannable summary.</summary>
    public string Detail { get; init; } = "";
}

/// <summary>One gate in a release policy, and its current result.</summary>
public sealed record ClearanceGate
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Requirement { get; init; }

    /// <summary>The policy document this gate comes from. A gate with no source is not a gate.</summary>
    public required string PolicySource { get; init; }

    public bool Mandatory { get; init; } = true;

    public string Result { get; init; } = "Not evaluated";

    public bool Passed { get; init; }

    public string EvidenceNote { get; init; } = "";

    public string WaiverReason { get; init; } = "";

    public string WaivedBy { get; init; } = "";
}

/// <summary>
/// An immutable, signed authority grant issued to an operator or agent against a target (Patent Table 10, Table 70).
/// </summary>
public sealed record AuthorityGrant
{
    public required GhoztId GrantId { get; init; }

    public required GhoztId TargetId { get; init; }

    public required string Actor { get; init; }

    public ExecutionIsolationLevel MaxLevel { get; init; } = ExecutionIsolationLevel.L1_ControlledInteraction;

    public IReadOnlyList<string> AllowedActionClasses { get; init; } = Array.Empty<string>();

    public DataSensitivityTier AllowedDataSensitivity { get; init; } = DataSensitivityTier.Internal;

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public string Approver { get; init; } = "";

    public string ScopeJustification { get; init; } = "";

    public string AuditSignature { get; init; } = "";

    public bool IsExpired(DateTimeOffset? now = null) => (now ?? DateTimeOffset.Now) > ExpiresAt;
}

/// <summary>
/// Result of an issue tracker backlog synchronization operation (Patent Table 75).
/// </summary>
public sealed record IssueSyncResult
{
    public required string Provider { get; init; } // GitHub, Jira, GitLab

    public required string IssueIdentifier { get; init; }

    public required string Title { get; init; }

    public required string PayloadMarkdown { get; init; }

    public required string FingerprintToken { get; init; }

    public bool IsUpdate { get; init; }

    public int RecurrenceCount { get; init; }

    public DateTimeOffset SyncedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Result of an observability ingestion operation (Patent Table 75).
/// </summary>
public sealed record ObservabilityIngestResult
{
    public required int IngestedCount { get; init; }

    public required int RedactedCount { get; init; }

    public required int MalformedCount { get; init; }

    public required TimeSpan Duration { get; init; }

    public required string BatchDigest { get; init; }
}

/// <summary>
/// Canonical security validation areas (Patent Table 36).
/// </summary>
public enum SecurityValidationArea
{
    IdentityAndSession,
    Authorisation,
    InputHandling,
    SecretsAndCredentials,
    TransportAndTrust,
    ClientStorage,
    AbuseAndRateControl,
    SecurityObservability,
}

/// <summary>
/// Canonical compatibility matrix validation axes (Patent Table 38).
/// </summary>
public enum CompatibilityAxis
{
    OperatingSystem,
    DisplayAndScaling,
    LocaleAndCulture,
    RuntimeAndDependencies,
    UpgradeAndMigration,
}

/// <summary>
/// Result for an individual security area validation assay (Patent Table 36).
/// </summary>
public sealed record SecurityAuditAreaResult
{
    public required SecurityValidationArea Area { get; init; }
    public required string Title { get; init; }
    public required bool Passed { get; init; }
    public required Severity Severity { get; init; }
    public required string Details { get; init; }
    public required string DiagnosticCode { get; init; }
    public string RemediativeGuidance { get; init; } = "";
}

/// <summary>
/// Complete Security Audit Report evaluating all 8 Table 36 areas.
/// </summary>
public sealed record SecurityAuditReport
{
    public required GhoztId TargetId { get; init; }
    public required bool Passed { get; init; }
    public required int TotalAreasChecked { get; init; }
    public required int PassedAreas { get; init; }
    public required int FailedAreas { get; init; }
    public required IReadOnlyList<SecurityAuditAreaResult> AreaResults { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.Now;
    public required string AuditSignature { get; init; }
}

/// <summary>
/// Result for an individual compatibility axis assay (Patent Table 38).
/// </summary>
public sealed record CompatibilityAxisResult
{
    public required CompatibilityAxis Axis { get; init; }
    public required string Name { get; init; }
    public required bool Passed { get; init; }
    public required string Assessment { get; init; }
    public required IReadOnlyDictionary<string, string> Diagnostics { get; init; }
}

/// <summary>
/// Complete Compatibility Matrix Report evaluating all 5 Table 38 axes.
/// </summary>
public sealed record CompatibilityMatrixReport
{
    public required GhoztId TargetId { get; init; }
    public required bool Passed { get; init; }
    public required int TotalAxesAssayed { get; init; }
    public required int PassedAxes { get; init; }
    public required IReadOnlyList<CompatibilityAxisResult> AxisResults { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.Now;
    public required string MatrixDigest { get; init; }
}

