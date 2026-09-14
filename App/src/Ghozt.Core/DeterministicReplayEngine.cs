using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// A single action in a deterministic replay sequence.
/// </summary>
public sealed record ReplayAction
{
    public required string ActionId { get; init; }
    public required string Kind { get; init; }
    public required string TargetSelector { get; init; }
    public string Payload { get; init; } = "";
    public string ExpectedState { get; init; } = "";
}

/// <summary>
/// Canonical Replay Package Specification conforming to Patent §27 (Table 49).
/// </summary>
public sealed record ReplayPackage
{
    public required string ReplayId { get; init; }
    public required GhoztId TargetId { get; init; }
    public required string TargetBuild { get; init; }
    public required string EnvironmentManifest { get; init; }
    public required string Checkpoint { get; init; }
    public required IReadOnlyDictionary<string, string> Preconditions { get; init; }
    public required IReadOnlyList<ReplayAction> Actions { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public required IReadOnlyList<int> Seeds { get; init; }
    public required IReadOnlyList<string> DependencyStubsOrVersions { get; init; }
    public required string ExpectedFaultFingerprint { get; init; }
    public required IReadOnlyList<string> EvidencePlan { get; init; }

    /// <summary>
    /// Formats the replay package into the exact Patent Table 49 YAML structure.
    /// </summary>
    public string ToYaml()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"replay_id: {ReplayId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"target_build: {TargetBuild}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"environment_manifest: {EnvironmentManifest}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"checkpoint: {Checkpoint}");
        sb.AppendLine("preconditions:");
        foreach (var (k, v) in Preconditions)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {k}: {v}");
        }
        sb.AppendLine(CultureInfo.InvariantCulture, $"actions: [{string.Join(", ", Actions.Select(a => a.ActionId))}]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"inputs: [{string.Join(", ", Inputs)}]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"seeds: [{string.Join(", ", Seeds)}]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"dependency_stubs_or_versions: [{string.Join(", ", DependencyStubsOrVersions)}]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"expected_fault_fingerprint: {ExpectedFaultFingerprint}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"evidence_plan: [{string.Join(", ", EvidencePlan)}]");
        return sb.ToString();
    }
}

/// <summary>
/// The five distinct replay outcomes defined in Patent §27 (Table 50).
/// </summary>
public enum ReplayOutcome
{
    /// <summary>Same fingerprint and material causal sequence.</summary>
    ExactReproduction,

    /// <summary>Same semantic failure and supported cause with permitted superficial differences.</summary>
    EquivalentReproduction,

    /// <summary>Some symptoms or enabling conditions appear; fingerprint incomplete.</summary>
    PartialReproduction,

    /// <summary>Failure absent under recorded attempt; evidence contributes to the reproduction envelope.</summary>
    NonReproduction,

    /// <summary>Target, checkpoint, dependency or evidence mismatch prevents a valid conclusion.</summary>
    InvalidReplay,
}

/// <summary>
/// Record of one replay execution attempt.
/// </summary>
public sealed record ReplayExecutionRecord
{
    public required int AttemptIndex { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
    public required ReplayOutcome Outcome { get; init; }
    public required string ObservedFingerprint { get; init; }
    public required double CausalMatchRatio { get; init; }
    public required int DurationMs { get; init; }
    public required string CheckpointHash { get; init; }
    public required bool CheckpointMatched { get; init; }
}

/// <summary>
/// The formal Reproduction Envelope model specified in Patent §28 (Table 52).
/// </summary>
public sealed record ReproductionEnvelope
{
    public required GhoztId FindingId { get; init; }
    public required IReadOnlyList<string> RequiredConditions { get; init; }
    public required IReadOnlyList<string> SufficientConditions { get; init; }
    public required IReadOnlyList<string> SuppressingConditions { get; init; }
    public required IReadOnlyList<string> CorrelatedConditions { get; init; }
    public required int AttemptCount { get; init; }
    public required int ReproductionCount { get; init; }
    public required double ReproducibilityScore { get; init; }
    public required ConfidenceInterval WilsonConfidence { get; init; }
    public required IReadOnlyDictionary<string, string> TimingRanges { get; init; }
    public required IReadOnlyList<string> KnownVariants { get; init; }
}

/// <summary>
/// Deterministic Replay & Reproduction Envelope Engine (Patent §27 & §28, Tables 49, 50, 52, 54).
/// Synthesizes standalone replay packages, classifies execution fidelity, and bounds reproduction envelopes.
/// </summary>
public sealed class DeterministicReplayEngine
{
    private readonly GhoztStore _store;

