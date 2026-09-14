using System.IO;

namespace Ghozt.Core;

public enum ChangeRiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// A State Lattice node impacted by a source code modification.
/// </summary>
public sealed record ImpactedNode
{
    public required string NodeId { get; init; }
    public required string Label { get; init; }
    public required NodeKind Kind { get; init; }
    public required string MatchedPath { get; init; }
    public required string ImpactReason { get; init; }
}

/// <summary>
/// A formal Change Impact Assessment detailing affected nodes, regression guards, and prioritized checks (Patent §31).
/// </summary>
public sealed record ChangeImpactAssessment
{
    public required GhoztId TargetId { get; init; }
    public required IReadOnlyList<string> ModifiedFiles { get; init; }
    public required IReadOnlyList<ImpactedNode> ImpactedNodes { get; init; }
    public required IReadOnlyList<RegressionGuard> ImpactedGuards { get; init; }
    public required IReadOnlyList<string> RecommendedSweepChecks { get; init; }
    public required ChangeRiskLevel RiskLevel { get; init; }
    public required string Summary { get; init; }
    public required DateTimeOffset AssessedAt { get; init; }
}

/// <summary>
/// Correlates source code changes with State Lattice nodes and Regression Guards (Patent §31).
/// Eliminates redundant full sweeps by scoping examination to the verified change impact envelope.
/// </summary>
public sealed class ChangeImpactEngine
{
    private readonly GhoztStore _store;

