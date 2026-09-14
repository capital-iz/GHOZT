using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// Specialized agent roles governing the intelligence plane under Patent §34 (Table 66).
/// </summary>
public enum AgentRole
{
    ManifestAgent,
    DiscoveryAgent,
    SweepPlanner,
    ExplorationAgent,
    ForensicsAgent,
    RepairAgent,
    RegressionAgent,
    ReportAgent,
    ClearanceEvaluator,
}

/// <summary>
/// Bounded agent permission levels matching Patent §34 (Table 69).
/// </summary>
public enum AgentPermissionLevel
{
    /// <summary>L0: Read-only source/runtime discovery. No target mutation.</summary>
    L0_Observe = 0,

    /// <summary>L1: UI/API actions in local/test environment. Cleanup required; no destructive side effects.</summary>
    L1_ControlledInteraction = 1,

    /// <summary>L2: State-changing data updates, restart services, apply fixtures. Approval required.</summary>
    L2_StateChanging = 2,

    /// <summary>L3: Destructive/chaos testing in disposable environment. Isolated blast radius.</summary>
    L3_Destructive = 3,

    /// <summary>L4: Branch-bounded AI/developer repair. Allowed paths, diff review, tests.</summary>
    L4_CodeModification = 4,

    /// <summary>L5: Production diagnostics or carefully bounded test. Strongest approval.</summary>
    L5_Production = 5,
}

/// <summary>
/// A candidate recommendation drafted by an agent under the canonical 7-step pipeline.
/// </summary>
public sealed record AgentRecommendation
{
    public required AgentRole Role { get; init; }

    public required string Summary { get; init; }

    public required string ProposedAction { get; init; }

    public AgentPermissionLevel RequiredLevel { get; init; } = AgentPermissionLevel.L0_Observe;

    public double Confidence { get; init; } = 1.0;

    public IReadOnlyList<string> CitedEvidenceIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CitedManifestEntityIds { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    public int EstimatedTokens { get; init; } = 100;
}

/// <summary>
/// Result of an automated grounding assay verifying claims against the evidence plane and manifest.
/// </summary>
public sealed record GroundingAssayResult
{
    public required bool Passed { get; init; }

    public required string Code { get; init; }

    public required string Detail { get; init; }