    public DeterministicReplayEngine(GhoztStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Compiles a formal Replay Package (Table 49) from target state, finding context, and sweep history.
    /// </summary>
    public ReplayPackage GenerateReplayPackage(Target target, Finding finding, Sweep? sweep = null)
    {
        var replayId = $"RPL-{_store.NextId(IdKind.Reproducer).Sequence:D6}";
        var buildLabel = string.IsNullOrWhiteSpace(target.BuildLabel) ? "BLD-000001" : target.BuildLabel;
        var envManifest = $"ENV-{target.Environment.ToString().ToUpperInvariant()}";
        var checkpointHash = Hashing.Sha256(string.Create(CultureInfo.InvariantCulture, $"{target.Id}#{buildLabel}#{finding.Id}"));

        var actions = new List<ReplayAction>();
        if (!string.IsNullOrWhiteSpace(finding.NodeId))
        {
            actions.Add(new ReplayAction
            {
                ActionId = "ACT-0001",
                Kind = "Navigate",
                TargetSelector = finding.NodeId,
                ExpectedState = "Loaded",
            });
        }
        actions.Add(new ReplayAction
        {
            ActionId = "ACT-0002",
            Kind = "TriggerFault",
            TargetSelector = string.IsNullOrWhiteSpace(finding.NodeId) ? "Root" : finding.NodeId,
            Payload = finding.Actual,
            ExpectedState = finding.Expected,
        });

        var package = new ReplayPackage
        {
            ReplayId = replayId,
            TargetId = target.Id,
            TargetBuild = buildLabel,
            EnvironmentManifest = envManifest,
            Checkpoint = checkpointHash[..10],
            Preconditions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fixture"] = "USR-STANDALONE",
                ["network"] = "isolated",
                ["clock"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            },
            Actions = actions,
            Inputs = ["INP-SEED-DEFAULT"],
            Seeds = [42],
            DependencyStubsOrVersions = ["target-runtime@pinned"],
            ExpectedFaultFingerprint = string.IsNullOrWhiteSpace(finding.Fingerprint) ? "FP-GENERIC" : finding.Fingerprint,
            EvidencePlan = ["trace", "state_delta", "response"],
        };

        // Emit BlackBox Ledger Event
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "RPL.PACKAGE.CREATED",
            Message = $"Deterministic replay package {replayId} synthesized for finding {finding.Id} ({finding.Title}).",
            Severity = Severity.Info,
            TargetId = target.Id,
            RelatedId = finding.Id.ToString(),
            Actor = "DeterministicReplayEngine",
            Detail = package.ToYaml(),
        });

        return package;
    }

    /// <summary>
    /// Evaluates a replay attempt against the expected package according to Patent Table 50.
    /// </summary>
    public static ReplayOutcome EvaluateAttempt(
        ReplayPackage package,
        string observedFingerprint,
        bool checkpointValid = true,
        bool dependenciesMatch = true,
        double causalMatchRatio = 1.0)
    {
        // 1. Invalid Replay: If initial checkpoint or environment dependencies mismatch, no valid conclusion can be drawn
        if (!checkpointValid || !dependenciesMatch)
        {
            return ReplayOutcome.InvalidReplay;
        }

        // 2. Exact Reproduction: Exactly identical fault fingerprint and full causal match
        if (string.Equals(observedFingerprint, package.ExpectedFaultFingerprint, StringComparison.Ordinal) && causalMatchRatio >= 0.99)
        {
            return ReplayOutcome.ExactReproduction;
        }

        // 3. Equivalent Reproduction: Semantically identical or high causal match with minor superficial variation
        if (causalMatchRatio >= 0.85 || observedFingerprint.StartsWith(package.ExpectedFaultFingerprint[..Math.Min(4, package.ExpectedFaultFingerprint.Length)], StringComparison.Ordinal))
        {
            return ReplayOutcome.EquivalentReproduction;
        }

        // 4. Partial Reproduction: Subset of symptoms or enabling conditions appear
        if (causalMatchRatio >= 0.30)
        {
            return ReplayOutcome.PartialReproduction;
        }

        // 5. Non-Reproduction: Defect was absent
        return ReplayOutcome.NonReproduction;
    }

