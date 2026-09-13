using System.IO;

namespace Ghozt.Core;

public sealed record ManifestFeature
{
    public required string FeatureId { get; init; }
    public required string Name { get; init; }
    public required Severity Risk { get; init; }
    public required IReadOnlyList<string> Preconditions { get; init; }
    public required IReadOnlyList<string> Actions { get; init; }
    public required IReadOnlyList<string> ExpectedStates { get; init; }
    public required IReadOnlyList<string> FailureStates { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public required IReadOnlyList<string> Oracles { get; init; }
    public required IReadOnlyList<string> EvidenceRequired { get; init; }
    public double Confidence { get; init; } = 1.0;
}

public sealed record BehaviourManifest
{
    public required string Id { get; init; }
    public required GhoztId TargetId { get; init; }
    public required string TargetName { get; init; }
    public required IReadOnlyList<ManifestFeature> Features { get; init; }
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.Now;
    public string SourcePath { get; init; } = "";
}

/// <summary>
/// Discovers and extracts behavioural manifests and contracts from target repositories and endpoints.
/// </summary>
public sealed class ManifestEngine
{
    private readonly GhoztStore _store;

    public ManifestEngine(GhoztStore store) => _store = store;

    /// <summary>
    /// Scans the target's repository or base address and extracts declared features, routes, and invariants into the State Lattice.
    /// </summary>
    public BehaviourManifest Discover(Target target)
    {
        var manifestId = $"MAN-{Math.Abs(target.Id.GetHashCode()) % 90000 + 10000:D5}";
        var features = new List<ManifestFeature>();

        var searchPath = !string.IsNullOrWhiteSpace(target.RepositoryPath) && Directory.Exists(target.RepositoryPath)
            ? target.RepositoryPath
            : !string.IsNullOrWhiteSpace(target.BaseAddress) && Directory.Exists(target.BaseAddress)
                ? target.BaseAddress
                : null;

        if (searchPath != null)
        {
            try
            {
                // Scan for source files, controllers, views, endpoints
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    MaxRecursionDepth = 4,
                };
                var files = Directory.EnumerateFiles(searchPath, "*.*", options)
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                    .Take(50)
                    .ToList();

                var ftrIndex = 1;
                foreach (var file in files)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.Contains("Workspace", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("View", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Service", StringComparison.OrdinalIgnoreCase))
                    {
                        var feature = new ManifestFeature
                        {
                            FeatureId = $"FTR-{ftrIndex:D4}",
                            Name = $"{name} Surface & Contract",
                            Risk = Severity.Medium,
                            Preconditions = ["target_process_running", "surface_visible"],
                            Actions = [$"navigate_to_{name.ToLowerInvariant()}", "verify_surface_contract"],
                            ExpectedStates = ["rendered_cleanly", "interactive"],
                            FailureStates = ["unhandled_exception_dialog", "unresponsive_hang"],
                            Permissions = ["UI_INTERACT", "READ_STATE"],
                            Oracles = ["surface_integrity", "vitality_oracle", "error_dialog_oracle"],
                            EvidenceRequired = ["screenshot", "ui_hierarchy", "blackbox_stream"],
                            Confidence = 0.95,
                        };
                        features.Add(feature);

                        // Populate into State Lattice in database
                        var nodeId = $"node-{name.ToLowerInvariant()}";
                        _store.Save(new LatticeNode
                        {
                            Id = nodeId,
                            TargetId = target.Id,
                            Label = name,
                            Kind = NodeKind.Feature,
                            State = "declared",
                            Provenance = $"manifest:{Path.GetFileName(file)}",
                            VisitCount = 0,
                            EvidenceCount = 0,
                            Detail = $"Discovered from {Path.GetFileName(file)} via static manifest scan.",
                            LastSeenAt = DateTimeOffset.Now,
                        });

                        ftrIndex++;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fall back to target metadata
            }
        }

        // If no file features were found, extract from target's kind and environment
        if (features.Count == 0)
        {
            features.Add(new ManifestFeature
            {
                FeatureId = "FTR-0001",
                Name = $"{target.Name} Core Lifecycle",
                Risk = Severity.High,
                Preconditions = ["network_or_process_available"],
                Actions = ["probe_target", "examine_state"],
                ExpectedStates = ["reachable", "healthy"],
                FailureStates = ["unreachable_timeout", "connection_refused"],
                Permissions = [target.Access.ToString().ToUpperInvariant()],
                Oracles = ["reachability_oracle", "vitality_oracle"],
                EvidenceRequired = ["capability_probe", "ledger_event"],
                Confidence = 0.90,
            });

            _store.Save(new LatticeNode
            {
                Id = $"node-{target.Id}-root",
                TargetId = target.Id,
                Label = $"{target.Name} Root",
                Kind = NodeKind.System,
                State = target.Reachable ? "observed" : "untested",
                Provenance = "manifest:target_registration",
                VisitCount = target.Reachable ? 1 : 0,
                EvidenceCount = 1,
                Detail = $"Declared manifest root for {target.Name} ({target.Kind}).",
                LastSeenAt = DateTimeOffset.Now,
            });
        }

        var manifest = new BehaviourManifest
        {
            Id = manifestId,
            TargetId = target.Id,
            TargetName = target.Name,
            Features = features,
            DiscoveredAt = DateTimeOffset.Now,
            SourcePath = searchPath ?? target.BaseAddress,
        };

        // Record manifest discovery to ledger
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "MAN.DISCOVER.OK",
            Message = $"Discovered behaviour manifest {manifest.Id} with {features.Count} feature(s).",
            Severity = Severity.Info,
            TargetId = target.Id,
            RelatedId = manifest.Id,
            Actor = "ManifestEngine",
            Detail = $"Discovered {features.Count} feature contracts from source: {manifest.SourcePath}",
        });

        return manifest;
    }
}
