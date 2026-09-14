using System.Globalization;
using System.IO;
using Ghozt.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Ghozt.Tests;

public sealed class MultiAgentAndAdapterSdkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly GhoztStore _store;
    private readonly Target _target;

    public MultiAgentAndAdapterSdkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ghozt_agent_sdk_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _store = new GhoztStore(_dbPath);

        _target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Sample Target",
            Kind = "Desktop application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "process:1",
            BuildLabel = "1.0.0",
            AuthorityNote = "Test authority grant",
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
    // 1. Supervised Multi-Agent Architecture (Patent §34)
    // ==================================================================

    [Fact]
    public async Task MultiAgentSupervisor_EnforcesRoleProhibitions_AndRejectsUnauthorizedActions()
    {
        var supervisor = new MultiAgentSupervisor(_store);
        var budget = new AgentBudgetTracker();

        // 1. ManifestAgent cannot silently approve expected behaviour
        var rec1 = new AgentRecommendation
        {
            Role = AgentRole.ManifestAgent,
            Summary = "Attempting silent approval",
            ProposedAction = "ApproveExpectedBehavior",
            Parameters = new Dictionary<string, string> { ["silent_approve"] = "true" },
        };
        var res1 = await supervisor.SuperviseAsync(_target, rec1, budget);
        Assert.False(res1.Approved);
        Assert.Equal("AGT.ROLE_PROHIBITION_VIOLATED", res1.Code);

        // 2. ForensicsAgent cannot label cause final without evidence
        var rec2 = new AgentRecommendation
        {
            Role = AgentRole.ForensicsAgent,
            Summary = "Attempting cause final label",
            ProposedAction = "LabelCauseFinal",
            CitedEvidenceIds = Array.Empty<string>(),
        };
        var res2 = await supervisor.SuperviseAsync(_target, rec2, budget);
        Assert.False(res2.Approved);
        Assert.Equal("AGT.ROLE_PROHIBITION_VIOLATED", res2.Code);

        // 3. ReportAgent cannot invent findings
        var rec3 = new AgentRecommendation
        {
            Role = AgentRole.ReportAgent,
            Summary = "Drafting report with invented finding",
            ProposedAction = "DraftExecutiveSummary",
            Parameters = new Dictionary<string, string> { ["invent_finding"] = "true" },
        };
        var res3 = await supervisor.SuperviseAsync(_target, rec3, budget);
        Assert.False(res3.Approved);
        Assert.Equal("AGT.ROLE_PROHIBITION_VIOLATED", res3.Code);
    }

    [Fact]
    public async Task MultiAgentSupervisor_GroundingAssay_FailsOnUnreferencedClaims()
    {
        var supervisor = new MultiAgentSupervisor(_store);
        var budget = new AgentBudgetTracker();

        // Recommendation cites a non-existent evidence item
        var rec = new AgentRecommendation
        {
            Role = AgentRole.ForensicsAgent,
            Summary = "Asserting defect cause based on unverified evidence",
            ProposedAction = "AnalyzeDeviation",
            Confidence = 0.95,
            CitedEvidenceIds = ["EVD-999999"],
        };

        var verdict = await supervisor.SuperviseAsync(_target, rec, budget);
        Assert.False(verdict.Approved);
        Assert.Equal("AGT.GROUNDING.FAIL", verdict.Code);
        Assert.False(verdict.Grounding.Passed);
        Assert.NotEmpty(verdict.Grounding.UngroundedClaims);
    }

    [Fact]
    public async Task MultiAgentSupervisor_PrivilegeBypass_BlocksMutationUnderObserveOnly()
    {
        var supervisor = new MultiAgentSupervisor(_store);
        var budget = new AgentBudgetTracker();

        // Target is ObserveOnly; recommendation attempts L2 State Changing action
        var rec = new AgentRecommendation
        {
            Role = AgentRole.RepairAgent,
            Summary = "Mutate database records",
            ProposedAction = "ApplyFixture",
            RequiredLevel = AgentPermissionLevel.L2_StateChanging,
            Confidence = 0.99,
        };

        var verdict = await supervisor.SuperviseAsync(_target, rec, budget);
        Assert.False(verdict.Approved);
        Assert.Equal("AGT.PRIVILEGE_BYPASS_BLOCKED", verdict.Code);
        Assert.Contains("ObserveOnly", verdict.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultiAgentSupervisor_EnforcesStepAndTokenBudget()
    {
        var supervisor = new MultiAgentSupervisor(_store);
        var budget = new AgentBudgetTracker { MaxSteps = 2 };

        var rec = new AgentRecommendation
        {
            Role = AgentRole.ExplorationAgent,
            Summary = "Step 1",
            ProposedAction = "ExploreFrontier",
        };

        var res1 = await supervisor.SuperviseAsync(_target, rec, budget);
        Assert.True(res1.Approved);

        var res2 = await supervisor.SuperviseAsync(_target, rec, budget);
        Assert.True(res2.Approved);

        // Step 3 exceeds MaxSteps = 2
        var res3 = await supervisor.SuperviseAsync(_target, rec, budget);
        Assert.False(res3.Approved);
        Assert.Equal("AGT.BUDGET_EXHAUSTED", res3.Code);
    }

    [Fact]
    public async Task MultiAgentSupervisor_ClearsValidRecommendation_AndSealsEvidence()
    {
        var supervisor = new MultiAgentSupervisor(_store);
        var budget = new AgentBudgetTracker();

        var rec = new AgentRecommendation
        {
            Role = AgentRole.ExplorationAgent,
            Summary = "Sample exploration step",
            ProposedAction = "InspectLatticeFrontier",
            RequiredLevel = AgentPermissionLevel.L0_Observe,
            Confidence = 0.85,
        };

        var verdict = await supervisor.SuperviseAsync(_target, rec, budget);
        Assert.True(verdict.Approved);
        Assert.Equal("AGT.APPROVED", verdict.Code);
        Assert.NotNull(verdict.SealedEvidence);
        Assert.True(verdict.SealedEvidence!.Sealed);
        Assert.Equal("AgentSupervision", verdict.SealedEvidence.Kind);
    }

    // ==================================================================
    // 2. Unified Adapter SDK (Patent §37)
    // ==================================================================

    [Fact]
    public void AdapterRegistry_ResolvesSpecializedAdapters_ForTargetClasses()
    {
        var registry = AdapterRegistry.Shared;

        var dbTarget = _target with { Kind = "SQLite database", BaseAddress = _dbPath };
        var httpTarget = _target with { Kind = "HTTP API", BaseAddress = "https://api.capital-iz.com" };
        var procTarget = _target with { Kind = "Process", BaseAddress = "process:1234" };
        var desktopTarget = _target with { Kind = "Desktop application", BaseAddress = @"C:\Program Files\App.exe" };

        Assert.IsType<DatabaseSqliteAdapter>(registry.Resolve(dbTarget));
        Assert.IsType<HttpRestAdapter>(registry.Resolve(httpTarget));
        Assert.IsType<ProcessSupervisorAdapter>(registry.Resolve(procTarget));
        Assert.IsType<DesktopUiaAdapter>(registry.Resolve(desktopTarget));
    }

    [Fact]
    public async Task DatabaseSqliteAdapter_DiscoversSchemaAndQueries()
    {
        var adapter = new DatabaseSqliteAdapter();

        // Create sample table in SQLite test db
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INTEGER PRIMARY KEY, Username TEXT, Balance REAL); INSERT INTO Users VALUES (1, 'Alice', 500.0);";
            cmd.ExecuteNonQuery();
        }

        var dbTarget = _target with { Kind = "SQLite database", BaseAddress = _dbPath };

        var preflight = await adapter.PreflightCheckAsync(dbTarget);
        Assert.True(preflight.Success);
        Assert.Equal("SQL.PREFLIGHT.PASS", preflight.Code);

        var discovery = await adapter.DiscoverAsync(dbTarget);
        Assert.True(discovery.Success);
        Assert.Contains(discovery.DiscoveredNodes, n => n.Label.Contains("Users", StringComparison.OrdinalIgnoreCase));

        var action = await adapter.ExecuteActionAsync(dbTarget, new AdapterActionRequest
        {
            ActionType = "Query",
            Parameters = new Dictionary<string, string> { ["query"] = "SELECT Balance FROM Users WHERE Id = 1;" },
        });
        Assert.True(action.Success);
        Assert.Equal("500", action.OutputPayload);
    }

    [Fact]
    public async Task ProcessSupervisorAdapter_CapturesCurrentProcessTelemetry()
    {
        var adapter = new ProcessSupervisorAdapter();
        var currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        var procTarget = _target with { Kind = "Process", BaseAddress = $"process:{currentPid}" };

        var preflight = await adapter.PreflightCheckAsync(procTarget);
        Assert.True(preflight.Success);
        Assert.Equal("PROC.PREFLIGHT.PASS", preflight.Code);
        Assert.True(preflight.Diagnostics.ContainsKey("WorkingSetMB"));

        var discovery = await adapter.DiscoverAsync(procTarget);
        Assert.True(discovery.Success);
        Assert.NotEmpty(discovery.DiscoveredNodes);
    }

    [Fact]
    public async Task DesktopUiaAdapter_DiscoversLatticeHierarchy()
    {
        var adapter = new DesktopUiaAdapter();
        var discovery = await adapter.DiscoverAsync(_target);

        Assert.True(discovery.Success);
        Assert.Equal(3, discovery.DiscoveredNodes.Count);
        Assert.Equal(2, discovery.DiscoveredEdges.Count);
    }

    // ==================================================================
    // 3. Data, Database and Pipeline Examination Oracles (Patent §19, §20)
    // ==================================================================

    [Fact]
    public void DataOracleEngine_ExactRowAndSetEquality_ValidatesCorrectly()
    {
        var engine = new DataOracleEngine(_store);

        // Create sample records
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Products (Sku TEXT PRIMARY KEY, Name TEXT, Price REAL); INSERT INTO Products VALUES ('SKU-1', 'Widget', 19.99);";
            cmd.ExecuteNonQuery();
        }

        // Positive exact row check
        var resPass = engine.EvaluateExactRow(
            _target, _dbPath, "Products", "Sku", "SKU-1",
            new Dictionary<string, string> { ["Name"] = "Widget", ["Price"] = "19.99" });
        Assert.True(resPass.Passed);
        Assert.NotNull(resPass.SealedEvidence);

        // Negative exact row check (mismatched price)
        var resFail = engine.EvaluateExactRow(
            _target, _dbPath, "Products", "Sku", "SKU-1",
            new Dictionary<string, string> { ["Name"] = "Widget", ["Price"] = "99.99" });
        Assert.False(resFail.Passed);
        Assert.NotNull(resFail.GeneratedFinding);
        Assert.Equal(Severity.High, resFail.GeneratedFinding!.Severity);

        // Set equality checks
        var setA = new[] { "apple", "banana", "cherry" };
        var setB = new[] { "cherry", "banana", "apple" };
        var setC = new[] { "apple", "banana" };

        var eqRes = engine.EvaluateSetRelation(_target, "FruitSetEq", setA, setB, SetRelation.ExactEqual);
        Assert.True(eqRes.Passed);

        var subRes = engine.EvaluateSetRelation(_target, "FruitSubset", setC, setA, SetRelation.Subset);
        Assert.True(subRes.Passed);

        var failSub = engine.EvaluateSetRelation(_target, "FruitFailSubset", setA, setC, SetRelation.Subset);
        Assert.False(failSub.Passed);
    }

    [Fact]
    public void DataOracleEngine_AggregateChecksum_ReconcilesSumsAndSha256()
    {
        var engine = new DataOracleEngine(_store);

        var numbers = new double[] { 10.0, 20.0, 30.5 };
        var payloads = new[] { "record1", "record2", "record3" };

        // Positive check
        var passRes = engine.EvaluateAggregateChecksum(_target, "TxTotals", 3, 60.5, "", numbers, payloads);
        Assert.True(passRes.Passed);

        // Negative check (sum discrepancy)
        var failRes = engine.EvaluateAggregateChecksum(_target, "TxTotalsTampered", 3, 100.0, "", numbers, payloads);
        Assert.False(failRes.Passed);
        Assert.NotNull(failRes.GeneratedFinding);
        Assert.Contains("Sum mismatch", failRes.Summary);
    }

    [Fact]
    public void DataOracleEngine_ReferentialInvariant_DetectsOrphanedRecords()
    {
        var engine = new DataOracleEngine(_store);

        var parentKeys = new[] { "ORDER-101", "ORDER-102" };
        var validChildren = new[]
        {
            ("ITEM-1", "ORDER-101"),
            ("ITEM-2", "ORDER-101"),
            ("ITEM-3", "ORDER-102"),
        };

        var passRes = engine.EvaluateReferentialIntegrity(_target, "OrderItemsRef", "Orders", "Items", parentKeys, validChildren);
        Assert.True(passRes.Passed);

        var invalidChildren = new[]
        {
            ("ITEM-1", "ORDER-101"),
            ("ITEM-ORPHAN", "ORDER-999"), // non-existent order
        };

        var failRes = engine.EvaluateReferentialIntegrity(_target, "OrderItemsRefFault", "Orders", "Items", parentKeys, invalidChildren);
        Assert.False(failRes.Passed);
        Assert.NotNull(failRes.GeneratedFinding);
        Assert.Equal(Severity.Critical, failRes.GeneratedFinding!.Severity);
    }

    [Fact]
    public void DataOracleEngine_ConservationEquation_ValidatesBalanceConservation()
    {
        var engine = new DataOracleEngine(_store);

        // Inflow = 1000, Outflow = 300, Initial = 500 => Final must be 1200
        var passRes = engine.EvaluateConservationEquation(
            _target, "AccountConservation",
            initialBalance: 500.0,
            finalBalance: 1200.0,
            inflows: [600.0, 400.0],
            outflows: [200.0, 100.0]);
        Assert.True(passRes.Passed);

        // Final balance altered to 1100 (100 missing)
        var failRes = engine.EvaluateConservationEquation(
            _target, "AccountConservationLeak",
            initialBalance: 500.0,
            finalBalance: 1100.0,
            inflows: [600.0, 400.0],
            outflows: [200.0, 100.0]);
        Assert.False(failRes.Passed);
        Assert.NotNull(failRes.GeneratedFinding);
        Assert.Equal(Severity.Critical, failRes.GeneratedFinding!.Severity);
    }

    [Fact]
    public void DataOracleEngine_TemporalFreshness_DetectsClockReversalsAndStaleness()
    {
        var engine = new DataOracleEngine(_store);

        var baseTime = DateTimeOffset.Now.AddSeconds(-30);
        var monotonicTimes = new[]
        {
            baseTime,
            baseTime.AddSeconds(5),
            baseTime.AddSeconds(10),
            baseTime.AddSeconds(20),
        };

        var passRes = engine.EvaluateTemporalFreshness(_target, "TelemetryTimestamps", monotonicTimes, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5));
        Assert.True(passRes.Passed);

        // Inverted timestamps (clock reversal / out-of-order logs)
        var invertedTimes = new[]
        {
            baseTime,
            baseTime.AddSeconds(10),
            baseTime.AddSeconds(5), // inversion!
        };

        var failRes = engine.EvaluateTemporalFreshness(_target, "ClockReversal", invertedTimes, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5));
        Assert.False(failRes.Passed);
        Assert.NotNull(failRes.GeneratedFinding);
        Assert.Contains("Timestamp inversions", failRes.Summary);
    }
}