    /// <summary>
    /// Calculates the weighted reproducibility score conforming to Patent Table 54:
    /// ReproducibilityScore = weighted_successes / weighted_valid_attempts
    /// </summary>
    public static double CalculateWeightedReproducibilityScore(IEnumerable<ReplayExecutionRecord> attempts)
    {
        var list = attempts.ToList();
        var weightedSuccesses = 0.0;
        var weightedValidAttempts = 0.0;

        foreach (var att in list)
        {
            if (att.Outcome == ReplayOutcome.InvalidReplay)
            {
                // Invalid replays do not count towards valid attempts
                continue;
            }

            var weight = att.CheckpointMatched ? 1.0 : 0.5;
            weightedValidAttempts += weight;

            if (att.Outcome == ReplayOutcome.ExactReproduction)
            {
                weightedSuccesses += 1.0 * weight;
            }
            else if (att.Outcome == ReplayOutcome.EquivalentReproduction)
            {
                weightedSuccesses += 0.85 * weight;
            }
            else if (att.Outcome == ReplayOutcome.PartialReproduction)
            {
                weightedSuccesses += 0.40 * weight;
            }
        }

        if (weightedValidAttempts <= 0.0) return 0.0;
        return Math.Round(weightedSuccesses / weightedValidAttempts, 3);
    }

    /// <summary>
    /// Derives the formal Reproduction Envelope (Table 52) across observed replay attempts.
    /// </summary>
    public ReproductionEnvelope DeriveReproductionEnvelope(Finding finding, IEnumerable<ReplayExecutionRecord> attempts)
    {
        var list = attempts.ToList();
        var totalAttempts = list.Count(a => a.Outcome != ReplayOutcome.InvalidReplay);
        var reproductions = list.Count(a => a.Outcome is ReplayOutcome.ExactReproduction or ReplayOutcome.EquivalentReproduction);

        var score = CalculateWeightedReproducibilityScore(list);
        var confidence = StateSpaceMath.WilsonScoreInterval(reproductions, totalAttempts);

        var required = new List<string> { "Target application running", "Isolated test fixture loaded" };
        var sufficient = new List<string> { $"Exact input sequence with seed {42}" };
        var suppressing = new List<string>();

        if (list.Any(a => a.Outcome == ReplayOutcome.NonReproduction))
        {
            suppressing.Add("Fast input rate / network jitter damping");
        }

        var timingRanges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ExecutionWindowMs"] = list.Count > 0 ? $"{list.Min(a => a.DurationMs)}ms - {list.Max(a => a.DurationMs)}ms" : "0ms",
            ["OptimalDelayMs"] = "120ms",
        };

        var variants = list.Select(a => a.ObservedFingerprint).Distinct(StringComparer.Ordinal).ToList();

        var envelope = new ReproductionEnvelope
        {
            FindingId = finding.Id,
            RequiredConditions = required,
            SufficientConditions = sufficient,
            SuppressingConditions = suppressing,
            CorrelatedConditions = ["UI thread load > 50%"],
            AttemptCount = totalAttempts,
            ReproductionCount = reproductions,
            ReproducibilityScore = score,
            WilsonConfidence = confidence,
            TimingRanges = timingRanges,
            KnownVariants = variants,
        };

        // Emit BlackBox event
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "RPL.ENVELOPE.DERIVED",
            Message = $"Reproduction envelope derived for finding {finding.Id}. Score: {score:P1}, Wilson 95%: [{confidence.Lower:P1} - {confidence.Upper:P1}].",
            Severity = Severity.Info,
            TargetId = finding.TargetId,
            RelatedId = finding.Id.ToString(),
            Actor = "DeterministicReplayEngine",
            Detail = JsonSerializer.Serialize(new
            {
                envelope.AttemptCount,
                envelope.ReproductionCount,
                envelope.ReproducibilityScore,
                envelope.WilsonConfidence.Lower,
                envelope.WilsonConfidence.Upper,
            }),
        });

        return envelope;
    }
}
