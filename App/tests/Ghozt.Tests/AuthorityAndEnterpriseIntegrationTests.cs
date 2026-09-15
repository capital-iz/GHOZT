using System.IO;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class AuthorityAndEnterpriseIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public AuthorityAndEnterpriseIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_auth_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_auth_test.db");
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
    public void AuthorityKernel_AuthorizeAction_PermitsCompliantLevel()
    {
        var targetId = GhoztId.Create(IdKind.Target, 101);
        var grantId = GhoztId.Create(IdKind.Authority, 501);

        var grant = AuthorityKernel.IssueGrant(
            grantId: grantId,
            targetId: targetId,
            actor: "ci-orchestrator",
            maxLevel: ExecutionIsolationLevel.L2_StateChanging,
            allowedActionClasses: ["ui:*", "api:*"],
            expiresAt: DateTimeOffset.Now.AddHours(4),
            approver: "Rea",
            scopeJustification: "Integration test sweep",
            sensitivity: DataSensitivityTier.Internal);

        _store.Save(grant);

        var verdict = AuthorityKernel.AuthorizeAction(
            grant: grant,
            actionClass: "ui:form_submit",
            requiredLevel: ExecutionIsolationLevel.L1_ControlledInteraction);

        Assert.True(verdict.Authorized);
        Assert.Equal("AUT.CHECK.OK", verdict.DiagnosticCode);
        Assert.Equal(ExecutionIsolationLevel.L2_StateChanging, verdict.GrantedLevel);
        Assert.Equal(ExecutionIsolationLevel.L1_ControlledInteraction, verdict.RequiredLevel);
    }

    [Fact]
    public void AuthorityKernel_AuthorizeAction_RejectsActionExceedingIsolationLevel()
    {
        var targetId = GhoztId.Create(IdKind.Target, 102);
        var grantId = GhoztId.Create(IdKind.Authority, 502);

        var grant = AuthorityKernel.IssueGrant(
            grantId: grantId,
            targetId: targetId,
            actor: "test-runner",
            maxLevel: ExecutionIsolationLevel.L2_StateChanging,
            allowedActionClasses: ["ui:*", "api:*"],
            expiresAt: DateTimeOffset.Now.AddHours(2),
            approver: "Rea",
            scopeJustification: "Standard interaction test",
            sensitivity: DataSensitivityTier.Internal);

        // Attempting an L3 Destructive Chaos action under an L2 grant
        var verdict = AuthorityKernel.AuthorizeAction(
            grant: grant,
            actionClass: "chaos:kill_process",
            requiredLevel: ExecutionIsolationLevel.L3_DestructiveChaos);

        Assert.False(verdict.Authorized);
        Assert.Equal("GZT-POL-ISOLATION-VIOLATION", verdict.DiagnosticCode);
        Assert.Contains("capped at", verdict.Reason);
    }

    [Fact]
    public void AuthorityKernel_ExpiredGrant_FailsClosed()
    {
        var targetId = GhoztId.Create(IdKind.Target, 103);
        var grantId = GhoztId.Create(IdKind.Authority, 503);

        var grant = AuthorityKernel.IssueGrant(
            grantId: grantId,
            targetId: targetId,
            actor: "stale-operator",
            maxLevel: ExecutionIsolationLevel.L2_StateChanging,
            allowedActionClasses: ["ui:*"],
            expiresAt: DateTimeOffset.Now.AddMinutes(-30), // Expired in the past
            approver: "Rea",
            scopeJustification: "Expired session",
            sensitivity: DataSensitivityTier.Internal);

        var verdict = AuthorityKernel.AuthorizeAction(
            grant: grant,
            actionClass: "ui:click",
            requiredLevel: ExecutionIsolationLevel.L1_ControlledInteraction);

        Assert.False(verdict.Authorized);
        Assert.Equal("AUT.EXPIRED_GRANT", verdict.DiagnosticCode);
        Assert.Contains("expired", verdict.Reason);
    }

    [Fact]
    public void AuthorityKernel_TamperedSignature_FailsClosed()
    {
        var targetId = GhoztId.Create(IdKind.Target, 104);
        var grantId = GhoztId.Create(IdKind.Authority, 504);

        var grant = AuthorityKernel.IssueGrant(
            grantId: grantId,
            targetId: targetId,
            actor: "operator",
            maxLevel: ExecutionIsolationLevel.L1_ControlledInteraction,
            allowedActionClasses: ["ui:read"],
            expiresAt: DateTimeOffset.Now.AddHours(1),
            approver: "Rea",
            scopeJustification: "Read only",
            sensitivity: DataSensitivityTier.Public);

        // Maliciously tamper with MaxLevel to escalate privileges without resigning
        var tamperedGrant = grant with { MaxLevel = ExecutionIsolationLevel.L5_ProductionObservation };

        Assert.False(AuthorityKernel.VerifySignature(tamperedGrant));

        var verdict = AuthorityKernel.AuthorizeAction(
            grant: tamperedGrant,
            actionClass: "ui:read",
            requiredLevel: ExecutionIsolationLevel.L1_ControlledInteraction);

        Assert.False(verdict.Authorized);
        Assert.Equal("AUT.TAMPER_DETECTED", verdict.DiagnosticCode);
    }

    [Fact]
    public void IssueTrackerSyncEngine_GeneratesGitHubMarkdown_WithTable46Fingerprint()
    {
        var targetId = GhoztId.Create(IdKind.Target, 201);
        var findingId = GhoztId.Create(IdKind.Fault, 301);
        var sweepId = GhoztId.Create(IdKind.Sweep, 401);

        var finding = new Finding
        {
            Id = findingId,
            TargetId = targetId,
            SweepId = sweepId,
            Title = "Crash on rapid button double-click",
            Severity = Severity.High,
            Maturity = FindingMaturity.Reproduced,
            Actual = "NullReferenceException in Handler()",
            Expected = "Idempotent button state",
            Fingerprint = "FP-8F3A4B9C1D2E3F4A5B6C7D8E9F0A1B2C",
            ReproAttempts = 10,
            ReproSuccesses = 8,
            NodeId = "NODE-BTN-01",
            Owner = "QA",
            FirstSeenAt = DateTimeOffset.Now.AddDays(-1),
            LastSeenAt = DateTimeOffset.Now,
            EvidenceCount = 2,
            SemanticCategory = SemanticCategory.CrashFatalExit,
        };

        var target = new Target
        {
            Id = targetId,
            Name = "Payment Terminal",
            Kind = "Desktop application",
            Environment = TargetEnvironment.Staging,
            Access = AccessClass.Interact,
            Reachable = true,
        };

        var markdown = IssueTrackerSyncEngine.FormatGitHubIssueMarkdown(finding, target);

        Assert.Contains($"<!-- ghozt:finding_id:{finding.Id} fingerprint:{finding.Fingerprint} -->", markdown);
        Assert.Contains("## 1. Deviation Overview", markdown);
        Assert.Contains("Laplace Reproducibility Prior", markdown);
        Assert.Contains("95% Wilson Interval", markdown);
        Assert.Contains("Crash on rapid button double-click", markdown);
        Assert.Contains("NullReferenceException in Handler()", markdown);
    }

    [Fact]
    public void IssueTrackerSyncEngine_Deduplicate_UpdatesRecurrenceOnMatchingFingerprint()
    {
        var targetId = GhoztId.Create(IdKind.Target, 202);
        var findingId = GhoztId.Create(IdKind.Fault, 302);

        var finding = new Finding
        {
            Id = findingId,
            TargetId = targetId,
            Title = "SQLite lock contention on high concurrency",
            Severity = Severity.Critical,
            Maturity = FindingMaturity.Reproduced,
            Actual = "SQLiteException: database is locked",
            Expected = "Query succeeds within retry budget",
            Fingerprint = "FP-A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4",
            ReproAttempts = 15,
            ReproSuccesses = 12,
            NodeId = "DB-WRITE",
            Owner = "Engine",
            FirstSeenAt = DateTimeOffset.Now.AddDays(-2),
            LastSeenAt = DateTimeOffset.Now,
            EvidenceCount = 3,
            SemanticCategory = SemanticCategory.ResourceDrift,
        };

        var target = new Target
        {
            Id = targetId,
            Name = "Reporting Database",
            Kind = "SQLite database",
            Environment = TargetEnvironment.Development,
            Access = AccessClass.StateChanging,
            Reachable = true,
        };

        var existingBacklog = new List<string>
        {
            "Unrelated issue body without fingerprint",
            "Prior report notes.\n<!-- ghozt:finding_id:FND-000001 fingerprint:FP-A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4 -->\nTrace details.",
        };

        var result = IssueTrackerSyncEngine.DeduplicateAndSync(finding, existingBacklog, target, provider: "GitHub");

        Assert.True(result.IsUpdate);
        Assert.Equal("FND-000001", result.IssueIdentifier);
        Assert.Contains("Ghozt Recurrence Notice", result.PayloadMarkdown);
        Assert.Contains("FP-A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4", result.PayloadMarkdown);
    }

    [Fact]
    public async Task ObservabilityIngestGateway_IngestsLogs_SanitizesPii_AndPersistsEvents()
    {
        var targetId = GhoztId.Create(IdKind.Target, 203);
        var rawJson = """
        {
            "timestamp": "2026-09-15T12:00:00Z",
            "level": "ERROR",
            "message": "Authentication failed for user admin with password='superSecretPassword123!'",
            "attributes": {
                "auth_header": "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.t-ID",
                "ip": "10.0.0.12"
            }
        }
        """;

        var ingestResult = await ObservabilityIngestGateway.IngestLogStreamAsync(
            _store,
            rawJson,
            targetId,
            defaultSource: "otel-receiver");

        Assert.Equal(1, ingestResult.IngestedCount);
        Assert.True(ingestResult.RedactedCount > 0);

        var events = _store.RecentEvents(limit: 10, targetId: targetId);
        Assert.NotEmpty(events);

        var recorded = events.First();
        Assert.Equal("GZT-OBS-ERR", recorded.Code);
        Assert.Equal(Severity.High, recorded.Severity);

        // Assert sensitive tokens were completely redacted
        Assert.DoesNotContain("superSecretPassword123!", recorded.Message);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", recorded.Detail);
        Assert.Contains("[REDACTED:", recorded.Message);
    }
}
