using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Ghozt.Core;

/// <summary>
/// 7-Feature Payload for Composite Fault Fingerprinting (Patent §26, Table 46).
/// </summary>
public sealed record FaultFingerprintPayload
{
    public required string ExceptionSignature { get; init; }
    public SemanticCategory SemanticCategory { get; init; } = SemanticCategory.Other;
    public string Expected { get; init; } = "";
    public string Actual { get; init; } = "";
    public string NodeId { get; init; } = "";
    public string EdgeId { get; init; } = "";
    public string WorkflowPosition { get; init; } = "";
    public IReadOnlyList<string> ActionSequence { get; init; } = Array.Empty<string>();
    public string StateDelta { get; init; } = "";
    public string DependencyTrace { get; init; } = "";
    public TargetEnvironment Environment { get; init; } = TargetEnvironment.Local;
    public string OsFamily { get; init; } = "Windows";
    public string Architecture { get; init; } = "x64";
}

/// <summary>
/// Formal duplicate decision record (Patent Table 47).
/// </summary>
public sealed record DuplicateDecision
{
    public required DuplicateOutcome Outcome { get; init; }
    public Finding? ClusteredWith { get; init; }
    public required string Rationale { get; init; }
    public double Confidence { get; init; } = 1.0;
    public string CompositeFingerprint { get; init; } = "";
}

/// <summary>
/// Evidence context submitted when requesting finding maturity progression (Patent Table 45).
/// </summary>
public sealed record TransitionEvidence
{
    public bool CausalHypothesisSupported { get; init; }
    public double CausalConfidence { get; init; }
    public bool FixBriefApproved { get; init; }
    public string FixBriefId { get; init; } = "";
    public bool RepairApplied { get; init; }
    public bool RepairVerified { get; init; }
    public int SealedEvidenceCount { get; init; }
    public string WaivedBy { get; init; } = "";
    public string WaiverReason { get; init; } = "";
    public DateTimeOffset? WaivedUntil { get; init; }
    public string RejectionReason { get; init; } = "";
}

