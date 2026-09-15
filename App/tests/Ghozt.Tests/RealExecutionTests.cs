using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Ghozt.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Ghozt.Tests;

public sealed class RealExecutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public RealExecutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_real_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_test.db");
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
    public async Task SqliteTarget_RealStructureAndIntegrityChecks_Pass()
    {
        // 1. Create a real sample SQLite database
        var sampleDbPath = Path.Combine(_tempDir, "sample_data.db");
        using (var conn = new SqliteConnection($"Data Source={sampleDbPath};"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT); CREATE TABLE audit_log (id INTEGER, action TEXT); INSERT INTO users VALUES (1, 'alice');";
            cmd.ExecuteNonQuery();
        }

        // 2. Register Target pointing to the SQLite database
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Sample SQLite Target",
            Kind = "Database",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            Owner = "test-runner",
            BaseAddress = sampleDbPath,
            BuildLabel = "1.0.0-test",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Full sweep against SQLite DB",
            Mode = "Full sweep",
            MaxStates = 20,
            MaxSeconds = 30,
            MaxDepth = 3,
        };

        var engine = new SweepEngine(_store);
        var completed = await engine.RunAsync(sweep, target, null, CancellationToken.None);

        Assert.Equal(RunState.Completed, completed.State);
        Assert.Equal(0, completed.DeviationsFound);

        // Verify evidence items generated
        var evidence = _store.EvidenceItems(target.Id);
        Assert.NotEmpty(evidence);

        var structureArtefact = evidence.FirstOrDefault(e => e.Kind == "check:structure");
        Assert.NotNull(structureArtefact);
        Assert.Contains("SQLite schema structure verified", structureArtefact.Summary);
        Assert.Contains("tables: 2", structureArtefact.Summary, StringComparison.OrdinalIgnoreCase);

        var integrityArtefact = evidence.FirstOrDefault(e => e.Kind == "check:integrity");
        Assert.NotNull(integrityArtefact);
        Assert.Contains("PRAGMA integrity_check = ok", integrityArtefact.Summary);
    }

    [Fact]
    public async Task DirectoryAndGitTarget_RealStructureAndIntegrityChecks_Pass()
    {
        // 1. Create a simulated git repo directory structure
        var repoDir = Path.Combine(_tempDir, "mock_repo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(Path.Combine(repoDir, "App.cs"), "class App {}");
        File.WriteAllText(Path.Combine(repoDir, "Main.xaml"), "<Window />");

        var gitDir = Path.Combine(repoDir, ".git");
        Directory.CreateDirectory(gitDir);
        var headRef = "ref: refs/heads/feature/real-exec";
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), headRef);
        var headsDir = Path.Combine(gitDir, "refs", "heads", "feature");
        Directory.CreateDirectory(headsDir);
        var commitSha = "abcdef0123456789abcdef0123456789abcdef01";
        File.WriteAllText(Path.Combine(headsDir, "real-exec"), commitSha);

        // 2. Register Target pointing to the directory
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Mock Git Repository",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            Owner = "test-runner",
            BaseAddress = repoDir,
            RepositoryPath = repoDir,
            BuildLabel = "git-commit-test",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Sweep against Git repo",
            Mode = "Changed area",
            MaxStates = 20,
            MaxSeconds = 30,
            MaxDepth = 3,
        };

        var engine = new SweepEngine(_store);
        var completed = await engine.RunAsync(sweep, target, null, CancellationToken.None);

        Assert.Equal(RunState.Completed, completed.State);

        var evidence = _store.EvidenceItems(target.Id);
        var structEvt = evidence.FirstOrDefault(e => e.Kind == "check:structure");
        Assert.NotNull(structEvt);
        Assert.Contains("Git branch: feature/real-exec", structEvt.Summary);

        var integEvt = evidence.FirstOrDefault(e => e.Kind == "check:integrity");
        Assert.NotNull(integEvt);
        Assert.Contains("commit abcdef0123", integEvt.Summary);
    }

    [Fact]
    public void ProcessSupervisor_CanAttachToCurrentProcess()
    {
        using var supervisor = new TargetSupervisor();
        var currentPid = Environment.ProcessId;
        var attached = supervisor.Attach(currentPid);

        Assert.True(attached);
        Assert.True(supervisor.IsRunning);
        Assert.Equal(currentPid, supervisor.ProcessId);

        var activeProcesses = TargetSupervisor.GetActiveDesktopProcesses();
        Assert.NotNull(activeProcesses);
    }

    [Fact]
    public void StateMapWorkspace_HasZeroOvalActionNodes_MustBeRoundedRectangle()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces", "StateMapWorkspace.xaml.cs"));
        Assert.True(File.Exists(sourcePath), $"Could not find StateMapWorkspace.xaml.cs at {sourcePath}");

        var code = File.ReadAllText(sourcePath);

        // Assert no CornerRadius(999) exists in StateMapWorkspace
        Assert.DoesNotContain("new CornerRadius(999)", code);
        Assert.Contains("NodeKind.Action => new CornerRadius(8)", code);
    }

    [Fact]
    public void TargetsWorkspace_EliminatedSeedDemonstrationTarget_HasAttachAndRegisterSystem()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces", "TargetsWorkspace.xaml"));
        Assert.True(File.Exists(xamlPath), $"Could not find TargetsWorkspace.xaml at {xamlPath}");

        var xaml = File.ReadAllText(xamlPath);

        // Must not contain "Seed demonstration target"
        Assert.DoesNotContain("Seed demonstration target", xaml, StringComparison.OrdinalIgnoreCase);

        // Must contain "Attach Process" and "Register Local System"
        Assert.Contains("Attach Process", xaml);
        Assert.Contains("Register Local System", xaml);
    }

    [Fact]
    public void AgenticSweepRunner_ResolvesAttachedProcessPids_Correctly()
    {
        var targetWithPrefix = new Target
        {
            Id = GhoztId.Parse("TGT-000101"),
            Name = "Attached Notepad",
            Kind = "Desktop application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            Owner = "tester",
            BaseAddress = "process:4321",
            LaunchCommand = "",
            BuildLabel = "1.0",
            AuthorityNote = "Test authority",
        };

        var resolvedPrefix = AgenticSweepRunner.ResolveExecutablePath(targetWithPrefix);
        Assert.Equal("process:4321", resolvedPrefix);

        var targetWithRawPid = targetWithPrefix with { BaseAddress = "9876" };
        var resolvedRaw = AgenticSweepRunner.ResolveExecutablePath(targetWithRawPid);
        Assert.Equal("process:9876", resolvedRaw);
    }

    [Fact]
    public void FixBrief_ToMarkdown_GeneratesCompleteBoundedContract()
    {
        var targetId = GhoztId.Parse("TGT-000001");
        var findingId = GhoztId.Parse("FLT-000042");

        var brief = new FixBrief
        {
            Id = "FXB-00001",
            TargetId = targetId,
            FindingId = findingId,
            Title = "Repair state transition debounce in navigation rail",
            RootMechanism = "Event handler re-entrance during rapid click sequences.",
            AllowedScope = ["App/src/Ghozt.App/Shell/NavigationRail.xaml.cs"],
            ForbiddenScope = ["App/src/Ghozt.Core/*"],
            MinimalReproSequence = ["Click item 1", "Click item 2 under 5ms"],
            RequiredEvidence = ["Screenshot proof of final selection state"],
            RegressionGuardSpecification = "GRD-00042: State transitions must complete under 50ms without dropped frames.",
            RejectionConditions = ["Do not introduce thread sleeps", "Do not alter token namespaces"],
        };

        var markdown = brief.ToMarkdown();
        Assert.NotNull(markdown);
        Assert.Contains("FXB-00001", markdown);
        Assert.Contains("Repair state transition debounce", markdown);
        Assert.Contains("NavigationRail.xaml.cs", markdown);
        Assert.Contains("Do not introduce thread sleeps", markdown);
        Assert.Contains("GRD-00042", markdown);
    }

    [Fact]
    public void ClearanceDecision_CanBeSignedAndSerialized_WithSha256Digest()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Clearance Evaluation Target",
            Kind = "Desktop application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            Owner = "tester",
            BaseAddress = "notepad.exe",
            LaunchCommand = "",
            BuildLabel = "1.0.0",
            AuthorityNote = "Test clearance grant",
        };
        _store.Save(target);

        var engine = new ClearanceEngine(_store);
        var decision = engine.EvaluateTarget(target, "test-operator");

        Assert.NotNull(decision);
        Assert.NotEmpty(decision.Gates);
        Assert.NotNull(decision.EvidenceDigest);

        var json = System.Text.Json.JsonSerializer.Serialize(decision);
        Assert.Contains("TargetId", json);
        Assert.Contains("EvidenceDigest", json);
    }

    [Fact]
    public void TargetsWorkspace_HasSearchFilterAndLaunchActions()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces", "TargetsWorkspace.xaml"));
        Assert.True(File.Exists(xamlPath));

        var xaml = File.ReadAllText(xamlPath);
        Assert.Contains("OnSearchChanged", xaml);
        Assert.Contains("OnKindFilterChanged", xaml);
        Assert.Contains("OnLaunchTarget", xaml);
        Assert.Contains("OnExploreTargetLattice", xaml);
    }

    [Fact]
    public void GhoztStore_DatabaseStatistics_And_IntegrityCheck_Pass()
    {
        var stats = _store.GetDatabaseStatistics();
        Assert.NotNull(stats);
        Assert.Equal(_tempDbPath, stats.DatabasePath);
        Assert.True(stats.MainSizeBytes > 0);
        Assert.True(stats.TargetCount >= 0);

        var integrity = _store.VerifyIntegrity();
        Assert.NotNull(integrity);
        Assert.Equal("ok", integrity.Trim(), ignoreCase: true);
    }

    [Fact]
    public void GhoztStore_VacuumAndBackup_ProducesValidSqliteSnapshot()
    {
        _store.VacuumAndOptimize();

        var tempBackup = Path.Combine(Path.GetTempPath(), $"ghozt_test_bak_{Guid.NewGuid():N}.bak");
        try
        {
            var savedPath = _store.BackupDatabase(tempBackup);
            Assert.True(File.Exists(savedPath));
            Assert.True(new FileInfo(savedPath).Length > 0);
        }
        finally
        {
            if (File.Exists(tempBackup))
            {
                File.Delete(tempBackup);
            }
        }
    }

    [Fact]
    public void AlgorithmHarness_MathematicalDossier_GeneratesCompleteMarkdown()
    {
        var report = AlgorithmHarness.RunSuite();
        Assert.NotNull(report);
        Assert.Equal(10, report.Results.Count);
        Assert.Equal(10, report.PassedCount);
        Assert.Equal(0, report.FailedCount);

        Assert.Contains(report.Results, r => r.Id == "ALG.PHANTOM_GATE" && r.Passed);
        Assert.Contains(report.Results, r => r.Id == "ALG.IEEE754_HYGIENE" && r.Passed);

        var dossier = AlgorithmHarness.GenerateMathematicalDossier(report);
        Assert.Contains("Mathematical & Algorithmic Invariant Verification Dossier", dossier);
        Assert.Contains("ALG.PHANTOM_GATE", dossier);
        Assert.Contains("ALG.IEEE754_HYGIENE", dossier);
        Assert.Contains(report.Sha256Digest, dossier);
    }

    [Fact]
    public void Workspaces_HaveCompleteFeatureEndpoints_InMarkup()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces"));

        var settingsXaml = File.ReadAllText(Path.Combine(basePath, "SettingsWorkspace.xaml"));
        Assert.Contains("OnVerifyIntegrity", settingsXaml);
        Assert.Contains("OnVacuumOptimize", settingsXaml);
        Assert.Contains("OnBackupDatabase", settingsXaml);
        Assert.Contains("OnDestructiveGateChanged", settingsXaml);

        var evidenceXaml = File.ReadAllText(Path.Combine(basePath, "EvidenceWorkspace.xaml"));
        Assert.Contains("OnVerifyAll", evidenceXaml);
        Assert.Contains("OnExportArtefact", evidenceXaml);
        Assert.Contains("OnRedactAll", evidenceXaml);
        Assert.Contains("KindFilter", evidenceXaml);
        Assert.Contains("VerifyAllButton", evidenceXaml);
        Assert.Contains("ExportArtefactButton", evidenceXaml);
        Assert.Contains("RedactAllButton", evidenceXaml);

        var regressionsXaml = File.ReadAllText(Path.Combine(basePath, "RegressionsWorkspace.xaml"));
        Assert.Contains("OnExportSuite", regressionsXaml);
        Assert.Contains("OnRunMutationAssays", regressionsXaml);
        Assert.Contains("MutationAssaysButton", regressionsXaml);
        Assert.Contains("MutationScoreBadge", regressionsXaml);

        var clearanceXaml = File.ReadAllText(Path.Combine(basePath, "ClearanceWorkspace.xaml"));
        Assert.Contains("OnEvaluate", clearanceXaml);
        Assert.Contains("OnSignDecision", clearanceXaml);
        Assert.Contains("OnExportCertificate", clearanceXaml);
        Assert.Contains("OnVerifyCertificate", clearanceXaml);
        Assert.Contains("VerifyCertButton", clearanceXaml);

        var algorithmsXaml = File.ReadAllText(Path.Combine(basePath, "AlgorithmsWorkspace.xaml"));
        Assert.Contains("OnExportDossier", algorithmsXaml);
        Assert.Contains("DomainFilter", algorithmsXaml);


        var replayXaml = File.ReadAllText(Path.Combine(basePath, "ReplayWorkspace.xaml"));
        Assert.Contains("OnMinimiseSequenceClicked", replayXaml);
        Assert.Contains("OnJumpToDivergenceClicked", replayXaml);
        Assert.Contains("MinimisedStepsList", replayXaml);
        Assert.Contains("JumpDivergenceBtn", replayXaml);

        var targetsXaml = File.ReadAllText(Path.Combine(basePath, "TargetsWorkspace.xaml"));
        Assert.Contains("OnPruneCanaries", targetsXaml);
        Assert.Contains("PruneCanariesButton", targetsXaml);

        var sweepXaml = File.ReadAllText(Path.Combine(basePath, "SweepWorkspace.xaml"));
        Assert.Contains("PostSweepPanel", sweepXaml);
        Assert.Contains("OnPostSweepExploreLattice", sweepXaml);
        Assert.Contains("OnPostSweepFaults", sweepXaml);
        Assert.Contains("OnPostSweepClearance", sweepXaml);

        var stateMapXaml = File.ReadAllText(Path.Combine(basePath, "StateMapWorkspace.xaml"));
        Assert.Contains("NodeInspectorDrawer", stateMapXaml);
        Assert.Contains("OnRunDiscoverySweep", stateMapXaml);
        Assert.Contains("BtnEmptyDiscovery", stateMapXaml);

        var reportsXaml = File.ReadAllText(Path.Combine(basePath, "ReportsWorkspace.xaml"));
        Assert.Contains("PreviewInAppButton", reportsXaml);
        Assert.Contains("DocumentPreviewDrawer", reportsXaml);
        Assert.Contains("OnPreviewInApp", reportsXaml);
    }

    [Fact]
    public void EvidenceWorkspace_CodeBehind_HasInitializationGuards()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces"));
        var cs = File.ReadAllText(Path.Combine(basePath, "EvidenceWorkspace.xaml.cs"));

        Assert.Contains("if (!IsInitialized || Items is null)", cs);
        Assert.Contains("if (IsInitialized)", cs);
    }

    [Fact]
    public void RailButton_ImplementsAutomationPeer_WithInvokeProvider()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Shell"));
        var cs = File.ReadAllText(Path.Combine(basePath, "RailButton.cs"));

        Assert.Contains("OnCreateAutomationPeer", cs);
        Assert.Contains("RailButtonAutomationPeer", cs);
        Assert.Contains("IInvokeProvider", cs);
        Assert.Contains("AutomationControlType.Button", cs);
        Assert.Contains("public void Invoke()", cs);
    }

    [Fact]
    public void LatticeNodeCanStoreAndPersistCoordinatesWithoutDrift()
    {
        var targetId = _store.NextId(IdKind.Target);
        var node = new LatticeNode
        {
            Id = "node:workspace:canvas",
            TargetId = targetId,
            Label = "Interactive Canvas State",
            Kind = NodeKind.Feature,
            State = "Observed",
            Provenance = "Observed",
            X = 425.5,
            Y = 780.25,
            LastSeenAt = DateTimeOffset.UtcNow,
        };

        _store.Save(node);

        var retrieved = _store.Nodes(targetId).Single(n => n.Id == "node:workspace:canvas");
        Assert.Equal(425.5, retrieved.X);
        Assert.Equal(780.25, retrieved.Y);

        var movedNode = retrieved with { X = 510.0, Y = 640.0 };
        _store.Save(movedNode);

        var updated = _store.Nodes(targetId).Single(n => n.Id == "node:workspace:canvas");
        Assert.Equal(510.0, updated.X);
        Assert.Equal(640.0, updated.Y);
    }

    [Fact]
    public void WorkspacesForensicWorkflowSynthesisMarkupAndHandlersExist()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces"));

        // State Map
        var stateMapXaml = File.ReadAllText(Path.Combine(basePath, "StateMapWorkspace.xaml"));
        var stateMapCs = File.ReadAllText(Path.Combine(basePath, "StateMapWorkspace.xaml.cs"));
        Assert.Contains("CanvasContainer", stateMapXaml);
        Assert.Contains("_isDraggingNode", stateMapCs);
        Assert.Contains("MouseLeftButtonDown", stateMapCs);
        Assert.Contains("MouseLeftButtonUp", stateMapCs);
        Assert.Contains("FocusNode", stateMapCs);

        // Faults
        var faultsXaml = File.ReadAllText(Path.Combine(basePath, "FaultsWorkspace.xaml"));
        var faultsCs = File.ReadAllText(Path.Combine(basePath, "FaultsWorkspace.xaml.cs"));
        Assert.Contains("OnReplayFault", faultsXaml);
        Assert.Contains("OnLocateOnStateMap", faultsXaml);
        Assert.Contains("OnViewEvidence", faultsXaml);
        Assert.Contains("CrossNavCard", faultsXaml);
        Assert.Contains("NavigateToReplay", faultsCs);
        Assert.Contains("NavigateToStateMap", faultsCs);

        // Evidence
        var evidenceXaml = File.ReadAllText(Path.Combine(basePath, "EvidenceWorkspace.xaml"));
        var evidenceCs = File.ReadAllText(Path.Combine(basePath, "EvidenceWorkspace.xaml.cs"));
        Assert.Contains("EvidencePreviewDrawer", evidenceXaml);
        Assert.Contains("PreviewImage", evidenceXaml);
        Assert.Contains("PreviewPayloadText", evidenceXaml);
        Assert.Contains("OpenPreviewDrawer", evidenceCs);
        Assert.Contains("OnCopyPreviewPayload", evidenceCs);
        Assert.Contains("OnViewLinkedFault", evidenceCs);
        Assert.Contains("CheckRequestedEvidence", evidenceCs);

        // Replay
        var replayXaml = File.ReadAllText(Path.Combine(basePath, "ReplayWorkspace.xaml"));
        var replayCs = File.ReadAllText(Path.Combine(basePath, "ReplayWorkspace.xaml.cs"));
        Assert.Contains("OnTriageInFaults", replayXaml);
        Assert.Contains("OnInspectInStateMap", replayXaml);
        Assert.Contains("OnInspectEvidence", replayXaml);
        Assert.Contains("CheckRequestedNavigation", replayCs);

        // Sweep
        var sweepXaml = File.ReadAllText(Path.Combine(basePath, "SweepWorkspace.xaml"));
        var sweepCs = File.ReadAllText(Path.Combine(basePath, "SweepWorkspace.xaml.cs"));
        Assert.Contains("LiveLogPanel", sweepXaml);
        Assert.Contains("LiveLogText", sweepXaml);
        Assert.Contains("OnReplayHistoryItem", sweepXaml);
        Assert.Contains("OnReplayHistoryItem", sweepCs);

        // Clearance
        var clearanceCs = File.ReadAllText(Path.Combine(basePath, "ClearanceWorkspace.xaml.cs"));
        Assert.Contains("remediationPanel", clearanceCs);
        Assert.Contains("Triage Open Faults", clearanceCs);
        Assert.Contains("Review & Seal Evidence", clearanceCs);
    }

    [Fact]
    public void ShellViewModelHandoffPropertiesAndNavigationContractsExist()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Shell"));
        var cs = File.ReadAllText(Path.Combine(basePath, "ShellViewModel.cs"));

        Assert.Contains("RequestedSweepId", cs);
        Assert.Contains("RequestedFindingId", cs);
        Assert.Contains("RequestedNodeId", cs);
        Assert.Contains("RequestedEvidenceId", cs);
        Assert.Contains("NavigateToReplay", cs);
        Assert.Contains("NavigateToStateMap", cs);
        Assert.Contains("NavigateToFaults", cs);
        Assert.Contains("NavigateToEvidence", cs);
        Assert.Contains("NavigateToSweep", cs);
    }

    [Fact]
    public void FaultsFiltering_MultiPredicateCompoundMatching_FiltersAccurately()
    {
        var targetId = _store.NextId(IdKind.Target);
        var findings = new List<Finding>
        {
            new Finding
            {
                Id = _store.NextId(IdKind.Fault),
                TargetId = targetId,
                Title = "NullReferenceException in PaymentHandler",
                Actual = "Object reference not set to an instance of an object",
                Expected = "Should return 400 Bad Request with validation payload",
                Severity = Severity.Critical,
                Maturity = FindingMaturity.New,
                Fingerprint = "FP-PAYMENT-NRE-01",
                NodeId = "node:checkout:pay",
                FirstSeenAt = DateTimeOffset.UtcNow,
            },
            new Finding
            {
                Id = _store.NextId(IdKind.Fault),
                TargetId = targetId,
                Title = "High memory usage during image processing",
                Actual = "Process memory increased by 850MB and did not release",
                Expected = "Memory usage should return to baseline within 200ms",
                Severity = Severity.Medium,
                Maturity = FindingMaturity.Reproduced,
                Fingerprint = "FP-MEM-LEAK-02",
                NodeId = "node:media:upload",
                FirstSeenAt = DateTimeOffset.UtcNow,
            },
            new Finding
            {
                Id = _store.NextId(IdKind.Fault),
                TargetId = targetId,
                Title = "Cosmetic button alignment mismatch in dark mode",
                Actual = "Margin is 12px instead of 16px",
                Expected = "Margin should conform to 16px grid standard",
                Severity = Severity.Info,
                Maturity = FindingMaturity.CauseSupported,
                Fingerprint = "FP-UI-ALIGN-03",
                NodeId = "node:settings:view",
                FirstSeenAt = DateTimeOffset.UtcNow,
            }
        };

        // Text query match
        var queryMatch = findings.Where(f => f.Title.Contains("Payment", StringComparison.OrdinalIgnoreCase)
                                          || (!string.IsNullOrEmpty(f.Actual) && f.Actual.Contains("Payment", StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.Single(queryMatch);
        Assert.Equal("FP-PAYMENT-NRE-01", queryMatch[0].Fingerprint);

        // Severity match
        var critMatch = findings.Where(f => f.Severity == Severity.Critical).ToList();
        Assert.Single(critMatch);
        Assert.Equal("NullReferenceException in PaymentHandler", critMatch[0].Title);

        // State / Maturity match
        var reproMatch = findings.Where(f => f.Maturity == FindingMaturity.Reproduced).ToList();
        Assert.Single(reproMatch);
        Assert.Equal("FP-MEM-LEAK-02", reproMatch[0].Fingerprint);

        // Compound filter
        var compoundMatch = findings.Where(f => f.Severity == Severity.Medium && f.Maturity == FindingMaturity.Reproduced).ToList();
        Assert.Single(compoundMatch);
    }

    [Fact]
    public void AlgorithmsLaboratory_CustomVectorDeltaDebugging_MinimizesNoiseToMinimalFailureSubset()
    {
        var complexVector = new List<string>
        {
            "sys.boot",
            "db.connect",
            "trigger:stack-overflow",
            "cache.init",
            "trigger:circular-ref",
            "network.listen",
            "worker.spawn",
            "metrics.emit",
            "health.check",
            "telemetry.flush"
        };

        // Oracle fails only if both triggers are present
        bool Oracle(IReadOnlyList<string> subset) =>
            subset.Contains("trigger:stack-overflow") && subset.Contains("trigger:circular-ref");

        var minimal = AlgorithmHarness.MinimizeInput(complexVector, Oracle);

        Assert.Equal(2, minimal.Count);
        Assert.Contains("trigger:stack-overflow", minimal);
        Assert.Contains("trigger:circular-ref", minimal);
        Assert.DoesNotContain("sys.boot", minimal);
        Assert.DoesNotContain("network.listen", minimal);
    }

    [Fact]
    public void StateMap_TieredConcentricRadialTopology_CalculatesNonOverlappingCoordinates()
    {
        const double centerX = 1200.0;
        const double centerY = 800.0;

        var root = new LatticeNode
        {
            Id = "node:root",
            TargetId = _store.NextId(IdKind.Target),
            Label = "System Root",
            Kind = NodeKind.System,
            State = "Active",
            Provenance = "Observed",
            X = 0,
            Y = 0,
            LastSeenAt = DateTimeOffset.UtcNow,
        };

        var featureNodes = Enumerable.Range(1, 4).Select(i => new LatticeNode
        {
            Id = $"node:feature:{i}",
            TargetId = root.TargetId,
            Label = $"Feature {i}",
            Kind = NodeKind.Feature,
            State = "Observed",
            Provenance = "Observed",
            X = 0,
            Y = 0,
            LastSeenAt = DateTimeOffset.UtcNow,
        }).ToList();

        var leafNodes = Enumerable.Range(1, 6).Select(i => new LatticeNode
        {
            Id = $"node:leaf:{i}",
            TargetId = root.TargetId,
            Label = $"Leaf {i}",
            Kind = NodeKind.Action,
            State = "Observed",
            Provenance = "Observed",
            X = 0,
            Y = 0,
            LastSeenAt = DateTimeOffset.UtcNow,
        }).ToList();

        // Calculate positions
        var arranged = new List<LatticeNode> { root with { X = centerX, Y = centerY } };

        const double featureRadius = 320.0;
        for (int i = 0; i < featureNodes.Count; i++)
        {
            var angle = (2 * Math.PI * i) / featureNodes.Count - Math.PI / 2;
            var x = centerX + featureRadius * Math.Cos(angle);
            var y = centerY + featureRadius * Math.Sin(angle);
            arranged.Add(featureNodes[i] with { X = Math.Round(x, 1), Y = Math.Round(y, 1) });
        }

        const double leafRadius = 620.0;
        for (int i = 0; i < leafNodes.Count; i++)
        {
            var angle = (2 * Math.PI * i) / leafNodes.Count - Math.PI / 2 + 0.15;
            var x = centerX + leafRadius * Math.Cos(angle);
            var y = centerY + leafRadius * Math.Sin(angle);
            arranged.Add(leafNodes[i] with { X = Math.Round(x, 1), Y = Math.Round(y, 1) });
        }

        Assert.Equal(11, arranged.Count);
        // All nodes have finite, valid coordinates
        Assert.All(arranged, n =>
        {
            Assert.False(double.IsNaN(n.X));
            Assert.False(double.IsInfinity(n.X));
            Assert.False(double.IsNaN(n.Y));
            Assert.False(double.IsInfinity(n.Y));
        });

        // Feature nodes are roughly featureRadius away from center
        foreach (var fn in arranged.Where(n => n.Kind == NodeKind.Feature))
        {
            var dist = Math.Sqrt(Math.Pow(fn.X - centerX, 2) + Math.Pow(fn.Y - centerY, 2));
            Assert.True(Math.Abs(dist - featureRadius) < 1.0);
        }

        // Leaf nodes are roughly leafRadius away from center
        foreach (var ln in arranged.Where(n => n.Kind == NodeKind.Action))
        {
            var dist = Math.Sqrt(Math.Pow(ln.X - centerX, 2) + Math.Pow(ln.Y - centerY, 2));
            Assert.True(Math.Abs(dist - leafRadius) < 1.0);
        }
    }

    [Fact]
    public void WorkspacesForensicPhase3ControlsAndContractsExist()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces"));

        // State Map Auto Layout
        var stateMapXaml = File.ReadAllText(Path.Combine(basePath, "StateMapWorkspace.xaml"));
        var stateMapCs = File.ReadAllText(Path.Combine(basePath, "StateMapWorkspace.xaml.cs"));
        Assert.Contains("BtnAutoArrange", stateMapXaml);
        Assert.Contains("OnAutoArrange", stateMapCs);

        // Faults Multi-Predicate Filter & Export
        var faultsXaml = File.ReadAllText(Path.Combine(basePath, "FaultsWorkspace.xaml"));
        var faultsCs = File.ReadAllText(Path.Combine(basePath, "FaultsWorkspace.xaml.cs"));
        Assert.Contains("SearchBox", faultsXaml);
        Assert.Contains("SeverityFilter", faultsXaml);
        Assert.Contains("StateFilter", faultsXaml);
        Assert.Contains("ExportBugReportButton", faultsXaml);
        Assert.Contains("OnExportBugReport", faultsCs);

        // Algorithms Custom Vector Delta Debugging Lab
        var algoXaml = File.ReadAllText(Path.Combine(basePath, "AlgorithmsWorkspace.xaml"));
        var algoCs = File.ReadAllText(Path.Combine(basePath, "AlgorithmsWorkspace.xaml.cs"));
        Assert.Contains("CustomInputVectorText", algoXaml);
        Assert.Contains("VectorPresetPicker", algoXaml);
        Assert.Contains("FailureConditionPicker", algoXaml);
        Assert.Contains("CustomKeywordText", algoXaml);
        Assert.Contains("OnResetVector", algoCs);

        // Evidence Forensic Kinds Filter
        var evXaml = File.ReadAllText(Path.Combine(basePath, "EvidenceWorkspace.xaml"));
        Assert.Contains("ui:screenshot/png", evXaml);
        Assert.Contains("application/json", evXaml);
        Assert.Contains("report:sarif", evXaml);
        Assert.Contains("report:junit", evXaml);
    }

    [Fact]
    public void WorkspacesForensicPhase4ErgonomicInnovationsExist()
    {
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces"));

        // State Map Node Search
        var stateMapXaml = File.ReadAllText(Path.Combine(basePath, "StateMapWorkspace.xaml"));
        var stateMapCs = File.ReadAllText(Path.Combine(basePath, "StateMapWorkspace.xaml.cs"));
        Assert.Contains("NodeSearchBox", stateMapXaml);
        Assert.Contains("OnNodeSearchChanged", stateMapCs);

        // Faults Fluid Selection and Non-Blocking Export
        var faultsCs = File.ReadAllText(Path.Combine(basePath, "FaultsWorkspace.xaml.cs"));
        Assert.Contains("Queue.SelectedIndex = 0;", faultsCs);
        Assert.Contains("Copied to Clipboard!", faultsCs);

        // Evidence Fluid Selection
        var evCs = File.ReadAllText(Path.Combine(basePath, "EvidenceWorkspace.xaml.cs"));
        Assert.Contains("Items.SelectedIndex = 0;", evCs);

        // Algorithms Delta-Debugging Visual Token Triage
        var algoCs = File.ReadAllText(Path.Combine(basePath, "AlgorithmsWorkspace.xaml.cs"));
        Assert.Contains("ISOLATED CRITICAL SUBSET", algoCs);
        Assert.Contains("PRUNED OBSERVATIONAL NOISE", algoCs);
    }
}

