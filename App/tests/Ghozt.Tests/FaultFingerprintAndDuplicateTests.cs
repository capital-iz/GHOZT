using System.IO;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class FaultFingerprintAndDuplicateTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public FaultFingerprintAndDuplicateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_fp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_fp_test.db");
        _store = new GhoztStore(_tempDbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void FaultFingerprintEngine_NormalizeExceptionSignature_StripsVolatileTokens()
    {
        var raw = "NullReferenceException at 0x7FFF5A8C9E10 in proc pid=8492 at 2026-09-14T18:22:30.123Z. Guid: e5d73c88-124b-4bfe-9892-01934ba219fe, connection to 127.0.0.1:5000 failed at MyClass.cs:line 128";
        var normalized = FaultFingerprintEngine.NormalizeExceptionSignature(raw);

        Assert.DoesNotContain("0x7FFF5A8C9E10", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("8492", normalized);
        Assert.DoesNotContain("e5d73c88-124b-4bfe-9892-01934ba219fe", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("128", normalized);

        Assert.Contains("<addr>", normalized);
        Assert.Contains("<pid>", normalized);
        Assert.Contains("<guid>", normalized);
        Assert.Contains("<port>", normalized);
        Assert.Contains(":line <n>", normalized);
    }

    [Fact]
    public void FaultFingerprintEngine_ComputeCompositeFingerprint_ProducesDeterministicTable46Sha256Digest()
    {
        var payload1 = new FaultFingerprintPayload
        {
            ExceptionSignature = "NullReferenceException at 0x7FFE0012: in SessionService.cs:line 84",
            SemanticCategory = SemanticCategory.StatusCodeMismatch,
            Expected = "200 OK with session payload",
            Actual = "500 InternalServerError",
            NodeId = "endpoint:/api/v1/session/refresh",
            EdgeId = "edge:auth->session",
            WorkflowPosition = "SessionRefresh",
            ActionSequence = ["action:login", "action:refresh_token"],
            StateDelta = "token_refreshed=false",
            DependencyTrace = "identity-service@v4.8.1",
            Environment = TargetEnvironment.Staging,
            OsFamily = "Windows",
            Architecture = "x64",
        };

        // Same defect with volatile tokens shifted (different memory address & line number)
        var payload2 = new FaultFingerprintPayload
        {
            ExceptionSignature = "NullReferenceException at 0x8FFE0099: in SessionService.cs:line 192",
            SemanticCategory = SemanticCategory.StatusCodeMismatch,
            Expected = "200 OK with session payload",
            Actual = "500 InternalServerError",
            NodeId = "endpoint:/api/v1/session/refresh",
            EdgeId = "edge:auth->session",
            WorkflowPosition = "SessionRefresh",
            ActionSequence = ["action:login", "action:refresh_token"],
            StateDelta = "token_refreshed=false",
            DependencyTrace = "identity-service@v4.8.1",
            Environment = TargetEnvironment.Staging,
            OsFamily = "Windows",
            Architecture = "x64",
        };

        var fp1 = FaultFingerprintEngine.ComputeCompositeFingerprint(payload1);
        var fp2 = FaultFingerprintEngine.ComputeCompositeFingerprint(payload2);

        Assert.StartsWith("FP-", fp1);
        Assert.Equal(15, fp1.Length); // "FP-" + 12 hex digits
        Assert.Equal(fp1, fp2); // Invariant: Volatile memory shifts do NOT alter the composite fingerprint!
    }

    [Fact]
    public void FaultFingerprintEngine_ComputeCompositeFingerprint_DistinctSemanticCategories_ProduceDistinctFingerprints()
    {
        // Anti-collapsing invariant: Two faults sharing the same raw error text ("System fault")
        // but belonging to distinct semantic categories must NEVER collapse into the same fingerprint!
        var payloadA = new FaultFingerprintPayload
        {
            ExceptionSignature = "Generic error occurred",
            SemanticCategory = SemanticCategory.StatusCodeMismatch,
            NodeId = "node:checkout",
            Expected = "200 OK",
            Actual = "500 Error",
        };

        var payloadB = new FaultFingerprintPayload
        {
            ExceptionSignature = "Generic error occurred",
            SemanticCategory = SemanticCategory.UiStateCorruption,
            NodeId = "node:checkout",
            Expected = "Dialog Dismissed",
            Actual = "Dialog Frozen",
        };

        var fpA = FaultFingerprintEngine.ComputeCompositeFingerprint(payloadA);
        var fpB = FaultFingerprintEngine.ComputeCompositeFingerprint(payloadB);

        Assert.NotEqual(fpA, fpB);
    }

    [Fact]
    public void FaultFingerprintEngine_ClassifyDuplicate_EvaluatesAllFourPatentTable47Outcomes()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Duplicate Test Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Staging,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.2-test",
            Reachable = true,
        };
        _store.Save(target);

        var existingPayload = new FaultFingerprintPayload
        {
            ExceptionSignature = "SqlException: Timeout expired",
            SemanticCategory = SemanticCategory.ContractViolation,
            NodeId = "db:query:users",
            Environment = TargetEnvironment.Staging,
        };
        var existingFp = FaultFingerprintEngine.ComputeCompositeFingerprint(existingPayload);

        var existingFinding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Database Timeout on User Query",
            Severity = Severity.High,
            Maturity = FindingMaturity.New,
            NodeId = "db:query:users",
            SemanticCategory = SemanticCategory.ContractViolation,
            Fingerprint = existingFp,
            Actual = "Timeout expired",
            Expected = "QueryResult within 500ms",
        };
        _store.Save(existingFinding);

        var knownFindings = new List<Finding> { existingFinding };

        // 1. SameFault: Identical fingerprint
        var candidateSame = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Recurred Timeout",
            Severity = Severity.High,
            Fingerprint = existingFp,
            NodeId = "db:query:users",
        };
        var decisionSame = FaultFingerprintEngine.ClassifyDuplicate(candidateSame, existingPayload, knownFindings, target);
        Assert.Equal(DuplicateOutcome.SameFault, decisionSame.Outcome);
        Assert.Equal(existingFinding.Id, decisionSame.ClusteredWith?.Id);
        Assert.Equal(1.0, decisionSame.Confidence);

        // 2. Variant: Same architectural node & semantic category, but distinct environment
        var variantPayload = new FaultFingerprintPayload
        {
            ExceptionSignature = "SqlException: Timeout expired",
            SemanticCategory = SemanticCategory.ContractViolation,
            NodeId = "db:query:users",
            Environment = TargetEnvironment.Production, // Environment drift
        };
        var candidateVariant = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Prod Timeout Variant",
            Severity = Severity.Critical,
            Fingerprint = FaultFingerprintEngine.ComputeCompositeFingerprint(variantPayload),
            NodeId = "db:query:users",
            SemanticCategory = SemanticCategory.ContractViolation,
        };
        var prodTarget = target with { Environment = TargetEnvironment.Production };
        var decisionVariant = FaultFingerprintEngine.ClassifyDuplicate(candidateVariant, variantPayload, knownFindings, prodTarget);
        Assert.Equal(DuplicateOutcome.Variant, decisionVariant.Outcome);
        Assert.Equal(existingFinding.Id, decisionVariant.ClusteredWith?.Id);

        // 3. Related: Shared symptom/title but unproven causal identity
        var relatedPayload = new FaultFingerprintPayload
        {
            ExceptionSignature = "SqlException: Connection refused",
            SemanticCategory = SemanticCategory.Other,
            NodeId = "db:query:orders", // Distinct node
            DependencyTrace = "Timeout expired",
        };
        var candidateRelated = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Database Timeout on User Query", // Same title
            Severity = Severity.Medium,
            Fingerprint = FaultFingerprintEngine.ComputeCompositeFingerprint(relatedPayload),
            NodeId = "db:query:orders",
        };
        var decisionRelated = FaultFingerprintEngine.ClassifyDuplicate(candidateRelated, relatedPayload, knownFindings, target);
        Assert.Equal(DuplicateOutcome.Related, decisionRelated.Outcome);
        Assert.Equal(existingFinding.Id, decisionRelated.ClusteredWith?.Id);

        // 4. Independent: Distinct mechanism & node
        var independentPayload = new FaultFingerprintPayload
        {
            ExceptionSignature = "IndexOutOfRangeException in ArrayParser",
            SemanticCategory = SemanticCategory.UiStateCorruption,
            NodeId = "ui:grid:render",
        };
        var candidateIndependent = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Grid Render Out of Bounds",
            Severity = Severity.Low,
            Fingerprint = FaultFingerprintEngine.ComputeCompositeFingerprint(independentPayload),
            NodeId = "ui:grid:render",
        };
        var decisionIndependent = FaultFingerprintEngine.ClassifyDuplicate(candidateIndependent, independentPayload, knownFindings, target);
        Assert.Equal(DuplicateOutcome.Independent, decisionIndependent.Outcome);
        Assert.Null(decisionIndependent.ClusteredWith);
    }

    [Fact]
    public void FaultFingerprintEngine_MaturityTransitions_EnforcePatentTable45Preconditions()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Maturity Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.2-test",
            Reachable = true,
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Session Leak",
            Severity = Severity.High,
            Maturity = FindingMaturity.New,
            NodeId = "auth:session",
            ReproAttempts = 0,
            ReproSuccesses = 0,
            EvidenceCount = 0,
        };
        _store.Save(finding);

        var emptyEvidence = new TransitionEvidence();

        // 1. Guard: Cannot jump directly to Reproduced with 0 successes
        Assert.False(FaultFingerprintEngine.CanTransition(finding, FindingMaturity.Reproduced, emptyEvidence, out var reasonRepro));
        Assert.Contains("requires at least 1 successful reproduction", reasonRepro);

        // 2. Guard: Cannot jump to CauseSupported without causal hypothesis
        Assert.False(FaultFingerprintEngine.CanTransition(finding, FindingMaturity.CauseSupported, emptyEvidence, out var reasonCause));
        Assert.Contains("requires empirical counterfactual causal support", reasonCause);

        // 3. Guard: Cannot jump to Verified without post-repair verification & sealed evidence
        Assert.False(FaultFingerprintEngine.CanTransition(finding, FindingMaturity.Verified, emptyEvidence, out var reasonVerified));
        Assert.Contains("requires passing post-repair verification", reasonVerified);

        // 4. Valid Progression Sequence
        // A. Transition to Reproduced with 1 success
        var reproFinding = finding with { ReproAttempts = 2, ReproSuccesses = 2 };
        var advancedRepro = FaultFingerprintEngine.AdvanceMaturity(reproFinding, FindingMaturity.Reproduced, emptyEvidence, _store, target);
        Assert.Equal(FindingMaturity.Reproduced, advancedRepro.Maturity);

        // B. Transition to CauseSupported with high confidence
        var causalEvidence = new TransitionEvidence { CausalHypothesisSupported = true, CausalConfidence = 0.92 };
        var advancedCause = FaultFingerprintEngine.AdvanceMaturity(advancedRepro, FindingMaturity.CauseSupported, causalEvidence, _store, target);
        Assert.Equal(FindingMaturity.CauseSupported, advancedCause.Maturity);

        // C. Transition to FixReady with approved Fix Brief
        var fixBriefEvidence = new TransitionEvidence { FixBriefApproved = true, FixBriefId = "FXB-001" };
        var advancedFixReady = FaultFingerprintEngine.AdvanceMaturity(advancedCause, FindingMaturity.FixReady, fixBriefEvidence, _store, target);
        Assert.Equal(FindingMaturity.FixReady, advancedFixReady.Maturity);

        // D. Transition to ReTest with repair applied
        var reTestEvidence = new TransitionEvidence { RepairApplied = true };
        var advancedReTest = FaultFingerprintEngine.AdvanceMaturity(advancedFixReady, FindingMaturity.ReTest, reTestEvidence, _store, target);
        Assert.Equal(FindingMaturity.ReTest, advancedReTest.Maturity);

        // E. Transition to Verified with repair verified and sealed evidence attached
        var verifiedEvidence = new TransitionEvidence { RepairVerified = true, SealedEvidenceCount = 2 };
        var advancedVerified = FaultFingerprintEngine.AdvanceMaturity(advancedReTest, FindingMaturity.Verified, verifiedEvidence, _store, target);
        Assert.Equal(FindingMaturity.Verified, advancedVerified.Maturity);

        // Verify BlackBox immutable events
        var events = _store.RecentEvents(limit: 10, targetId: target.Id);
        Assert.Contains(events, e => e.Code == "FLT.MATURITY.ADVANCE");
        Assert.Contains(events, e => e.Code == "FLT.VERIFIED");
    }

    [Fact]
    public void FaultFingerprintEngine_WaivedAndRejectedTransitions_VerifyAuthorityAndReason()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Waiver Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.2-test",
            Reachable = true,
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Low-Impact Font Offset",
            Severity = Severity.Low,
            Maturity = FindingMaturity.New,
        };
        _store.Save(finding);

        // Waiver missing authorizer rejected
        Assert.False(FaultFingerprintEngine.CanTransition(finding, FindingMaturity.Waived, new TransitionEvidence(), out var reasonNoAuth));
        Assert.Contains("requires a named approving authority", reasonNoAuth);

        // Valid waiver succeeds
        var validWaiver = new TransitionEvidence
        {
            WaivedBy = "LeadArchitect",
            WaiverReason = "Cosmetic font rendering in legacy high-DPI scaling; accepted until v2.0.",
            WaivedUntil = DateTimeOffset.Now.AddDays(90),
        };
        var waived = FaultFingerprintEngine.AdvanceMaturity(finding, FindingMaturity.Waived, validWaiver, _store, target);
        Assert.Equal(FindingMaturity.Waived, waived.Maturity);

        // Rejection missing reason rejected
        Assert.False(FaultFingerprintEngine.CanTransition(finding, FindingMaturity.Rejected, new TransitionEvidence(), out var reasonNoJust));
        Assert.Contains("requires an explicit rejection rationale", reasonNoJust);

        // Valid rejection succeeds
        var validRejection = new TransitionEvidence
        {
            RejectionReason = "Test oracle expected wrong button ID; verified correct behavior in target.",
        };
        var rejected = FaultFingerprintEngine.AdvanceMaturity(finding, FindingMaturity.Rejected, validRejection, _store, target);
        Assert.Equal(FindingMaturity.Rejected, rejected.Maturity);

        var events = _store.RecentEvents(limit: 10, targetId: target.Id);
        Assert.Contains(events, e => e.Code == "FLT.WAIVED");
        Assert.Contains(events, e => e.Code == "FLT.REJECTED");
    }
}
