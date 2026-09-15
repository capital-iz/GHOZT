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

        // Gate 7: Multi-Modal Quality & Security Validation
        var securityOrA11yViolations = findings.Count(f =>
            (f.Severity is Severity.Critical or Severity.High) &&
            (f.NodeId.StartsWith("sec:", StringComparison.OrdinalIgnoreCase) ||
             f.NodeId.StartsWith("theme:", StringComparison.OrdinalIgnoreCase) ||
             f.Title.Contains("security", StringComparison.OrdinalIgnoreCase) ||
             f.Title.Contains("contrast", StringComparison.OrdinalIgnoreCase) ||
             f.Title.Contains("key", StringComparison.OrdinalIgnoreCase) ||
             f.Title.Contains("token", StringComparison.OrdinalIgnoreCase)));
        var validationPassed = securityOrA11yViolations == 0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.VALIDATION_REGISTRY",
            Name = "Cross-Cutting Quality & Security Gate",
            Requirement = "Zero unmitigated High or Critical security, credential leak, or accessibility defects.",
            PolicySource = "Patent §16 & §22 Cross-Cutting Quality Specification",
            Mandatory = true,
            Passed = validationPassed,
            Result = validationPassed ? "Passed" : $"Failed ({securityOrA11yViolations} high/critical breach{(securityOrA11yViolations == 1 ? "" : "es")})",
            EvidenceNote = validationPassed
                ? "Zero critical or high security/accessibility defects in active finding backlog."
                : $"{securityOrA11yViolations} high/critical security or accessibility defect(s) require remediation before clearance.",
        });

        // Gate 8: Regression Guards Invariant
        var regEngine = new RegressionEngine(_store);
        var guards = regEngine.GenerateGuards(target.Id);
        var failingGuards = guards.Count(g => g.Status == GuardStatus.Failing);
        var guardsPassed = failingGuards == 0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.REGRESSION_GUARDS",
            Name = "Automated Regression Guard Integrity",
            Requirement = "100% of active automated regression guards must be in passing state.",
            PolicySource = "Patent §31 Regression Memory & Change Impact Canon",
            Mandatory = true,
            Passed = guardsPassed,
            Result = guardsPassed ? "Passed" : $"Failed ({failingGuards} failing guard{(failingGuards == 1 ? "" : "s")})",
            EvidenceNote = guardsPassed
                ? $"All {guards.Count} active regression guard(s) evaluated and passing."
                : $"{failingGuards} of {guards.Count} regression guard(s) currently failing.",
        });

        // Gate 9: Performance Stability & Resilience (Patent §21)
        var leakFindings = findings.Count(f =>
            f.Severity >= Severity.High &&
            (f.Title.Contains("Resource Drift", StringComparison.OrdinalIgnoreCase) ||
             f.Title.Contains("Deterministic Leak", StringComparison.OrdinalIgnoreCase) ||
             f.Actual.Contains("resource growth", StringComparison.OrdinalIgnoreCase)));
        var soakPassed = leakFindings == 0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.PERFORMANCE_SOAK",
            Name = "Performance Stability & Bounded Resource Drift",
            Requirement = "Zero unmitigated deterministic memory leaks (slope >= 0.05 MB/s), handle leaks, or thread leaks.",
            PolicySource = "Patent §21 Soak and Resilience Performance Canon",
            Mandatory = true,
            Passed = soakPassed,
            Result = soakPassed ? "Passed" : $"Failed ({leakFindings} leak defect{(leakFindings == 1 ? "" : "s")})",
            EvidenceNote = soakPassed
                ? "Zero critical or high resource drift defects detected in active soak telemetry."
                : $"{leakFindings} deterministic resource leak defect(s) require remediation before release clearance.",
        });

        // Gate 10: Anti-Gaming Evidence Integrity (Patent §33)
        var hasValidSweeps = sweeps.Count > 0 && sweeps.All(s => s.StatesVisited > 0 || s.State != RunState.Completed);
        var tamperedEvidence = evidence.Count(e => !string.IsNullOrWhiteSpace(e.Payload) && !string.IsNullOrWhiteSpace(e.Sha256) && e.Sha256 != Hashing.Sha256(e.Payload));
        var antiGamingPassed = hasValidSweeps && tamperedEvidence == 0;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.ANTI_GAMING",
            Name = "Anti-Gaming Evidence Integrity",
            Requirement = "Authentic non-zero exploration state traversal and 100% cryptographic evidence payload match.",
            PolicySource = "Patent §33 Anti-Gaming Verification Specification",
            Mandatory = true,
            Passed = antiGamingPassed,
            Result = antiGamingPassed ? "Passed" : (tamperedEvidence > 0 ? $"Failed ({tamperedEvidence} tampered)" : "Failed (Empty sweeps)"),
            EvidenceNote = antiGamingPassed
                ? $"Verified {evidence.Count} cryptographic evidence items and {sweeps.Count} authentic sweep traversals."
                : (tamperedEvidence > 0 ? $"{tamperedEvidence} evidence item(s) have invalid SHA-256 payload checksums." : "Clearance rejected: all sweeps traversed 0 authentic states."),
        });

        // Gate 11: Compatibility Matrix Envelope (Patent §22, Table 38)
        var compatReport = CompatibilityMatrixEngine.EvaluateCompatibility(target, _store);
        var compatPassed = compatReport.Passed;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.COMPATIBILITY_MATRIX",
            Name = "Compatibility Matrix Envelope",
            Requirement = "Target and runtime environment must satisfy all 5 Table 38 compatibility axes (OS, Scaling, Locale, Runtime, Migration).",
            PolicySource = "Patent §22 & Table 38 Compatibility Matrix Canon",
            Mandatory = true,
            Passed = compatPassed,
            Result = compatPassed ? "Passed" : $"Failed ({compatReport.TotalAxesAssayed - compatReport.PassedAxes} axis failure(s))",
            EvidenceNote = compatPassed
                ? $"All {compatReport.TotalAxesAssayed} compatibility axes verified (Digest: {compatReport.MatrixDigest[..8]})."
                : $"Compatibility failure(s) detected across {compatReport.TotalAxesAssayed - compatReport.PassedAxes} axis/axes.",
        });

        // Gate 12: Governed Security Audit Clearance (Patent §22, Table 36)
        var activeGrant = _store.ActiveGrantFor(target.Id);
        var secReport = SecurityAuditEngine.RunAudit(target, _store, activeGrant);
        var secPassed = secReport.Passed;
        gates.Add(new ClearanceGate
        {
            Id = "GATE.SECURITY_AUDIT",
            Name = "Governed Security Audit Clearance",
            Requirement = "Target must satisfy all 8 Table 36 security validation areas with zero unmitigated High or Critical security breaches.",
            PolicySource = "Patent §22 & Table 36 Security Audit Canon",
            Mandatory = true,
            Passed = secPassed,
            Result = secPassed ? "Passed" : $"Failed ({secReport.FailedAreas} security area violation(s))",
            EvidenceNote = secPassed
                ? $"All {secReport.TotalAreasChecked} security areas verified with 0 critical breaches (Signature: {secReport.AuditSignature[..8]})."
                : $"{secReport.FailedAreas} security validation area(s) breached with high/critical severity.",
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
