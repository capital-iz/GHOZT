using System.IO;
using System.Text.Json;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class ClearanceDossierAndPortfolioTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public ClearanceDossierAndPortfolioTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_clearance_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_clearance_test.db");
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
    public void ClearanceDossierEngine_BuildDigest_MatchesPatentAppendixBSchema()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Ghozt Dev Tester Core",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.0-rc1",
            Reachable = true,
        };
        _store.Save(target);

        // Add verified node
        var node = new LatticeNode
        {
            Id = "node_reports",
            TargetId = target.Id,
            Label = "Shell/Reports",
            Kind = NodeKind.Feature,
            State = "Verified",
            Provenance = "observed",
        };
        _store.Save(node);

        // Add sealed evidence
        var ev = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "Dossier",
            Summary = "Test report evidence",
            Sha256 = Hashing.Sha256(new byte[] { 1, 2, 3, 4 }),
            SizeBytes = 4,
            Payload = "report.json",
            CapturedAt = DateTimeOffset.Now,
        };
        _store.Save(ev);

        var engine = new ClearanceDossierEngine(_store);
        var digest = engine.BuildDigest(target, "Lead Release Architect");

        // Verify Patent Appendix B Table 10 contract fields
        Assert.StartsWith("CLR-", digest.ClearanceId);
        Assert.Contains("v1.0.0-rc1", digest.TargetAndBuild);
        Assert.Equal("CAPITAL-IZ-Release-Standard@v1.0", digest.Policy);
        Assert.True(digest.VerifiedWeightedCoverage >= 0.0);
        Assert.True(digest.UnknownRisk >= 0.0);
        Assert.Equal(1.0, Math.Round(digest.VerifiedWeightedCoverage + digest.UnknownRisk, 2));
        Assert.True(digest.MandatoryGates.Count > 0);
        Assert.NotEmpty(digest.EvidenceDigest);
        Assert.Contains("Lead Release Architect", digest.Approvers);
        Assert.True(digest.ValidUntil > DateTimeOffset.UtcNow);

        // Test YAML output
        var yaml = digest.ToYaml();
        Assert.Contains("clearance_id:", yaml);
        Assert.Contains("verified_weighted_coverage:", yaml);
        Assert.Contains("unknown_risk:", yaml);
    }

    [Fact]
    public void ClearanceDossierEngine_ComputesCompositeEvidenceDigest_DetectsTamperedArtefact()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Tamper Test Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Staging,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.0-tamper-check",
            Reachable = true,
        };
        _store.Save(target);

        var ev1 = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "Artefact",
            Summary = "Evidence 1",
            Sha256 = Hashing.Sha256(new byte[] { 10, 20, 30 }),
            SizeBytes = 3,
            Payload = "evidence_1.bin",
            CapturedAt = DateTimeOffset.Now,
        };
        _store.Save(ev1);

        var engine = new ClearanceDossierEngine(_store);
        var digestOriginal = engine.BuildDigest(target, "Auditor A");

        // Now simulate tampered evidence by adding a second modified item
        var ev2 = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "Artefact",
            Summary = "Evidence 2",
            Sha256 = Hashing.Sha256(new byte[] { 99, 99, 99 }),
            SizeBytes = 3,
            Payload = "evidence_2.bin",
            CapturedAt = DateTimeOffset.Now,
        };
        _store.Save(ev2);

        var digestModified = engine.BuildDigest(target, "Auditor A");

        // The Merkle evidence root must differ, detecting any tamper or alteration
        Assert.NotEqual(digestOriginal.EvidenceDigest, digestModified.EvidenceDigest);
    }

    [Fact]
    public void ClearanceDossierEngine_EnforcesThreeStateHonesty_PreservesUnknownScope()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Honesty Validation Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.0-honesty",
            Reachable = true,
        };
        _store.Save(target);

        // Add 10 nodes, only 2 verified (20% coverage)
        for (int i = 0; i < 10; i++)
        {
            var node = new LatticeNode
            {
                Id = $"node_{i}",
                TargetId = target.Id,
                Label = $"Screen/Element_{i}",
                Kind = NodeKind.Action,
                State = i < 2 ? "Verified" : "Discovered",
                Provenance = "inferred",
            };
            _store.Save(node);
        }

        var engine = new ClearanceDossierEngine(_store);
        var digest = engine.BuildDigest(target, "Auditor B");

        // Three-state honesty: Unknown risk must reflect the 80% unverified state space, NEVER 0
        Assert.Equal(0.2, Math.Round(digest.VerifiedWeightedCoverage, 2));
        Assert.Equal(0.8, Math.Round(digest.UnknownRisk, 2));
        Assert.NotEqual(0.0, digest.UnknownRisk);
    }

    [Fact]
    public void ClearanceDossierEngine_IssueCertificate_GeneratesHtmlAndJsonDossier()
    {
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Certificate Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "v1.0.0-cert",
            Reachable = true,
        };
        _store.Save(target);

        var outDir = Path.Combine(_tempDir, "certs");
        var engine = new ClearanceDossierEngine(_store);

        var cert = engine.IssueCertificate(target, "Rea (Lead Architect)", outDir);

        Assert.True(File.Exists(cert.SavedFilePath));

        var jsonPath = Path.Combine(outDir, $"GHOZT-CLEARANCE-{target.Id}-{cert.Digest.ClearanceId}.json");
        Assert.True(File.Exists(jsonPath));

        var jsonContent = File.ReadAllText(jsonPath);
        Assert.Contains(cert.Digest.ClearanceId, jsonContent);
        Assert.Contains("CAPITAL-IZ-Release-Standard@v1.0", jsonContent);

        var htmlContent = File.ReadAllText(cert.SavedFilePath);
        Assert.Contains("CAPITAL-IZ GHOZT", htmlContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Release Clearance Certificate", htmlContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#D4AF37", htmlContent, StringComparison.OrdinalIgnoreCase); // Gold Authority seal
        Assert.Contains("Three-State Scope Decomposition", htmlContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(cert.Digest.EvidenceDigest[..8], htmlContent, StringComparison.OrdinalIgnoreCase);

        // BlackBox ledger check
        var events = _store.RecentEvents(limit: 50, targetId: target.Id);
        Assert.Contains(events, e => e.Code == "CLR.CERT.ISSUED");
    }

    [Fact]
    public void PortfolioRollupEngine_AggregatesEstateTargets_ComputesQualityMetricsAndGuardHealth()
    {
        // Target 1: Ghozt (Qualified)
        var t1 = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Ghozt Dev Tester",
            Kind = "Desktop",
            Environment = TargetEnvironment.Production,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "1.0.0",
            Reachable = true,
        };
        _store.Save(t1);

        var n1 = new LatticeNode
        {
            Id = "ghozt_shell",
            TargetId = t1.Id,
            Label = "Shell/Main",
            Kind = NodeKind.Feature,
            State = "Verified",
            Provenance = "observed",
        };
        _store.Save(n1);

        // Target 2: CommanDeck (Local)
        var t2 = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "CommanDeck",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "2.3.0",
            Reachable = true,
        };
        _store.Save(t2);

        var rollupEngine = new PortfolioRollupEngine(_store);
        var rollup = rollupEngine.GenerateRollup();

        Assert.Equal(2, rollup.TotalTargets);
        Assert.Equal(1, rollup.ProductionTargets);
        Assert.NotNull(rollup.EstateReadinessIndex);
        Assert.Equal(2, rollup.Targets.Count);

        // Verify BlackBox event emitted
        var allEvents = _store.RecentEvents(limit: 50);
        Assert.Contains(allEvents, e => e.Code == "SYS.PORTFOLIO.ROLLUP");
    }

    [Fact]
    public void PortfolioRollupEngine_DetectsUnclearedProductionTargets_FlagsRisk()
    {
        // Unverified production target with open critical finding
        var prodTarget = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Uncleared Production Core",
            Kind = "Desktop",
            Environment = TargetEnvironment.Production,
            Access = AccessClass.Interact,
            BaseAddress = _tempDir,
            BuildLabel = "0.9.9",
            Reachable = false,
        };
        _store.Save(prodTarget);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = prodTarget.Id,
            SweepId = _store.NextId(IdKind.Sweep),
            Title = "Critical Memory Leak on Startup",
            Severity = Severity.Critical,
            Maturity = FindingMaturity.New,
            FirstSeenAt = DateTimeOffset.Now,
            LastSeenAt = DateTimeOffset.Now,
            ReproAttempts = 1,
            ReproSuccesses = 1,
        };
        _store.Save(finding);

        var rollupEngine = new PortfolioRollupEngine(_store);
        var rollup = rollupEngine.GenerateRollup();

        Assert.Equal("BLOCKED", rollup.EstateReadinessIndex);
        Assert.True(rollup.TotalCriticalFindings >= 1);
        Assert.NotEmpty(rollup.StrategicRiskWarnings);
        Assert.Contains(rollup.StrategicRiskWarnings, w => w.Contains("open critical defect"));
    }
}
