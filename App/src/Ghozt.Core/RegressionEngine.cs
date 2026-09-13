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
}
