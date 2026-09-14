using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// Exploration strategy controlling candidate action prioritisation (Patent §16 & §17).
/// </summary>
public enum ExplorationStrategy
{
    /// <summary>Prioritises the boundary of unvisited states and untested paths.</summary>
    Frontier,

    /// <summary>Guides exploration towards a specific goal node, endpoint or target state.</summary>
    GoalDirected,

    /// <summary>Injects boundary conditions, malformed inputs, and stress patterns to probe failure modes.</summary>
    Adversarial,
}

/// <summary>
/// The concrete class of interaction represented by a candidate action.
/// </summary>
public enum ActionKind
{
    Inspect,
    Navigate,
    Click,
    Type,
    Invoke,
    Scroll,
    ApiCall,
    FuzzPayload,
    StateReset,
}

/// <summary>
/// Tunable weights for candidate-action scoring formula S(a) (Patent §17).
/// S(a) = w_u * U(s') + w_d * D(a) + w_c * C(a) - w_r * R(a) - w_h * H(s, a)
/// </summary>
public sealed record ExplorationWeights
{
    public double WeightUnvisited { get; init; } = 1.0;
    public double WeightDepthDiversity { get; init; } = 0.5;
    public double WeightCoverage { get; init; } = 0.8;
    public double WeightRisk { get; init; } = 1.5;
    public double WeightHistory { get; init; } = 1.2;

    public static ExplorationWeights ForStrategy(ExplorationStrategy strategy) => strategy switch
    {
        ExplorationStrategy.Frontier => new()
        {
            WeightUnvisited = 1.5,
            WeightDepthDiversity = 0.8,
            WeightCoverage = 1.0,
            WeightRisk = 1.2,
            WeightHistory = 1.5,
        },
        ExplorationStrategy.GoalDirected => new()
        {
            WeightUnvisited = 0.8,
            WeightDepthDiversity = 1.2,
            WeightCoverage = 0.6,
            WeightRisk = 1.0,
            WeightHistory = 1.0,
        },
        ExplorationStrategy.Adversarial => new()
        {
            WeightUnvisited = 1.0,
            WeightDepthDiversity = 0.6,
            WeightCoverage = 1.4,
            WeightRisk = 0.5, // Controlled stress tolerance
            WeightHistory = 1.8,
        },
        _ => new(),
    };
}

