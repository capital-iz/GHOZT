using System.Security.Cryptography;
using System.Text;

namespace Ghozt.Core;

/// <summary>
/// Verdict returned by the Authority Kernel upon action evaluation (Patent Table 70).
/// </summary>
public sealed record AuthorityVerdict
{
    public required bool Authorized { get; init; }

    public required string Reason { get; init; }

    public required string DiagnosticCode { get; init; }

    public ExecutionIsolationLevel GrantedLevel { get; init; }

    public ExecutionIsolationLevel RequiredLevel { get; init; }

    public static AuthorityVerdict Pass(AuthorityGrant grant, ExecutionIsolationLevel requiredLevel, string actionClass) =>
        new()
        {
            Authorized = true,
            Reason = $"Authorized: Action '{actionClass}' ({requiredLevel}) permitted under grant {grant.GrantId} (ceiling {grant.MaxLevel}).",
            DiagnosticCode = "AUT.CHECK.OK",
            GrantedLevel = grant.MaxLevel,
            RequiredLevel = requiredLevel,
        };

    public static AuthorityVerdict Fail(string code, string reason, ExecutionIsolationLevel granted = ExecutionIsolationLevel.L0_Observe, ExecutionIsolationLevel required = ExecutionIsolationLevel.L0_Observe) =>
        new()
        {
            Authorized = false,
            Reason = reason,
            DiagnosticCode = code,
            GrantedLevel = granted,
            RequiredLevel = required,
        };
}

/// <summary>
/// Core enforcement authority for target action authorization and execution isolation levels (Patent §35, Table 70).
/// </summary>
public static class AuthorityKernel
{
    /// <summary>
    /// Issues a sealed, tamper-evident Authority Grant for a target (Patent Table 10, Table 70).
    /// </summary>
    public static AuthorityGrant IssueGrant(
        GhoztId grantId,
        GhoztId targetId,
        string actor,
        ExecutionIsolationLevel maxLevel,
        IEnumerable<string> allowedActionClasses,
        DateTimeOffset expiresAt,
        string approver = "admin@capital-iz.com",
        string scopeJustification = "Standard testing authorization",
        DataSensitivityTier sensitivity = DataSensitivityTier.Internal)
    {
        var actionsList = allowedActionClasses?.ToList() ?? new List<string>();
        var issuedAt = DateTimeOffset.Now;

        var payload = BuildSignaturePayload(grantId, targetId, actor, maxLevel, actionsList, issuedAt, expiresAt, approver);
        var signature = Hashing.Sha256(payload);

        return new AuthorityGrant
        {
            GrantId = grantId,
            TargetId = targetId,
            Actor = actor,
            MaxLevel = maxLevel,
            AllowedActionClasses = actionsList,
            AllowedDataSensitivity = sensitivity,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            Approver = approver,
            ScopeJustification = scopeJustification,
            AuditSignature = signature,
        };
    }

