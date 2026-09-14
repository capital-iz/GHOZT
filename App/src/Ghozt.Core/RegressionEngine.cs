namespace Ghozt.Core;

public enum GuardStatus
{
    Active,
    Passing,
    Failing,
    Waived,
    Retired,
}

public enum MutationOutcome
{
    /// <summary>The guard detected the seeded defect -> Effective guard.</summary>
    Killed,

    /// <summary>The guard erroneously passed despite the seeded defect -> Ineffective / False-Confidence guard.</summary>
    Survived,
}

/// <summary>
/// A regression guard derived from a confirmed defect or contract invariant.
/// </summary>
public sealed record RegressionGuard
{
    public required string Id { get; init; }
    public required GhoztId TargetId { get; init; }
    public GhoztId? FindingId { get; init; }
    public required string Title { get; init; }
    public required Severity Severity { get; init; }
    public required string ExpectedBehavior { get; init; }
    public required string Fingerprint { get; init; }
    public GuardStatus Status { get; init; } = GuardStatus.Active;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? LastEvaluatedAt { get; init; }
    public int EvaluationCount { get; init; }
    public string Details { get; init; } = "";
}

/// <summary>
/// Detailed result of a single mutation assay applied against a regression guard.
/// </summary>
public sealed record MutationAssay
{
    public required string GuardId { get; init; }
    public required string MutationOperator { get; init; }
    public required string MutatedInput { get; init; }
    public required MutationOutcome Outcome { get; init; }
    public required string DiagnosticNote { get; init; }
}

/// <summary>
/// Comprehensive summary of guard mutation assays and false-confidence detection.
/// </summary>
public sealed record GuardEfficacyReport
{
    public required GhoztId TargetId { get; init; }
    public required int TotalGuards { get; init; }
    public required int TotalMutants { get; init; }
    public required int MutantsKilled { get; init; }
    public required int MutantsSurvived { get; init; }
    public double MutationScore => TotalMutants == 0 ? 1.0 : (double)MutantsKilled / TotalMutants;
    public required IReadOnlyList<MutationAssay> Assays { get; init; }
    public required IReadOnlyList<string> IneffectiveGuardIds { get; init; }
}

/// <summary>
/// Engine for generating, managing, evaluating, and mutation-testing automated regression guards (Patent §31).
/// </summary>
public sealed class RegressionEngine
{
    private readonly GhoztStore _store;

    public RegressionEngine(GhoztStore store) => _store = store;

    /// <summary>
    /// Generates regression guards from confirmed deviations and findings in the store.
    /// </summary>
    public IReadOnlyList<RegressionGuard> GenerateGuards(GhoztId targetId)
    {
        var findings = _store.Findings(targetId);
        var guards = new List<RegressionGuard>();

        var index = 1;
        foreach (var finding in findings)
        {
            var isResolved = finding.Maturity is FindingMaturity.Verified or FindingMaturity.FixReady;
            var status = isResolved ? GuardStatus.Passing : GuardStatus.Failing;

            guards.Add(new RegressionGuard
            {
                Id = $"GRD-{index:D5}",
                TargetId = targetId,
                FindingId = finding.Id,
                Title = $"Guard against {finding.Title}",
                Severity = finding.Severity,
                ExpectedBehavior = finding.Expected,
                Fingerprint = finding.Fingerprint,
                Status = status,
                CreatedAt = finding.FirstSeenAt,
                LastEvaluatedAt = finding.LastSeenAt,
                EvaluationCount = finding.ReproAttempts,
                Details = $"Source finding: {finding.Id}. Actual observed failure: \"{finding.Actual}\"",
            });
            index++;
        }

        return guards;
    }

    /// <summary>
    /// Evaluates a single guard against the specified target state and optional finding override.
    /// </summary>
    public (bool Passes, string Note) EvaluateGuard(RegressionGuard guard, Target target, Finding? findingOverride = null)
    {
        if (guard.ExpectedBehavior.Contains("readable", StringComparison.OrdinalIgnoreCase) ||
            guard.Details.Contains("Path not found", StringComparison.OrdinalIgnoreCase))
        {
            var exists = !string.IsNullOrWhiteSpace(target.BaseAddress) &&
                         (System.IO.File.Exists(target.BaseAddress) || System.IO.Directory.Exists(target.BaseAddress));
            return (exists, exists ? $"Path exists at {target.BaseAddress}" : $"Target path still unreachable: {target.BaseAddress}");
        }

        if (guard.ExpectedBehavior.Contains("version", StringComparison.OrdinalIgnoreCase) ||
            guard.Details.Contains("build identity", StringComparison.OrdinalIgnoreCase))
        {
            var hasBuild = !string.IsNullOrWhiteSpace(target.BuildLabel);
            return (hasBuild, hasBuild ? $"Build label present: {target.BuildLabel}" : "Build identity still empty");
        }

        // Defect resolution check
        var finding = findingOverride ??
                      (guard.FindingId.HasValue ? _store.Findings(target.Id).FirstOrDefault(f => f.Id == guard.FindingId.Value) : null);

        var passes = finding is not null && (finding.Maturity is FindingMaturity.Verified or FindingMaturity.FixReady);
        var note = passes
            ? "Defect confirmed verified or marked fix-ready in store"
            : (finding is null ? "No matching finding record found in store" : $"Defect remains unverified (Maturity: {finding.Maturity})");

        return (passes, note);
    }

