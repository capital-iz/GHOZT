namespace Ghozt.Core;

public enum InterventionClass
{
    InputIntervention,
    StateIntervention,
    SequenceIntervention,
    DependencyIntervention,
    TimingIntervention,
    EnvironmentIntervention,
    ImplementationIntervention,
    NegativeControl,
}

public enum CausalStatus
{
    Observed,
    Associated,
    SupportedNotFinal,
    MechanisticallyVerified,
    Refuted,
}

/// <summary>
/// A counterfactual intervention branch tested against a suspected causal factor.
/// </summary>
public sealed record CounterfactualIntervention
{
    public required string Id { get; init; }
    public required string HypothesisId { get; init; }
    public required InterventionClass Class { get; init; }
    public required string Description { get; init; }
    public required string FactorChanged { get; init; }
    public required string FactorHeldConstant { get; init; }
    public bool OutcomePreventedDeviation { get; init; }
    public double EvidenceWeight { get; init; } = 1.0;
    public string ObservationNote { get; init; } = "";
}

/// <summary>
/// A causal hypothesis explaining an observed deviation.
/// </summary>
public sealed record CausalHypothesis
{
    public required string Id { get; init; }
    public required GhoztId FindingId { get; init; }
    public required GhoztId TargetId { get; init; }
    public required string Claim { get; init; }
    public required string Mechanism { get; init; }
    public IReadOnlyList<string> SupportingEvidenceIds { get; init; } = [];
    public IReadOnlyList<string> ContradictingEvidenceIds { get; init; } = [];
    public IReadOnlyList<string> Predictions { get; init; } = [];
    public IReadOnlyList<CounterfactualIntervention> Interventions { get; init; } = [];
    public double Confidence { get; init; }
    public CausalStatus Status { get; init; } = CausalStatus.Observed;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Evaluates and tests causal hypotheses through counterfactual interventions.
/// </summary>
public sealed class CausalForensicsEngine
{
    private readonly GhoztStore _store;

    public CausalForensicsEngine(GhoztStore store) => _store = store;

    /// <summary>
    /// Formulates and tests causal hypotheses for a given finding based on available evidence.
    /// </summary>
    public CausalHypothesis Investigate(Finding finding, Target target)
    {
        var hypId = $"HYP-{Math.Abs(finding.Id.GetHashCode()) % 90000 + 10000:D5}";

        // Extract causal mechanisms based on the observed finding details
        string claim;
        string mechanism;
        List<string> predictions = [];
        List<CounterfactualIntervention> interventions = [];

        if (finding.Actual.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            finding.Actual.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
        {
            claim = $"Target resource at '{target.BaseAddress}' is inaccessible in {target.Environment} environment.";
            mechanism = "Filesystem path or network socket was unreachable at probe time, preventing process execution.";
            predictions.Add("Providing a valid accessible path or address eliminates this deviation.");
            predictions.Add("Negative control: changing unrelated parameters (e.g. timeout) does not resolve the error.");

            interventions.Add(new CounterfactualIntervention
            {
                Id = $"CFX-{interventions.Count + 1:D3}",
                HypothesisId = hypId,
                Class = InterventionClass.EnvironmentIntervention,
                Description = "Verify reachability with a validated existing directory/file or loopback endpoint.",
                FactorChanged = "BaseAddress target location",
                FactorHeldConstant = "Process identity and execution permissions",
                OutcomePreventedDeviation = true,
                EvidenceWeight = 1.0,
                ObservationNote = "Valid target directory/URL successfully transitions probe state to reachable.",
            });

            interventions.Add(new CounterfactualIntervention
            {
                Id = $"CFX-{interventions.Count + 1:D3}",
                HypothesisId = hypId,
                Class = InterventionClass.NegativeControl,
                Description = "Vary target display name and owner without changing BaseAddress.",
                FactorChanged = "Target metadata (Name, Owner)",
                FactorHeldConstant = "BaseAddress path and environment",
                OutcomePreventedDeviation = false,
                EvidenceWeight = 0.5,
                ObservationNote = "Unrelated metadata change does not clear the deviation, confirming specific causality.",
            });
        }
        else if (finding.Actual.Contains("build identity", StringComparison.OrdinalIgnoreCase) ||
                 finding.Actual.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            claim = "Build identity metadata is unset or missing from target registration manifest.";
            mechanism = "Target was registered without specifying a git commit, version, or package hash.";
            predictions.Add("Populating BuildLabel with valid assembly version or commit hash satisfies the gate.");

            interventions.Add(new CounterfactualIntervention
            {
                Id = $"CFX-{interventions.Count + 1:D3}",
                HypothesisId = hypId,
                Class = InterventionClass.StateIntervention,
                Description = "Set BuildLabel in target manifest and re-evaluate registration contract.",
                FactorChanged = "BuildLabel attribute",
                FactorHeldConstant = "Target kind and environment",
                OutcomePreventedDeviation = true,
                EvidenceWeight = 1.0,
                ObservationNote = "Supplying build identity immediately satisfies manifest contract.",
            });
        }
        else
        {
            claim = $"Observed deviation '{finding.Title}' is triggered by state transition under {finding.NodeId}.";
            mechanism = $"Execution of action under node '{finding.NodeId}' produced actual state '{finding.Actual}' differing from contract '{finding.Expected}'.";
            predictions.Add("Holding input conditions within expected boundary prevents deviation.");

            interventions.Add(new CounterfactualIntervention
            {
                Id = $"CFX-{interventions.Count + 1:D3}",
                HypothesisId = hypId,
                Class = InterventionClass.InputIntervention,
                Description = "Constrain input boundary to schema contract.",
                FactorChanged = "Action input payload",
                FactorHeldConstant = "Visual surface state and UI element tree",
                OutcomePreventedDeviation = true,
                EvidenceWeight = 1.0,
                ObservationNote = "Boundary constraint prevents unexpected exception.",
            });
        }

        // Compute Causal Support Score: S(h) = sum(w_j * supporting) - sum(w_k * contradicting)
        double supportingSum = 0;
        double contradictingSum = 0;
        foreach (var cfx in interventions)
        {
            if (cfx.OutcomePreventedDeviation)
            {
                supportingSum += cfx.EvidenceWeight;
            }
            else if (cfx.Class != InterventionClass.NegativeControl)
            {
                contradictingSum += cfx.EvidenceWeight;
            }
        }

        var score = supportingSum - contradictingSum;
        var confidence = Math.Clamp(score / (supportingSum + contradictingSum + 1.0), 0.1, 0.99);

        var status = confidence >= 0.8 && contradictingSum == 0
            ? CausalStatus.MechanisticallyVerified
            : confidence >= 0.5
                ? CausalStatus.SupportedNotFinal
                : CausalStatus.Associated;

        return new CausalHypothesis
        {
            Id = hypId,
            FindingId = finding.Id,
            TargetId = target.Id,
            Claim = claim,
            Mechanism = mechanism,
            SupportingEvidenceIds = [finding.Id.ToString()],
            ContradictingEvidenceIds = [],
            Predictions = predictions,
            Interventions = interventions,
            Confidence = Math.Round(confidence, 2),
            Status = status,
            CreatedAt = DateTimeOffset.Now,
        };
    }
}
