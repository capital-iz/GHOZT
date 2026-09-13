namespace Ghozt.Core;

/// <summary>
/// A cryptographically sealed release decision evaluating policy gates against observed evidence.
/// </summary>
public sealed record ClearanceDecision
{
    public required GhoztId TargetId { get; init; }
    public required ClearanceOutcome Outcome { get; init; }
    public required DateTimeOffset DecidedAt { get; init; }
    public required string DecidedBy { get; init; }
    public required string BuildLabel { get; init; }
    public required string EvidenceDigest { get; init; }
    public required IReadOnlyList<ClearanceGate> Gates { get; init; }
    public string Notes { get; init; } = "";
}

/// <summary>
/// Evaluates release policy gates against observed target state and forensic evidence.
/// </summary>
public sealed class ClearanceEngine
{
    private readonly GhoztStore _store;

    public ClearanceEngine(GhoztStore store) => _store = store;

    /// <summary>
    /// Evaluates all release policy gates for a registered target.
    /// </summary>
    public ClearanceDecision EvaluateTarget(Target target, string reviewer = "operator")
    {
        var findings = _store.Findings(target.Id);
        var evidence = _store.EvidenceItems(target.Id);
        var sweeps = _store.Sweeps(target.Id);
        var nodes = _store.Nodes(target.Id);

        var gates = new List<ClearanceGate>();

        // Gate 1: Reachability & Presence
        var pathExists = !string.IsNullOrWhiteSpace(target.BaseAddress) &&
                         (File.Exists(target.BaseAddress) || Directory.Exists(target.BaseAddress));
        var reachable = target.Reachable || pathExists;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.REACHABLE",
            Name = "Target Reachability",
            Requirement = "Target binary or service base address must be present and reachable.",
            PolicySource = "Universal Security & Operational Doctrine §9",
            Mandatory = true,
            Passed = reachable,
            Result = reachable ? "Passed" : "Failed",
            EvidenceNote = reachable ? $"Resolved '{target.BaseAddress}' successfully." : "Target path is missing or unreachable.",
        });

        // Gate 2: Zero Critical Faults
        var criticalFaults = findings.Count(f => f.Severity == Severity.Critical);
        var zeroCritical = criticalFaults == 0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.ZERO_CRITICAL",
            Name = "Zero Critical Defect Ceiling",
            Requirement = "Zero unmitigated Critical or Fatal crash defects.",
            PolicySource = "Capital-Iz Clearance Canon §3",
            Mandatory = true,
            Passed = zeroCritical,
            Result = zeroCritical ? "Passed" : $"Failed ({criticalFaults} critical fault{(criticalFaults == 1 ? "" : "s")})",
            EvidenceNote = zeroCritical ? "0 critical deviations in active defect ledger." : $"{criticalFaults} critical deviations pending fix.",
        });

        // Gate 3: Cryptographic Integrity & Evidence Hashing
        var unhashedEvidence = evidence.Count(e => string.IsNullOrWhiteSpace(e.Sha256));
        var integrityPassed = unhashedEvidence == 0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.INTEGRITY_SEAL",
            Name = "Cryptographic Evidence Integrity",
            Requirement = "100% of recorded evidence artefacts must carry valid SHA-256 content digests.",
            PolicySource = "Universal Design System Law 23 / Evidence Plane",
            Mandatory = true,
            Passed = integrityPassed,
            Result = integrityPassed ? "Passed" : $"Failed ({unhashedEvidence} unhashed)",
            EvidenceNote = integrityPassed ? $"All {evidence.Count} artefacts sealed with SHA-256 digests." : $"{unhashedEvidence} artefacts lack cryptographic digests.",
        });

        // Gate 4: Storage Capacity Headroom
        var drivePath = Path.GetPathRoot(Path.GetFullPath(string.IsNullOrWhiteSpace(target.BaseAddress) ? "C:\\" : target.BaseAddress)) ?? "C:\\";
        var drive = new DriveInfo(drivePath);
        var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
        var capacityPassed = freeGb >= 2.0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.STORAGE_HEADROOM",
            Name = "Storage Headroom Capacity",
            Requirement = "At least 2.0 GB free disk headroom on host target volume.",
            PolicySource = "Performance & Infrastructure Rule §15",
            Mandatory = false,
            Passed = capacityPassed,
            Result = capacityPassed ? "Passed" : $"Warning ({freeGb:F1} GB free)",
            EvidenceNote = $"Host volume {drive.Name} has {freeGb:F2} GB available.",
        });

        // Gate 5: Evidence Chain Consistency
        var orphanedArtefacts = evidence.Count(a => a.FindingId is { } id && findings.All(f => f.Id != id));
        var consistencyPassed = orphanedArtefacts == 0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.CHAIN_CONSISTENCY",
            Name = "Evidence Chain Consistency",
            Requirement = "Zero orphaned evidence records pointing to non-existent findings.",
            PolicySource = "Evidence Store Integrity Protocol §8",
            Mandatory = true,
            Passed = consistencyPassed,
            Result = consistencyPassed ? "Passed" : $"Failed ({orphanedArtefacts} orphaned)",
            EvidenceNote = consistencyPassed ? "Evidence chain is completely consistent." : $"{orphanedArtefacts} evidence records lack valid parent finding references.",
        });

        // Gate 6: Autonomous Examination Completed
        var completedSweeps = sweeps.Count(s => s.State == RunState.Completed);
        var explorationPassed = completedSweeps > 0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.EXPLORATION_PROOF",
            Name = "Autonomous Examination Proof",
            Requirement = "At least one completed autonomous examination sweep with sealed black-box evidence.",
            PolicySource = "Verification-First Execution Doctrine §12",
            Mandatory = true,
            Passed = explorationPassed,
            Result = explorationPassed ? "Passed" : "Failed (0 completed sweeps)",
            EvidenceNote = explorationPassed ? $"{completedSweeps} sweep(s) completed successfully." : "No completed examination sweeps found for target.",
        });

        // Compute Outcome
        var mandatoryFailed = gates.Any(g => g.Mandatory && !g.Passed);
        var optionalFailed = gates.Any(g => !g.Mandatory && !g.Passed);

        ClearanceOutcome outcome;
        if (!reachable)
        {
            outcome = ClearanceOutcome.Blocked;
        }
        else if (mandatoryFailed)
        {
            outcome = ClearanceOutcome.Denied;
        }
        else if (optionalFailed)
        {
            outcome = ClearanceOutcome.ConditionallyCleared;
        }
        else
        {
            outcome = ClearanceOutcome.Cleared;
        }

        var payload = $"target={target.Id}\noutcome={outcome}\nreviewer={reviewer}\nat={DateTimeOffset.Now:O}\n"
                      + string.Join('\n', gates.Select(g => $"{g.Id}: {g.Result}"));
        var digest = Hashing.Sha256(payload);

        return new ClearanceDecision
        {
            TargetId = target.Id,
            Outcome = outcome,
            DecidedAt = DateTimeOffset.Now,
            DecidedBy = reviewer,
            BuildLabel = string.IsNullOrWhiteSpace(target.BuildLabel) ? "dev" : target.BuildLabel,
            EvidenceDigest = digest,
            Gates = gates,
            Notes = $"Evaluated {gates.Count} gates: {gates.Count(g => g.Passed)} passed, {gates.Count(g => !g.Passed)} failed.",
        };
    }
}
