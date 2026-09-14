using System.IO;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class TimeAlignmentAndDeterministicReplayTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public TimeAlignmentAndDeterministicReplayTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_replay_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_replay_test.db");
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
    public void TimeAlignmentEngine_AlignTimeline_PreservesCausalParentageAndLamportOrdering()
    {
        var baseTime = DateTimeOffset.UtcNow;

        // Raw events where an unsynchronized/drifting clock caused the child span to appear with an earlier timestamp
        var parentSpan = new RawTimelineEvent
        {
            Id = "SPAN-ROOT",
            Source = EventSourceKind.DistributedTrace,
            LocalTimestamp = baseTime.AddMilliseconds(50),
            TraceId = "TRC-001",
            SpanId = "SPAN-ROOT",
            ParentSpanId = null,
            Description = "Root controller execution",
        };

        var childSpan = new RawTimelineEvent
        {
            Id = "SPAN-CHILD",
            Source = EventSourceKind.DistributedTrace,
            LocalTimestamp = baseTime.AddMilliseconds(10), // Clock inversion
            TraceId = "TRC-001",
            SpanId = "SPAN-CHILD",
            ParentSpanId = "SPAN-ROOT",
            Description = "Database query execution",
        };

        var report = TimeAlignmentEngine.AlignTimeline([parentSpan, childSpan]);

        Assert.Equal(2, report.TotalEvents);
        Assert.True(report.IsStrictlyAcyclic);

        var alignedParent = report.AlignedRecords.First(r => r.EventId == "SPAN-ROOT");
        var alignedChild = report.AlignedRecords.First(r => r.EventId == "SPAN-CHILD");

        // Lamport clock must enforce cause before effect
        Assert.True(alignedChild.LamportLogicalClock > alignedParent.LamportLogicalClock);
        // Sequence index must place parent before child
        Assert.True(alignedParent.SequenceIndex < alignedChild.SequenceIndex);
    }

    [Fact]
    public void TimeAlignmentEngine_RendezvousCalibration_BindsUncertaintyInterval()
    {
        var baseTime = DateTimeOffset.UtcNow;

        var uiClick = new RawTimelineEvent
        {
            Id = "UI-CLICK",
            Source = EventSourceKind.UiAction,
            LocalTimestamp = baseTime,
            CorrelationId = "CORR-LOGIN-99",
            Description = "User clicks submit button",
            EstimatedUncertaintyMs = 2.0,
        };

        // Server reports receiving request 20ms before UI click due to negative clock drift
        var netRecv = new RawTimelineEvent
        {
            Id = "NET-RECV",
            Source = EventSourceKind.Network,
            LocalTimestamp = baseTime.AddMilliseconds(-20),
            CorrelationId = "CORR-LOGIN-99",
            Description = "API gateway receives login payload",
            EstimatedUncertaintyMs = 5.0,
        };

        var report = TimeAlignmentEngine.AlignTimeline([uiClick, netRecv]);

        Assert.Equal(2, report.TotalEvents);
        var alignedUi = report.AlignedRecords.First(r => r.EventId == "UI-CLICK");
        var alignedNet = report.AlignedRecords.First(r => r.EventId == "NET-RECV");

        // UI click must precede network receive in sequence
        Assert.True(alignedUi.SequenceIndex < alignedNet.SequenceIndex);
        Assert.True(alignedNet.UncertaintyMs > 0.0);
        Assert.Contains("±", alignedNet.FormattedTimeInterval);
    }

    [Fact]
    public void DeterministicReplayEngine_GenerateReplayPackage_MatchesPatentTable49Yaml()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Replay Target App",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "BLD-000219",
            Reachable = true,
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Null Identity Claim on Refresh",
            Severity = Severity.High,
            Maturity = FindingMaturity.New,
            Actual = "HTTP 500 error",
            Expected = "HTTP 200 Session",
            Fingerprint = "FP-88A19",
            NodeId = "Auth/Refresh",
        };
        _store.Save(finding);

        var engine = new DeterministicReplayEngine(_store);
        var package = engine.GenerateReplayPackage(target, finding);

        Assert.StartsWith("RPL-", package.ReplayId);
        Assert.Equal("BLD-000219", package.TargetBuild);
        Assert.NotEmpty(package.Checkpoint);
        Assert.Equal("FP-88A19", package.ExpectedFaultFingerprint);
        Assert.True(package.Actions.Count >= 2);
        Assert.Contains("trace", package.EvidencePlan);

        var yaml = package.ToYaml();
        Assert.Contains("replay_id:", yaml);
        Assert.Contains("target_build: BLD-000219", yaml);
        Assert.Contains("checkpoint:", yaml);
        Assert.Contains("expected_fault_fingerprint: FP-88A19", yaml);
        Assert.Contains("evidence_plan:", yaml);

        // BlackBox event verification
        var events = _store.RecentEvents(limit: 10, targetId: target.Id);
        Assert.Contains(events, e => e.Code == "RPL.PACKAGE.CREATED");
    }

    [Fact]
    public void DeterministicReplayEngine_EvaluateAttempt_ClassifiesAllFivePatentTable50Outcomes()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Outcome Evaluation Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Staging,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.0-test",
            Reachable = true,
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Race Condition on Dispatch",
            Severity = Severity.Critical,
            Maturity = FindingMaturity.New,
            Fingerprint = "FP-RACE-001",
        };
        _store.Save(finding);

        var engine = new DeterministicReplayEngine(_store);
        var package = engine.GenerateReplayPackage(target, finding);

        // 1. ExactReproduction: Identical fingerprint, causal match = 1.0
        var exact = DeterministicReplayEngine.EvaluateAttempt(package, "FP-RACE-001", checkpointValid: true, dependenciesMatch: true, causalMatchRatio: 1.0);
        Assert.Equal(ReplayOutcome.ExactReproduction, exact);

        // 2. EquivalentReproduction: Semantic match, causal match = 0.90
        var equiv = DeterministicReplayEngine.EvaluateAttempt(package, "FP-RACE-001-VAR", checkpointValid: true, dependenciesMatch: true, causalMatchRatio: 0.90);
        Assert.Equal(ReplayOutcome.EquivalentReproduction, equiv);

        // 3. PartialReproduction: Partial symptoms, causal match = 0.45
        var partial = DeterministicReplayEngine.EvaluateAttempt(package, "FP-OTHER", checkpointValid: true, dependenciesMatch: true, causalMatchRatio: 0.45);
        Assert.Equal(ReplayOutcome.PartialReproduction, partial);

        // 4. NonReproduction: Defect absent, causal match = 0.1
        var nonRepro = DeterministicReplayEngine.EvaluateAttempt(package, "FP-BENIGN", checkpointValid: true, dependenciesMatch: true, causalMatchRatio: 0.1);
        Assert.Equal(ReplayOutcome.NonReproduction, nonRepro);

        // 5. InvalidReplay: Checkpoint mismatch prevents valid conclusion
        var invalid = DeterministicReplayEngine.EvaluateAttempt(package, "FP-RACE-001", checkpointValid: false, dependenciesMatch: true, causalMatchRatio: 1.0);
        Assert.Equal(ReplayOutcome.InvalidReplay, invalid);
    }

    [Fact]
    public void DeterministicReplayEngine_DeriveReproductionEnvelope_ComputesTable52AndWilsonConfidence()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Envelope Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.0-env",
            Reachable = true,
        };
        _store.Save(target);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            Title = "Intermittent Lockout Defect",
            Severity = Severity.High,
            Maturity = FindingMaturity.New,
            Fingerprint = "FP-LOCK-007",
        };
        _store.Save(finding);

        var attempts = new List<ReplayExecutionRecord>
        {
            new() { AttemptIndex = 1, ExecutedAt = DateTimeOffset.Now, Outcome = ReplayOutcome.ExactReproduction, ObservedFingerprint = "FP-LOCK-007", CausalMatchRatio = 1.0, DurationMs = 110, CheckpointHash = "CHK-1", CheckpointMatched = true },
            new() { AttemptIndex = 2, ExecutedAt = DateTimeOffset.Now, Outcome = ReplayOutcome.ExactReproduction, ObservedFingerprint = "FP-LOCK-007", CausalMatchRatio = 1.0, DurationMs = 120, CheckpointHash = "CHK-1", CheckpointMatched = true },
            new() { AttemptIndex = 3, ExecutedAt = DateTimeOffset.Now, Outcome = ReplayOutcome.EquivalentReproduction, ObservedFingerprint = "FP-LOCK-007-B", CausalMatchRatio = 0.90, DurationMs = 135, CheckpointHash = "CHK-1", CheckpointMatched = true },
            new() { AttemptIndex = 4, ExecutedAt = DateTimeOffset.Now, Outcome = ReplayOutcome.NonReproduction, ObservedFingerprint = "FP-NONE", CausalMatchRatio = 0.0, DurationMs = 90, CheckpointHash = "CHK-1", CheckpointMatched = true },
        };

        var engine = new DeterministicReplayEngine(_store);
        var envelope = engine.DeriveReproductionEnvelope(finding, attempts);

        Assert.Equal(4, envelope.AttemptCount);
        Assert.Equal(3, envelope.ReproductionCount);
        Assert.True(envelope.ReproducibilityScore > 0.5);
        Assert.True(envelope.WilsonConfidence.Lower >= 0.0);
        Assert.True(envelope.WilsonConfidence.Upper <= 1.0);
        Assert.True(envelope.WilsonConfidence.Lower < envelope.WilsonConfidence.Upper);
        Assert.NotEmpty(envelope.RequiredConditions);
        Assert.NotEmpty(envelope.SufficientConditions);

        // Verify BlackBox event emitted
        var events = _store.RecentEvents(limit: 10, targetId: target.Id);
        Assert.Contains(events, e => e.Code == "RPL.ENVELOPE.DERIVED");
    }
}
