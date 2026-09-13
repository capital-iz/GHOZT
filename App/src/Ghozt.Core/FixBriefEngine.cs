namespace Ghozt.Core;

public enum FixBriefStatus
{
    Proposed,
    Approved,
    Verified,
    Rejected,
}

/// <summary>
/// A formal, bounded implementation contract for repairing a confirmed defect.
/// </summary>
public sealed record FixBrief
{
    public required string Id { get; init; }
    public required GhoztId FindingId { get; init; }
    public required GhoztId TargetId { get; init; }
    public required string Title { get; init; }
    public required string RootMechanism { get; init; }
    public required IReadOnlyList<string> AllowedScope { get; init; }
    public required IReadOnlyList<string> ForbiddenScope { get; init; }
    public required IReadOnlyList<string> MinimalReproSequence { get; init; }
    public required IReadOnlyList<string> RequiredEvidence { get; init; }
    public required string RegressionGuardSpecification { get; init; }
    public required IReadOnlyList<string> RejectionConditions { get; init; }
    public FixBriefStatus Status { get; init; } = FixBriefStatus.Proposed;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string CreatedBy { get; init; } = "Ghozt.FixBriefEngine";

    public string ToMarkdown()
    {
        return $"""
            # Fix Brief: {Id}
            **Target ID:** {TargetId}  
            **Finding ID:** {FindingId}  
            **Status:** {Status.ToString().ToUpperInvariant()}  
            **Created At:** {CreatedAt:O}  
            **Author:** {CreatedBy}  

            ## 1. Title & Objective
            {Title}

            ## 2. Root Causal Mechanism
            {RootMechanism}

            ## 3. Bounded Scope
            ### Allowed Modification Scope
            {string.Join("\n", AllowedScope.Select(s => $"- `{s}`"))}

            ### Forbidden Boundary (Zero Mutation Permitted)
            {string.Join("\n", ForbiddenScope.Select(s => $"- `{s}`"))}

            ## 4. Minimal Reproduction Sequence
            {string.Join("\n", MinimalReproSequence.Select((s, i) => $"{i + 1}. {s}"))}

            ## 5. Required Verification Evidence
            {string.Join("\n", RequiredEvidence.Select(e => $"- {e}"))}

            ## 6. Regression Guard Contract
            `{RegressionGuardSpecification}`

            ## 7. Mandatory Repair Rejection Conditions
            {string.Join("\n", RejectionConditions.Select((c, i) => $"{i + 1}. {c}"))}
            """;
    }
}

/// <summary>
/// Converts confirmed deviations and causal hypotheses into formal Fix Briefs.
/// </summary>
public sealed class FixBriefEngine
{
    private static readonly string[] StandardRejectionConditions =
    [
        "The proposed change exceeds allowed scope without explicit architectural re-approval.",
        "The plan addresses only an observable symptom while the supported root mechanism remains uncorrected.",
        "Required verification tests cannot execute or evidence capture is unavailable.",
        "The change weakens an oracle, deletes a guard, or suppresses an error without justification.",
        "A passing result depends on corrupted test data, disabled features, or artificially excluded code paths.",
        "New high-severity regressions or unresolved counterfactual contradictions appear during verification.",
        "The agent claims success without an executed and cryptographically evidenced re-test pass.",
    ];

    private readonly GhoztStore _store;

    public FixBriefEngine(GhoztStore store) => _store = store;

    /// <summary>
    /// Generates a formal Fix Brief from a confirmed finding and its causal hypothesis.
    /// </summary>
    public FixBrief Generate(Finding finding, Target target, CausalHypothesis? hypothesis = null)
    {
        var fxbId = $"FXB-{Math.Abs(finding.Id.GetHashCode()) % 90000 + 10000:D5}";

        var allowedScope = new List<string>();
        if (!string.IsNullOrWhiteSpace(target.RepositoryPath))
        {
            allowedScope.Add(target.RepositoryPath);
        }
        else if (!string.IsNullOrWhiteSpace(target.BaseAddress))
        {
            allowedScope.Add(target.BaseAddress);
        }
        else
        {
            allowedScope.Add("Target source repository and configuration manifest");
        }

        var forbiddenScope = new List<string>
        {
            "Central security/authorization gates and credential providers",
            "Production database connection strings and migration tables",
            "Ghozt evidence store, audit ledger, and hash-chain seals",
        };

        var reproSequence = new List<string>
        {
            $"Launch or connect to target {target.Name} ({target.Id}).",
            $"Navigate to state/surface associated with node '{finding.NodeId}'.",
            $"Execute trigger action reproducing '{finding.Title}'.",
            $"Observe actual state: \"{finding.Actual}\" vs expected: \"{finding.Expected}\".",
        };

        var requiredEvidence = new List<string>
        {
            "Black-box execution stream with zero critical/high error codes",
            "Post-repair visual capture or HTTP response matching expected contract",
            "Automated regression guard execution pass record",
        };

        var mechanism = hypothesis?.Mechanism ??
            $"Execution of action under node '{finding.NodeId}' produced actual state '{finding.Actual}', failing expected contract '{finding.Expected}'.";

        return new FixBrief
        {
            Id = fxbId,
            FindingId = finding.Id,
            TargetId = target.Id,
            Title = $"Bounded Repair Contract for {finding.Id}: {finding.Title}",
            RootMechanism = mechanism,
            AllowedScope = allowedScope,
            ForbiddenScope = forbiddenScope,
            MinimalReproSequence = reproSequence,
            RequiredEvidence = requiredEvidence,
            RegressionGuardSpecification = $"GRD-RPR: Fingerprint {finding.Fingerprint} must evaluate to PASS.",
            RejectionConditions = StandardRejectionConditions,
            Status = FixBriefStatus.Proposed,
            CreatedAt = DateTimeOffset.Now,
            CreatedBy = Environment.UserName,
        };
    }
}
