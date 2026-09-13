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
        Assert.Equal(6, decision.Gates.Count);
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
}
