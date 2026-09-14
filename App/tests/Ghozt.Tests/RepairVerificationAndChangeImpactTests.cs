using System.IO;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class RepairVerificationAndChangeImpactTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly GhoztStore _store;
    private readonly Target _target;

    public RepairVerificationAndChangeImpactTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ghozt_repair_impact_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "repair_impact.db");
        _store = new GhoztStore(_dbPath);

        _target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Automation Test Target",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "C:\\",
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
    // 1. Headless CLI Runner Tests (Patent §38)
    // ==================================================================

    [Fact]
    public async Task HeadlessRunner_ExecutesSelfTest_OutputsCertificateAndExitsZero()
    {
        var certPath = Path.Combine(_tempDir, "self-test-cert.json");
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        var runner = new HeadlessRunner(_store, swOut, swErr);
        var options = new HeadlessOptions
        {
            RunSelfTest = true,
            ClearanceCertPath = certPath,
        };

        var exitCode = await runner.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(certPath));
        var content = File.ReadAllText(certPath);
        Assert.Contains("SELF_HOST_QUALIFIED", content);
        Assert.Contains("GhoztSelfHostSupervisor@v1", content);
        Assert.Contains("Autonomous 12-Domain Self-Test", swOut.ToString());
    }

    [Fact]
    public async Task HeadlessRunner_MissingTarget_ReturnsExitCode2()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        var runner = new HeadlessRunner(_store, swOut, swErr);
        var options = new HeadlessOptions
        {
            TargetIdentifier = "TGT-NONEXISTENT",
        };

        var exitCode = await runner.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Contains("ERROR: Target 'TGT-NONEXISTENT' not found", swErr.ToString());
    }

    [Fact]
    public async Task HeadlessRunner_SmokeSweep_ExecutesAndGeneratesSarifAndJunit()
    {
        var sarifPath = Path.Combine(_tempDir, "report.sarif");
        var junitPath = Path.Combine(_tempDir, "report.xml");
        var certPath = Path.Combine(_tempDir, "clearance.json");

        using var swOut = new StringWriter();
        using var swErr = new StringWriter();

        var runner = new HeadlessRunner(_store, swOut, swErr);
        var options = new HeadlessOptions
        {
            TargetIdentifier = _target.Id.ToString(),
            Mode = "Smoke",
            SarifPath = sarifPath,
            JunitPath = junitPath,
            ClearanceCertPath = certPath,
        };

        var exitCode = await runner.RunAsync(options);

        // Either Cleared (0) or Defect Refused (1) based on clearance gates
        Assert.True(exitCode is 0 or 1);
        Assert.True(File.Exists(sarifPath));
        Assert.True(File.Exists(junitPath));
        Assert.True(File.Exists(certPath));

        var sarif = File.ReadAllText(sarifPath);
        Assert.Contains("sarif-schema-2.1.0.json", sarif);

        var junit = File.ReadAllText(junitPath);
        Assert.Contains("<testsuite", junit);
    }

    // ==================================================================
    // 2. Bounded Repair Verification Tests (Patent §30)
    // ==================================================================

    [Fact]
    public async Task RepairVerifier_SuccessfulRepair_TransitionsFindingToVerifiedAndLogsAudit()
    {
        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = _target.Id,
            NodeId = "SurfaceCheck:auth",
            Title = "Authentication timeout bug",
            Severity = Severity.High,
            Maturity = FindingMaturity.FixReady,
            Expected = "HTTP 200 OK",
            Actual = "HTTP 408 Timeout",
            Fingerprint = "FP-AUTH-TIMEOUT-001",
        };
        _store.Save(finding);

        var fixBriefEngine = new FixBriefEngine(_store);
        var fixBrief = fixBriefEngine.Generate(finding, _target);

        var verifier = new RepairVerifier(_store);
        var result = await verifier.VerifyRepairAsync(_target, finding, fixBrief, modifiedFiles: ["C:\\App\\Auth\\Login.cs"]);

        Assert.NotNull(result);
        Assert.True(result.Passed);
        Assert.Equal(RepairVerificationStatus.Verified, result.Status);
        Assert.Empty(result.RejectionReasons);

        // Verify finding transitioned to Verified
        var updatedFinding = _store.Findings(_target.Id).FirstOrDefault(f => f.Id == finding.Id);
        Assert.NotNull(updatedFinding);
        Assert.Equal(FindingMaturity.Verified, updatedFinding.Maturity);

        // Verify audit event
        var events = _store.RecentEvents(10, _target.Id);
        Assert.Contains(events, e => e.Code == "SYS.REPAIR.VERIFIED");
    }

    [Fact]
    public async Task RepairVerifier_ForbiddenScopeViolation_RejectsRepair()
    {
        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = _target.Id,
            NodeId = "sec:gate",
            Title = "Bypass defect",
            Severity = Severity.Critical,
            Maturity = FindingMaturity.FixReady,
            Expected = "401 Unauthorized",
            Actual = "200 OK",
            Fingerprint = "FP-SEC-BYPASS-001",
        };
        _store.Save(finding);

        var fixBriefEngine = new FixBriefEngine(_store);
        var fixBrief = fixBriefEngine.Generate(finding, _target);

        var verifier = new RepairVerifier(_store);
        // Attempt change in forbidden scope (Central security/authorization gates)
        var result = await verifier.VerifyRepairAsync(_target, finding, fixBrief,
            modifiedFiles: ["C:\\App\\Central security/authorization gates and credential providers\\Secrets.cs"]);

        Assert.False(result.Passed);
        Assert.Equal(RepairVerificationStatus.Rejected, result.Status);
        Assert.Contains(result.RejectionReasons, r => r.Contains("Scope Violation"));

        var events = _store.RecentEvents(10, _target.Id);
        Assert.Contains(events, e => e.Code == "SYS.REPAIR.REJECTED");
    }

    [Fact]
    public async Task RepairVerifier_RegressionIntroduced_RejectsRepair()
    {
        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = _target.Id,
            NodeId = "data:balance",
            Title = "Balance drift",
            Severity = Severity.High,
            Maturity = FindingMaturity.FixReady,
            Expected = "Conserved",
            Actual = "Drifted",
            Fingerprint = "FP-DATA-DRIFT-001",
        };
        _store.Save(finding);

        var fixBriefEngine = new FixBriefEngine(_store);
        var fixBrief = fixBriefEngine.Generate(finding, _target);

        var verifier = new RepairVerifier(_store);
        var result = await verifier.VerifyRepairAsync(_target, finding, fixBrief, simulateRegressions: true);

        Assert.False(result.Passed);
        Assert.Equal(RepairVerificationStatus.Rejected, result.Status);
        Assert.Contains(result.RejectionReasons, r => r.Contains("Regression Introduced"));
    }

    // ==================================================================
    // 3. Change Impact Analysis Tests (Patent §31)
    // ==================================================================

    [Fact]
    public void ChangeImpactEngine_AnalyzesModifiedFiles_IdentifiesImpactedNodesAndGuards()
    {
        // Add nodes
        _store.Save(new LatticeNode
        {
            Id = "sec:auth",
            TargetId = _target.Id,
            Label = "Auth Security Controller",
            Kind = NodeKind.System,
            State = "Observed",
            Provenance = "Manifest",
        });

        _store.Save(new LatticeNode
        {
            Id = "data:sqlite",
            TargetId = _target.Id,
            Label = "Database Store",
            Kind = NodeKind.DataState,
            State = "Observed",
            Provenance = "Manifest",
        });

        var engine = new ChangeImpactEngine(_store);
        var modifiedFiles = new[]
        {
            "src/Security/AuthService.cs",
            "src/Data/StoreDatabase.cs",
        };

        var assessment = engine.AssessImpact(_target, modifiedFiles);

        Assert.NotNull(assessment);
        Assert.Equal(2, assessment.ModifiedFiles.Count);
        Assert.NotEmpty(assessment.ImpactedNodes);
        Assert.Equal(ChangeRiskLevel.Critical, assessment.RiskLevel);
        Assert.Contains("AuthSecurityCheck", assessment.RecommendedSweepChecks);
        Assert.Contains("DataIntegrityCheck", assessment.RecommendedSweepChecks);

        var events = _store.RecentEvents(10, _target.Id);
        Assert.Contains(events, e => e.Code == "SYS.CHANGE_IMPACT.ASSESSED");
    }
}