/// <summary>
/// Patent §25 & §26: Composite Fault Fingerprint, Duplicate Decision & Finding Maturity Lifecycle Engine.
/// </summary>
public static class FaultFingerprintEngine
{
    private static readonly Regex MemoryAddressRegex = new(@"\b0x[0-9a-fA-F]{4,16}\b", RegexOptions.Compiled);
    private static readonly Regex GuidRegex = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex TimestampRegex = new(@"\b\d{4}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?\b", RegexOptions.Compiled);
    private static readonly Regex ProcessIdRegex = new(@"\b(pid|processid|process\s*id)\s*[:=]?\s*\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LocalPortRegex = new(@"(127\.0\.0\.1|localhost):\d{4,5}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LineNumberRegex = new(@":\s*line\s+\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Strips volatile runtime execution tokens from stack traces and error messages.
    /// </summary>
    public static string NormalizeExceptionSignature(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var text = raw;
        text = MemoryAddressRegex.Replace(text, "<ADDR>");
        text = GuidRegex.Replace(text, "<GUID>");
        text = TimestampRegex.Replace(text, "<TIMESTAMP>");
        text = ProcessIdRegex.Replace(text, "<PID>");
        text = LocalPortRegex.Replace(text, "$1:<PORT>");
        text = LineNumberRegex.Replace(text, ":line <N>");
        return text.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Computes the 7-feature composite fault fingerprint (Patent Table 46).
    /// </summary>
    public static string ComputeCompositeFingerprint(FaultFingerprintPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Feature 1: Normalise(exception_signature)
        var f1 = NormalizeExceptionSignature(payload.ExceptionSignature);

        // Feature 2: SemanticDiff(expected, actual)
        var f2 = $"cat={payload.SemanticCategory}|exp={NormalizeExceptionSignature(payload.Expected)}|act={NormalizeExceptionSignature(payload.Actual)}";

        // Feature 3: LatticeContext(node, edge, workflow_position)
        var f3 = $"node={(payload.NodeId ?? "").Trim().ToLowerInvariant()}|edge={(payload.EdgeId ?? "").Trim().ToLowerInvariant()}|phase={(payload.WorkflowPosition ?? "").Trim().ToLowerInvariant()}";

        // Feature 4: SequenceSketch(minimal_actions)
        var actionTokens = payload.ActionSequence.Select(a => a.Trim().ToLowerInvariant());
        var f4 = $"seq={string.Join(">", actionTokens)}";

        // Feature 5: StateDeltaSketch(before, after)
        var f5 = $"delta={NormalizeExceptionSignature(payload.StateDelta)}";

        // Feature 6: DependencyTraceSketch(trace)
        var f6 = $"dep={NormalizeExceptionSignature(payload.DependencyTrace)}";

        // Feature 7: EnvironmentClass(environment)
        var f7 = $"env={payload.Environment}|os={payload.OsFamily.Trim().ToLowerInvariant()}|arch={payload.Architecture.Trim().ToLowerInvariant()}";

        var composite = string.Join('\u001F', f1, f2, f3, f4, f5, f6, f7);
        var hash = Hashing.Sha256(composite);
        return $"FP-{hash[..12].ToUpperInvariant()}";
    }

    /// <summary>
    /// Classifies an observed deviation against existing target findings (Patent Table 47).
    /// </summary>
    public static DuplicateDecision ClassifyDuplicate(
        Finding candidate,
        FaultFingerprintPayload payload,
        IReadOnlyList<Finding> existingFindings,
        Target target)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(existingFindings);

        var fp = string.IsNullOrWhiteSpace(candidate.Fingerprint)
            ? ComputeCompositeFingerprint(payload)
            : candidate.Fingerprint;

        // 1. Same fault: Exact composite fingerprint match
        var exactMatch = existingFindings.FirstOrDefault(f => f.Fingerprint == fp);
        if (exactMatch is not null)
        {
            return new DuplicateDecision
            {
                Outcome = DuplicateOutcome.SameFault,
                ClusteredWith = exactMatch,
                Rationale = $"Exact composite fingerprint match ({fp}) observed in prior finding {exactMatch.Id}.",
                Confidence = 1.0,
                CompositeFingerprint = fp,
            };
        }

        // 2. Variant: Same underlying cause/node, but materially different environment, input values, or downstream impact
        var normException = NormalizeExceptionSignature(payload.ExceptionSignature);
        var variantMatch = existingFindings.FirstOrDefault(f =>
            !string.IsNullOrWhiteSpace(f.NodeId) &&
            f.NodeId.Equals(candidate.NodeId, StringComparison.OrdinalIgnoreCase) &&
            (f.SemanticCategory == candidate.SemanticCategory || candidate.SemanticCategory == SemanticCategory.Other));

        if (variantMatch is not null)
        {
            var isEnvDiff = target.Environment != TargetEnvironment.Local;
            return new DuplicateDecision
            {
                Outcome = DuplicateOutcome.Variant,
                ClusteredWith = variantMatch,
                Rationale = $"Same architectural node ({candidate.NodeId}) and semantic category with environmental/input parameter drift compared to {variantMatch.Id}.",
                Confidence = 0.85,
                CompositeFingerprint = fp,
            };
        }

        // 3. Related: Similar symptom or dependency path, but cause not proven equivalent
        var relatedMatch = existingFindings.FirstOrDefault(f =>
            (!string.IsNullOrWhiteSpace(payload.DependencyTrace) && f.Actual.Contains(payload.DependencyTrace, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(candidate.Title) && f.Title.Equals(candidate.Title, StringComparison.OrdinalIgnoreCase)));

        if (relatedMatch is not null)
        {
            return new DuplicateDecision
            {
                Outcome = DuplicateOutcome.Related,
                ClusteredWith = relatedMatch,
                Rationale = $"Shared dependency path or symptom signature with finding {relatedMatch.Id}; causal identity unverified.",
                Confidence = 0.60,
                CompositeFingerprint = fp,
            };
        }

        // 4. Independent: Distinct mechanism
        return new DuplicateDecision
        {
            Outcome = DuplicateOutcome.Independent,
            ClusteredWith = null,
            Rationale = "Distinct failure signature and causal path across all evaluated targets.",
            Confidence = 1.0,
            CompositeFingerprint = fp,
        };
    }

    /// <summary>
    /// Evaluates whether a finding satisfies preconditions to advance to target maturity (Patent Table 45).
    /// </summary>
    public static bool CanTransition(
        Finding finding,
        FindingMaturity targetMaturity,
        TransitionEvidence evidence,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(evidence);

        switch (targetMaturity)
        {
            case FindingMaturity.New:
                failureReason = "";
                return true;

            case FindingMaturity.Phantom:
                if (finding.ReproAttempts < 2)
                {
                    failureReason = "Phantom maturity requires at least 2 reproduction attempts.";
                    return false;
                }
                if (finding.Reproducibility is not null && finding.Reproducibility.Value >= 0.50)
                {
                    failureReason = $"Reproducibility rate ({finding.Reproducibility:P0}) is not intermittent (must be < 50%).";
                    return false;
                }
                failureReason = "";
                return true;

            case FindingMaturity.Confirming:
                failureReason = "";
                return true;

            case FindingMaturity.Reproduced:
                if (finding.ReproSuccesses < 1)
                {
                    failureReason = "Reproduced maturity requires at least 1 successful reproduction.";
                    return false;
                }
                failureReason = "";
                return true;

            case FindingMaturity.CauseSupported:
                if (!evidence.CausalHypothesisSupported)
                {
                    failureReason = "CauseSupported maturity requires empirical counterfactual causal support.";
                    return false;
                }
                if (evidence.CausalConfidence < 0.70)
                {
                    failureReason = $"Causal hypothesis confidence ({evidence.CausalConfidence:P0}) is below the required 70% threshold.";
                    return false;
                }
                failureReason = "";
                return true;

            case FindingMaturity.FixReady:
                if (!evidence.FixBriefApproved)
                {
                    failureReason = "FixReady maturity requires an approved bounded Fix Brief contract.";
                    return false;
                }
                failureReason = "";
                return true;

            case FindingMaturity.ReTest:
                if (!evidence.RepairApplied)
                {
                    failureReason = "ReTest maturity requires the bounded repair to be applied.";
                    return false;
                }
                failureReason = "";
                return true;

            case FindingMaturity.Verified:
                if (!evidence.RepairVerified)
                {
                    failureReason = "Verified maturity requires passing post-repair verification.";
                    return false;
                }
                if (evidence.SealedEvidenceCount < 1)
                {
                    failureReason = "Verified maturity requires at least 1 sealed evidence artifact.";
                    return false;
                }
                failureReason = "";
                return true;

            case FindingMaturity.Waived:
                if (string.IsNullOrWhiteSpace(evidence.WaivedBy))
                {
                    failureReason = "Waived maturity requires a named approving authority.";
                    return false;
                }
                if (evidence.WaivedUntil is null || evidence.WaivedUntil.Value <= DateTimeOffset.Now)
                {
                    failureReason = "Waived maturity requires a valid future expiration timestamp.";
                    return false;
                }
                failureReason = "";
                return true;

            case FindingMaturity.Rejected:
                if (string.IsNullOrWhiteSpace(evidence.RejectionReason))
                {
                    failureReason = "Rejected maturity requires an explicit rejection rationale.";
                    return false;
                }
                failureReason = "";
                return true;

            default:
                failureReason = "Unknown maturity state.";
                return false;
        }
    }

    /// <summary>
    /// Executes a formal finding maturity transition and records immutable ledger events (Patent Table 45).
    /// </summary>
    public static Finding AdvanceMaturity(
        Finding finding,
        FindingMaturity nextMaturity,
        TransitionEvidence evidence,
        GhoztStore store,
        Target target,
        Sweep? sweep = null)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(target);

        if (!CanTransition(finding, nextMaturity, evidence, out var reason))
        {
            throw new InvalidOperationException($"Cannot advance finding {finding.Id} to {nextMaturity}: {reason}");
        }

        var updated = finding with
        {
            Maturity = nextMaturity,
            LastSeenAt = DateTimeOffset.Now,
        };

        store.Save(updated);

        var eventCode = nextMaturity switch
        {
            FindingMaturity.Waived => "FLT.WAIVED",
            FindingMaturity.Rejected => "FLT.REJECTED",
            FindingMaturity.Verified => "FLT.VERIFIED",
            FindingMaturity.Phantom => "FLT.PHANTOM",
            _ => "FLT.MATURITY.ADVANCE",
        };

        store.Append(new LedgerEvent
        {
            Id = store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = eventCode,
            Message = $"Finding {updated.Id} transitioned to {nextMaturity}",
            Severity = updated.Severity,
            TargetId = target.Id,
            SweepId = sweep?.Id,
            RelatedId = updated.Id.ToString(),
            Actor = "FindingMaturityEngine",
            Detail = $"Transitioned from {finding.Maturity} to {nextMaturity}. Evidence: Causal={evidence.CausalHypothesisSupported}, FixBrief={evidence.FixBriefApproved}, Verified={evidence.RepairVerified}, SealedEvidence={evidence.SealedEvidenceCount}.",
        });

        return updated;
    }
}
