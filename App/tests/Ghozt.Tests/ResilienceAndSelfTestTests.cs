using System.IO;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class ResilienceAndSelfTestTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly GhoztStore _store;
    private readonly Target _target;

    public ResilienceAndSelfTestTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ghozt_resilience_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "resilience.db");
        _store = new GhoztStore(_dbPath);

        _target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Resilience Target",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "http://localhost:8080",
            BuildLabel = "1.0.0",
            AuthorityNote = "Test grant",
            Reachable = true,
        };
        _store.Save(_target);
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
        catch { }
    }

    // ==================================================================
    // 1. Failure Handling & Recovery Engine (Patent §39)
    // ==================================================================

    [Fact]
    public void ResilienceRecoveryEngine_ClassifyFailure_MatchesAll14PatentTable77Modes()
    {
        var modes = Enum.GetValues<SystemFailureMode>();
        Assert.Equal(14, modes.Length);

        foreach (var mode in modes)
        {
            var directive = ResilienceRecoveryEngine.ClassifyFailure(mode);
            Assert.Equal(mode, directive.FailureMode);
            Assert.False(string.IsNullOrWhiteSpace(directive.RequiredResponse));
            Assert.False(string.IsNullOrWhiteSpace(directive.DegradedOperationNote));
        }

        // Verify specific non-negotiable policy responses from Table 77
        var missingInput = ResilienceRecoveryEngine.ClassifyFailure(SystemFailureMode.MissingInputs);
        Assert.False(missingInput.CanRetry);
        Assert.True(missingInput.RequiresHumanReview);

        var permDenial = ResilienceRecoveryEngine.ClassifyFailure(SystemFailureMode.PermissionDenial);
        Assert.False(permDenial.CanRetry);
        Assert.True(permDenial.HaltsHighRiskExecution);

        var evFailure = ResilienceRecoveryEngine.ClassifyFailure(SystemFailureMode.EvidenceStoreFailure);
        Assert.False(evFailure.CanRetry);
        Assert.True(evFailure.HaltsHighRiskExecution);

        var rollbackFailure = ResilienceRecoveryEngine.ClassifyFailure(SystemFailureMode.RollbackFailure);
        Assert.False(rollbackFailure.CanRetry);
        Assert.True(rollbackFailure.HaltsHighRiskExecution);
    }

    [Fact]
    public void ResilienceRecoveryEngine_ExponentialBackoff_CalculatesBoundedDelaysWithJitter()
    {
        var baseDelay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromMilliseconds(800);

        var d1 = ResilienceRecoveryEngine.CalculateBackoff(1, baseDelay, maxDelay, 0.0);
        Assert.Equal(baseDelay, d1);

        var d2 = ResilienceRecoveryEngine.CalculateBackoff(2, baseDelay, maxDelay, 0.0);
        Assert.Equal(TimeSpan.FromMilliseconds(200), d2);

        var d3 = ResilienceRecoveryEngine.CalculateBackoff(3, baseDelay, maxDelay, 0.0);
        Assert.Equal(TimeSpan.FromMilliseconds(400), d3);

        // High attempt should cap at maxDelay
        var d10 = ResilienceRecoveryEngine.CalculateBackoff(10, baseDelay, maxDelay, 0.0);
        Assert.Equal(maxDelay, d10);

        // With jitter, result should remain positive and within bounds
        var dJitter = ResilienceRecoveryEngine.CalculateBackoff(3, baseDelay, maxDelay, 0.25);
        Assert.InRange(dJitter.TotalMilliseconds, 300, 500);
    }

    [Fact]
    public async Task ResilienceRecoveryEngine_IdempotencyGuard_BlocksUnsafeRetries()
    {
        var engine = new ResilienceRecoveryEngine(_store);
        var attempts = 0;

        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            IsIdempotent = false, // Mutation action: NOT safe to retry automatically!
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await engine.ExecuteWithResilienceAsync("DestructiveOperation", _ =>
            {
                attempts++;
                return Task.FromException(new InvalidOperationException("First attempt failed"));
            }, policy, _target);
        });

        // Non-idempotent operation must abort immediately after attempt 1
        Assert.Equal(1, attempts);
        Assert.Contains("non-idempotent", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify refusal logged in BlackBox event ledger
        var events = _store.RecentEvents(10, _target.Id);
        Assert.Contains(events, e => e.Code == "SYS.RESILIENCE.RETRY_REFUSED");
    }

    [Fact]
    public async Task ResilienceRecoveryEngine_ExecuteWithResilience_RecoversAndLogsImmutably()
    {
        var engine = new ResilienceRecoveryEngine(_store);
        var attempts = 0;

        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromMilliseconds(50),
            IsIdempotent = true,
        };

        var result = await engine.ExecuteWithResilienceAsync("TransientQuery", attempt =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new IOException("Socket timeout on first attempt");
            }
            return Task.FromResult(42);
        }, policy, _target);

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);

        // Immutable ledger verification: Initial failure must NOT be overwritten!
        var events = _store.RecentEvents(10, _target.Id);
        Assert.Contains(events, e => e.Code == "SYS.RESILIENCE.FAULT");
        Assert.Contains(events, e => e.Code == "SYS.RESILIENCE.RETRY");
        Assert.Contains(events, e => e.Code == "SYS.RESILIENCE.RECOVERED");
    }

    [Fact]
    public void ResilienceRecoveryEngine_StorageHeadroom_EmitsDegradedDirectiveUnderPressure()
    {
        var engine = new ResilienceRecoveryEngine(_store);

        // Normal headroom (2 GB available > 500 MB threshold)
        var normal = engine.EvaluateStorageHeadroom(2L * 1024 * 1024 * 1024);
        Assert.False(normal.DegradeActive);
        Assert.False(normal.OmitHighResolutionMedia);

        // Constrained headroom (100 MB available < 500 MB threshold)
        var constrained = engine.EvaluateStorageHeadroom(100L * 1024 * 1024);
        Assert.True(constrained.DegradeActive);
        Assert.True(constrained.OmitHighResolutionMedia);
        Assert.True(constrained.SpoolPayloadsByReference);
        Assert.Contains("Storage pressure", constrained.PolicyMessage);
    }

    // ==================================================================
    // 2. Self-Hosting Autonomous Examination & Certification (Patent §40)
    // ==================================================================

    [Fact]
    public async Task SelfTestEngine_All12Domains_PassVerification_AndIssuesSealedCertificate()
    {
        var engine = new SelfTestEngine(_store);
        var cert = await engine.RunFullSelfTestAsync();

        Assert.NotNull(cert);
        Assert.Equal(12, cert.TotalDomains);
        Assert.Equal(12, cert.PassedDomains);
        Assert.Equal("SELF_HOST_QUALIFIED", cert.Verdict);
        Assert.Equal("GhoztSelfHostSupervisor@v1", cert.SignedBy);
        Assert.False(string.IsNullOrWhiteSpace(cert.Sha256Digest));

        // Verify all 12 domains from Patent Table 78 are present
        var domainNames = cert.DomainVerdicts.Select(d => d.Domain).ToList();
        Assert.Contains("ThemeEngine", domainNames);
        Assert.Contains("PermissionEngine", domainNames);
        Assert.Contains("EventLedger", domainNames);
        Assert.Contains("EvidenceIntegrity", domainNames);
        Assert.Contains("RunOrchestration", domainNames);
        Assert.Contains("Adapters", domainNames);
        Assert.Contains("StateLattice", domainNames);
        Assert.Contains("FaultForensics", domainNames);
        Assert.Contains("AiGovernance", domainNames);
        Assert.Contains("DataOracles", domainNames);
        Assert.Contains("Reports", domainNames);
        Assert.Contains("Clearance", domainNames);

        // Verify certificate is sealed in evidence store
        Assert.NotNull(cert.SealedEvidence);
        Assert.True(cert.SealedEvidence!.Sealed);
        Assert.Equal("SelfHostingCertificate", cert.SealedEvidence.Kind);

        // Verify audit event written to ledger
        var events = _store.RecentEvents(10);
        Assert.Contains(events, e => e.Code == "SELFTEST.QUALIFIED");
    }

    [Fact]
    public async Task SelfTestEngine_TamperDetection_FlagsCorruptedPayload()
    {
        var engine = new SelfTestEngine(_store);
        var cert = await engine.RunFullSelfTestAsync();

        Assert.NotNull(cert.SealedEvidence);
        var ev = cert.SealedEvidence!;

        // Cryptographic check: uncorrupted payload matches SHA-256
        Assert.Equal(ev.Sha256, Hashing.Sha256(ev.Payload));

        // Corrupt one character in the payload
        var tamperedPayload = ev.Payload.Replace("SELF_HOST_QUALIFIED", "TAMPERED_VERDICT");
        var tamperedSha = Hashing.Sha256(tamperedPayload);

        // Assert tamper is detectable
        Assert.NotEqual(ev.Sha256, tamperedSha);
    }
}