    /// <summary>
    /// Actively executes and validates regression guards against the target, updating store records and blackbox stream.
    /// </summary>
    public async Task<IReadOnlyList<RegressionGuard>> ExecuteGuardsAsync(Target target, CancellationToken ct = default)
    {
        var existing = GenerateGuards(target.Id);
        var evaluated = new List<RegressionGuard>();

        foreach (var guard in existing)
        {
            ct.ThrowIfCancellationRequested();

            var (passes, note) = EvaluateGuard(guard, target);
            var newStatus = passes ? GuardStatus.Passing : GuardStatus.Failing;

            _store.Append(new LedgerEvent
            {
                Id = _store.NextId(IdKind.Event),
                At = DateTimeOffset.Now,
                Code = passes ? "RGR.PASS" : "RGR.FAIL",
                Message = $"Regression guard {guard.Id}: {(passes ? "PASSED" : "FAILED")}.",
                Severity = passes ? Severity.Info : Severity.Medium,
                TargetId = target.Id,
                RelatedId = guard.Id,
                Actor = "RegressionEngine",
                Detail = $"{guard.Title} -> {note}",
            });

            evaluated.Add(guard with
            {
                Status = newStatus,
                LastEvaluatedAt = DateTimeOffset.Now,
                EvaluationCount = guard.EvaluationCount + 1,
                Details = $"{guard.Details} | Last evaluated: {note}",
            });

            await Task.Delay(20, ct).ConfigureAwait(false);
        }

        return evaluated;
    }