/// <summary>
/// A candidate transition or stimulus proposed for autonomous execution.
/// </summary>
public sealed record CandidateAction
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string TargetNodeId { get; init; }
    public required ActionKind Kind { get; init; }
    public required string Description { get; init; }
    public double BaseRisk { get; init; }
    public bool MutatesState { get; init; }
    public bool IsDestructive { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Outcome of scoring and executing a single candidate action.
/// </summary>
public sealed record ExplorationStepResult
{
    public required CandidateAction Action { get; init; }
    public required double Score { get; init; }
    public required bool Executed { get; init; }
    public required string ResultStateId { get; init; }
    public string Observation { get; init; } = "";
    public string PruneReason { get; init; } = "";
}

/// <summary>
/// Autonomous Exploration Engine & Candidate-Action Scoring (Patent §16 & §17).
/// Evaluates reachable state frontiers, enforces access-class risk boundaries,
/// suppresses cycles, and drives autonomous examination across targets.
/// </summary>
public sealed class ExplorationEngine
{
    private readonly GhoztStore _store;

    public ExplorationEngine(GhoztStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Evaluates candidate-action score S(a) using the patent objective formula:
    /// S(a) = w_u * U(s') + w_d * D(a) + w_c * C(a) - w_r * R(a) - w_h * H(s, a)
    /// </summary>
    public double ScoreAction(
        CandidateAction action,
        Target target,
        LatticeNode? targetNode,
        int historyCount,
        ExplorationWeights? customWeights = null,
        ExplorationStrategy strategy = ExplorationStrategy.Frontier)
    {
        var weights = customWeights ?? ExplorationWeights.ForStrategy(strategy);

        // 1. Unvisited Bonus U(s')
        double u;
        if (targetNode == null || targetNode.VisitCount == 0)
        {
            u = 1.0;
        }
        else
        {
            u = 1.0 / (targetNode.VisitCount + 1.0);
        }

        // 2. Depth / Diversity Bonus D(a)
        var d = action.Kind switch
        {
            ActionKind.FuzzPayload => 1.0,
            ActionKind.ApiCall => 0.9,
            ActionKind.Invoke => 0.85,
            ActionKind.Click or ActionKind.Type => 0.7,
            ActionKind.Navigate => 0.6,
            ActionKind.Inspect => 0.4,
            _ => 0.5,
        };

        // 3. Coverage Value C(a)
        double c;
        if (targetNode == null)
        {
            c = 1.0;
        }
        else
        {
            c = targetNode.State.ToLowerInvariant() switch
            {
                "untested" => 1.0,
                "fault" or "deviation" => 0.85,
                "observed" => 0.3,
                "verified" => 0.05,
                _ => 0.5,
            };
        }

        // 4. Risk Penalty R(a)
        var r = CalculateRiskPenalty(action, target);

        // 5. History Penalty H(s, a)
        var h = historyCount * 0.25;

        // Final score S(a)
        var score = (weights.WeightUnvisited * u)
                  + (weights.WeightDepthDiversity * d)
                  + (weights.WeightCoverage * c)
                  - (weights.WeightRisk * r)
                  - (weights.WeightHistory * h);

        return Math.Round(score, 4);
    }

    /// <summary>
    /// Computes risk penalty based on Target AccessClass and action destructiveness.
    /// </summary>
    public static double CalculateRiskPenalty(CandidateAction action, Target target)
    {
        return target.Access switch
        {
            AccessClass.ObserveOnly => (action.MutatesState || action.IsDestructive || action.Kind is not (ActionKind.Inspect or ActionKind.Navigate))
                ? 2.5 // Strictly punitive
                : action.BaseRisk,

            AccessClass.Interact => action.IsDestructive
                ? 2.5
                : action.MutatesState
                    ? 1.0 + action.BaseRisk
                    : 0.1 + action.BaseRisk,

            AccessClass.StateChanging => action.IsDestructive
                ? 2.0
                : (action.MutatesState ? 0.2 : 0.1) + action.BaseRisk,

            AccessClass.Destructive => action.BaseRisk,

            _ => 1.0,
        };
    }

    /// <summary>
    /// Enforces access class invariants. Returns false if the action violates safety boundaries.
    /// </summary>
    public static bool IsPermitted(CandidateAction action, Target target, bool allowDestructive = false)
    {
        // ObserveOnly targets never execute mutations or interaction clicks
        if (target.Access == AccessClass.ObserveOnly)
        {
            if (action.MutatesState || action.IsDestructive)
            {
                return false;
            }

            if (action.Kind is ActionKind.Click or ActionKind.Type or ActionKind.FuzzPayload or ActionKind.StateReset)
            {
                return false;
            }
        }

        // Interact targets can drive UI/APIs, but cannot perform persistent mutations or destructive sweeps
        if (target.Access == AccessClass.Interact)
        {
            if (action.IsDestructive)
            {
                return false;
            }

            if (action.MutatesState)
            {
                return false;
            }
        }

        // StateChanging targets permit mutations, but require allowDestructive flag for destructive actions
        if (target.Access == AccessClass.StateChanging)
        {
            if (action.IsDestructive && !allowDestructive)
            {
                return false;
            }
        }

        // Destructive actions require explicit authorization
        if (action.IsDestructive && !allowDestructive && target.Access != AccessClass.Destructive)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Generates candidate actions from the current state lattice and strategy.
    /// </summary>
    public IReadOnlyList<CandidateAction> GenerateCandidates(
        Target target,
        string currentNodeId,
        ExplorationStrategy strategy = ExplorationStrategy.Frontier,
        string? goalNodeId = null)
    {
        var candidates = new List<CandidateAction>();
        var nodes = _store.Nodes(target.Id);
        var edges = _store.Edges(target.Id);
        var nodeMap = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        var currentNode = nodeMap.GetValueOrDefault(currentNodeId);
        if (currentNode == null)
        {
            // Default to root node or first available
            currentNode = nodes.FirstOrDefault() ?? new LatticeNode
            {
                Id = $"node-{target.Id}-root",
                TargetId = target.Id,
                Label = $"{target.Name} Root",
                Kind = NodeKind.System,
                State = "observed",
                Provenance = "exploration:boot",
            };
        }

        // 1. Existing outward edges from current node
        var outgoing = edges.Where(e => e.FromId == currentNode.Id).ToList();
        var idx = 1;
        foreach (var edge in outgoing)
        {
            var targetNode = nodeMap.GetValueOrDefault(edge.ToId);
            var isMutating = edge.Label.Contains("submit", StringComparison.OrdinalIgnoreCase) ||
                             edge.Label.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                             edge.Label.Contains("save", StringComparison.OrdinalIgnoreCase);

            candidates.Add(new CandidateAction
            {
                Id = $"ACT-{target.Id}-{idx++:D4}",
                SourceNodeId = currentNode.Id,
                TargetNodeId = edge.ToId,
                Kind = isMutating ? ActionKind.Invoke : ActionKind.Navigate,
                Description = $"Traverse edge: {edge.Label} -> {targetNode?.Label ?? edge.ToId}",
                BaseRisk = isMutating ? 0.3 : 0.05,
                MutatesState = isMutating,
                IsDestructive = edge.Label.Contains("delete", StringComparison.OrdinalIgnoreCase),
            });
        }

        // 2. Discover unvisited frontier nodes reachable from target
        var unvisited = nodes.Where(n => n.VisitCount == 0 && n.Id != currentNode.Id).Take(5).ToList();
        foreach (var u in unvisited)
        {
            candidates.Add(new CandidateAction
            {
                Id = $"ACT-{target.Id}-{idx++:D4}",
                SourceNodeId = currentNode.Id,
                TargetNodeId = u.Id,
                Kind = u.Kind == NodeKind.Endpoint ? ActionKind.ApiCall : ActionKind.Navigate,
                Description = $"Explore unvisited node: {u.Label} ({u.Kind})",
                BaseRisk = 0.1,
                MutatesState = false,
                IsDestructive = false,
            });
        }

        // 3. Strategy-specific generation
        if (strategy == ExplorationStrategy.Adversarial)
        {
            // Boundary & fuzz actions
            candidates.Add(new CandidateAction
            {
                Id = $"ACT-{target.Id}-ADV-01",
                SourceNodeId = currentNode.Id,
                TargetNodeId = currentNode.Id,
                Kind = ActionKind.FuzzPayload,
                Description = "Boundary check: inject max-length 4096-character buffer payload",
                BaseRisk = 0.4,
                MutatesState = false,
                IsDestructive = false,
                Parameters = new Dictionary<string, string> { ["payload"] = new string('A', 4096), ["type"] = "buffer-limit" },
            });

            candidates.Add(new CandidateAction
            {
                Id = $"ACT-{target.Id}-ADV-02",
                SourceNodeId = currentNode.Id,
                TargetNodeId = currentNode.Id,
                Kind = ActionKind.FuzzPayload,
                Description = "Input sanitisation check: inject SQL probe \"' OR '1'='1\"",
                BaseRisk = 0.5,
                MutatesState = false,
                IsDestructive = false,
                Parameters = new Dictionary<string, string> { ["payload"] = "' OR '1'='1' --", ["type"] = "sqli-probe" },
            });

            candidates.Add(new CandidateAction
            {
                Id = $"ACT-{target.Id}-ADV-03",
                SourceNodeId = currentNode.Id,
                TargetNodeId = currentNode.Id,
                Kind = ActionKind.FuzzPayload,
                Description = "Unicode encoding check: inject null bytes and surrogate pairs",
                BaseRisk = 0.3,
                MutatesState = false,
                IsDestructive = false,
                Parameters = new Dictionary<string, string> { ["payload"] = "\u0000\uFFFF\uD83D\uDCA9", ["type"] = "unicode-probe" },
            });
        }
        else if (strategy == ExplorationStrategy.GoalDirected && !string.IsNullOrWhiteSpace(goalNodeId))
        {
            var goalNode = nodeMap.GetValueOrDefault(goalNodeId);
            if (goalNode != null)
            {
                candidates.Add(new CandidateAction
                {
                    Id = $"ACT-{target.Id}-GOAL-01",
                    SourceNodeId = currentNode.Id,
                    TargetNodeId = goalNode.Id,
                    Kind = ActionKind.Navigate,
                    Description = $"Directed path towards goal: {goalNode.Label}",
                    BaseRisk = 0.1,
                    MutatesState = false,
                    IsDestructive = false,
                });
            }
        }

        return candidates;
    }

    /// <summary>
    /// Executes one bounded exploration step: scores candidates, selects optimal permitted action,
    /// updates the state lattice, and appends a BlackBox event.
    /// </summary>
    public ExplorationStepResult Step(
        Target target,
        string currentNodeId,
        ExplorationStrategy strategy = ExplorationStrategy.Frontier,
        string? goalNodeId = null,
        bool allowDestructive = false)
    {
        var candidates = GenerateCandidates(target, currentNodeId, strategy, goalNodeId);
        if (candidates.Count == 0)
        {
            return new ExplorationStepResult
            {
                Action = new CandidateAction
                {
                    Id = "ACT-NONE",
                    SourceNodeId = currentNodeId,
                    TargetNodeId = currentNodeId,
                    Kind = ActionKind.Inspect,
                    Description = "No candidates available on frontier",
                },
                Score = 0.0,
                Executed = false,
                ResultStateId = currentNodeId,
                PruneReason = "No candidate actions generated",
            };
        }

        var nodes = _store.Nodes(target.Id).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var scoredList = new List<(CandidateAction Action, double Score, bool Permitted, string Reason)>();

        foreach (var action in candidates)
        {
            var permitted = IsPermitted(action, target, allowDestructive);
            if (!permitted)
            {
                scoredList.Add((action, -999.0, false, $"Pruned by Target AccessClass ({target.Access})"));
                continue;
            }

            var targetNode = nodes.GetValueOrDefault(action.TargetNodeId);
            var historyCount = targetNode?.VisitCount ?? 0;
            var score = ScoreAction(action, target, targetNode, historyCount, strategy: strategy);
            scoredList.Add((action, score, true, ""));
        }

        var selected = scoredList.Where(s => s.Permitted).OrderByDescending(s => s.Score).FirstOrDefault();
        if (selected.Action == null)
        {
            var pruned = scoredList.FirstOrDefault();
            return new ExplorationStepResult
            {
                Action = pruned.Action,
                Score = pruned.Score,
                Executed = false,
                ResultStateId = currentNodeId,
                PruneReason = pruned.Reason,
            };
        }

        // Execute action: evolve state lattice
        var targetNodeId = selected.Action.TargetNodeId;
        var existingTargetNode = nodes.GetValueOrDefault(targetNodeId);

        if (existingTargetNode != null)
        {
            _store.Save(existingTargetNode with
            {
                VisitCount = existingTargetNode.VisitCount + 1,
                State = "observed",
                LastSeenAt = DateTimeOffset.Now,
                Detail = $"{existingTargetNode.Detail} (Visited via {selected.Action.Kind})".Trim(),
            });
        }
        else
        {
            _store.Save(new LatticeNode
            {
                Id = targetNodeId,
                TargetId = target.Id,
                Label = selected.Action.Description,
                Kind = selected.Action.Kind == ActionKind.ApiCall ? NodeKind.Endpoint : NodeKind.Feature,
                State = "observed",
                Provenance = $"exploration:{strategy}",
                ParentId = currentNodeId,
                VisitCount = 1,
                LastSeenAt = DateTimeOffset.Now,
            });
        }

        // Record traversal edge
        _store.Save(new LatticeEdge
        {
            TargetId = target.Id,
            FromId = currentNodeId,
            ToId = targetNodeId,
            Label = selected.Action.Kind.ToString(),
            Relationship = "observed",
            TraversalCount = 1,
        });

        // Record BlackBox Event
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "SYS.EXPLORE.ACTION",
            Message = $"Autonomous exploration executed {selected.Action.Kind} ({selected.Action.Description}) with score {selected.Score:F2}",
            Severity = Severity.Info,
            TargetId = target.Id,
            Actor = "ExplorationEngine",
            Detail = JsonSerializer.Serialize(new
            {
                selected.Action.Id,
                selected.Action.Kind,
                selected.Action.SourceNodeId,
                selected.Action.TargetNodeId,
                selected.Score,
                Strategy = strategy.ToString(),
            }),
        });

        return new ExplorationStepResult
        {
            Action = selected.Action,
            Score = selected.Score,
            Executed = true,
            ResultStateId = targetNodeId,
            Observation = $"State transitioned to {targetNodeId}; lattice node updated.",
        };
    }
}
