using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// The three independent dimensions of software truth (Patent §14).
/// </summary>
public enum TruthSourceKind
{
    /// <summary>Declared product specifications, OpenAPI manifests, and documentation.</summary>
    DeclaredIntent,

    /// <summary>Static source code AST, controllers, endpoints, and event listeners.</summary>
    SourceReality,

    /// <summary>Dynamically observed runtime behavior, responses, and UI elements.</summary>
    RuntimeObservation,
}

/// <summary>
/// Classification of multi-source contract drift (Patent §14 & §15).
/// </summary>
public enum TruthDriftCategory
{
    /// <summary>Declared in Intent, but absent in Source or unreachable at Runtime.</summary>
    MissingImplementation,

    /// <summary>Present in Source or Runtime, but absent in Declared Intent (shadow route / hidden UI).</summary>
    UndeclaredBehaviour,

    /// <summary>Contract specification contradicts test assertions or oracle expectations.</summary>
    OracleConflict,

    /// <summary>Runtime observation consistently diverges from manifest contract.</summary>
    ObservedDeviation,
}

/// <summary>
/// A specific instance of contract drift discovered during truth triangulation.
/// </summary>
public sealed record TruthDriftItem
{
    public required string EntityId { get; init; }
    public required string EntityName { get; init; }
    public required TruthDriftCategory Category { get; init; }
    public required Severity Severity { get; init; }
    public required string Description { get; init; }
    public string IntentSpecification { get; init; } = "";
    public string SourceRepresentation { get; init; } = "";
    public string RuntimeObservation { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
}

/// <summary>
/// Comprehensive behavioural truth triangulation report across all three dimensions.
/// </summary>
public sealed record TruthTriangulationReport
{
    public required GhoztId TargetId { get; init; }
    public required string TargetName { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required int TotalEntitiesEvaluated { get; init; }
    public required int DriftCount { get; init; }
    public required IReadOnlyList<TruthDriftItem> Drifts { get; init; }
    public required IReadOnlyDictionary<Severity, int> SeverityBreakdown { get; init; }
    public required string Summary { get; init; }

    public bool HasCriticalDrift => Drifts.Any(d => d.Severity is Severity.Critical or Severity.High);
}

/// <summary>
/// Behavioural Truth Triangulation Engine (Patent §14 & §15).
/// Reconciles Declared Intent, Source Reality, and Runtime Observation.
/// Discovers undeclared shadow behaviors, missing features, and oracle conflicts.
/// </summary>
public sealed class TruthTriangulationEngine
{
    private readonly GhoztStore _store;

