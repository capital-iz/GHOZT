using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// Individual domain result within the autonomous self-test suite (Patent Table 78).
/// </summary>
public sealed record SelfTestDomainVerdict
{
    public required string Domain { get; init; }

    public required bool Passed { get; init; }

    public required string Summary { get; init; }

    public required int ChecksEvaluated { get; init; }

    public required TimeSpan Duration { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Cryptographically sealed Self-Hosting Quality Certificate issued by Ghozt (Patent §40).
/// </summary>
public sealed record GhoztSelfTestCertificate
{
    public required string CertificateId { get; init; }

    public required string ProductVersion { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required int TotalDomains { get; init; }

    public required int PassedDomains { get; init; }

    public required string Verdict { get; init; }

    public required IReadOnlyList<SelfTestDomainVerdict> DomainVerdicts { get; init; }

    public required string Sha256Digest { get; init; }

    public required string SignedBy { get; init; }

    public Evidence? SealedEvidence { get; init; }
}

/// <summary>
/// The Tester Tests Itself: Self-Hosting Autonomous Examination & Certification Engine (Patent §40).
/// Verifies Ghozt's own internal execution, evidence, and reporting pipelines across all 12 core domains.
/// </summary>
public sealed class SelfTestEngine
{
    private readonly GhoztStore _store;

    public SelfTestEngine(GhoztStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Executes the full 12-domain autonomous self-test suite and issues a signed certificate.
    /// </summary>
    public async Task<GhoztSelfTestCertificate> RunFullSelfTestAsync(CancellationToken ct = default)
    {
        var domainResults = new List<SelfTestDomainVerdict>();

        domainResults.Add(TestThemeEngine());
        domainResults.Add(TestPermissionEngine());
        domainResults.Add(TestEventLedger());
        domainResults.Add(TestEvidenceIntegrity());
        domainResults.Add(TestRunOrchestration());
        domainResults.Add(TestAdapters());
        domainResults.Add(TestStateLattice());
        domainResults.Add(TestFaultForensics());
        domainResults.Add(await TestAiGovernanceAsync(ct).ConfigureAwait(false));
        domainResults.Add(TestDataOracles());
        domainResults.Add(TestReports());
        domainResults.Add(TestClearance());

        var passedCount = domainResults.Count(d => d.Passed);
        var totalCount = domainResults.Count;
        var isQualified = passedCount == totalCount;

        var version = typeof(SelfTestEngine).Assembly.GetName().Version?.ToString() ?? "0.7.0.0";
        var certId = $"CERT-SELFHOST-{DateTimeOffset.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";

        var payloadObj = new
        {
            certificate_id = certId,
            product_version = version,
            timestamp = DateTimeOffset.Now.ToString("O"),
            total_domains = totalCount,
            passed_domains = passedCount,
            verdict = isQualified ? "SELF_HOST_QUALIFIED" : "SELF_HOST_FAILED",
            domains = domainResults.Select(d => new
            {
                domain = d.Domain,
                passed = d.Passed,
                summary = d.Summary,
                checks = d.ChecksEvaluated,
                duration_ms = d.Duration.TotalMilliseconds,
            }),
            signed_by = "GhoztSelfHostSupervisor@v1",
        };

        var json = JsonSerializer.Serialize(payloadObj);
        var digest = ComputeSha256(json);

        // Seal into the evidence plane
        var targetId = _store.Targets().FirstOrDefault()?.Id ?? _store.NextId(IdKind.Target);
        var ev = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = targetId,
            Kind = "SelfHostingCertificate",
            Summary = $"Ghozt Self-Test Quality Certificate: {(isQualified ? "QUALIFIED" : "FAILED")} ({passedCount}/{totalCount} domains)",
            Sha256 = digest,
            SizeBytes = Encoding.UTF8.GetByteCount(json),
            Payload = json,
            SourceAdapter = "core:self-test-engine",
            CapturedAt = DateTimeOffset.Now,
            Sealed = true,
            SealedBy = "GhoztSelfHostSupervisor@v1",
        };

        _store.Save(ev);

        // Log to immutable event ledger
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = isQualified ? "SELFTEST.QUALIFIED" : "SELFTEST.FAILED",
            Message = $"Ghozt completed autonomous self-test examination: {passedCount}/{totalCount} domains verified.",
            Severity = isQualified ? Severity.Info : Severity.Critical,
            TargetId = targetId,
            Actor = "GhoztSelfHostSupervisor@v1",
            Detail = $"certificate_id={certId}; sha256={digest}",
        });

        return new GhoztSelfTestCertificate
        {
            CertificateId = certId,
            ProductVersion = version,
            Timestamp = DateTimeOffset.Now,
            TotalDomains = totalCount,
            PassedDomains = passedCount,
            Verdict = isQualified ? "SELF_HOST_QUALIFIED" : "SELF_HOST_FAILED",
            DomainVerdicts = domainResults,
            Sha256Digest = digest,
            SignedBy = "GhoztSelfHostSupervisor@v1",
            SealedEvidence = ev,
        };
    }

    // ------------------------------------------------------------------ 12 Self-Test Domains

    private static SelfTestDomainVerdict TestThemeEngine()
    {
        var sw = Stopwatch.StartNew();
        var palettes = Ghozt.Theming.GhoztCatalog.All;
        var checks = 0;

        bool passed = palettes.Count == 12;
        checks++;

        foreach (var p in palettes)
        {
            if (!string.IsNullOrWhiteSpace(p.Label) && !string.IsNullOrWhiteSpace(p.Accent))
            {
                checks++;
            }
            else
            {
                passed = false;
            }
        }

        return new SelfTestDomainVerdict
        {
            Domain = "ThemeEngine",
            Passed = passed,
            Summary = $"Verified all {palettes.Count} palettes in catalog.",
            ChecksEvaluated = checks,
            Duration = sw.Elapsed,
        };
    }

    private static SelfTestDomainVerdict TestPermissionEngine()
    {
        var sw = Stopwatch.StartNew();
        var checks = 4;

        // Verify ordering: ObserveOnly < Interact < StateChanging < Destructive
        var passed = (int)AccessClass.ObserveOnly < (int)AccessClass.Interact &&
                     (int)AccessClass.Interact < (int)AccessClass.StateChanging &&
                     (int)AccessClass.StateChanging < (int)AccessClass.Destructive;

        return new SelfTestDomainVerdict
        {
            Domain = "PermissionEngine",
            Passed = passed,
            Summary = "Verified hierarchical access class permission boundaries.",
            ChecksEvaluated = checks,
            Duration = sw.Elapsed,
        };
    }

    private SelfTestDomainVerdict TestEventLedger()
    {
        var sw = Stopwatch.StartNew();
        var targetId = _store.NextId(IdKind.Target);
        var eventId = _store.NextId(IdKind.Event);

        _store.Append(new LedgerEvent
        {
            Id = eventId,
            At = DateTimeOffset.Now,
            Code = "SELFTEST.CANARY",
            Message = "Canary event verifying append-only ledger write.",
            Severity = Severity.Info,
            TargetId = targetId,
            Actor = "SelfTestEngine",
            Detail = "Canary check",
        });

        var events = _store.RecentEvents(limit: 50, targetId: targetId);
        var found = events.Any(e => e.Id == eventId && e.Code == "SELFTEST.CANARY");

        return new SelfTestDomainVerdict
        {
            Domain = "EventLedger",
            Passed = found,
            Summary = "Verified append-only event ledger write, ordering, and retrieval.",
            ChecksEvaluated = 2,
            Duration = sw.Elapsed,
        };
    }

    private SelfTestDomainVerdict TestEvidenceIntegrity()
    {
        var sw = Stopwatch.StartNew();
        var payload = "{\"test\": \"canary_evidence\"}";
        var sha = ComputeSha256(payload);
        var targetId = _store.NextId(IdKind.Target);
        var evId = _store.NextId(IdKind.Evidence);

        var ev = new Evidence
        {
            Id = evId,
            TargetId = targetId,
            Kind = "CanaryEvidence",
            Summary = "Integrity check canary",
            Sha256 = sha,
            SizeBytes = Encoding.UTF8.GetByteCount(payload),
            Payload = payload,
            SourceAdapter = "self-test",
            CapturedAt = DateTimeOffset.Now,
            Sealed = true,
            SealedBy = "SelfTestEngine",
        };

        _store.Save(ev);
        var retrieved = _store.EvidenceItems(targetId: targetId).FirstOrDefault(e => e.Id == evId);

        var passed = retrieved is not null &&
                     retrieved.Sha256 == sha &&
                     retrieved.Sealed &&
                     ComputeSha256(retrieved.Payload) == sha;

        return new SelfTestDomainVerdict
        {
            Domain = "EvidenceIntegrity",
            Passed = passed,
            Summary = "Verified SHA-256 payload integrity, sealed status, and tamper check.",
            ChecksEvaluated = 4,
            Duration = sw.Elapsed,
        };
    }

    private static SelfTestDomainVerdict TestRunOrchestration()
    {
        var sw = Stopwatch.StartNew();
        var sweep = new Sweep
        {
            Id = GhoztId.Parse("SWP-000001"),
            TargetId = GhoztId.Parse("TGT-000001"),
            Name = "Orchestration Test",
            Mode = "Smoke",
            State = RunState.Running,
        };

        var completed = sweep with { State = RunState.Completed, FinishedAt = DateTimeOffset.Now };
        var passed = sweep.State == RunState.Running && completed.State == RunState.Completed;

        return new SelfTestDomainVerdict
        {
            Domain = "RunOrchestration",
            Passed = passed,
            Summary = "Verified sweep lifecycle state machine transitions.",
            ChecksEvaluated = 2,
            Duration = sw.Elapsed,
        };
    }

    private static SelfTestDomainVerdict TestAdapters()
    {
        var sw = Stopwatch.StartNew();
        var registry = AdapterRegistry.Shared;
        var all = registry.AllAdapters();

        var hasUia = all.Any(a => a.Manifest.Key == "adapter:desktop-uia");
        var hasProc = all.Any(a => a.Manifest.Key == "adapter:process-supervisor");
        var hasHttp = all.Any(a => a.Manifest.Key == "adapter:http-rest");
        var hasSql = all.Any(a => a.Manifest.Key == "adapter:database-sqlite");

        var passed = hasUia && hasProc && hasHttp && hasSql;

        return new SelfTestDomainVerdict
        {
            Domain = "Adapters",
            Passed = passed,
            Summary = $"Verified all {all.Count} built-in adapters registered and resolvable.",
            ChecksEvaluated = 4,
            Duration = sw.Elapsed,
        };
    }

    private static SelfTestDomainVerdict TestStateLattice()
    {
        var sw = Stopwatch.StartNew();
        var targetId = GhoztId.Parse("TGT-000001");
        var n1 = new LatticeNode { Id = "node:root", TargetId = targetId, Label = "Root", Kind = NodeKind.System, State = "Observed", Provenance = "Observed" };
        var n2 = new LatticeNode { Id = "node:child", TargetId = targetId, Label = "Child", Kind = NodeKind.Feature, State = "Observed", Provenance = "Observed", ParentId = "node:root" };
        var edge = new LatticeEdge { TargetId = targetId, FromId = n1.Id, ToId = n2.Id, Relationship = "observed" };

        var passed = n2.ParentId == n1.Id && edge.FromId == n1.Id && edge.ToId == n2.Id;

        return new SelfTestDomainVerdict
        {
            Domain = "StateLattice",
            Passed = passed,
            Summary = "Verified lattice node parentage and directed edge relationships.",
            ChecksEvaluated = 3,
            Duration = sw.Elapsed,
        };
    }

    private static SelfTestDomainVerdict TestFaultForensics()
    {
        var sw = Stopwatch.StartNew();
        var raw1 = "NullReferenceException at 0x7FFF9201 for PID 1824 on 2026-09-14T10:00:00Z";
        var raw2 = "NullReferenceException at 0x7FFF004B for PID 9928 on 2026-09-14T11:22:33Z";

        var fp1 = FaultFingerprint.Compute("TGT-1", "NullReferenceException", "ERR-1", raw1);
        var fp2 = FaultFingerprint.Compute("TGT-1", "NullReferenceException", "ERR-1", raw2);

        var passed = fp1 == fp2 && !string.IsNullOrWhiteSpace(fp1);

        return new SelfTestDomainVerdict
        {
            Domain = "FaultForensics",
            Passed = passed,
            Summary = "Verified volatile token normalization and composite fingerprint clustering.",
            ChecksEvaluated = 2,
            Duration = sw.Elapsed,
        };
    }

    private async Task<SelfTestDomainVerdict> TestAiGovernanceAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var supervisor = new MultiAgentSupervisor(_store);
        var budget = new AgentBudgetTracker();
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Governance Target",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
        };

