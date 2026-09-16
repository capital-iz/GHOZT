using System;
using System.Linq;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

/// <summary>
/// Comprehensive verification tests for the Universal Multi-Domain Audit Engines:
/// 1. First-Principles Scientific Mathematical Computing Audit
/// 2. Machine Learning & Agentic AI Hardening / Eval Engine
/// 3. Mobile Application Runtime & Android Emulator E2E Engine
/// 4. Windows Native Desktop Runtime & UI Automation Engine
/// </summary>
public sealed class MultiDomainAuditTests
{
    [Fact]
    public void ScientificMathAudit_VerifiesFormulasAndBoundaryInvariants()
    {
        var engine = new ScientificMathAuditEngine();
        var results = engine.RunFullAudit();

        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.True(r.Passed, $"Math audit failed: {r.AuditName} - {r.Summary}"));

        // Verify specific invariants:
        var verlet = results.First(r => r.Domain == MathAuditDomain.ClassicalMechanics);
        Assert.True(verlet.MetricValue < 0.01, "Verlet energy drift must be < 1%");

        var quat = results.First(r => r.Domain == MathAuditDomain.RotationalKinematics);
        Assert.True(quat.MetricValue < 1e-9, "Quaternion norm error must be < 1e-9");

        var robust = results.First(r => r.Domain == MathAuditDomain.InferentialStatistics);
        Assert.True(robust.MetricValue < 2.5, "Robust MAD std must be < 2.5");

        var dag = results.First(r => r.Domain == MathAuditDomain.LayeredDagCascade);
        Assert.True(dag.MetricValue < 1e-12, "Probability sum error must be < 1e-12");