    public TruthTriangulationEngine(GhoztStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Executes truth triangulation for the target by cross-referencing its Behaviour Manifest,
    /// known source endpoints, and live State Lattice nodes.
    /// </summary>
    public TruthTriangulationReport Triangulate(
        Target target,
        BehaviourManifest? manifest = null,
        IReadOnlyList<string>? sourceEndpoints = null)
    {
        var timestamp = DateTimeOffset.Now;
        var drifts = new List<TruthDriftItem>();

        var latticeNodes = _store.Nodes(target.Id);
        var findings = _store.Findings(target.Id);

        // 1. Collect Declared Intent (Features & Endpoints from Manifest)
        var declaredFeatures = manifest?.Features ?? [];
        var declaredRouteMap = new Dictionary<string, ManifestFeature>(StringComparer.OrdinalIgnoreCase);
        var declaredPathMap = new Dictionary<string, ManifestFeature>(StringComparer.OrdinalIgnoreCase);
        foreach (var ftr in declaredFeatures)
        {
            if (!string.IsNullOrWhiteSpace(ftr.RoutePath))
            {
                var key = $"{ftr.HttpMethod}:{ftr.RoutePath}".Trim(':').ToLowerInvariant();
                declaredRouteMap[key] = ftr;
                declaredPathMap[ftr.RoutePath.ToLowerInvariant()] = ftr;
            }
        }

        // 2. Collect Source Reality (Explicitly provided or extracted)
        var sourceRouteSet = new HashSet<string>(sourceEndpoints ?? [], StringComparer.OrdinalIgnoreCase);

        // 3. Collect Runtime Observations (Lattice Nodes representing endpoints & features)
        var runtimeNodeMap = latticeNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var observedEndpoints = latticeNodes
            .Where(n => n.Kind == NodeKind.Endpoint && n.VisitCount > 0)
            .ToDictionary(n => n.Label.ToLowerInvariant(), n => n, StringComparer.OrdinalIgnoreCase);

        var evaluatedEntities = 0;

        // --- CHECK A: Missing Implementation ---
        // Declared in Intent, but missing in Source Reality or unreached at Runtime
        foreach (var (routeKey, ftr) in declaredRouteMap)
        {
            evaluatedEntities++;
            var inSource = sourceRouteSet.Count == 0 || sourceRouteSet.Contains(routeKey) || sourceRouteSet.Contains(ftr.RoutePath);
            var inRuntime = observedEndpoints.ContainsKey(routeKey) || observedEndpoints.ContainsKey(ftr.RoutePath) || observedEndpoints.ContainsKey(ftr.Name);

            if (!inSource)
            {
                drifts.Add(new TruthDriftItem
                {
                    EntityId = ftr.FeatureId,
                    EntityName = ftr.Name,
                    Category = TruthDriftCategory.MissingImplementation,
                    Severity = Severity.High,
                    Description = $"Feature '{ftr.Name}' declared in manifest ({ftr.RoutePath}) has no matching implementation in source code.",
                    IntentSpecification = $"{ftr.HttpMethod} {ftr.RoutePath} (Risk: {ftr.Risk})",
                    SourceRepresentation = "NOT_FOUND_IN_SOURCE",
                    RuntimeObservation = inRuntime ? "Observed dynamically" : "Unreached",
                    RecommendedAction = "Implement corresponding route in API controller or retire obsolete specification.",
                });
            }
            else if (!inRuntime && ftr.Risk >= Severity.Medium)
            {
                // In source but untested/unreached in runtime
                drifts.Add(new TruthDriftItem
                {
                    EntityId = ftr.FeatureId,
                    EntityName = ftr.Name,
                    Category = TruthDriftCategory.MissingImplementation,
                    Severity = Severity.Low,
                    Description = $"Feature '{ftr.Name}' ({ftr.RoutePath}) exists in specification and source, but has not yet been observed at runtime.",
                    IntentSpecification = $"{ftr.HttpMethod} {ftr.RoutePath}",
                    SourceRepresentation = "Implemented in source",
                    RuntimeObservation = "UNREACHED_IN_RUNTIME",
                    RecommendedAction = "Schedule automated sweep to exercise this route and verify reachability.",
                });
            }
        }

        // --- CHECK B: Undeclared Behaviour (Shadow Endpoints) ---
        // Present in Source Reality or Runtime, but absent in Declared Intent
        foreach (var srcRoute in sourceRouteSet)
        {
            evaluatedEntities++;
            var isDeclared = declaredRouteMap.ContainsKey(srcRoute) || declaredPathMap.ContainsKey(srcRoute);
            if (!isDeclared)
            {
                drifts.Add(new TruthDriftItem
                {
                    EntityId = $"SHADOW-{Math.Abs(srcRoute.GetHashCode()) % 90000 + 10000:D5}",
                    EntityName = srcRoute,
                    Category = TruthDriftCategory.UndeclaredBehaviour,
                    Severity = Severity.High,
                    Description = $"Shadow route '{srcRoute}' exists in source code but is absent from the declared manifest.",
                    IntentSpecification = "UNDECLARED_IN_INTENT",
                    SourceRepresentation = srcRoute,
                    RuntimeObservation = observedEndpoints.ContainsKey(srcRoute) ? "OBSERVED_IN_RUNTIME" : "Unobserved",
                    RecommendedAction = "Register endpoint in OpenAPI manifest or remove unauthorized route.",
                });
            }
        }

        foreach (var (obsKey, obsNode) in observedEndpoints)
        {
            var isDeclared = declaredRouteMap.ContainsKey(obsKey) || declaredPathMap.ContainsKey(obsKey);
            if (declaredRouteMap.Count > 0 && !isDeclared && !sourceRouteSet.Contains(obsKey))
            {
                evaluatedEntities++;
                drifts.Add(new TruthDriftItem
                {
                    EntityId = obsNode.Id,
                    EntityName = obsNode.Label,
                    Category = TruthDriftCategory.UndeclaredBehaviour,
                    Severity = Severity.Critical,
                    Description = $"Runtime endpoint '{obsNode.Label}' was actively observed and exercised, but is neither declared in intent nor mapped to source code.",
                    IntentSpecification = "UNDECLARED",
                    SourceRepresentation = "UNKNOWN",
                    RuntimeObservation = $"Observed {obsNode.VisitCount} times; State: {obsNode.State}",
                    RecommendedAction = "Investigate rogue or unmanaged endpoint immediately; check for unauthorized telemetry or backdoor.",
                });
            }
        }

        // --- CHECK C: Oracle Conflicts & Observed Deviations ---
        foreach (var finding in findings)
        {
            evaluatedEntities++;
            if (finding.Severity >= Severity.Medium)
            {
                var isOracleConflict = finding.Title.Contains("contract", StringComparison.OrdinalIgnoreCase) ||
                                       finding.Title.Contains("schema", StringComparison.OrdinalIgnoreCase) ||
                                       finding.Title.Contains("expected status", StringComparison.OrdinalIgnoreCase);

                drifts.Add(new TruthDriftItem
                {
                    EntityId = finding.Id.ToString(),
                    EntityName = finding.Title,
                    Category = isOracleConflict ? TruthDriftCategory.OracleConflict : TruthDriftCategory.ObservedDeviation,
                    Severity = finding.Severity,
                    Description = $"Runtime divergence on node '{finding.NodeId}': Actual [{finding.Actual}] != Expected [{finding.Expected}].",
                    IntentSpecification = finding.Expected,
                    SourceRepresentation = "Implemented",
                    RuntimeObservation = finding.Actual,
                    RecommendedAction = isOracleConflict
                        ? "Reconcile test assertion with formal contract specification."
                        : "Remediate runtime defect or handle unexpected state.",
                });
            }
        }

        if (evaluatedEntities == 0)
        {
            evaluatedEntities = Math.Max(1, latticeNodes.Count);
        }

        // Calculate severity breakdown
        var breakdown = new Dictionary<Severity, int>
        {
            [Severity.Critical] = drifts.Count(d => d.Severity == Severity.Critical),
            [Severity.High] = drifts.Count(d => d.Severity == Severity.High),
            [Severity.Medium] = drifts.Count(d => d.Severity == Severity.Medium),
            [Severity.Low] = drifts.Count(d => d.Severity == Severity.Low),
            [Severity.Info] = drifts.Count(d => d.Severity == Severity.Info),
        };

        var summary = drifts.Count == 0
            ? $"Full 3-way truth concordance across {evaluatedEntities} entities. Zero contract drift detected."
            : $"Truth triangulation identified {drifts.Count} drift issues across {evaluatedEntities} entities: " +
              $"{breakdown[Severity.Critical]} Critical, {breakdown[Severity.High]} High, {breakdown[Severity.Medium]} Medium.";

        var report = new TruthTriangulationReport
        {
            TargetId = target.Id,
            TargetName = target.Name,
            GeneratedAt = timestamp,
            TotalEntitiesEvaluated = evaluatedEntities,
            DriftCount = drifts.Count,
            Drifts = drifts,
            SeverityBreakdown = breakdown,
            Summary = summary,
        };

        // Record BlackBox Event
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = timestamp,
            Code = drifts.Count > 0 ? "SYS.TRUTH.DRIFT" : "SYS.TRUTH.TRIANGULATED",
            Message = summary,
            Severity = report.HasCriticalDrift ? Severity.High : Severity.Info,
            TargetId = target.Id,
            Actor = "TruthTriangulationEngine",
            Detail = JsonSerializer.Serialize(new
            {
                report.TargetId,
                report.TotalEntitiesEvaluated,
                report.DriftCount,
                report.SeverityBreakdown,
            }),
        });

        return report;
    }
}
