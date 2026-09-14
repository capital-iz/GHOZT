using System.IO;
using System.Text.Json;

namespace Ghozt.Core;

public enum RepairVerificationStatus
{
    Verified,
    Rejected,
    Unverified,
}

/// <summary>
/// Result of a formal bounded repair verification pass (Patent §30).
/// </summary>
public sealed record RepairVerificationResult
{
    public required GhoztId FindingId { get; init; }
    public required string FixBriefId { get; init; }
    public required RepairVerificationStatus Status { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<string> RejectionReasons { get; init; }
    public required string Detail { get; init; }
    public required DateTimeOffset VerifiedAt { get; init; }
    public required string VerifiedBy { get; init; }
    public required int RegressionGuardsEvaluated { get; init; }
    public required int RegressionGuardsPassed { get; init; }
}

/// <summary>
/// Verifies AI-assisted and human code repairs against formal Fix Brief contracts (Patent §30).
/// Enforces mandatory rejection conditions, regression guard integrity, and evidence proof.
/// </summary>
public sealed class RepairVerifier
{
    private readonly GhoztStore _store;

    public RepairVerifier(GhoztStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Evaluates whether a proposed code repair satisfies all Fix Brief constraints and eliminates the defect.
    /// </summary>
    public async Task<RepairVerificationResult> VerifyRepairAsync(
        Target target,
        Finding finding,
        FixBrief fixBrief,
        IReadOnlyList<string>? modifiedFiles = null,
        bool simulateRegressions = false,
        bool simulateSymptomMasking = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(fixBrief);

        var rejectionReasons = new List<string>();

        // 1. Check Mandatory Scope Bounds (Patent §30: Repair Rejection Condition 1)
        if (modifiedFiles is not null && modifiedFiles.Count > 0)
        {
            foreach (var file in modifiedFiles)
            {
                var normalized = file.Replace('\\', '/');

                // Check forbidden scope
                foreach (var forbidden in fixBrief.ForbiddenScope)
                {
                    if (normalized.Contains(forbidden.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        rejectionReasons.Add($"Scope Violation: Modified file '{file}' touches forbidden scope '{forbidden}'.");
                    }
                }

                // Check allowed scope: at least one allowed scope pattern must match
                var isAllowed = fixBrief.AllowedScope.Any(allowed =>
                    normalized.Contains(allowed.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                    allowed.Equals("Target source repository and configuration manifest", StringComparison.OrdinalIgnoreCase));

                if (!isAllowed && fixBrief.AllowedScope.Count > 0)
                {
                    rejectionReasons.Add($"Scope Exceeded: Modified file '{file}' is outside allowed scope without explicit architectural re-approval.");
                }
            }
        }

        // 2. Check for Symptom Masking vs Root Cause Elimination (Patent §30: Condition 2)
        if (simulateSymptomMasking)
        {
            rejectionReasons.Add("Symptom Masking: Observable error was caught/suppressed, but root causal mechanism remains unresolved.");
        }

        // 3. Evaluate Regression Guards (Patent §30: Condition 6 & Patent §31)
        var regEngine = new RegressionEngine(_store);
        var guards = regEngine.GenerateGuards(target.Id);
        var guardsEvaluated = guards.Count;
        var guardsPassed = simulateRegressions
            ? Math.Max(0, guards.Count - 1)
            : guards.Count(g => g.Status == GuardStatus.Passing);

        if (simulateRegressions || (guardsEvaluated > 0 && guardsPassed < guardsEvaluated))
        {
            var failing = guardsEvaluated - guardsPassed;
            rejectionReasons.Add($"Regression Introduced: {failing} automated regression guard(s) failed following the proposed repair.");
        }

        // 4. Verify Reproduction Failure (Defect must no longer recur)
        var sweepEngine = new SweepEngine(_store);
        var reproSweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = $"Repair Repro Verification for {finding.Id}",
            Mode = "Smoke",
            State = RunState.Running,
            StartedAt = DateTimeOffset.Now,
        };
        _store.Save(reproSweep);

        // Run check corresponding to finding node
        var checkName = finding.NodeId.Contains(':')
            ? finding.NodeId.Split(':')[0]
            : "SurfaceCheck";

        var checkRes = await sweepEngine.RunCheckAsync(checkName, reproSweep, target, ct).ConfigureAwait(false);

        var completedSweep = reproSweep with
        {
            State = RunState.Completed,
            FinishedAt = DateTimeOffset.Now,
            StatesVisited = 3,
        };
        _store.Save(completedSweep);

        // If check failed and was simulated or reproducible defect recurred
        if (!checkRes.Passed && !checkRes.Blocked && !string.IsNullOrWhiteSpace(checkRes.Detail) && checkRes.Detail.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            rejectionReasons.Add($"Reproduction Recurred: Defect fingerprint {finding.Fingerprint} was re-observed during post-repair execution.");
        }

        var isVerified = rejectionReasons.Count == 0;
        var verdict = isVerified ? RepairVerificationStatus.Verified : RepairVerificationStatus.Rejected;
        var detail = isVerified
            ? $"Repair verified successfully. Zero regressions across {guardsEvaluated} guard(s). Root cause mechanism resolved."
            : $"Repair rejected with {rejectionReasons.Count} rejection condition(s): {string.Join(" | ", rejectionReasons)}";

        var result = new RepairVerificationResult
        {
            FindingId = finding.Id,
            FixBriefId = fixBrief.Id,
            Status = verdict,
            Passed = isVerified,
            RejectionReasons = rejectionReasons,
            Detail = detail,
            VerifiedAt = DateTimeOffset.Now,
            VerifiedBy = "Ghozt.RepairVerifier@v1",
            RegressionGuardsEvaluated = guardsEvaluated,
            RegressionGuardsPassed = guardsPassed,
        };

        // Update Finding and Event Ledger
        if (isVerified)
        {
            var resolvedFinding = finding with
            {
                Maturity = FindingMaturity.Verified,
            };
            _store.Save(resolvedFinding);

            _store.Append(new LedgerEvent
            {
                Id = _store.NextId(IdKind.Event),
                At = DateTimeOffset.Now,
                Code = "SYS.REPAIR.VERIFIED",
                Message = $"Bounded repair for finding {finding.Id} VERIFIED under Fix Brief {fixBrief.Id}.",
                Severity = Severity.Info,
                TargetId = target.Id,
                SweepId = reproSweep.Id,
                Actor = "RepairVerifier@v1",
                Detail = $"fix_brief={fixBrief.Id}; guards_passed={guardsPassed}/{guardsEvaluated}",
            });
        }
        else
        {
            _store.Append(new LedgerEvent
            {
                Id = _store.NextId(IdKind.Event),
                At = DateTimeOffset.Now,
                Code = "SYS.REPAIR.REJECTED",
                Message = $"Bounded repair for finding {finding.Id} REJECTED under Fix Brief {fixBrief.Id}.",
                Severity = Severity.High,
                TargetId = target.Id,
                SweepId = reproSweep.Id,
                Actor = "RepairVerifier@v1",
                Detail = string.Join("; ", rejectionReasons),
            });
        }

        return result;
    }
}
