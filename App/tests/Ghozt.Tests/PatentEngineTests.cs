using System.IO;
using System.Text.Json;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class PatentEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public PatentEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_patent_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_patent_test.db");
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
    public void ManifestEngine_DiscoversOpenApiEndpoints_AndCreatesLatticeNodes()
    {
        // 1. Create a dummy OpenAPI 3.0 specification
        var openApiJson = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Sample API Service", "version": "1.0.0" },
          "paths": {
            "/api/v1/targets": {
              "get": {
                "summary": "List all active targets",
                "responses": { "200": { "description": "OK" } }
              },
              "post": {
                "summary": "Register a new examination target",
                "responses": { "201": { "description": "Created" } }
              }
            },
            "/api/v1/sweeps/{id}/abort": {
              "post": {
                "summary": "Abort an in-flight sweep",
                "responses": { "200": { "description": "Aborted" } }
              }
            }
          }
        }
        """;
        var openApiPath = Path.Combine(_tempDir, "openapi.json");
        File.WriteAllText(openApiPath, openApiJson);

        // 2. Register Target pointing to tempDir
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "OpenAPI Service Target",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            Reachable = true,
        };
        _store.Save(target);

        var engine = new ManifestEngine(_store);
        var manifest = engine.Discover(target);

        Assert.NotNull(manifest);
        Assert.True(manifest.EndpointCount >= 3);
        Assert.Contains(manifest.Features, f => f.RoutePath == "/api/v1/targets" && f.HttpMethod == "GET");
        Assert.Contains(manifest.Features, f => f.RoutePath == "/api/v1/targets" && f.HttpMethod == "POST");
        Assert.Contains(manifest.Features, f => f.RoutePath == "/api/v1/sweeps/{id}/abort" && f.HttpMethod == "POST");

        // Verify Lattice Nodes created in SQLite
        var nodes = _store.Nodes(target.Id);
        var endpointNode = nodes.FirstOrDefault(n => n.Kind == NodeKind.Endpoint && n.Label.Contains("POST /api/v1/targets"));
        Assert.NotNull(endpointNode);
        Assert.Equal("manifest:openapi.json", endpointNode.Provenance);

        // Verify Lattice Edge created
        var edges = _store.Edges(target.Id);
        Assert.Contains(edges, e => e.ToId == endpointNode.Id && e.Relationship == "declares_endpoint");
    }

    [Fact]
    public void ManifestEngine_DiscoversDotNetProjectAndLaunchSettings()
    {
        // 1. Create a sample .csproj
        var projDir = Path.Combine(_tempDir, "SampleProject");
        Directory.CreateDirectory(projDir);
        var csprojPath = Path.Combine(projDir, "SampleProject.csproj");
        var csprojContent = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net8.0-windows</TargetFramework>
            <AssemblyName>SampleProject</AssemblyName>
          </PropertyGroup>
        </Project>
        """;
        File.WriteAllText(csprojPath, csprojContent);

        // 2. Create Properties/launchSettings.json
        var propsDir = Path.Combine(projDir, "Properties");
        Directory.CreateDirectory(propsDir);
        var launchPath = Path.Combine(propsDir, "launchSettings.json");
        var launchContent = """
        {
          "profiles": {
            "SampleProject.Development": {
              "commandName": "Project",
              "applicationUrl": "https://localhost:7054;http://localhost:5054",
              "commandLineArgs": "--mode forensic"
            }
          }
        }
        """;
        File.WriteAllText(launchPath, launchContent);

        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "DotNet App Target",
            Kind = "Application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = projDir,
            Reachable = true,
        };
        _store.Save(target);

        var engine = new ManifestEngine(_store);
        var manifest = engine.Discover(target);

        Assert.NotNull(manifest);

        var nodes = _store.Nodes(target.Id);
        var projNode = nodes.FirstOrDefault(n => n.Kind == NodeKind.Feature && n.Label == "SampleProject");
        Assert.NotNull(projNode);
        Assert.Contains("net8.0-windows", projNode.Detail);
        Assert.Contains("WinExe", projNode.Detail);

        var launchNode = nodes.FirstOrDefault(n => n.Kind == NodeKind.Action && n.Label.Contains("SampleProject.Development"));
        Assert.NotNull(launchNode);
        Assert.Contains("https://localhost:7054", launchNode.Detail);
    }

    [Fact]
    public void FaultFingerprint_NormalizesVolatileTokens_ProducesIdenticalHash()
    {
        var targetId = "tgt-0001";
        var excClass = "System.NullReferenceException";
        var errorCode = "ERR_NULL_PTR";

        // Two error occurrences with different memory addresses, PIDs, timestamps, and localhost ports
        var msg1 = "Object reference not set at 0x7FFE28A13B20 (pid 4120) on 2026-09-14T10:15:30Z connected to http://127.0.0.1:5054/api: line 42";
        var msg2 = "Object reference not set at 0x7FFE99B48F10 (pid 9831) on 2026-09-14T11:45:12Z connected to http://127.0.0.1:8088/api: line 88";

        var fp1 = FaultFingerprint.Compute(targetId, excClass, errorCode, msg1, "node-view");
        var fp2 = FaultFingerprint.Compute(targetId, excClass, errorCode, msg2, "node-view");

        Assert.Equal(fp1, fp2);
        Assert.Equal(32, fp1.Length);

        // Different error class should produce a distinct fingerprint
        var fp3 = FaultFingerprint.Compute(targetId, "System.InvalidOperationException", errorCode, msg1, "node-view");
        Assert.NotEqual(fp1, fp3);
    }

    [Fact]
    public void FaultFingerprint_ClustersRepeatDeviations_UpdatesWilsonStats()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Recurrence Target",
            Kind = "Application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            Reachable = true,
        };
        _store.Save(target);

        var fp = FaultFingerprint.Compute(target.Id.ToString(), "NullReferenceException", "NRE_01", "Null pointer crash");

        var finding1 = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Null pointer crash in UI thread",
            Severity = Severity.Critical,
            Maturity = FindingMaturity.New,
            Fingerprint = fp,
            ReproAttempts = 1,
            ReproSuccesses = 1,
            EvidenceCount = 1,
        };

        // First registration
        var (f1, isRecur1) = FaultFingerprint.ClusterFinding(_store, finding1, target);
        Assert.False(isRecur1);
        Assert.Equal(1, f1.ReproAttempts);
        Assert.Equal(1, f1.ReproSuccesses);
        Assert.Equal(FindingMaturity.New, f1.Maturity);

        // Second occurrence (recurrence)
        var finding2 = finding1 with { Id = _store.NextId(IdKind.Fault) };
        var (f2, isRecur2) = FaultFingerprint.ClusterFinding(_store, finding2, target);
        Assert.True(isRecur2);
        Assert.Equal(2, f2.ReproAttempts);
        Assert.Equal(2, f2.ReproSuccesses);
        Assert.Equal(FindingMaturity.Reproduced, f2.Maturity);

        // Bayesian probability and Wilson score confidence
        Assert.True(f2.BayesianReproducibility > 0.6);
        Assert.True(f2.WilsonConfidence.Lower > 0.0);
    }

    [Fact]
    public void SequenceMinimizer_HeuristicMinimization_PrunesObservationalNoise()
    {
        var steps = new List<(string Action, bool IsStateChange, bool IsTerminal)>
        {
            ("Launch application", true, false),
            ("Hover over navigation rail", false, false),
            ("Passive scroll window", false, false),
            ("Click 'Targets' menu item", true, false),
            ("Read active target list", false, false),
            ("Click 'Sweep' menu item", true, false),
            ("Click 'Sweep' menu item", true, false), // Duplicate consecutive action
            ("Hover over run button", false, false),
            ("Click 'Run Sweep' -> Crash", true, true), // Terminal failure
        };

        var result = SequenceMinimizer.MinimizeSequence(
            steps,
            isStateChanging: s => s.IsStateChange,
            isTerminalFailure: s => s.IsTerminal,
            areEquivalentActions: (a, b) => a.Action == b.Action);

        Assert.Equal(9, result.OriginalCount);
        // Expect: Launch, Click Targets, Click Sweep (deduplicated), Click Run Sweep (Terminal) = 4 steps
        Assert.Equal(4, result.MinimalCount);
        Assert.Equal(5, result.PrunedCount);
        Assert.True(result.ReductionPercent > 50.0);
        Assert.Contains(result.MinimalSequence, s => s.Action == "Launch application");
        Assert.Contains(result.MinimalSequence, s => s.Action == "Click 'Targets' menu item");
        Assert.Contains(result.MinimalSequence, s => s.Action == "Click 'Sweep' menu item");
        Assert.Contains(result.MinimalSequence, s => s.Action == "Click 'Run Sweep' -> Crash");
    }

    [Fact]
    public async Task SequenceMinimizer_DeltaDebug_IsolatesMinimalSubsequence()
    {
        // Sequence with 10 steps, only steps 2 and 7 together trigger the bug
        var fullSequence = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        Task<bool> Oracle(IReadOnlyList<int> subset)
        {
            // Bug requires both step 2 and step 7
            var has2 = subset.Contains(2);
            var has7 = subset.Contains(7);
            return Task.FromResult(has2 && has7);
        }

        var result = await SequenceMinimizer.DeltaDebugAsync(fullSequence, Oracle);

        Assert.Equal(10, result.OriginalCount);
        Assert.Equal(2, result.MinimalCount);
        Assert.Equal(8, result.PrunedCount);
        Assert.Contains(2, result.MinimalSequence);
        Assert.Contains(7, result.MinimalSequence);
        Assert.Equal(80.0, result.ReductionPercent);
    }

    [Fact]
    public void StateSpaceMath_FindShortestPath_ResolvesShortestTraversalAndHandlesCycles()
    {
        var targetId = GhoztId.Create(IdKind.Target, 901);
        var nodes = new List<LatticeNode>
        {
            new() { Id = "node:root", TargetId = targetId, Label = "System Root", Kind = NodeKind.System, State = "Observed", Provenance = "Declared" },
            new() { Id = "node:auth", TargetId = targetId, Label = "Auth Module", Kind = NodeKind.Feature, State = "Verified", Provenance = "Observed" },
            new() { Id = "node:login", TargetId = targetId, Label = "Login Endpoint", Kind = NodeKind.Endpoint, State = "Observed", Provenance = "Observed" },
            new() { Id = "node:fault", TargetId = targetId, Label = "SQL Row Leak", Kind = NodeKind.Action, State = "Fault", Provenance = "Observed" },
            new() { Id = "node:isolated", TargetId = targetId, Label = "Isolated Node", Kind = NodeKind.Feature, State = "Untested", Provenance = "Declared" },
        };

        var edges = new List<LatticeEdge>
        {
            new() { TargetId = targetId, FromId = "node:root", ToId = "node:auth", Relationship = "contains" },
            new() { TargetId = targetId, FromId = "node:auth", ToId = "node:login", Relationship = "exposes" },
            new() { TargetId = targetId, FromId = "node:login", ToId = "node:fault", Relationship = "triggers" },
            // Cycle back to auth
            new() { TargetId = targetId, FromId = "node:login", ToId = "node:auth", Relationship = "loops" },
        };

        // 1. Direct / Multi-hop path resolution
        var path = StateSpaceMath.FindShortestPath(nodes, edges, "node:root", "node:fault");
        Assert.Equal(4, path.Count);
        Assert.Equal(["node:root", "node:auth", "node:login", "node:fault"], path);

        // 2. Self path
        var selfPath = StateSpaceMath.FindShortestPath(nodes, edges, "node:auth", "node:auth");
        Assert.Equal(["node:auth"], selfPath);

        // 3. Unreachable node
        var unreachable = StateSpaceMath.FindShortestPath(nodes, edges, "node:root", "node:isolated");
        Assert.Empty(unreachable);

        // 4. Undirected fallback (reverse traversal)
        var reversePath = StateSpaceMath.FindShortestPath(nodes, edges, "node:fault", "node:root");
        Assert.Equal(4, reversePath.Count);
        Assert.Equal("node:fault", reversePath[0]);
        Assert.Equal("node:root", reversePath[3]);
    }

    [Fact]
    public void Evidence_TamperAssay_FlagsDiscrepanciesAndBatchVerifies()
    {
        var targetId = GhoztId.Create(IdKind.Target, 910);
        var originalPayload = "{\"session_token\":\"valid_cryptographic_payload\",\"status\":\"ok\"}";
        var originalSha = Hashing.Sha256(originalPayload);

        var validArtefact = new Evidence
        {
            Id = GhoztId.Create(IdKind.Evidence, 911),
            TargetId = targetId,
            Kind = "application/json",
            Summary = "Valid Authenticated Session",
            Payload = originalPayload,
            Sha256 = originalSha,
        };

        var tamperedArtefact = new Evidence
        {
            Id = GhoztId.Create(IdKind.Evidence, 912),
            TargetId = targetId,
            Kind = "application/json",
            Summary = "Tampered Host Log",
            Payload = "{\"modified_by_attacker\":\"unauthorized_state\"}",
            Sha256 = originalSha, // Stale recorded hash does not match modified payload!
        };

        // Recompute hashes
        var validRecomputed = Hashing.Sha256(validArtefact.Payload);
        var tamperedRecomputed = Hashing.Sha256(tamperedArtefact.Payload);

        Assert.Equal(validArtefact.Sha256, validRecomputed);
        Assert.NotEqual(tamperedArtefact.Sha256, tamperedRecomputed);

        // Verify batch tamper detection logic
        var artefacts = new List<Evidence> { validArtefact, tamperedArtefact };
        var verifiedIds = new HashSet<GhoztId>();
        var tamperedIds = new HashSet<GhoztId>();

        foreach (var ev in artefacts)
        {
            var hash = Hashing.Sha256(ev.Payload);
            if (string.Equals(hash, ev.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                verifiedIds.Add(ev.Id);
            }
            else
            {
                tamperedIds.Add(ev.Id);
            }
        }

        Assert.Single(verifiedIds);
        Assert.Contains(validArtefact.Id, verifiedIds);
        Assert.Single(tamperedIds);
        Assert.Contains(tamperedArtefact.Id, tamperedIds);
    }
}