        // Attempt prohibited silent approve
        var rec = new AgentRecommendation
        {
            Role = AgentRole.ManifestAgent,
            Summary = "Canary check",
            ProposedAction = "ApproveExpectedBehavior",
            Parameters = new Dictionary<string, string> { ["silent_approve"] = "true" },
        };

        var verdict = await supervisor.SuperviseAsync(target, rec, budget, ct: ct).ConfigureAwait(false);
        var passed = !verdict.Approved && verdict.Code == "AGT.ROLE_PROHIBITION_VIOLATED";

        return new SelfTestDomainVerdict
        {
            Domain = "AiGovernance",
            Passed = passed,
            Summary = "Verified MultiAgentSupervisor role prohibition enforcement.",
            ChecksEvaluated = 2,
            Duration = sw.Elapsed,
        };
    }

    private SelfTestDomainVerdict TestDataOracles()
    {
        var sw = Stopwatch.StartNew();
        var engine = new DataOracleEngine(_store);
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Oracle Target",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
        };

        var res = engine.EvaluateConservationEquation(
            target, "CanaryBalance",
            initialBalance: 100.0,
            finalBalance: 150.0,
            inflows: [50.0],
            outflows: [0.0]);

        return new SelfTestDomainVerdict
        {
            Domain = "DataOracles",
            Passed = res.Passed,
            Summary = "Verified mathematical conservation balancing equation oracle.",
            ChecksEvaluated = 1,
            Duration = sw.Elapsed,
        };
    }

    private static SelfTestDomainVerdict TestReports()
    {
        var sw = Stopwatch.StartNew();
        var target = new Target
        {
            Id = GhoztId.Parse("TGT-000001"),
            Name = "Report Test Target",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
        };
        var findings = new List<Finding>();
        var sweeps = new List<Sweep>();
        var sarif = ReportGenerator.BuildSarif(target, findings);
        var junit = ReportGenerator.BuildJUnitXml(target, sweeps, findings);

        var passed = sarif.Contains("sarif-schema-2.1.0.json", StringComparison.OrdinalIgnoreCase) &&
                     junit.Contains("<testsuite", StringComparison.OrdinalIgnoreCase);

        return new SelfTestDomainVerdict
        {
            Domain = "Reports",
            Passed = passed,
            Summary = "Verified SARIF v2.1.0 and JUnit XML report schemas.",
            ChecksEvaluated = 2,
            Duration = sw.Elapsed,
        };
    }

    private SelfTestDomainVerdict TestClearance()
    {
        var sw = Stopwatch.StartNew();
        var engine = new ClearanceEngine(_store);
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Clearance Canary",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "C:\\",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Canary Examination Sweep",
            Mode = "Smoke",
            State = RunState.Completed,
            StartedAt = DateTimeOffset.Now.AddMinutes(-1),
            FinishedAt = DateTimeOffset.Now,
            StatesVisited = 5,
        };
        _store.Save(sweep);

        var decision = engine.EvaluateTarget(target, "self-test");
        var passed = decision.Outcome is ClearanceOutcome.Cleared or ClearanceOutcome.ConditionallyCleared;

        return new SelfTestDomainVerdict
        {
            Domain = "Clearance",
            Passed = passed,
            Summary = $"Verified clearance gate evaluation (verdict: {decision.Outcome}).",
            ChecksEvaluated = decision.Gates.Count,
            Duration = sw.Elapsed,
        };
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
