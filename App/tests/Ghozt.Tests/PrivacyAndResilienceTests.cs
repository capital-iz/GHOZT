using System.IO;
using System.Text;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class PrivacyAndResilienceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public PrivacyAndResilienceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_res_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_res_test.db");
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

    // ---------------------------------------------------------------- REDACTION ENGINE (Patent §35, §22)

    [Fact]
    public void RedactionEngine_ContainsSensitiveData_DetectsCredentialsAndTokens()
    {
        var clean = "Window Title: Ghozt Main Shell. Rendering at 60fps. Memory: 142MB.";
        Assert.False(RedactionEngine.ContainsSensitiveData(clean));

        var bearer = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.doNotLeakThisToken";
        Assert.True(RedactionEngine.ContainsSensitiveData(bearer));

        var apiKey = "Config: api_key=ciz_live_9876543210fedcba9876543210";
        Assert.True(RedactionEngine.ContainsSensitiveData(apiKey));

        var connStr = "Server=db.capital-iz.internal;Database=Ghozt;User Id=sa;Password=SuperSecretP@ssw0rd!;";
        Assert.True(RedactionEngine.ContainsSensitiveData(connStr));

        var email = "Audit operator: engineer@capital-iz.com reviewed test run.";
        Assert.True(RedactionEngine.ContainsSensitiveData(email));
    }

    [Fact]
    public void RedactionEngine_Redact_ReplacesSecretsWithMaskTokens_PreservesStructure()
    {
        var payload = "auth: Bearer secret_bearer_token_123456\n"
                    + "connection: Server=127.0.0.1;Database=Ghozt;Pwd=TopSecret12345;\n"
                    + "contact: audit-lead@capital-iz.com";

        var redacted = RedactionEngine.Redact(payload);

        Assert.DoesNotContain("secret_bearer_token_123456", redacted);
        Assert.DoesNotContain("TopSecret12345", redacted);
        Assert.DoesNotContain("audit-lead@capital-iz.com", redacted);

        Assert.Contains("Bearer [REDACTED:TOKEN]", redacted);
        Assert.Contains("Pwd=[REDACTED:DB_PASSWORD];", redacted);
        Assert.Contains("[REDACTED:EMAIL]", redacted);
        Assert.Contains("Server=127.0.0.1;Database=Ghozt;", redacted);
    }

    [Fact]
    public void RedactionEngine_RedactEvidence_UpdatesPayloadAndHash_PreservesLineageInLedger()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Evidence Privacy Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
        };
        _store.Save(target);

        var rawPayload = "Dump: client_secret=\"CS_99887766554433221100\" for admin@capital-iz.com";
        var rawHash = Hashing.Sha256(rawPayload);

        var evidence = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "debug-dump",
            Summary = "Crash memory dump containing credentials",
            Sha256 = rawHash,
            SizeBytes = Encoding.UTF8.GetByteCount(rawPayload),
            Payload = rawPayload,
            Redacted = false,
        };
        _store.Save(evidence);

        var result = RedactionEngine.RedactEvidence(evidence, _store, "AuditorAlice");

        Assert.True(result.Redacted);
        Assert.Contains("[Redacted]", result.Summary);
        Assert.DoesNotContain("CS_99887766554433221100", result.Payload);
        Assert.Contains("[REDACTED:API_KEY]", result.Payload);
        Assert.NotEqual(rawHash, result.Sha256);
        Assert.Equal(Hashing.Sha256(result.Payload), result.Sha256);

        // Verify ledger event
        var events = _store.RecentEvents(10);
        var redactEvent = Assert.Single(events, e => e.Code == "EVD.REDACT");
        Assert.Equal("AuditorAlice", redactEvent.Actor);
        Assert.Contains(rawHash, redactEvent.Detail);
        Assert.Contains(result.Sha256, redactEvent.Detail);
    }

    [Fact]
    public void RedactionEngine_RedactTargetEvidence_ScansAndBatchSanitizes()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Batch Privacy Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
        };
        _store.Save(target);

        // 1. Sensitive
        var ev1 = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "logs",
            Summary = "Log with token",
            Sha256 = "dummy1",
            Payload = "User token: Bearer abcdef1234567890",
        };
        // 2. Clean
        var ev2 = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "logs",
            Summary = "Clean log",
            Sha256 = "dummy2",
            Payload = "Application booted cleanly.",
        };
        // 3. Sensitive
        var ev3 = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "stdout",
            Summary = "Stdout with password",
            Sha256 = "dummy3",
            Payload = "password=SuperSecretPassword123!",
        };

        _store.Save(ev1);
        _store.Save(ev2);
        _store.Save(ev3);

        var count = RedactionEngine.RedactTargetEvidence(_store, target.Id);
        Assert.Equal(2, count);

        var all = _store.EvidenceItems(target.Id);
        Assert.True(all.First(e => e.Id == ev1.Id).Redacted);
        Assert.False(all.First(e => e.Id == ev2.Id).Redacted);
        Assert.True(all.First(e => e.Id == ev3.Id).Redacted);
    }

    // ---------------------------------------------------------------- MUTATION & FALSE-CONFIDENCE ASSAYS (Patent §31)

    [Fact]
    public async Task RegressionEngine_VerifyGuardEfficacyAsync_KillsMutantsAndDetectsEffectiveGuards()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Mutation Test Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v0.4.0-test",
            Reachable = true,
        };
        _store.Save(target);

        var engine = new RegressionEngine(_store);
        var report = await engine.VerifyGuardEfficacyAsync(target);

        Assert.True(report.TotalMutants >= 2);
        Assert.Equal(report.TotalMutants, report.MutantsKilled);
        Assert.Equal(0, report.MutantsSurvived);
        Assert.Equal(1.0, report.MutationScore);
        Assert.Empty(report.IneffectiveGuardIds);

        var events = _store.RecentEvents(20);
        Assert.Contains(events, e => e.Code == "RGR.MUTATION.KILLED");
        Assert.Contains(events, e => e.Code == "RGR.EFFICACY.SUMMARY");
    }

    // ---------------------------------------------------------------- SOAK & DRIFT ANALYSIS (Patent §21)

    [Fact]
    public void SoakAnalysisEngine_CalculateDrift_DetectsDeterministicMemoryLeak()
    {
        var now = DateTimeOffset.Now;
        var samples = new List<ResourceSample>();

        // 60 samples, 1 second apart, increasing 0.1 MB/s (steady deterministic leak)
        for (var i = 0; i < 60; i++)
        {
            samples.Add(new ResourceSample
            {
                Timestamp = now.AddSeconds(i),
                WorkingSetMB = 100.0 + (i * 0.10),
                PrivateBytesMB = 90.0 + (i * 0.08),
                ThreadCount = 12,
                HandleCount = 200,
            });
        }

        var metrics = SoakAnalysisEngine.CalculateDrift(
            samples,
            s => s.WorkingSetMB,
            "WorkingSetMB",
            SoakAnalysisEngine.MemoryLeakSlopeThresholdMBPerSec);

        Assert.True(metrics.IsLeaking);
        Assert.True(metrics.SlopePerSecond >= 0.09 && metrics.SlopePerSecond <= 0.11);
        Assert.True(metrics.RSquared >= 0.99);
        Assert.True(metrics.ProjectedGrowthPerHour >= 350.0);
    }

    [Fact]
    public void SoakAnalysisEngine_CalculateDrift_RejectsFlatOrFluctuatingTelemetry()
    {
        var now = DateTimeOffset.Now;
        var samples = new List<ResourceSample>();

        // 60 samples with flat baseline + minimal fluctuation
        for (var i = 0; i < 60; i++)
        {
            var noise = (i % 2 == 0 ? 0.2 : -0.2);
            samples.Add(new ResourceSample
            {
                Timestamp = now.AddSeconds(i),
                WorkingSetMB = 150.0 + noise,
                PrivateBytesMB = 120.0 + noise,
                ThreadCount = 14,
                HandleCount = 250,
            });
        }

        var metrics = SoakAnalysisEngine.CalculateDrift(
            samples,
            s => s.WorkingSetMB,
            "WorkingSetMB",
            SoakAnalysisEngine.MemoryLeakSlopeThresholdMBPerSec);

        Assert.False(metrics.IsLeaking);
        Assert.True(metrics.SlopePerSecond < 0.01);
    }

    [Fact]
    public void SoakAnalysisEngine_Analyze_GeneratesPhantomFinding_WhenCriticalDriftObserved()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Soak Telemetry Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
        };
        _store.Save(target);

        var now = DateTimeOffset.Now;
        var samples = new List<ResourceSample>();
        for (var i = 0; i < 100; i++)
        {
            samples.Add(new ResourceSample
            {
                Timestamp = now.AddSeconds(i),
                WorkingSetMB = 120.0 + (i * 0.12),
                PrivateBytesMB = 100.0 + (i * 0.10),
                ThreadCount = 10 + (i / 10),
                HandleCount = 300,
            });
        }

        var report = SoakAnalysisEngine.Analyze(target, samples, _store);

        Assert.True(report.HasCriticalDrift);
        Assert.NotNull(report.GeneratedFinding);
        Assert.Equal(Severity.High, report.GeneratedFinding.Severity);
        Assert.Contains("Deterministic Leak", report.GeneratedFinding.Title);

        var findings = _store.Findings(target.Id);
        Assert.Single(findings);

        var events = _store.RecentEvents(10);
        Assert.Contains(events, e => e.Code == "SOAK.DRIFT.DETECTED");
    }
}