    public IReadOnlyList<string> GroundedEntities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> UngroundedClaims { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Supervisor verdict determining whether an agent recommendation may execute.
/// </summary>
public sealed record SupervisionVerdict
{
    public required bool Approved { get; init; }

    public required string Code { get; init; }

    public required string Rationale { get; init; }

    public required AgentRecommendation Recommendation { get; init; }

    public required GroundingAssayResult Grounding { get; init; }

    public Evidence? SealedEvidence { get; init; }

    public string? ExecutionOutput { get; init; }
}

/// <summary>
/// Tracks step, token, and time budgets for a supervised agent session.
/// </summary>
public sealed class AgentBudgetTracker
{
    public int MaxSteps { get; init; } = 50;

    public int MaxTokens { get; init; } = 100_000;

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(10);

    public int CurrentStep { get; private set; }

    public int TokensConsumed { get; private set; }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    public bool IsBudgetExhausted =>
        CurrentStep >= MaxSteps ||
        TokensConsumed >= MaxTokens ||
        (DateTimeOffset.Now - StartedAt) > MaxDuration;

    public string ExhaustionReason
    {
        get
        {
            if (CurrentStep >= MaxSteps) return $"Step limit of {MaxSteps} reached.";
            if (TokensConsumed >= MaxTokens) return $"Token budget of {MaxTokens:N0} exhausted ({TokensConsumed:N0} used).";
            if ((DateTimeOffset.Now - StartedAt) > MaxDuration) return $"Session duration limit of {MaxDuration.TotalSeconds:N0}s exceeded.";
            return "";
        }
    }

    public void Consume(int tokens)
    {
        CurrentStep++;
        TokensConsumed += tokens;
    }
}

/// <summary>
/// Supervised Multi-Agent Architecture Engine (Patent §34).
/// Mediates between agent intelligence and bounded execution authority, enforcing
/// role prohibitions, grounding assays, privilege bypass prevention, and budget limits.
/// </summary>
public sealed class MultiAgentSupervisor
{
    private readonly GhoztStore _store;

    public MultiAgentSupervisor(GhoztStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Evaluates an agent recommendation against the target, store, and governance policies.
    /// Executes through a bounded service only if completely cleared.
    /// </summary>
    public async Task<SupervisionVerdict> SuperviseAsync(
        Target target,
        AgentRecommendation recommendation,
        AgentBudgetTracker budget,
        GhoztId? sweepId = null,
        Func<AgentRecommendation, Task<string>>? boundedExecutor = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(budget);

        // 1. Budget Guardrail Check
        if (budget.IsBudgetExhausted)
        {
            return CreateRejectedVerdict(
                recommendation,
                "AGT.BUDGET_EXHAUSTED",
                $"Execution blocked: {budget.ExhaustionReason}",
                new GroundingAssayResult
                {
                    Passed = false,
                    Code = "AGT.GROUNDING.SKIPPED",
                    Detail = "Evaluation halted due to budget exhaustion.",
                });
        }

        budget.Consume(recommendation.EstimatedTokens);

        // 2. Role Prohibitions Check (Patent Table 66)
        var prohibitionViolation = CheckRoleProhibitions(recommendation);
        if (prohibitionViolation is not null)
        {
            return CreateRejectedVerdict(
                recommendation,
                "AGT.ROLE_PROHIBITION_VIOLATED",
                $"Role prohibition violated for {recommendation.Role}: {prohibitionViolation}",
                new GroundingAssayResult
                {
                    Passed = false,
                    Code = "AGT.GROUNDING.SKIPPED",
                    Detail = "Grounding skipped due to role prohibition violation.",
                });
        }

        // 3. Red-Team Privilege Bypass Defense (Table 69 & Target AccessClass)
        var bypassError = CheckPrivilegeBypass(target, recommendation);
        if (bypassError is not null)
        {
            return CreateRejectedVerdict(
                recommendation,
                "AGT.PRIVILEGE_BYPASS_BLOCKED",
                $"Privilege escalation blocked: {bypassError}",
                new GroundingAssayResult
                {
                    Passed = false,
                    Code = "AGT.GROUNDING.BLOCKED",
                    Detail = "Privilege escalation attempt detected and rejected.",
                });
        }

        // 4. Grounding Assay (Patent §34.3)
        var grounding = PerformGroundingAssay(target, recommendation);
        if (!grounding.Passed)
        {
            return CreateRejectedVerdict(
                recommendation,
                "AGT.GROUNDING.FAIL",
                $"Grounding assay failed: {grounding.Detail}",
                grounding);
        }

        // 5. Confidence Threshold & Policy Routing
        if (recommendation.Confidence < 0.50)
        {
            return CreateRejectedVerdict(
                recommendation,
                "AGT.LOW_CONFIDENCE_ROUTED",
                $"Confidence {recommendation.Confidence:P1} is below autonomous execution threshold (50.0%). Requires operator approval.",
                grounding);
        }

        // 6. Bounded Service Execution
        string? executionOutput = null;
        if (boundedExecutor is not null)
        {
            try
            {
                executionOutput = await boundedExecutor(recommendation).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return CreateRejectedVerdict(
                    recommendation,
                    "AGT.EXECUTION_ERROR",
                    $"Bounded executor threw exception: {ex.Message}",
                    grounding);
            }
        }
        else
        {
            executionOutput = $"Recommendation cleared for action '{recommendation.ProposedAction}' at level {recommendation.RequiredLevel}.";
        }

        // 7. Record Sealed Evidence (Step 7 of Canonical Pipeline)
        var evidence = SealSupervisionEvidence(target, recommendation, grounding, executionOutput, sweepId);

        return new SupervisionVerdict
        {
            Approved = true,
            Code = "AGT.APPROVED",
            Rationale = $"Recommendation cleared by supervisor under role {recommendation.Role} at level {recommendation.RequiredLevel}.",
            Recommendation = recommendation,
            Grounding = grounding,
            SealedEvidence = evidence,
            ExecutionOutput = executionOutput,
        };
    }

    /// <summary>
    /// Enforces the non-negotiable prohibited authorities defined in Patent Table 66.
    /// </summary>
    public static string? CheckRoleProhibitions(AgentRecommendation rec)
    {
        return rec.Role switch
        {
            AgentRole.ManifestAgent when rec.Parameters.ContainsKey("silent_approve") ||
                                         rec.ProposedAction.Contains("ApproveExpectedBehavior", StringComparison.OrdinalIgnoreCase) =>
                "Manifest Agent cannot silently approve expected behaviour.",

            AgentRole.DiscoveryAgent when rec.RequiredLevel > AgentPermissionLevel.L1_ControlledInteraction =>
                "Discovery Agent cannot exceed target observation and controlled discovery authority.",

            AgentRole.SweepPlanner when rec.Parameters.ContainsKey("auto_approve_high_risk") ||
                                       (rec.RequiredLevel >= AgentPermissionLevel.L3_Destructive && rec.Parameters.ContainsKey("self_approved")) =>
                "Sweep Planner cannot approve its own high-risk execution plan.",

            AgentRole.ExplorationAgent when rec.Parameters.ContainsKey("direct_tool_invocation") ||
                                           rec.Parameters.ContainsKey("bypass_policy_gate") =>
                "Exploration Agent cannot invoke tools directly outside the bounded service/policy gate.",

            AgentRole.ForensicsAgent when rec.ProposedAction.Contains("LabelCauseFinal", StringComparison.OrdinalIgnoreCase) &&
                                         rec.CitedEvidenceIds.Count == 0 =>
                "Forensics Agent cannot label cause final without required supporting evidence artefacts.",

            AgentRole.RepairAgent when rec.Parameters.ContainsKey("weaken_test") ||
                                       rec.Parameters.ContainsKey("modify_protected_branch") =>
                "Repair Agent cannot modify unauthorised branches or weaken test assertions.",

            AgentRole.RegressionAgent when rec.ProposedAction.Contains("RetireProtection", StringComparison.OrdinalIgnoreCase) &&
                                           !rec.Parameters.ContainsKey("formal_decision_id") =>
                "Regression Agent cannot retire test protection without a recorded formal decision.",

            AgentRole.ReportAgent when rec.Parameters.ContainsKey("invent_finding") ||
                                       rec.ProposedAction.Contains("MarkUnreviewedAsVerified", StringComparison.OrdinalIgnoreCase) =>
                "Report Agent cannot invent findings or mark unreviewed claims as verified.",

            AgentRole.ClearanceEvaluator when rec.ProposedAction.Contains("SignClearanceCertificate", StringComparison.OrdinalIgnoreCase) &&
                                             !rec.Parameters.ContainsKey("assigned_approver_key") =>
                "Clearance Evaluator cannot sign or waive risk unless assigned explicit cryptographic approver authority.",

            _ => null,
        };
    }

    /// <summary>
    /// Validates that the requested action does not exceed the target's granted AccessClass.
    /// </summary>
    public static string? CheckPrivilegeBypass(Target target, AgentRecommendation rec)
    {
        return target.Access switch
        {
            AccessClass.ObserveOnly when rec.RequiredLevel > AgentPermissionLevel.L0_Observe =>
                $"Target '{target.Name}' is registered as ObserveOnly, but action requires {rec.RequiredLevel}.",

            AccessClass.Interact when rec.RequiredLevel > AgentPermissionLevel.L1_ControlledInteraction =>
                $"Target '{target.Name}' is registered for Interact, but action requires {rec.RequiredLevel}.",

            AccessClass.StateChanging when rec.RequiredLevel > AgentPermissionLevel.L2_StateChanging =>
                $"Target '{target.Name}' is registered for StateChanging, but action requires {rec.RequiredLevel}.",

            _ => null,
        };
    }

    /// <summary>
    /// Validates cited entities against actual items in the GhoztStore.
    /// </summary>
    public GroundingAssayResult PerformGroundingAssay(Target target, AgentRecommendation rec)
    {
        var grounded = new List<string>();
        var ungrounded = new List<string>();

        // If the recommendation makes specific factual claims, it must cite evidence or manifest entities
        if (rec.CitedEvidenceIds.Count == 0 && rec.CitedManifestEntityIds.Count == 0)
        {
            // For general exploration prompts without specific claims, pass with caution
            if (rec.Role is AgentRole.ExplorationAgent or AgentRole.DiscoveryAgent or AgentRole.SweepPlanner)
            {
                return new GroundingAssayResult
                {
                    Passed = true,
                    Code = "AGT.GROUNDING.PASS",
                    Detail = "Autonomous exploration step grounded in current live state.",
                    GroundedEntities = ["state:live"],
                };
            }

            return new GroundingAssayResult
            {
                Passed = false,
                Code = "AGT.GROUNDING.FAIL",
                Detail = "Recommendation makes factual claims without citing any supporting evidence or manifest entity.",
                UngroundedClaims = [rec.Summary],
            };
        }

        // Verify cited evidence items
        foreach (var evIdStr in rec.CitedEvidenceIds)
        {
            if (GhoztId.TryParse(evIdStr, out var evId))
            {
                var ev = _store.EvidenceItems().FirstOrDefault(e => e.Id == evId);
                if (ev is not null)
                {
                    grounded.Add($"evidence:{ev.Id}");
                }
                else
                {
                    ungrounded.Add($"evidence:{evIdStr} (not found in evidence store)");
                }
            }
            else
            {
                ungrounded.Add($"evidence:{evIdStr} (invalid GhoztId format)");
            }
        }

        // Verify cited manifest entities (nodes or findings)
        var targetNodes = _store.Nodes(target.Id);
        foreach (var entityId in rec.CitedManifestEntityIds)
        {
            if (GhoztId.TryParse(entityId, out var fId) && _store.Findings().Any(f => f.Id == fId))
            {
                grounded.Add($"finding:{entityId}");
            }
            else if (targetNodes.Any(n => n.Id.Equals(entityId, StringComparison.OrdinalIgnoreCase)))
            {
                grounded.Add($"node:{entityId}");
            }
            else
            {
                ungrounded.Add($"manifest_entity:{entityId} (not found in lattice or findings)");
            }
        }

        if (ungrounded.Count > 0)
        {
            return new GroundingAssayResult
            {
                Passed = false,
                Code = "AGT.GROUNDING.FAIL",
                Detail = $"Unverified or fabricated references detected: {string.Join(", ", ungrounded)}",
                GroundedEntities = grounded,
                UngroundedClaims = ungrounded,
            };
        }

        return new GroundingAssayResult
        {
            Passed = true,
            Code = "AGT.GROUNDING.PASS",
            Detail = $"All {grounded.Count} cited references verified against evidence ledger and manifest.",
            GroundedEntities = grounded,
        };
    }

    private Evidence SealSupervisionEvidence(
        Target target,
        AgentRecommendation rec,
        GroundingAssayResult grounding,
        string? output,
        GhoztId? sweepId)
    {
        var payloadObj = new
        {
            role = rec.Role.ToString(),
            action = rec.ProposedAction,
            level = rec.RequiredLevel.ToString(),
            confidence = rec.Confidence,
            summary = rec.Summary,
            grounding_passed = grounding.Passed,
            grounded_entities = grounding.GroundedEntities,
            output = output ?? "",
            timestamp = DateTimeOffset.Now.ToString("O"),
        };

        var json = JsonSerializer.Serialize(payloadObj);
        var sha256 = ComputeSha256(json);

        var ev = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            SweepId = sweepId,
            Kind = "AgentSupervision",
            Summary = $"Supervised action '{rec.ProposedAction}' by {rec.Role} ({rec.RequiredLevel})",
            Sha256 = sha256,
            SizeBytes = Encoding.UTF8.GetByteCount(json),
            Payload = json,
            SourceAdapter = "core:multiagent-supervisor",
            CapturedAt = DateTimeOffset.Now,
            Sealed = true,
            SealedBy = "MultiAgentSupervisor@v1",
        };

        _store.Save(ev);
        return ev;
    }

    private static SupervisionVerdict CreateRejectedVerdict(
        AgentRecommendation rec,
        string code,
        string rationale,
        GroundingAssayResult grounding)
    {
        return new SupervisionVerdict
        {
            Approved = false,
            Code = code,
            Rationale = rationale,
            Recommendation = rec,
            Grounding = grounding,
        };
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