    /// <summary>
    /// Performs mutation assays across all guards to verify fault detection efficacy (Patent §31).
    /// Injects controlled faults into target state and assertions to verify that guards do not pass tautologically.
    /// </summary>
    public async Task<GuardEfficacyReport> VerifyGuardEfficacyAsync(Target target, CancellationToken ct = default)
    {
        var guards = GenerateGuards(target.Id);
        var assays = new List<MutationAssay>();
        var ineffectiveGuards = new HashSet<string>();

        // If no guards currently exist, synthesize baseline regression guards to assay target invariants
        if (guards.Count == 0)
        {
            guards =
            [
                new RegressionGuard
                {
                    Id = "GRD-BASE-PATH",
                    TargetId = target.Id,
                    Title = "Guard target path reachability",
                    Severity = Severity.High,
                    ExpectedBehavior = "Target executable or directory is readable and accessible",
                    Fingerprint = "FP_BASE_PATH",
                    Status = GuardStatus.Passing,
                    Details = "Baseline reachability invariant",
                },
                new RegressionGuard
                {
                    Id = "GRD-BASE-BUILD",
                    TargetId = target.Id,
                    Title = "Guard target build identity",
                    Severity = Severity.Medium,
                    ExpectedBehavior = "Target declares valid version or build label",
                    Fingerprint = "FP_BASE_BUILD",
                    Status = GuardStatus.Passing,
                    Details = "Baseline build identity invariant",
                }
            ];
        }

        foreach (var guard in guards)
        {
            ct.ThrowIfCancellationRequested();

            if (guard.ExpectedBehavior.Contains("readable", StringComparison.OrdinalIgnoreCase) ||
                guard.Details.Contains("Path not found", StringComparison.OrdinalIgnoreCase))
            {
                // Mutation Operator: Corrupt target base address with non-existent path
                var corruptedTarget = target with { BaseAddress = $@"C:\NonExistent_Corrupted_{Guid.NewGuid():N}\ghost.bin" };
                var (mutantPasses, mutantNote) = EvaluateGuard(guard, corruptedTarget);

                var outcome = !mutantPasses ? MutationOutcome.Killed : MutationOutcome.Survived;
                var assay = new MutationAssay
                {
                    GuardId = guard.Id,
                    MutationOperator = "InvertPathExistence",
                    MutatedInput = corruptedTarget.BaseAddress,
                    Outcome = outcome,
                    DiagnosticNote = outcome == MutationOutcome.Killed
                        ? "Guard caught path corruption fault as expected (Killed)."
                        : "Guard failed to detect unreachable path; passed erroneously (Survived).",
                };

                assays.Add(assay);
                if (outcome == MutationOutcome.Survived)
                {
                    ineffectiveGuards.Add(guard.Id);
                }

                _store.Append(new LedgerEvent
                {
                    Id = _store.NextId(IdKind.Event),
                    At = DateTimeOffset.Now,
                    Code = outcome == MutationOutcome.Killed ? "RGR.MUTATION.KILLED" : "RGR.MUTATION.SURVIVED",
                    Message = $"Guard {guard.Id} mutation assay ({assay.MutationOperator}): {outcome}.",
                    Severity = outcome == MutationOutcome.Killed ? Severity.Info : Severity.High,
                    TargetId = target.Id,
                    RelatedId = guard.Id,
                    Actor = "RegressionEngine",
                    Detail = assay.DiagnosticNote,
                });
            }
            else if (guard.ExpectedBehavior.Contains("version", StringComparison.OrdinalIgnoreCase) ||
                     guard.Details.Contains("build identity", StringComparison.OrdinalIgnoreCase))
            {
                // Mutation Operator: Empty out build label
                var blankTarget = target with { BuildLabel = "" };
                var (mutantPasses, mutantNote) = EvaluateGuard(guard, blankTarget);

                var outcome = !mutantPasses ? MutationOutcome.Killed : MutationOutcome.Survived;
                var assay = new MutationAssay
                {
                    GuardId = guard.Id,
                    MutationOperator = "WipeBuildIdentity",
                    MutatedInput = "BuildLabel=\"\"",
                    Outcome = outcome,
                    DiagnosticNote = outcome == MutationOutcome.Killed
                        ? "Guard caught missing build label fault as expected (Killed)."
                        : "Guard failed to detect missing build label (Survived).",
                };

                assays.Add(assay);
                if (outcome == MutationOutcome.Survived)
                {
                    ineffectiveGuards.Add(guard.Id);
                }

                _store.Append(new LedgerEvent
                {
                    Id = _store.NextId(IdKind.Event),
                    At = DateTimeOffset.Now,
                    Code = outcome == MutationOutcome.Killed ? "RGR.MUTATION.KILLED" : "RGR.MUTATION.SURVIVED",
                    Message = $"Guard {guard.Id} mutation assay ({assay.MutationOperator}): {outcome}.",
                    Severity = outcome == MutationOutcome.Killed ? Severity.Info : Severity.High,
                    TargetId = target.Id,
                    RelatedId = guard.Id,
                    Actor = "RegressionEngine",
                    Detail = assay.DiagnosticNote,
                });
            }
            else
            {
                // Mutation Operator: Invert finding maturity to New (unresolved)
                var finding = guard.FindingId.HasValue ? _store.Findings(target.Id).FirstOrDefault(f => f.Id == guard.FindingId.Value) : null;
                var mutatedFinding = finding is not null
                    ? finding with { Maturity = FindingMaturity.New }
                    : new Finding
                    {
                        Id = _store.NextId(IdKind.Fault),
                        TargetId = target.Id,
                        Title = "Synthetic active defect",
                        Severity = Severity.Medium,
                        Maturity = FindingMaturity.New,
                    };

                var (mutantPasses, mutantNote) = EvaluateGuard(guard, target, mutatedFinding);
                var outcome = !mutantPasses ? MutationOutcome.Killed : MutationOutcome.Survived;
                var assay = new MutationAssay
                {
                    GuardId = guard.Id,
                    MutationOperator = "InvertMaturityToUnresolved",
                    MutatedInput = "Maturity=New",
                    Outcome = outcome,
                    DiagnosticNote = outcome == MutationOutcome.Killed
                        ? "Guard detected unresolved defect recurrence (Killed)."
                        : "Guard passed despite unresolved defect (Survived).",
                };

                assays.Add(assay);
                if (outcome == MutationOutcome.Survived)
                {
                    ineffectiveGuards.Add(guard.Id);
                }

                _store.Append(new LedgerEvent
                {
                    Id = _store.NextId(IdKind.Event),
                    At = DateTimeOffset.Now,
                    Code = outcome == MutationOutcome.Killed ? "RGR.MUTATION.KILLED" : "RGR.MUTATION.SURVIVED",
                    Message = $"Guard {guard.Id} mutation assay ({assay.MutationOperator}): {outcome}.",
                    Severity = outcome == MutationOutcome.Killed ? Severity.Info : Severity.High,
                    TargetId = target.Id,
                    RelatedId = guard.Id,
                    Actor = "RegressionEngine",
                    Detail = assay.DiagnosticNote,
                });
            }

            await Task.Delay(10, ct).ConfigureAwait(false);
        }

        var killed = assays.Count(a => a.Outcome == MutationOutcome.Killed);
        var survived = assays.Count(a => a.Outcome == MutationOutcome.Survived);

        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "RGR.EFFICACY.SUMMARY",
            Message = $"Guard mutation efficacy assay completed: {killed}/{assays.Count} mutants killed.",
            Severity = survived == 0 ? Severity.Info : Severity.High,
            TargetId = target.Id,
            Actor = "RegressionEngine",
            Detail = $"Mutation Score: {(assays.Count == 0 ? 1.0 : (double)killed / assays.Count):P0} | Ineffective Guards: {ineffectiveGuards.Count}",
        });

        return new GuardEfficacyReport
        {
            TargetId = target.Id,
            TotalGuards = guards.Count,
            TotalMutants = assays.Count,
            MutantsKilled = killed,
            MutantsSurvived = survived,
            Assays = assays,
            IneffectiveGuardIds = [.. ineffectiveGuards],
        };
    }
}