    public ChangeImpactEngine(GhoztStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Analyzes a set of modified files or diff paths and computes the impacted verification envelope.
    /// </summary>
    public ChangeImpactAssessment AssessImpact(Target target, IReadOnlyList<string> modifiedFiles)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(modifiedFiles);

        var impactedNodes = new List<ImpactedNode>();
        var impactedGuards = new List<RegressionGuard>();
        var recommendedChecks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxRisk = ChangeRiskLevel.Low;

        var allNodes = _store.Nodes(target.Id);
        var regEngine = new RegressionEngine(_store);
        var allGuards = regEngine.GenerateGuards(target.Id);

        foreach (var file in modifiedFiles)
        {
            var normalized = file.Replace('\\', '/').ToLowerInvariant();
            var fileName = Path.GetFileName(normalized);

            // 1. Evaluate Risk Level Heuristics
            var fileRisk = ClassifyFileRisk(normalized);
            if (fileRisk > maxRisk)
            {
                maxRisk = fileRisk;
            }

            // 2. Correlate with State Lattice Nodes
            var matchedNode = false;
            foreach (var node in allNodes)
            {
                if (IsNodeImpactedByPath(node, normalized, fileName))
                {
                    impactedNodes.Add(new ImpactedNode
                    {
                        NodeId = node.Id,
                        Label = node.Label,
                        Kind = node.Kind,
                        MatchedPath = file,
                        ImpactReason = $"Direct path or keyword correspondence to lattice node '{node.Label}'.",
                    });
                    matchedNode = true;
                }
            }

            // If no explicit node matched, generate synthetic module node
            if (!matchedNode && !string.IsNullOrWhiteSpace(fileName))
            {
                impactedNodes.Add(new ImpactedNode
                {
                    NodeId = $"module:{Path.GetFileNameWithoutExtension(fileName)}",
                    Label = Path.GetFileNameWithoutExtension(fileName),
                    Kind = NodeKind.Feature,
                    MatchedPath = file,
                    ImpactReason = "Modified source compilation unit mapped to feature cluster.",
                });
            }

            // 3. Recommend prioritized sweep checks based on file domain
            RecommendChecksForPath(normalized, recommendedChecks);
        }

        // 4. Correlate with Active Regression Guards
        var targetFindings = _store.Findings(target.Id);
        foreach (var guard in allGuards)
        {
            var finding = guard.FindingId is { } fid ? targetFindings.FirstOrDefault(f => f.Id == fid) : null;
            var isImpacted = impactedNodes.Any(n =>
                (finding != null && n.NodeId.Equals(finding.NodeId, StringComparison.OrdinalIgnoreCase)) ||
                guard.Title.Contains(n.Label, StringComparison.OrdinalIgnoreCase) ||
                guard.Details.Contains(n.Label, StringComparison.OrdinalIgnoreCase));

            if (isImpacted && !impactedGuards.Any(g => g.Id == guard.Id))
            {
                impactedGuards.Add(guard);
            }
        }

        // If no specific checks identified, default to base verification
        if (recommendedChecks.Count == 0)
        {
            recommendedChecks.Add("SurfaceCheck");
            recommendedChecks.Add("ProcessSupervisionCheck");
        }

        var summary = $"Change Impact: {modifiedFiles.Count} file(s) modified, impacting {impactedNodes.Count} node(s) and {impactedGuards.Count} guard(s). Overall Risk: {maxRisk}.";

        var assessment = new ChangeImpactAssessment
        {
            TargetId = target.Id,
            ModifiedFiles = modifiedFiles,
            ImpactedNodes = impactedNodes.DistinctBy(n => n.NodeId).ToList(),
            ImpactedGuards = impactedGuards,
            RecommendedSweepChecks = recommendedChecks.ToList(),
            RiskLevel = maxRisk,
            Summary = summary,
            AssessedAt = DateTimeOffset.Now,
        };

        // Append to event ledger
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "SYS.CHANGE_IMPACT.ASSESSED",
            Message = summary,
            Severity = maxRisk >= ChangeRiskLevel.High ? Severity.High : Severity.Info,
            TargetId = target.Id,
            Actor = "ChangeImpactEngine@v1",
            Detail = $"files={modifiedFiles.Count}; impacted_nodes={impactedNodes.Count}; risk={maxRisk}",
        });

        return assessment;
    }

    private static ChangeRiskLevel ClassifyFileRisk(string normalized)
    {
        if (normalized.Contains("auth") || normalized.Contains("security") ||
            normalized.Contains("crypto") || normalized.Contains("secret") ||
            normalized.Contains("permission") || normalized.Contains("token"))
        {
            return ChangeRiskLevel.Critical;
        }

        if (normalized.Contains("kernel") || normalized.Contains("risk") ||
            normalized.Contains("database") || normalized.Contains("migration") ||
            normalized.Contains("execution") || normalized.Contains("order"))
        {
            return ChangeRiskLevel.High;
        }

        if (normalized.Contains("view") || normalized.Contains("xaml") ||
            normalized.Contains("theme") || normalized.Contains("component") ||
            normalized.Contains("controller") || normalized.Contains("service"))
        {
            return ChangeRiskLevel.Medium;
        }

        return ChangeRiskLevel.Low;
    }

    private static bool IsNodeImpactedByPath(LatticeNode node, string normalizedPath, string fileName)
    {
        var nodeLower = node.Id.ToLowerInvariant();
        var labelLower = node.Label.ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        if (nodeLower.Contains(baseName) || labelLower.Contains(baseName))
        {
            return true;
        }

        if (normalizedPath.Contains("auth") && (nodeLower.Contains("auth") || labelLower.Contains("auth")))
        {
            return true;
        }

        if (normalizedPath.Contains("theme") && (nodeLower.Contains("theme") || labelLower.Contains("token")))
        {
            return true;
        }

        if (normalizedPath.Contains("db") || normalizedPath.Contains("store") || normalizedPath.Contains("data"))
        {
            if (nodeLower.Contains("data") || nodeLower.Contains("store") || nodeLower.Contains("db"))
            {
                return true;
            }
        }

        return false;
    }

    private static void RecommendChecksForPath(string normalized, HashSet<string> checks)
    {
        if (normalized.Contains("auth") || normalized.Contains("sec"))
        {
            checks.Add("AuthSecurityCheck");
            checks.Add("PermissionEngineCheck");
        }

        if (normalized.Contains("db") || normalized.Contains("sql") || normalized.Contains("data"))
        {
            checks.Add("DataIntegrityCheck");
            checks.Add("ConservationBalanceCheck");
        }

        if (normalized.Contains("algo") || normalized.Contains("model") || normalized.Contains("calc"))
        {
            checks.Add("AlgorithmInvariantCheck");
            checks.Add("MetamorphicRelationCheck");
        }

        if (normalized.Contains("view") || normalized.Contains("xaml") || normalized.Contains("ui"))
        {
            checks.Add("UiaDiscoveryCheck");
            checks.Add("ContrastTokenCheck");
        }

        checks.Add("SurfaceCheck");
        checks.Add("ProcessSupervisionCheck");
    }
}
