using System.IO;
using System.Text.Json;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class PerformanceSoakAndClearanceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public PerformanceSoakAndClearanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_soak_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_soak_test.db");
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
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task SweepEngine_PerformanceSoakMode_ExecutesAndEmitsSealedEvidence()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Soak Test Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v0.5.0-test",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Performance Soak Sweep",
            Mode = "Performance Soak",
            State = RunState.PreFlight,
            MaxStates = 50,
            MaxSeconds = 180,
            MaxDepth = 3,
            CreatedAt = DateTimeOffset.Now,
        };

        var engine = new SweepEngine(_store);
        var completed = await engine.RunAsync(sweep, target, null, CancellationToken.None);

        Assert.Equal(RunState.Completed, completed.State);
        Assert.True(completed.StatesVisited >= 5);
        Assert.True(completed.EvidenceCaptured >= 5);

        // Verify sealed performance:soak evidence
        var evidence = _store.EvidenceItems(target.Id);
        var soakEvidence = Assert.Single(evidence, e => e.Kind == "performance:soak");
        Assert.True(soakEvidence.Sealed);
        Assert.False(string.IsNullOrWhiteSpace(soakEvidence.Sha256));
        Assert.Contains("Soak & Resilience", soakEvidence.Summary);

        // Verify lattice node
        var nodes = _store.Nodes(target.Id);
        Assert.Contains(nodes, n => n.Id == "check:soak and resilience analysis");
    }

    [Fact]
    public void ReportGenerator_BuildHtml_ContainsSvgChartsAndCapitalIzTheming()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "HTML Dossier Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v0.5.0-gold",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Full Sweep",
            Mode = "Full sweep",
            State = RunState.Completed,
            StatesVisited = 42,
            DeviationsFound = 0,
            EvidenceCaptured = 8,
            CreatedAt = DateTimeOffset.Now,
            FinishedAt = DateTimeOffset.Now,
        };
        _store.Save(sweep);

        var generator = new ReportGenerator(_store);
        var report = generator.Generate(target, "AuditorBob");

        Assert.False(string.IsNullOrWhiteSpace(report.HtmlContent));
        Assert.Contains("<svg", report.HtmlContent);
        Assert.Contains("circle", report.HtmlContent);
        Assert.Contains("Forensic Examination Dossier", report.HtmlContent);
        Assert.Contains("Release Policy Clearance Gates", report.HtmlContent);
        Assert.Contains("Capital-Iz Ghozt", report.HtmlContent);
        Assert.Contains("badge-", report.HtmlContent);
        Assert.Contains("AuditorBob", report.GeneratedBy);
        Assert.False(string.IsNullOrWhiteSpace(report.Sha256Seal));
    }

    [Fact]
    public void ReportGenerator_BuildSarif_UsesDynamicProductVersion()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "SARIF Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v0.5.0-sarif",
        };

        var findings = new List<Finding>
        {
            new()
            {
                Id = _store.NextId(IdKind.Fault),
                TargetId = target.Id,
                Title = "Test Fault for SARIF",
                Severity = Severity.High,
                Actual = "Observed fault",
                Expected = "Nominal state",
                Fingerprint = "12345678abcdef",
                FirstSeenAt = DateTimeOffset.Now,
                LastSeenAt = DateTimeOffset.Now,
            }
        };

        var sarifJson = ReportGenerator.BuildSarif(target, findings);
        Assert.False(string.IsNullOrWhiteSpace(sarifJson));

        using var doc = JsonDocument.Parse(sarifJson);
        var root = doc.RootElement;
        var version = root.GetProperty("version").GetString();
        Assert.Equal("2.1.0", version);

        var runs = root.GetProperty("runs");
        var driver = runs[0].GetProperty("tool").GetProperty("driver");
        var toolVersion = driver.GetProperty("version").GetString();

        Assert.NotEqual("0.2.7", toolVersion);
        Assert.Equal(ReportGenerator.GetProductVersion(), toolVersion);
    }

    [Fact]
    public void ClearanceEngine_EvaluateTarget_EvaluatesPerformanceSoakAndAntiGamingGates()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Clearance Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v0.5.0-clearance",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Baseline Sweep",
            Mode = "Smoke",
            State = RunState.Completed,
            StatesVisited = 10,
            DeviationsFound = 0,
            EvidenceCaptured = 1,
            CreatedAt = DateTimeOffset.Now,
        };
        _store.Save(sweep);

        var payload = "check=Reachability\noutcome=pass";
        var evidence = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "check:reachability",
            Summary = "Reachability passed",
            Payload = payload,
            Sha256 = Hashing.Sha256(payload),
            SizeBytes = payload.Length,
        };
        _store.Save(evidence);

        var engine = new ClearanceEngine(_store);
        var decision = engine.EvaluateTarget(target, "ReviewerDan");

        var soakGate = Assert.Single(decision.Gates, g => g.Id == "GATE.PERFORMANCE_SOAK");
        Assert.True(soakGate.Passed);
        Assert.True(soakGate.Mandatory);

        var antiGamingGate = Assert.Single(decision.Gates, g => g.Id == "GATE.ANTI_GAMING");
        Assert.True(antiGamingGate.Passed);
        Assert.True(antiGamingGate.Mandatory);
    }

    [Fact]
    public void ClearanceEngine_AntiGamingGate_DetectsTamperedEvidencePayload()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Tamper Test Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v0.5.0-tamper",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Tamper Sweep",
            Mode = "Smoke",
            State = RunState.Completed,
            StatesVisited = 5,
            DeviationsFound = 0,
            EvidenceCaptured = 1,
            CreatedAt = DateTimeOffset.Now,
        };
        _store.Save(sweep);

        // Create tampered evidence: payload does not match SHA-256 digest
        var evidence = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "check:reachability",
            Summary = "Tampered evidence item",
            Payload = "Tampered payload that was modified after hashing",
            Sha256 = Hashing.Sha256("Original authentic payload"),
            SizeBytes = 40,
        };
        _store.Save(evidence);

        var engine = new ClearanceEngine(_store);
        var decision = engine.EvaluateTarget(target, "ReviewerDan");

        var antiGamingGate = Assert.Single(decision.Gates, g => g.Id == "GATE.ANTI_GAMING");
        Assert.False(antiGamingGate.Passed);
        Assert.Contains("tampered", antiGamingGate.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ClearanceOutcome.Denied, decision.Outcome);
    }
}
