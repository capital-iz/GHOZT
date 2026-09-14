using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// Summary quality telemetry for one target under portfolio management.
/// </summary>
public sealed record TargetQualitySummary
{
    public required GhoztId TargetId { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required TargetEnvironment Environment { get; init; }
    public required AccessClass Access { get; init; }
    public required string BuildLabel { get; init; }
    public required bool Reachable { get; init; }
    public required ClearanceOutcome ClearanceStatus { get; init; }
    public required int NodeCount { get; init; }
    public required int VerifiedNodeCount { get; init; }
    public required int OpenFindingCount { get; init; }
    public required int CriticalFindingCount { get; init; }
    public required int EvidenceCount { get; init; }
    public required double CoveragePercentage { get; init; }
    public required double UnknownRiskPercentage { get; init; }
    public DateTimeOffset? LastSweepAt { get; init; }
}

/// <summary>
/// Executive quality portfolio report covering all registered targets across the application estate.
/// </summary>
public sealed record PortfolioRollupReport
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required int TotalTargets { get; init; }
    public required int ProductionTargets { get; init; }
    public required int ClearedTargets { get; init; }
    public required int DeniedOrBlockedTargets { get; init; }
    public required int TotalCriticalFindings { get; init; }
    public required double AverageCoverage { get; init; }
    public required double AverageUnknownRisk { get; init; }
    public required IReadOnlyList<TargetQualitySummary> Targets { get; init; }
    public required IReadOnlyList<string> StrategicRiskWarnings { get; init; }
    public required string EstateReadinessIndex { get; init; }
}

/// <summary>
/// Multi-Target Portfolio Quality Rollup Engine (Patent §41 & P511).
/// Evaluates cross-product quality, clearance status, and estate-wide regression health.
/// </summary>
public sealed class PortfolioRollupEngine
{
    private readonly GhoztStore _store;
    private readonly ClearanceEngine _clearanceEngine;

    public PortfolioRollupEngine(GhoztStore store)
    {
        _store = store;
        _clearanceEngine = new ClearanceEngine(store);
    }

    /// <summary>
    /// Evaluates all registered targets and generates a comprehensive executive portfolio rollup report.
    /// </summary>
    public PortfolioRollupReport GenerateRollup()
    {
        var targets = _store.Targets();
        var summaries = new List<TargetQualitySummary>();
        var warnings = new List<string>();

        var totalCritical = 0;
        var totalCoverageSum = 0.0;
        var totalUnknownSum = 0.0;

        foreach (var target in targets)
        {
            var nodes = _store.Nodes(target.Id);
            var findings = _store.Findings(target.Id);
            var evidence = _store.EvidenceItems(target.Id);

            var decision = _clearanceEngine.EvaluateTarget(target);

            var totalNodes = Math.Max(1, nodes.Count);
            var verifiedCount = nodes.Count(n => n.State.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
                                                 n.State.Equals("observed", StringComparison.OrdinalIgnoreCase));
            var coverage = Math.Clamp((double)verifiedCount / totalNodes, 0.0, 1.0);
            var unknownRisk = Math.Clamp(1.0 - coverage, 0.0, 1.0);

            var openFindings = findings.Where(f => f.Maturity != FindingMaturity.Verified).ToList();
            var criticalCount = openFindings.Count(f => f.Severity == Severity.Critical);

            totalCritical += criticalCount;
            totalCoverageSum += coverage;
            totalUnknownSum += unknownRisk;

            var summary = new TargetQualitySummary
            {
                TargetId = target.Id,
                Name = target.Name,
                Kind = target.Kind,
                Environment = target.Environment,
                Access = target.Access,
                BuildLabel = string.IsNullOrWhiteSpace(target.BuildLabel) ? "dev" : target.BuildLabel,
                Reachable = target.Reachable,
                ClearanceStatus = decision.Outcome,
                NodeCount = nodes.Count,
                VerifiedNodeCount = verifiedCount,
                OpenFindingCount = openFindings.Count,
                CriticalFindingCount = criticalCount,
                EvidenceCount = evidence.Count,
                CoveragePercentage = Math.Round(coverage * 100.0, 1),
                UnknownRiskPercentage = Math.Round(unknownRisk * 100.0, 1),
                LastSweepAt = target.LastSweepAt,
            };

            summaries.Add(summary);

            // Risk warnings
            if (target.Environment == TargetEnvironment.Production && decision.Outcome != ClearanceOutcome.Cleared)
            {
                warnings.Add($"Production target '{target.Name}' ({target.Id}) release status is {decision.Outcome} (Requires urgent remediation).");
            }

            if (criticalCount > 0)
            {
                warnings.Add($"Target '{target.Name}' has {criticalCount} open critical defect(s) violating release quality floor.");
            }

            if (nodes.Count > 0 && verifiedCount == 0)
            {
                warnings.Add($"Target '{target.Name}' has 0 verified lattice nodes (100% unverified state space).");
            }
        }

        var count = Math.Max(1, summaries.Count);
        var avgCoverage = totalCoverageSum / count;
        var avgUnknown = totalUnknownSum / count;

        var prodTargets = summaries.Count(s => s.Environment == TargetEnvironment.Production);
        var cleared = summaries.Count(s => s.ClearanceStatus == ClearanceOutcome.Cleared);
        var deniedOrBlocked = summaries.Count(s => s.ClearanceStatus is ClearanceOutcome.Denied or ClearanceOutcome.Blocked);

        string estateIndex;
        if (totalCritical > 0 || summaries.Any(s => s.Environment == TargetEnvironment.Production && s.ClearanceStatus != ClearanceOutcome.Cleared))
        {
            estateIndex = "BLOCKED";
        }
        else if (deniedOrBlocked > 0 || warnings.Count > 0)
        {
            estateIndex = "AT_RISK";
        }
        else
        {
            estateIndex = "QUALIFIED";
        }

        var report = new PortfolioRollupReport
        {
            GeneratedAt = DateTimeOffset.Now,
            TotalTargets = summaries.Count,
            ProductionTargets = prodTargets,
            ClearedTargets = cleared,
            DeniedOrBlockedTargets = deniedOrBlocked,
            TotalCriticalFindings = totalCritical,
            AverageCoverage = Math.Round(avgCoverage * 100.0, 1),
            AverageUnknownRisk = Math.Round(avgUnknown * 100.0, 1),
            Targets = summaries,
            StrategicRiskWarnings = warnings,
            EstateReadinessIndex = estateIndex,
        };

        // Record BlackBox event
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "SYS.PORTFOLIO.ROLLUP",
            Message = $"Portfolio rollup generated for {summaries.Count} targets across estate. Readiness Index: {estateIndex}.",
            Severity = estateIndex == "QUALIFIED" ? Severity.Info : Severity.High,
            Actor = "PortfolioRollupEngine",
            Detail = JsonSerializer.Serialize(new
            {
                report.TotalTargets,
                report.ProductionTargets,
                report.ClearedTargets,
                report.DeniedOrBlockedTargets,
                report.EstateReadinessIndex,
                WarningCount = warnings.Count,
            }),
        });

        return report;
    }
}
