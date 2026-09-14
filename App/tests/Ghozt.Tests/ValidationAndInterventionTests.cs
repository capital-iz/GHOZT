using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class ValidationAndInterventionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public ValidationAndInterventionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_val_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_val_test.db");
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
    public async Task AccessibilityValidationModule_EvaluatesLatticeNodesAndDesignSystemContrast()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Accessibility Target",
            Kind = "Wpf",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            Reachable = true
        };
        _store.Save(target);

        // Add 2 lattice nodes: one with accessible name, one missing
        var node1 = new LatticeNode
        {
            Id = "node-btn-submit",
            TargetId = target.Id,
            Label = "Submit Form",
            Kind = NodeKind.Action,
            State = "untested",
            Provenance = "manifest",
            Detail = "Name=Submit Form;AutomationId=btnSubmit"
        };
        var node2 = new LatticeNode
        {
            Id = "node-btn-unlabeled",
            TargetId = target.Id,
            Label = "action:unlabeled",
            Kind = NodeKind.Action,
            State = "untested",
            Provenance = "manifest",
            Detail = "Name=;AutomationId=btn42"
        };
        _store.Save(node1);
        _store.Save(node2);

        var module = new AccessibilityValidationModule();
        var report = await module.ValidateAsync(target, _store, CancellationToken.None);

        Assert.Equal("val:a11y", report.ModuleId);
        Assert.Contains(report.Metrics, m => m.Name == "AveragePaletteContrast" && m.Value >= 4.5);
        Assert.Contains(report.Metrics, m => m.Name == "MissingAccessibleLabels" && m.Value == 1);

        var missingNameFinding = Assert.Single(report.Findings);
        Assert.Equal("node-btn-unlabeled", missingNameFinding.LatticeNodeId);
        Assert.Equal(Severity.Low, missingNameFinding.Severity);
        Assert.Contains("Missing Accessible Name", missingNameFinding.Title);
    }

    [Fact]
    public async Task SecurityPrivacyValidationModule_DetectsAndRedactsHardcodedSecrets()
    {
        var secretFile = Path.Combine(_tempDir, "appsettings.Development.json");
        var secretContent = """
        {
          "Logging": { "LogLevel": { "Default": "Debug" } },
          "AwsKey": "AKIAIOSFODNN7EXAMPLE",
          "Jwt": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
        }
        """;
        await File.WriteAllTextAsync(secretFile, secretContent);

        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Secret Target",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            Reachable = true
        };
        _store.Save(target);

        var module = new SecurityPrivacyValidationModule();
        var report = await module.ValidateAsync(target, _store, CancellationToken.None);

        Assert.Equal("val:sec-privacy", report.ModuleId);
        Assert.True(report.Findings.Count >= 2);

        var awsFinding = Assert.Single(report.Findings, f => f.Title.Contains("AWS Access Key"));
        Assert.Equal(Severity.Critical, awsFinding.Severity);
        Assert.Contains("AKIA************", awsFinding.Actual);
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", awsFinding.Actual);

        var jwtFinding = Assert.Single(report.Findings, f => f.Title.Contains("JWT Bearer Token"));
        Assert.Equal(Severity.High, jwtFinding.Severity);
        Assert.Contains("eyJhbGci...sw5c", jwtFinding.Actual);
    }

    [Fact]
    public async Task ResourceLeakValidationModule_CollectsTelemetryAndFlagsAnomalies()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Telemetry Target",
            Kind = "Process",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = "process:" + Environment.ProcessId,
            Reachable = true
        };
        _store.Save(target);

        var module = new ResourceLeakValidationModule();
        var report = await module.ValidateAsync(target, _store, CancellationToken.None);

        Assert.Equal("val:leak-vitality", report.ModuleId);
        Assert.Contains(report.Metrics, m => m.Name == "WorkingSetMemory" && m.Value > 0);
        Assert.Contains(report.Metrics, m => m.Name == "HandleCount" && m.Value > 0);
        Assert.Contains(report.Metrics, m => m.Name == "ThreadCount" && m.Value > 0);

        // Real process handle count in test run should not trigger threshold (> 10000)
        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task ContractSchemaValidationModule_DetectsMismatchedPathParameters()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "API Target",
            Kind = "RestApi",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = "https://api.example.com",
            Reachable = true
        };
        _store.Save(target);

        // Valid node
        var validNode = new LatticeNode
        {
            Id = "route-valid",
            TargetId = target.Id,
            Label = "GET /api/v1/orders/{orderId}/items/{itemId}",
            Kind = NodeKind.Endpoint,
            State = "untested",
            Provenance = "manifest"
        };

        // Broken node with unmatched brace
        var brokenNode = new LatticeNode
        {
            Id = "route-broken",
            TargetId = target.Id,
            Label = "POST /api/v1/users/{userId/profile",
            Kind = NodeKind.Endpoint,
            State = "untested",
            Provenance = "manifest"
        };

        _store.Save(validNode);
        _store.Save(brokenNode);

        var module = new ContractSchemaValidationModule();
        var report = await module.ValidateAsync(target, _store, CancellationToken.None);

        Assert.Equal("val:contract-schema", report.ModuleId);
        var finding = Assert.Single(report.Findings);
        Assert.Equal("route-broken", finding.LatticeNodeId);
        Assert.Equal(Severity.High, finding.Severity);
        Assert.Contains("Malformed Route Template", finding.Title);
    }

    [Fact]
    public async Task CausalForensicsEngine_ExecuteIntervention_TransitionsToMechanisticVerification()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Intervention Target",
            Kind = "Wpf",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            Reachable = true
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Authentication token expired prematurely",
            Expected = "Token valid for 3600s",
            Actual = "Token invalidated after 30s",
            Severity = Severity.High,
            Maturity = FindingMaturity.New,
            Fingerprint = "fp-auth-token-expire",
            EvidenceCount = 1,
            ReproAttempts = 2,
            ReproSuccesses = 2,
            NodeId = "node-auth-token"
        };
        _store.Save(finding);

        var engine = new CausalForensicsEngine(_store);
        var hypothesis = engine.Investigate(finding, target);

        Assert.NotNull(hypothesis);
        Assert.True(hypothesis.Status is CausalStatus.Observed or CausalStatus.SupportedNotFinal);
        Assert.NotEmpty(hypothesis.Interventions);

        var intervention = hypothesis.Interventions[0];
        var (updatedHyp, updatedIntervention) = await engine.ExecuteInterventionAsync(hypothesis, intervention, finding, target);

        Assert.NotNull(updatedHyp);
        Assert.Equal(hypothesis.Id, updatedHyp.Id);
        Assert.Equal(intervention.Id, updatedIntervention.Id);
        Assert.True(updatedIntervention.OutcomePreventedDeviation);
        Assert.True(updatedHyp.Status is CausalStatus.SupportedNotFinal or CausalStatus.MechanisticallyVerified);
        Assert.True(updatedHyp.Confidence >= 0.5);

        // Verify evidence item saved to store
        var evidence = _store.EvidenceItems(target.Id);
        Assert.Contains(evidence, e => e.Kind == "cfx:intervention");
    }

    [Fact]
    public void ReportGenerator_BuildsSarifV210_WithCompliantRulesAndResults()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Sarif Test Target",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = "C:\\Projects\\MyApp",
            Reachable = true
        };

        var finding1 = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "SQL Injection in Search Query",
            Expected = "Parameterized queries",
            Actual = "Raw string interpolation detected",
            Severity = Severity.Critical,
            Fingerprint = "fp-sqli-00112233",
            ReproAttempts = 1,
            ReproSuccesses = 1,
            NodeId = "node-1"
        };

        var finding2 = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Unstyled focus state",
            Expected = "High visibility outline",
            Actual = "No focus ring rendered",
            Severity = Severity.Low,
            Fingerprint = "fp-a11y-focus-44",
            ReproAttempts = 5,
            ReproSuccesses = 4,
            NodeId = "node-2"
        };

        var sarifJson = ReportGenerator.BuildSarif(target, new[] { finding1, finding2 });
        Assert.False(string.IsNullOrWhiteSpace(sarifJson));

        using var doc = JsonDocument.Parse(sarifJson);
        var root = doc.RootElement;
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());

        var runs = root.GetProperty("runs");
        Assert.Equal(1, runs.GetArrayLength());

        var driver = runs[0].GetProperty("tool").GetProperty("driver");
        Assert.Equal("Ghozt - Dev Tester", driver.GetProperty("name").GetString());
        Assert.Equal(ReportGenerator.GetProductVersion(), driver.GetProperty("version").GetString());

        var rules = driver.GetProperty("rules");
        Assert.Equal(2, rules.GetArrayLength());

        var results = runs[0].GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());

        Assert.Equal("error", results[0].GetProperty("level").GetString());
        Assert.Equal("note", results[1].GetProperty("level").GetString());
    }

    [Fact]
    public void ReportGenerator_BuildsJUnitXml_WithValidStructureAndDurations()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "JUnit Test Target",
            Kind = "Wpf",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = "C:\\Projects\\WpfApp",
            Reachable = true
        };

        var sweepId = _store.NextId(IdKind.Sweep);
        var sweep = new Sweep
        {
            Id = sweepId,
            TargetId = target.Id,
            Name = "Regression Sweep 1",
            Mode = "Autonomous E2E",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            FinishedAt = DateTimeOffset.UtcNow,
            StatesVisited = 42
        };

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            SweepId = sweepId,
            NodeId = "node-42",
            Title = "Navigation stack overflow",
            Expected = "Clamped back-stack",
            Actual = "Stack exploded past 500 items",
            Severity = Severity.High
        };

        var junitXml = ReportGenerator.BuildJUnitXml(target, new[] { sweep }, new[] { finding });
        Assert.False(string.IsNullOrWhiteSpace(junitXml));

        var xdoc = XDocument.Parse(junitXml);
        var root = xdoc.Root;
        Assert.NotNull(root);
        Assert.Equal("testsuites", root.Name.LocalName);
        Assert.Equal("42", root.Attribute("tests")?.Value);
        Assert.Equal("1", root.Attribute("failures")?.Value);

        var testsuite = Assert.Single(root.Elements("testsuite"));
        Assert.Equal($"Sweep.{sweepId}", testsuite.Attribute("name")?.Value);

        var testcase = Assert.Single(testsuite.Elements("testcase"));
        Assert.Equal("Navigation stack overflow", testcase.Attribute("name")?.Value);

        var failure = Assert.Single(testcase.Elements("failure"));
        Assert.Equal("High", failure.Attribute("type")?.Value);
        Assert.Equal("Stack exploded past 500 items", failure.Attribute("message")?.Value);
        Assert.Equal("Clamped back-stack", failure.Value);
    }

    [Fact]
    public void AlgorithmHarness_Suite_EvaluatesAll10Invariants_IncludingMetamorphicAndAsymptotic()
    {
        var report = AlgorithmHarness.RunSuite();

        Assert.Equal(10, report.Results.Count);
        Assert.Equal(10, report.PassedCount);
        Assert.Equal(0, report.FailedCount);
        Assert.False(string.IsNullOrWhiteSpace(report.Sha256Digest));

        var permute = report.Results.FirstOrDefault(r => r.Id == "ALG.METAMORPHIC_PERMUTE");
        Assert.NotNull(permute);
        Assert.True(permute.Passed);
        Assert.Equal("Information Theory", permute.Domain);

        var monotone = report.Results.FirstOrDefault(r => r.Id == "ALG.METAMORPHIC_MONOTONE");
        Assert.NotNull(monotone);
        Assert.True(monotone.Passed);

        var asymp = report.Results.FirstOrDefault(r => r.Id == "ALG.WILSON_ASYMPTOTIC");
        Assert.NotNull(asymp);
        Assert.True(asymp.Passed);

        // Verify markdown dossier generation
        var dossier = AlgorithmHarness.GenerateMathematicalDossier(report);
        Assert.Contains("ALG.METAMORPHIC_PERMUTE", dossier);
        Assert.Contains("ALG.WILSON_ASYMPTOTIC", dossier);
    }

    [Fact]
    public void AlgorithmHarness_RunComplexitySuite_EvaluatesEmpiricalBigOScaling_PassesBounds()
    {
        var report = AlgorithmHarness.RunComplexitySuite();

        Assert.Equal(3, report.Results.Count);
        Assert.True(report.PassedCount >= 2); // At least 2 of 3 assays pass strictly under timer variations
        Assert.False(string.IsNullOrWhiteSpace(report.Sha256Digest));

        foreach (var benchmark in report.Results)
        {
            Assert.NotEmpty(benchmark.Samples);
            Assert.True(benchmark.EmpiricalAlpha >= 0.0);
            Assert.Contains("O(", benchmark.TheoreticalComplexity);
        }

        // Test empirical alpha helper directly
        var p1 = new ComplexitySample(100, 100.0);
        var p2 = new ComplexitySample(1000, 1000.0); // Exact linear scaling: 10x size -> 10x time
        var alpha = AlgorithmHarness.ComputeEmpiricalAlpha(p1, p2);
        Assert.InRange(alpha, 0.99, 1.01);
    }

    [Fact]
    public void AlgorithmHarness_MinimizeInput_DeltaDebugging_IsolatesMinimalTriggerSubset()
    {
        var inputVector = new[]
        {
            "noise_0", "noise_1", "key:trigger_a", "noise_2",
            "noise_3", "noise_4", "key:trigger_b", "noise_5"
        };

        static bool IsFailing(IReadOnlyList<string> sub) =>
            sub.Contains("key:trigger_a") && sub.Contains("key:trigger_b");

        var minimal = AlgorithmHarness.MinimizeInput(inputVector, IsFailing);

        Assert.Equal(2, minimal.Count);
        Assert.Contains("key:trigger_a", minimal);
        Assert.Contains("key:trigger_b", minimal);
    }

    [Fact]
    public async Task SweepEngine_RunsRegressionGuardingCheck_WhenGuardsExist_EvaluatesAndCapturesEvidence()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Regression Target",
            Kind = "API Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = _tempDir,
            Reachable = true
        };
        _store.Save(target);

        // Add a finding that derives into a guard
        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            SweepId = _store.NextId(IdKind.Sweep),
            NodeId = "node-reg-1",
            Title = "Path unreachable",
            Expected = "readable file path",
            Actual = "Path not found: non_existent",
            Severity = Severity.Medium,
            Maturity = FindingMaturity.Verified, // Marks guard as passing
            Fingerprint = "fp-reg-1"
        };
        _store.Save(finding);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Full Sweep Regression Audit",
            Mode = "Full sweep",
            State = RunState.PreFlight,
            MaxStates = 100,
            MaxSeconds = 30,
            MaxDepth = 10,
            CreatedAt = DateTimeOffset.Now
        };
        _store.Save(sweep);

        var engine = new SweepEngine(_store);
        var completed = await engine.RunAsync(sweep, target, null, CancellationToken.None);

        Assert.Equal(RunState.Completed, completed.State);

        // Verify regression:audit evidence was captured
        var evidenceItems = _store.EvidenceItems(target.Id);
        var regEvidence = evidenceItems.FirstOrDefault(e => e.Kind == "regression:audit");
        Assert.NotNull(regEvidence);
        Assert.Contains("Regression Guard Audit", regEvidence.Summary);
    }
}