    /// <summary>
    /// Evaluates whether an intended action is permitted under the provided Authority Grant (Patent Table 70).
    /// </summary>
    public static AuthorityVerdict AuthorizeAction(
        AuthorityGrant? grant,
        string actionClass,
        ExecutionIsolationLevel requiredLevel,
        DateTimeOffset? evaluationTime = null)
    {
        if (grant == null)
        {
            return AuthorityVerdict.Fail(
                "AUT.MISSING_GRANT",
                "Execution Refused: Target has no active Authority Grant (AUT-xxxxxx). Operation rejected under fail-closed invariant.",
                ExecutionIsolationLevel.L0_Observe,
                requiredLevel);
        }

        // Verify cryptographic seal
        if (!VerifySignature(grant))
        {
            return AuthorityVerdict.Fail(
                "AUT.TAMPER_DETECTED",
                $"Security Integrity Breach: Cryptographic audit signature for grant {grant.GrantId} is invalid or corrupted.",
                grant.MaxLevel,
                requiredLevel);
        }

        // Verify expiration
        var now = evaluationTime ?? DateTimeOffset.Now;
        if (grant.IsExpired(now))
        {
            return AuthorityVerdict.Fail(
                "AUT.EXPIRED_GRANT",
                $"Execution Refused: Authority Grant {grant.GrantId} expired at {grant.ExpiresAt:O} (evaluation time: {now:O}).",
                grant.MaxLevel,
                requiredLevel);
        }

        // Verify Isolation Level Ceiling (Patent Table 70)
        if (requiredLevel > grant.MaxLevel)
        {
            return AuthorityVerdict.Fail(
                "GZT-POL-ISOLATION-VIOLATION",
                $"Isolation Policy Refusal: Action requires '{GetLevelDescription(requiredLevel)}' ({requiredLevel}), but grant {grant.GrantId} ceiling is capped at '{GetLevelDescription(grant.MaxLevel)}' ({grant.MaxLevel}).",
                grant.MaxLevel,
                requiredLevel);
        }

        // Verify Action Class whitelist if populated
        if (grant.AllowedActionClasses != null && grant.AllowedActionClasses.Count > 0)
        {
            var match = grant.AllowedActionClasses.Any(a =>
                string.Equals(a, actionClass, StringComparison.OrdinalIgnoreCase) ||
                a == "*" ||
                (a.EndsWith('*') && actionClass.StartsWith(a[..^1], StringComparison.OrdinalIgnoreCase)));
            if (!match)
            {
                return AuthorityVerdict.Fail(
                    "AUT.UNAUTHORIZED_ACTION_CLASS",
                    $"Policy Refusal: Action class '{actionClass}' is not in the allowed action whitelist for grant {grant.GrantId} [{string.Join(", ", grant.AllowedActionClasses)}].",
                    grant.MaxLevel,
                    requiredLevel);
            }
        }

        return AuthorityVerdict.Pass(grant, requiredLevel, actionClass);
    }

    /// <summary>
    /// Verifies the SHA-256 cryptographic signature of an Authority Grant.
    /// </summary>
    public static bool VerifySignature(AuthorityGrant grant)
    {
        if (string.IsNullOrWhiteSpace(grant.AuditSignature))
        {
            return false;
        }

        var expectedPayload = BuildSignaturePayload(
            grant.GrantId,
            grant.TargetId,
            grant.Actor,
            grant.MaxLevel,
            grant.AllowedActionClasses,
            grant.IssuedAt,
            grant.ExpiresAt,
            grant.Approver);

        var computed = Hashing.Sha256(expectedPayload);
        return string.Equals(computed, grant.AuditSignature, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns human-readable label and patent Table 70 description for an isolation level.
    /// </summary>
    public static string GetLevelDescription(ExecutionIsolationLevel level) => level switch
    {
        ExecutionIsolationLevel.L0_Observe => "L0 — Observe (Read-only discovery; no target mutation)",
        ExecutionIsolationLevel.L1_ControlledInteraction => "L1 — Controlled Interaction (Standard UI/API actions; reset required)",
        ExecutionIsolationLevel.L2_StateChanging => "L2 — State-Changing (Create/update data; requires policy approval)",
        ExecutionIsolationLevel.L3_DestructiveChaos => "L3 — Destructive/Chaos (Resource corruption/fault injection; isolated blast radius)",
        ExecutionIsolationLevel.L4_CodeModification => "L4 — Code Modification (Branch-bounded AI/developer repair; Fix Brief required)",
        ExecutionIsolationLevel.L5_ProductionObservation => "L5 — Production Observation (Approved production diagnostics; zero mutation)",
        _ => level.ToString(),
    };

    private static string BuildSignaturePayload(
        GhoztId grantId,
        GhoztId targetId,
        string actor,
        ExecutionIsolationLevel maxLevel,
        IEnumerable<string>? actions,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string approver)
    {
        var actionTokens = actions != null ? string.Join(",", actions.OrderBy(x => x)) : "";
        return $"GRANT:{grantId}:TARGET:{targetId}:ACTOR:{actor}:LEVEL:{(int)maxLevel}:ACTIONS:{actionTokens}:ISSUED:{issuedAt.ToUnixTimeSeconds()}:EXPIRES:{expiresAt.ToUnixTimeSeconds()}:APPROVER:{approver}";
    }
}