        var dossier = ScientificMathAuditEngine.GenerateMarkdownDossier(results);
        Assert.Contains("# First-Principles Mathematical & Layered Algorithmic Audit Dossier", dossier);
        Assert.Contains("Hamiltonian Energy Conservation", dossier);
    }

    [Fact]
    public void AgenticAiAudit_VerifiesDataContractsAndConvergence()
    {
        var engine = new AgenticAiAuditEngine();
        var results = engine.RunFullAudit();

        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.True(r.Passed, $"AI audit failed: {r.AuditName} - {r.Summary}"));

        var leakage = results.First(r => r.Category == AiAuditCategory.DataContractsAndLeakage);
        Assert.Equal(0.0, leakage.MetricValue);

        var stats = results.First(r => r.Category == AiAuditCategory.ModelConvergenceAndSignificance);
        Assert.True(stats.MetricValue >= 2.0, "Day-clustered t-statistic must be >= 2.0");

        var loops = results.First(r => r.AuditName.Contains("Circular Tool Calling"));
        Assert.True(loops.Passed, "Loop governor must intercept circular sequences");

        var dossier = AgenticAiAuditEngine.GenerateMarkdownDossier(results);
        Assert.Contains("# Machine Learning & Agentic AI Hardening Audit Dossier", dossier);
        Assert.Contains("Zero Lookahead", dossier);
    }

    [Fact]
    public void MobileAudit_DetectsViolationsAndGeneratesFixBriefs()
    {
        var engine = new MobileAuditEngine();
        var results = engine.RunFullAudit();

        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.True(r.Passed, $"Mobile audit failed: {r.AuditName} - {r.Summary}"));

        // Test SQLite execSQL row returning violation detection (Doctrine §25.3)
        var rowCheck = MobileAuditEngine.DetectSqliteRowExecSqlViolation("Error executing: PRAGMA journal_mode = WAL\nQueries can be performed using SQLiteDatabase query or rawQuery methods only");
        Assert.True(rowCheck, "Must detect Android execSQL row violation");

        // Test Flutter layout overflow parser
        var overflow = MobileAuditEngine.DetectFlutterOverflow("A RenderFlex overflowed by 64 pixels on the bottom. The relevant error-causing widget was: Column main.dart:88:12");
        Assert.NotNull(overflow);
        Assert.Equal(64, overflow.Pixels);
        Assert.Equal("bottom", overflow.Direction);

        // Test Stack Trace Fix Brief
        const string stack = "Unhandled exception:\nNull check operator used on a null value\n#0      AuthGuard.verify (package:app/auth.dart:32:10)\n";
        var brief = MobileAuditEngine.ParseStackTraceToFixBrief(stack);
        Assert.Contains("auth.dart", brief.TargetFile);
        Assert.Equal(32, brief.LineNumber);

        var dossier = MobileAuditEngine.GenerateMarkdownDossier(results);
        Assert.Contains("# Android Mobile Emulator E2E & UI Runtime Audit Dossier", dossier);
    }

    [Fact]
    public void DesktopRuntimeAudit_SupervisesProcessAndStorage()
    {
        var engine = new DesktopRuntimeAuditEngine();
        var results = engine.RunFullAudit();

        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.True(r.Passed, $"Desktop audit failed: {r.AuditName} - {r.Summary}"));

        // Verify telemetry bounding
        var telemetry = DesktopRuntimeAuditEngine.AuditProcessTelemetryBounds(9999, 120 * 1024 * 1024, 75 * 1024 * 1024);
        Assert.True(telemetry.Passed, "Memory within 250MB bounds must pass");

        var badTelemetry = DesktopRuntimeAuditEngine.AuditProcessTelemetryBounds(9999, 320 * 1024 * 1024, 75 * 1024 * 1024);
        Assert.False(badTelemetry.Passed, "Memory > 250MB must fail");

        var dossier = DesktopRuntimeAuditEngine.GenerateMarkdownDossier(results);
        Assert.Contains("# Windows Native Desktop Runtime & UI Automation Audit Dossier", dossier);
    }

    [Fact]
    public void ConfigurableMathAudit_RespectsParametersAndGeneratesFixDiffs()
    {
        var engine = new ScientificMathAuditEngine();
        var results = engine.RunConfigurableAudit(dt: 0.005, steps: 500, outlierRatio: 0.15, dampingEpsilon: 1e-10);

        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.True(r.Passed, $"Configurable math audit failed: {r.AuditName}"));

        foreach (var r in results)
        {
            var diff = ScientificMathAuditEngine.GenerateSelfHealingCodeFix(r);
            Assert.False(string.IsNullOrWhiteSpace(diff), $"Diff for {r.AuditName} must not be empty");
            Assert.Contains("--- a/", diff);
            Assert.Contains("+++ b/", diff);
        }
    }

    [Fact]
    public void ConfigurableAiAudit_DetectsCyclesAndGeneratesFixDiffs()
    {
        var engine = new AgenticAiAuditEngine();
        var results = engine.RunConfigurableAudit(sampleTrades: 60, targetSharpe: 1.5, maxRecursionDepth: 5, tokenBudgetThreshold: 100_000);

        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.True(r.Passed, $"Configurable AI audit failed: {r.AuditName}"));

        // Test cycle detection
        var cyclicalTools = new[] { "search", "view_file", "search", "view_file", "search", "view_file" };
        var hasCycle = AgenticAiAuditEngine.DetectToolCycle(cyclicalTools, 3);
        Assert.True(hasCycle, "DetectToolCycle must catch 3 repeats of length 2");

        var nonCyclical = new[] { "search", "view_file", "edit_file", "run_tests", "commit" };
        var noCycle = AgenticAiAuditEngine.DetectToolCycle(nonCyclical, 3);
        Assert.False(noCycle, "DetectToolCycle must return false for non-repeating sequence");

        foreach (var r in results)
        {
            var diff = AgenticAiAuditEngine.GenerateSelfHealingCodeFix(r);
            Assert.False(string.IsNullOrWhiteSpace(diff), $"Diff for {r.AuditName} must not be empty");
            Assert.Contains("--- a/", diff);
            Assert.Contains("+++ b/", diff);
        }
    }

    [Fact]
    public void MobileAudit_CommandSynthesisAndAdbDiscovery()
    {
        var tap = MobileAuditEngine.BuildTapCommand(450, 800);
        Assert.Equal("shell input tap 450 800", tap);

        var swipe = MobileAuditEngine.BuildSwipeCommand(100, 200, 100, 800, 300);
        Assert.Equal("shell input swipe 100 200 100 800 300", swipe);

        var adbPath = MobileAuditEngine.FindAdbPath();
        Assert.False(string.IsNullOrWhiteSpace(adbPath), "FindAdbPath must return an executable candidate or adb fallback");

        var sampleDevicesOutput = "List of devices attached\r\nemulator-5554\tdevice\r\n192.168.1.50:5555\tunauthorized\r\n";
        var devices = MobileAuditEngine.ParseAdbDevices(sampleDevicesOutput);
        Assert.Equal(2, devices.Count);
        Assert.Equal("emulator-5554", devices[0].Id);
        Assert.Equal("device", devices[0].State);
        Assert.Equal("192.168.1.50:5555", devices[1].Id);
        Assert.Equal("unauthorized", devices[1].State);
    }

    [Fact]
    public void DesktopAudit_TelemetrySamplingAndDbVerification()
    {
        var currentPid = Environment.ProcessId;
        var snapshot = DesktopRuntimeAuditEngine.SampleProcessTelemetry(currentPid);
        Assert.NotNull(snapshot);
        Assert.Equal(currentPid, snapshot.Pid);
        Assert.True(snapshot.WorkingSetMb > 0, "WorkingSetMb must be positive");
        Assert.True(snapshot.ThreadCount > 0, "ThreadCount must be positive");

        // Verify SQLite database verification logic on a temp db
        var tempDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ghozt_audit_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new GhoztStore(tempDb))
            {
                var (healthy, integrity, mode) = DesktopRuntimeAuditEngine.VerifySqliteDatabase(tempDb);
                Assert.True(healthy, "Fresh store must be healthy");
                Assert.Equal("ok", integrity.ToLowerInvariant());
                Assert.Equal("wal", mode.ToLowerInvariant());
            }
        }
        finally
        {
            if (System.IO.File.Exists(tempDb)) System.IO.File.Delete(tempDb);
            var wal = tempDb + "-wal";
            if (System.IO.File.Exists(wal)) System.IO.File.Delete(wal);
            var shm = tempDb + "-shm";
            if (System.IO.File.Exists(shm)) System.IO.File.Delete(shm);
        }
    }

    [Fact]
    public void PatchApplicator_AppliesPatchAndCreatesBackup()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ghozt_patch_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            var targetFile = System.IO.Path.Combine(tempDir, "sample_code.cs");
            System.IO.File.WriteAllText(targetFile, "public class Sample { public void Foo() { int x = 1; } }");

            const string diff = """
            --- a/sample_code.cs
            +++ b/sample_code.cs
            @@ -1,1 +1,1 @@
            -int x = 1;
            +int x = 42; // Self-healed
            """;

            var result = PatchApplicator.ApplyPatch(diff, tempDir);
            Assert.True(result.Success, $"Patch application failed: {result.Message}");
            Assert.True(System.IO.File.Exists(targetFile + ".bak"), "Backup file must be created");

            var updatedContent = System.IO.File.ReadAllText(targetFile);
            Assert.Contains("int x = 42;", updatedContent);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LogcatStreaming_ParsesViolationsAndProvidesSummary()
    {
        var streamResult = MobileAuditEngine.FetchLiveLogcat(null, "*", 60);
        Assert.NotNull(streamResult);
        Assert.NotEmpty(streamResult.Lines);
        Assert.Contains("Streamed", streamResult.Summary);

        // Deterministic violation parsing assertions
        const string mockViolationLog = "E/SQLiteDatabase: Error executing: PRAGMA journal_mode = WAL\nQueries can be performed using SQLiteDatabase query or rawQuery methods only\n" +
                                        "E/flutter: A RenderFlex overflowed by 48 pixels on the bottom.";
        Assert.True(MobileAuditEngine.DetectSqliteRowExecSqlViolation(mockViolationLog));
        var overflow = MobileAuditEngine.DetectFlutterOverflow(mockViolationLog);
        Assert.NotNull(overflow);
        Assert.Equal(48, overflow.Pixels);
    }

    [Fact]
    public void DesktopInspection_EnumeratesWindowAndControls()
    {
        var inspection = DesktopRuntimeAuditEngine.InspectProcessWindowControls();
        Assert.NotNull(inspection);
        Assert.True(inspection.Pid > 0, "PID must be positive");
        Assert.False(string.IsNullOrWhiteSpace(inspection.ProcessName), "ProcessName must not be empty");
        Assert.NotEmpty(inspection.Controls);
        Assert.Contains(inspection.Controls, c => c.AutomationId == "RunAuditButton");
        Assert.Contains(inspection.Controls, c => c.AutomationId == "AuditConsoleTextBox");
    }

    [Fact]
    public void EnergyTrajectory_VerletRemainsBoundedWhileEulerDiverges()
    {
        var trajectory = ScientificMathAuditEngine.ComputeEnergyTrajectory(dt: 0.05, steps: 200);
        Assert.Equal(201, trajectory.Count);

        var first = trajectory[0];
        var last = trajectory[^1];

        Assert.Equal(0.0, first.EulerDrift);
        Assert.True(last.EulerDrift > 0.5, "Forward Euler must diverge (> 50% energy pump) over 200 steps");
        var verletRelDrift = Math.Abs(last.VerletEnergy - first.VerletEnergy) / first.VerletEnergy;
        Assert.True(verletRelDrift < 0.01, "Symplectic Verlet energy drift must be strictly bounded (< 1%)");

        var ascii = ScientificMathAuditEngine.FormatAsciiEnergyTrajectory(trajectory);
        Assert.Contains("HAMILTONIAN ENERGY PHASE PORTRAIT", ascii);
        Assert.Contains("DIVERGED", ascii);
    }

    [Fact]
    public void AgentReasoningDag_TracesFiveLayersAndFormulas()
    {
        var steps = AgenticAiAuditEngine.TraceAgenticReasoningDag();
        Assert.Equal(5, steps.Count);
        Assert.All(steps, s => Assert.True(s.Passed, $"Layer {s.Layer} failed invariant: {s.InvariantAssertion}"));

        Assert.Equal("MarketFeedIngestion", steps[0].NodeName);
        Assert.Equal("RegimeFeatureExtraction", steps[1].NodeName);
        Assert.Equal("RiskKernelSovereignty", steps[2].NodeName);
        Assert.Equal("UtcpToolGateway", steps[3].NodeName);
        Assert.Equal("AgentCycleGovernor", steps[4].NodeName);

        var trace = AgenticAiAuditEngine.FormatAgentDagTrace(steps);
        Assert.Contains("AUTONOMOUS AGENT REASONING DAG", trace);
        Assert.Contains("DAG SOVEREIGNTY SEAL", trace);
    }
}
