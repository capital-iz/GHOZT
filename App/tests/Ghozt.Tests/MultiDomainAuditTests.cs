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
}
