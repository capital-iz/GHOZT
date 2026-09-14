using System.IO;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class OperationalEnginesTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public OperationalEnginesTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"ghozt_op_test_{Guid.NewGuid():N}.db");
        _store = new GhoztStore(_tempDbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
            var wal = _tempDbPath + "-wal";
            if (File.Exists(wal)) File.Delete(wal);
            var shm = _tempDbPath + "-shm";
            if (File.Exists(shm)) File.Delete(shm);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void ClearanceEngineEvaluatesReleasePolicyGatesCorrectly()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Production System",
            Kind = "Application",
            Environment = TargetEnvironment.Production,
            Access = AccessClass.Interact,
            BaseAddress = Environment.GetFolderPath(Environment.SpecialFolder.System),
            BuildLabel = "v1.0.0",
            Reachable = true,
        };
        _store.Save(target);

        // Add completed sweep
        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Full Sweep",
            Mode = "Full sweep",
            State = RunState.Completed,
            StatesVisited = 15,
        };
        _store.Save(sweep);

        // Add sealed evidence
        _store.Save(new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            SweepId = sweep.Id,
            Kind = "test:evidence",
            Summary = "Baseline verification",
            Sha256 = Hashing.Sha256("payload"),
            Sealed = true,
        });

        var engine = new ClearanceEngine(_store);
        var decision = engine.EvaluateTarget(target);

        Assert.Equal(ClearanceOutcome.Cleared, decision.Outcome);
        Assert.Equal(8, decision.Gates.Count);
        Assert.All(decision.Gates.Where(g => g.Mandatory), g => Assert.True(g.Passed));
        Assert.NotEmpty(decision.EvidenceDigest);
    }

    [Fact]
    public void ClearanceEngineDeniesReleaseWhenCriticalDefectExists()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Flawed System",
            Kind = "Application",
            Environment = TargetEnvironment.Staging,
            Access = AccessClass.Interact,
            Reachable = true,
        };
        _store.Save(target);

        // Inject critical defect
        _store.Save(new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Fatal Memory Corruption Crash",
            Severity = Severity.Critical,
            Actual = "AccessViolationException",
            Expected = "Clean execution",
        });

        var engine = new ClearanceEngine(_store);
        var decision = engine.EvaluateTarget(target);

        Assert.Equal(ClearanceOutcome.Denied, decision.Outcome);
        var criticalGate = decision.Gates.First(g => g.Id == "GATE.ZERO_CRITICAL");
        Assert.False(criticalGate.Passed);
    }

    [Fact]
    public void AlgorithmHarnessRunsAndProvesAllInvariants()
    {
        var report = AlgorithmHarness.RunSuite();

        Assert.NotNull(report);
        Assert.True(report.Results.Count >= 5);
        Assert.Equal(0, report.FailedCount);
        Assert.Equal(report.Results.Count, report.PassedCount);
        Assert.NotEmpty(report.Sha256Digest);
        Assert.True(report.TotalDuration.TotalSeconds < 5.0);
    }

    [Fact]
    public void RegressionEngineDerivesGuardsFromConfirmedFaults()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Regressing Target",
            Kind = "Application",
            Environment = TargetEnvironment.Development,
            Access = AccessClass.Interact,
        };
        _store.Save(target);

        _store.Save(new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Button click deadlock",
            Severity = Severity.High,
            Actual = "Window unresponsive",
            Expected = "Dialog opens within 500ms",
            Fingerprint = "fp_deadlock_123",
            Maturity = FindingMaturity.Verified, // Resolved
        });

        _store.Save(new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Null pointer in report export",
            Severity = Severity.Medium,
            Actual = "NullReferenceException",
            Expected = "Report saved to disk",
            Fingerprint = "fp_null_export_456",
            Maturity = FindingMaturity.New, // Open
        });

        var engine = new RegressionEngine(_store);
        var guards = engine.GenerateGuards(target.Id);

        Assert.Equal(2, guards.Count);
        Assert.Contains(guards, g => g.Status == GuardStatus.Passing && g.Title.Contains("deadlock"));
        Assert.Contains(guards, g => g.Status == GuardStatus.Failing && g.Title.Contains("report export"));
    }

    [Fact]
    public void ReportGeneratorCompilesDossierWithCryptographicSeals()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Reportable App",
            Kind = "Application",
            Environment = TargetEnvironment.Staging,
            Access = AccessClass.Interact,
            BuildLabel = "rc-20260913",
            Reachable = true,
            BaseAddress = Environment.GetFolderPath(Environment.SpecialFolder.System),
        };
        _store.Save(target);

        var generator = new ReportGenerator(_store);
        var report = generator.Generate(target, "auditor");

        Assert.NotNull(report);
        Assert.Contains("Reportable App", report.MarkdownContent);
        Assert.Contains("<!DOCTYPE html>", report.HtmlContent);
        Assert.Contains("\"TargetName\": \"Reportable App\"", report.JsonContent);
        Assert.NotEmpty(report.Sha256Seal);
        Assert.Equal(32, Hashing.Fingerprint("test").Length);
    }

    [Fact]
    public void CausalForensicsEngineFormulatesHypothesisAndInterventions()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Unreachable Target",
            Kind = "Service",
            Environment = TargetEnvironment.Staging,
            Access = AccessClass.ObserveOnly,
            BaseAddress = @"D:\non_existent_folder\api",
            Reachable = false,
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Path not found at probe time",
            Severity = Severity.High,
            Actual = "Path not found: D:\\non_existent_folder\\api",
            Expected = "Valid readable directory path",
            Fingerprint = "fp_path_not_found_123",
        };
        _store.Save(finding);

        var engine = new CausalForensicsEngine(_store);
        var hyp = engine.Investigate(finding, target);

        Assert.NotNull(hyp);
        Assert.StartsWith("HYP-", hyp.Id);
        Assert.Contains("inaccessible", hyp.Claim);
        Assert.NotEmpty(hyp.Interventions);
        Assert.Contains(hyp.Interventions, cfx => cfx.Class == InterventionClass.NegativeControl);
        Assert.Contains(hyp.Interventions, cfx => cfx.Class == InterventionClass.EnvironmentIntervention);
        Assert.True(hyp.Confidence > 0.4);
        Assert.NotEqual(CausalStatus.Refuted, hyp.Status);
    }

    [Fact]
    public void FixBriefEngineBuildsBoundedRepairContract()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Core API",
            Kind = "Service",
            Environment = TargetEnvironment.Development,
            Access = AccessClass.Interact,
            RepositoryPath = @"C:\repos\core_api",
            BaseAddress = "http://localhost:5000",
            Reachable = true,
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Session refresh produces HTTP 500",
            Severity = Severity.Critical,
            Actual = "HTTP 500 Internal Server Error",
            Expected = "HTTP 200 with refreshed JWT",
            Fingerprint = "fp_jwt_refresh_500",
            NodeId = "node-auth-refresh",
        };
        _store.Save(finding);

        var causalEngine = new CausalForensicsEngine(_store);
        var hyp = causalEngine.Investigate(finding, target);

        var fixEngine = new FixBriefEngine(_store);
        var brief = fixEngine.Generate(finding, target, hyp);

        Assert.NotNull(brief);
        Assert.StartsWith("FXB-", brief.Id);
        Assert.Contains(@"C:\repos\core_api", brief.AllowedScope);
        Assert.NotEmpty(brief.ForbiddenScope);
        Assert.Equal(7, brief.RejectionConditions.Count);
        Assert.Equal(FixBriefStatus.Proposed, brief.Status);

        var md = brief.ToMarkdown();
        Assert.Contains("# Fix Brief: FXB-", md);
        Assert.Contains("Mandatory Repair Rejection Conditions", md);
        Assert.Contains("Minimal Reproduction Sequence", md);
    }

    [Fact]
    public async Task RegressionEngineExecutesGuardsAsynchronously()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Guarded App",
            Kind = "Application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = Environment.GetFolderPath(Environment.SpecialFolder.System),
            BuildLabel = "1.0.0",
            Reachable = true,
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Crash on startup",
            Severity = Severity.Critical,
            Actual = "Path not found: D:\\missing",
            Expected = "Target path exists and is readable",
            Fingerprint = "fp_crash_1",
            Maturity = FindingMaturity.Verified,
        };
        _store.Save(finding);

        var engine = new RegressionEngine(_store);
        var evaluated = await engine.ExecuteGuardsAsync(target);

        Assert.NotEmpty(evaluated);
        var events = _store.RecentEvents(10);
        Assert.Contains(events, e => e.Code is "RGR.PASS" or "RGR.FAIL");
    }

    [Fact]
    public void ManifestEngineDiscoversFeaturesAndLatticeNodes()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "System Library",
            Kind = "Library",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = Environment.GetFolderPath(Environment.SpecialFolder.System),
            Reachable = true,
        };
        _store.Save(target);

        var engine = new ManifestEngine(_store);
        var manifest = engine.Discover(target);

        Assert.NotNull(manifest);
        Assert.StartsWith("MAN-", manifest.Id);
        Assert.NotEmpty(manifest.Features);

        var nodes = _store.Nodes(target.Id);
        Assert.NotEmpty(nodes);

        var events = _store.RecentEvents(10);
        Assert.Contains(events, e => e.Code == "MAN.DISCOVER.OK");
    }

    [Fact]
    public void AlgorithmHarnessSealsReportToEvidenceVault()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Math Platform",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            Reachable = true,
        };
        _store.Save(target);

        var report = AlgorithmHarness.RunSuite();
        Assert.NotNull(report);
        Assert.Equal(7, report.Results.Count);
        Assert.Equal(7, report.PassedCount);

        var evidence = AlgorithmHarness.SealReportToEvidence(_store, target.Id, report);
        Assert.NotNull(evidence);
        Assert.True(evidence.Sealed);
        Assert.Equal("algorithm-proof", evidence.Kind);

        var savedItems = _store.EvidenceItems(target.Id);
        Assert.Contains(savedItems, e => e.Id == evidence.Id && e.Sealed);
    }
}

