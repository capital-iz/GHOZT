namespace Ghozt.Core;

public enum GuardStatus
{
    Active,
    Passing,
    Failing,
    Waived,
    Retired,
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
/// Engine for generating, managing, and evaluating automated regression guards.
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
            // A defect generates a guard to verify it does not recur
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
    /// Actively executes and validates regression guards against the target, updating store records and blackbox stream.
    /// </summary>
    public async Task<IReadOnlyList<RegressionGuard>> ExecuteGuardsAsync(Target target, CancellationToken ct = default)
    {
        var existing = GenerateGuards(target.Id);
        var evaluated = new List<RegressionGuard>();

        foreach (var guard in existing)
        {
            ct.ThrowIfCancellationRequested();

            var passes = false;
            var note = "";

            if (guard.ExpectedBehavior.Contains("readable", StringComparison.OrdinalIgnoreCase) ||
                guard.Details.Contains("Path not found", StringComparison.OrdinalIgnoreCase))
            {
                var exists = System.IO.File.Exists(target.BaseAddress) || System.IO.Directory.Exists(target.BaseAddress);
                passes = exists;
                note = exists ? $"Path exists at {target.BaseAddress}" : $"Target path still unreachable: {target.BaseAddress}";
            }
            else if (guard.ExpectedBehavior.Contains("version", StringComparison.OrdinalIgnoreCase) ||
                     guard.Details.Contains("build identity", StringComparison.OrdinalIgnoreCase))
            {
                passes = !string.IsNullOrWhiteSpace(target.BuildLabel);
                note = passes ? $"Build label present: {target.BuildLabel}" : "Build identity still empty";
            }
            else
            {
                // Check if the finding's maturity was advanced or if reproducibility dropped
                var finding = guard.FindingId.HasValue ? _store.Findings(target.Id).FirstOrDefault(f => f.Id == guard.FindingId.Value) : null;
                passes = finding is not null && (finding.Maturity is FindingMaturity.Verified or FindingMaturity.FixReady);
                note = passes ? "Defect confirmed verified or marked fix-ready in store" : "Defect remains unverified or unresolved";
            }

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
}

