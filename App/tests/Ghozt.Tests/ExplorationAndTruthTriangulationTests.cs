using System.IO;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class ExplorationAndTruthTriangulationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly GhoztStore _store;
    private readonly Target _observeTarget;
    private readonly Target _stateChangingTarget;

    public ExplorationAndTruthTriangulationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ghozt_explore_triangulate_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "explore_triangulate.db");
        _store = new GhoztStore(_dbPath);

        _observeTarget = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Production Gateway Target",
            Kind = "API",
            Environment = TargetEnvironment.Production,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "https://api.example.com",
            BuildLabel = "prod-v1.0",
            AuthorityNote = "Observe-only gate",
            Reachable = true,
        };
        _store.Save(_observeTarget);

        _stateChangingTarget = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Development Local Target",
            Kind = "Service",
            Environment = TargetEnvironment.Development,
            Access = AccessClass.StateChanging,
            BaseAddress = "http://localhost:5000",
            BuildLabel = "dev-v1.1",
            AuthorityNote = "Dev state mutations permitted",
            Reachable = true,
        };
        _store.Save(_stateChangingTarget);
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
    // 1. Autonomous Exploration & Candidate-Action Scoring (Patent §16 & §17)
    // ==================================================================

    [Fact]
    public void ExplorationEngine_CandidateActionScore_PrioritizesUnvisitedNodesAndPenalizesLoops()
    {
        var engine = new ExplorationEngine(_store);

        var action = new CandidateAction
        {
            Id = "ACT-001",
            SourceNodeId = "node-root",
            TargetNodeId = "node-target",
            Kind = ActionKind.Navigate,
            Description = "Navigate to feature",
        };

        var unvisitedNode = new LatticeNode
        {
            Id = "node-target",
            TargetId = _stateChangingTarget.Id,
            Label = "Checkout Feature",
            Kind = NodeKind.Feature,
            State = "untested",
            Provenance = "manifest",
            VisitCount = 0,
        };

        var visitedNode = unvisitedNode with
        {
            VisitCount = 10,
            State = "verified",
        };

        // Unvisited node should score higher than heavily visited node
        var scoreUnvisited = engine.ScoreAction(action, _stateChangingTarget, unvisitedNode, historyCount: 0);
        var scoreVisited = engine.ScoreAction(action, _stateChangingTarget, visitedNode, historyCount: 0);

        Assert.True(scoreUnvisited > scoreVisited, $"Expected unvisited score ({scoreUnvisited}) > visited score ({scoreVisited})");

        // History count increases loop penalty H(s, a)
        var scoreWithHistoryLoop = engine.ScoreAction(action, _stateChangingTarget, unvisitedNode, historyCount: 5);
        Assert.True(scoreWithHistoryLoop < scoreUnvisited, $"Expected loop-penalized score ({scoreWithHistoryLoop}) < pristine score ({scoreUnvisited})");
    }

    [Fact]
    public void ExplorationEngine_ObserveOnlyTarget_PrunesStateChangingActions()
    {
        var readAction = new CandidateAction
        {
            Id = "ACT-READ",
            SourceNodeId = "node-root",
            TargetNodeId = "node-endpoint",
            Kind = ActionKind.Inspect,
            Description = "Read metrics endpoint",
            MutatesState = false,
            IsDestructive = false,
        };

        var mutateAction = new CandidateAction
        {
            Id = "ACT-MUTATE",
            SourceNodeId = "node-root",
            TargetNodeId = "node-endpoint",
            Kind = ActionKind.Invoke,
            Description = "POST /users/delete-all",
            MutatesState = true,
            IsDestructive = false,
        };

        var clickAction = new CandidateAction
        {
            Id = "ACT-CLICK",
            SourceNodeId = "node-root",
            TargetNodeId = "node-btn",
            Kind = ActionKind.Click,
            Description = "Click checkout submit button",
            MutatesState = false,
            IsDestructive = false,
        };

        // ObserveOnly target permits inspect/read, strictly refuses mutations or interaction clicks
        Assert.True(ExplorationEngine.IsPermitted(readAction, _observeTarget));
        Assert.False(ExplorationEngine.IsPermitted(mutateAction, _observeTarget));
        Assert.False(ExplorationEngine.IsPermitted(clickAction, _observeTarget));

        // StateChanging target permits mutations
        Assert.True(ExplorationEngine.IsPermitted(mutateAction, _stateChangingTarget));
    }

    [Fact]
    public void ExplorationEngine_AdversarialStrategy_GeneratesBoundaryAndMaliciousInputs()
    {
        var engine = new ExplorationEngine(_store);

        var candidates = engine.GenerateCandidates(
            _stateChangingTarget,
            "node-root",
            ExplorationStrategy.Adversarial);

        Assert.NotEmpty(candidates);

        // Adversarial candidates must contain buffer limit, SQL injection, and unicode probes
        var fuzzActions = candidates.Where(c => c.Kind == ActionKind.FuzzPayload).ToList();
        Assert.True(fuzzActions.Count >= 3, $"Expected at least 3 fuzz actions, found {fuzzActions.Count}");

        Assert.Contains(fuzzActions, a => a.Parameters.GetValueOrDefault("type") == "buffer-limit");
        Assert.Contains(fuzzActions, a => a.Parameters.GetValueOrDefault("type") == "sqli-probe");
        Assert.Contains(fuzzActions, a => a.Parameters.GetValueOrDefault("type") == "unicode-probe");
    }

    // ==================================================================
    // 2. Behavioural Truth Triangulation Tests (Patent §14 & §15)
    // ==================================================================

    [Fact]
    public void TruthTriangulationEngine_Triangulate_DetectsMissingImplementationAndUndeclaredRoutes()
    {
        var engine = new TruthTriangulationEngine(_store);

        // Define Declared Intent with 2 features
        var manifest = new BehaviourManifest
        {
            Id = "MAN-TEST-01",
            TargetId = _stateChangingTarget.Id,
            TargetName = _stateChangingTarget.Name,
            Features =
            [
                new ManifestFeature
                {
                    FeatureId = "FTR-001",
                    Name = "Order Checkout",
                    Risk = Severity.High,
                    Preconditions = [],
                    Actions = ["POST"],
                    ExpectedStates = ["Success"],
                    FailureStates = ["Rejected"],
                    Permissions = [],
                    Oracles = [],
                    EvidenceRequired = [],
                    HttpMethod = "POST",
                    RoutePath = "/api/orders/checkout",
                },
                new ManifestFeature
                {
                    FeatureId = "FTR-002",
                    Name = "User Invoicing",
                    Risk = Severity.Critical,
                    Preconditions = [],
                    Actions = ["GET"],
                    ExpectedStates = ["Invoice"],
                    FailureStates = ["NotFound"],
                    Permissions = [],
                    Oracles = [],
                    EvidenceRequired = [],
                    HttpMethod = "GET",
                    RoutePath = "/api/billing/invoices",
                }
            ]
        };

        // Source Reality has /api/orders/checkout and a shadow route /api/admin/backdoor
        var sourceEndpoints = new List<string>
        {
            "/api/orders/checkout",
            "/api/admin/backdoor", // Undeclared shadow route!
        };

        // Seed lattice node for checkout
        _store.Save(new LatticeNode
        {
            Id = "node-checkout",
            TargetId = _stateChangingTarget.Id,
            Label = "/api/orders/checkout",
            Kind = NodeKind.Endpoint,
            State = "observed",
            Provenance = "runtime",
            VisitCount = 2,
        });

        var report = engine.Triangulate(_stateChangingTarget, manifest, sourceEndpoints);

        Assert.NotNull(report);
        Assert.True(report.DriftCount >= 2, $"Expected at least 2 drifts, got {report.DriftCount}");

        // 1. Missing Implementation: FTR-002 (/api/billing/invoices) is not in source code
        var missing = report.Drifts.FirstOrDefault(d => d.Category == TruthDriftCategory.MissingImplementation);
        Assert.NotNull(missing);
        Assert.Equal("FTR-002", missing.EntityId);

        // 2. Undeclared Behaviour: /api/admin/backdoor is in source but absent in manifest
        var shadow = report.Drifts.FirstOrDefault(d => d.Category == TruthDriftCategory.UndeclaredBehaviour);
        Assert.NotNull(shadow);
        Assert.Contains("backdoor", shadow.EntityName);
    }

    [Fact]
    public void TruthTriangulationEngine_OracleConflict_FlagsContractContradiction()
    {
        var engine = new TruthTriangulationEngine(_store);

        // Add a finding that represents contract schema divergence
        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = _stateChangingTarget.Id,
            Title = "Contract schema mismatch: expected 200 with JSON payload",
            Severity = Severity.High,
            Maturity = FindingMaturity.New,
            Actual = "HTTP 500 Internal Server Error",
            Expected = "HTTP 200 OK with valid schema",
            Fingerprint = "FP-SCHEMA-001",
            NodeId = "node-checkout",
        };
        _store.Save(finding);

        var report = engine.Triangulate(_stateChangingTarget);

        var conflict = report.Drifts.FirstOrDefault(d => d.Category == TruthDriftCategory.OracleConflict);
        Assert.NotNull(conflict);
        Assert.Equal(finding.Id.ToString(), conflict.EntityId);
        Assert.Equal(Severity.High, conflict.Severity);
    }

    // ==================================================================
    // 3. Adaptive Capture & Pre-Failure Ring Buffer Tests (Patent §23)
    // ==================================================================

    [Fact]
    public void AdaptiveCaptureEngine_EscalatesToForensicMode_PreservesPreFailureRingBuffer()
    {
        var engine = new AdaptiveCaptureEngine(_store, ringBufferCapacity: 10);

        Assert.Equal(CaptureMode.Baseline, engine.CurrentMode);

        // Append regular telemetry frames
        engine.RecordTelemetry("UI.NAVIGATE", "Operator", "Navigated to login screen");
        engine.RecordTelemetry("UI.INPUT", "Operator", "Entered username admin");
        engine.RecordTelemetry("UI.CLICK", "Operator", "Clicked submit button");

        Assert.Equal(3, engine.RingBuffer.Count);
        Assert.Equal(CaptureMode.Baseline, engine.CurrentMode);

        // Record a critical failure event
        engine.RecordTelemetry("SYS.CRASH", "Runtime", "NullReferenceException on PaymentProcessor", severity: Severity.Critical);

        // Must auto-escalate to Forensic mode
        Assert.Equal(CaptureMode.Forensic, engine.CurrentMode);

        // Seal forensic package
        var evidence = engine.SealForensicPackage(_stateChangingTarget, "Critical NullReferenceException");

        Assert.NotNull(evidence);
        Assert.Equal("forensic-ring-buffer", evidence.Kind);
        Assert.True(evidence.Sealed);
        Assert.NotEmpty(evidence.Sha256);
        Assert.Contains("PreFailureSequence", evidence.Payload);
        Assert.Contains("NullReferenceException", evidence.Payload);
        Assert.Contains("Navigated to login screen", evidence.Payload); // Preserves preceding history!
    }

    [Fact]
    public void AdaptiveCaptureEngine_StoragePressure_DegradesMediaGracefully()
    {
        var engine = new AdaptiveCaptureEngine(_store, ringBufferCapacity: 10)
        {
            CurrentMode = CaptureMode.StoragePressure
        };

        // Record huge payload (> 512 bytes)
        var hugePayload = new string('X', 2000);
        engine.RecordTelemetry("HTTP.RESPONSE", "Adapter", "Large API response payload", hugePayload);

        var evidence = engine.SealForensicPackage(_stateChangingTarget, "Storage pressure test");

        Assert.NotNull(evidence);
        // Payload must include graceful degradation marker
        Assert.Contains("OMITTED_1488_BYTES_UNDER_STORAGE_PRESSURE", evidence.Payload);
    }
}
