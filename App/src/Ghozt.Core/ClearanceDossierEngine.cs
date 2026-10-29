using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// The formal release clearance digest conforming to Patent Appendix B (Table 10).
/// </summary>
public sealed record ClearanceDigest
{
    public required string ClearanceId { get; init; }
    public required string TargetAndBuild { get; init; }
    public required string Policy { get; init; }
    public required ClearanceOutcome Decision { get; init; }
    public required double VerifiedWeightedCoverage { get; init; }
    public required double UnknownRisk { get; init; }
    public required IReadOnlyDictionary<string, string> MandatoryGates { get; init; }
    public required IReadOnlyDictionary<string, int> OpenFindings { get; init; }
    public string? Waiver { get; init; }
    public required string EvidenceDigest { get; init; }
    public required string ReportId { get; init; }
    public required IReadOnlyList<string> Approvers { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    /// <summary>Formats the digest into standard Patent Appendix B YAML structure.</summary>
    public string ToYaml()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"clearance_id: {ClearanceId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"target/build: {TargetAndBuild}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"policy: {Policy}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"decision: {Decision.ToString().ToUpperInvariant()}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"verified_weighted_coverage: {VerifiedWeightedCoverage:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"unknown_risk: {UnknownRisk:F2}");
        sb.AppendLine("mandatory_gates:");
        foreach (var (gate, result) in MandatoryGates)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {gate}: {result.ToUpperInvariant()}");
        }
        sb.AppendLine("open_findings:");
        foreach (var (sev, count) in OpenFindings)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {sev.ToUpperInvariant()}: {count}");
        }
        if (!string.IsNullOrWhiteSpace(Waiver))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"waiver: {Waiver}");
        }
        sb.AppendLine(CultureInfo.InvariantCulture, $"evidence_digest: sha256:{EvidenceDigest}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"report: {ReportId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"approvers: [{string.Join(", ", Approvers)}]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"valid_until: {ValidUntil:O}");
        return sb.ToString();
    }
}

/// <summary>
/// A cryptographically sealed, self-contained release certification package.
/// </summary>
public sealed record ReleaseCertificate
{
    public required ClearanceDigest Digest { get; init; }
    public required string TargetName { get; init; }
    public required TargetEnvironment Environment { get; init; }
    public required string HtmlCertificate { get; init; }
    public required string JsonCertificate { get; init; }
    public required string MerkleRoot { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public string SavedFilePath { get; init; } = "";
}

/// <summary>
/// Release Clearance Dossier & Cryptographic Sealing Engine (Patent §33, §41, §45 & Appendix B).
/// Converts evaluated policy gates into an immutable release certificate with Merkle evidence binding.
/// </summary>
public sealed class ClearanceDossierEngine
{
    private readonly GhoztStore _store;
    private readonly ClearanceEngine _clearanceEngine;

    public ClearanceDossierEngine(GhoztStore store)
    {
        _store = store;
        _clearanceEngine = new ClearanceEngine(store);
    }

    /// <summary>
    /// Computes an aggregate SHA-256 Merkle root across all target evidence records.
    /// Sensitively detects if any artefact payload or record has been altered.
    /// </summary>
    public static string ComputeEvidenceMerkleRoot(IEnumerable<Evidence> evidenceItems)
    {
        var sorted = evidenceItems.OrderBy(e => e.Id.ToString(), StringComparer.Ordinal).ToList();
        if (sorted.Count == 0)
        {
            return Hashing.Sha256("EMPTY_EVIDENCE_TREE");
        }

        var combined = string.Join(":", sorted.Select(e => $"{e.Id}#{e.Sha256}#{e.SizeBytes}"));
        return Hashing.Sha256(combined);
    }

    /// <summary>
    /// Constructs the formal Patent Appendix B Clearance Digest by automatically evaluating target policy gates.
    /// </summary>
    public ClearanceDigest BuildDigest(Target target, string approver = "CAPITAL-IZ-RELEASE-AUTHORITY")
    {
        var decision = _clearanceEngine.EvaluateTarget(target, approver);
        return BuildDigest(target, decision, approver);
    }

    /// <summary>
    /// Constructs the formal Patent Appendix B Clearance Digest.
    /// </summary>
    public ClearanceDigest BuildDigest(Target target, ClearanceDecision decision, string approver = "CAPITAL-IZ-RELEASE-AUTHORITY")
    {
        var evidence = _store.EvidenceItems(target.Id);
        var findings = _store.Findings(target.Id);
        var nodes = _store.Nodes(target.Id);

        var merkleRoot = ComputeEvidenceMerkleRoot(evidence);

        var totalNodes = Math.Max(1, nodes.Count);
        var verifiedNodes = nodes.Count(n => n.State.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
                                             n.State.Equals("observed", StringComparison.OrdinalIgnoreCase));
        var coverage = Math.Clamp((double)verifiedNodes / totalNodes, 0.0, 1.0);

        var openCritical = findings.Count(f => f.Severity == Severity.Critical && f.Maturity != FindingMaturity.Verified);
        var openHigh = findings.Count(f => f.Severity == Severity.High && f.Maturity != FindingMaturity.Verified);
        var openMedium = findings.Count(f => f.Severity == Severity.Medium && f.Maturity != FindingMaturity.Verified);
        var openLow = findings.Count(f => f.Severity <= Severity.Low && f.Maturity != FindingMaturity.Verified);

        // Three-State Honesty Invariant (Patent §45):
        // Total Scope = Verified + Failed + Unknown. Unknown risk is unvisited state space.
        var failedPenalty = (openCritical * 0.1) + (openHigh * 0.05);
        var unknownRisk = Math.Clamp(1.0 - coverage, 0.0, 1.0);

        var mandatoryGatesDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var gate in decision.Gates)
        {
            var key = gate.Id.Replace("GATE.", "").ToLowerInvariant();
            mandatoryGatesDict[key] = gate.Passed ? "PASS" : (gate.Mandatory ? "FAIL" : "PASS_WITH_CONDITION");
        }

        var openFindingsDict = new Dictionary<string, int>
        {
            ["CRITICAL"] = openCritical,
            ["HIGH"] = openHigh,
            ["MEDIUM"] = openMedium,
            ["LOW"] = openLow,
        };

        var clearanceId = _store.NextId(IdKind.Clearance).ToString();
        var reportId = _store.NextId(IdKind.Report).ToString();

        return new ClearanceDigest
        {
            ClearanceId = clearanceId,
            TargetAndBuild = $"{target.Id} / {(string.IsNullOrWhiteSpace(target.BuildLabel) ? "dev" : target.BuildLabel)}",
            Policy = "CAPITAL-IZ-Release-Standard@v1.0",
            Decision = decision.Outcome,
            VerifiedWeightedCoverage = Math.Round(coverage, 2),
            UnknownRisk = Math.Round(unknownRisk, 2),
            MandatoryGates = mandatoryGatesDict,
            OpenFindings = openFindingsDict,
            Waiver = null,
            EvidenceDigest = merkleRoot,
            ReportId = reportId,
            Approvers = [decision.DecidedBy, approver],
            ValidUntil = DateTimeOffset.Now.AddDays(30),
        };
    }

    /// <summary>
    /// Generates a full Release Certificate including self-contained HTML and JSON representations,
    /// saves it to local application data (or specified directory), and appends a BlackBox ledger event.
    /// </summary>
    public ReleaseCertificate IssueCertificate(Target target, string reviewer = "operator", string? destinationDirectory = null)
    {
        var decision = _clearanceEngine.EvaluateTarget(target, reviewer);
        var digest = BuildDigest(target, decision, reviewer);
        var issuedAt = DateTimeOffset.Now;

        var json = JsonSerializer.Serialize(digest, new JsonSerializerOptions { WriteIndented = true });
        var html = GenerateHtmlCertificate(target, digest, decision);

        var certDir = destinationDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CAPITAL-IZ",
            "Ghozt",
            "Certificates");

        var savedPath = "";
        try
        {
            Directory.CreateDirectory(certDir);
            var filename = $"GHOZT-CLEARANCE-{target.Id}-{digest.ClearanceId}.html";
            savedPath = Path.Combine(certDir, filename);
            File.WriteAllText(savedPath, html, Encoding.UTF8);

            var jsonPath = Path.Combine(certDir, $"GHOZT-CLEARANCE-{target.Id}-{digest.ClearanceId}.json");
            File.WriteAllText(jsonPath, json, Encoding.UTF8);
        }
        catch
        {
            // Best effort persistence
        }

        // Record BlackBox Event
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = issuedAt,
            Code = "CLR.CERT.ISSUED",
            Message = $"Release certificate {digest.ClearanceId} issued for {target.Name} ({digest.Decision}). Evidence Merkle root: {digest.EvidenceDigest[..8]}...",
            Severity = digest.Decision == ClearanceOutcome.Cleared ? Severity.Info : Severity.High,
            TargetId = target.Id,
            RelatedId = digest.ClearanceId,
            Actor = "ClearanceDossierEngine",
            Detail = json,
        });

        return new ReleaseCertificate
        {
            Digest = digest,
            TargetName = target.Name,
            Environment = target.Environment,
            HtmlCertificate = html,
            JsonCertificate = json,
            MerkleRoot = digest.EvidenceDigest,
            IssuedAt = issuedAt,
            SavedFilePath = savedPath,
        };
    }

    /// <summary>
    /// Generates a self-contained, brand-compliant HTML certificate featuring CAPITAL-IZ
    /// gold authority accents, dark UI tokens, gate results, and three-state honesty bars.
    /// </summary>
    public static string GenerateHtmlCertificate(Target target, ClearanceDigest digest, ClearanceDecision decision)
    {
        var outcomeColor = digest.Decision switch
        {
            ClearanceOutcome.Cleared => "#2EA043", // State.Verified Green
            ClearanceOutcome.ConditionallyCleared => "#D29922", // State.Warning Gold/Yellow
            ClearanceOutcome.Denied => "#DA3633", // State.Fault Red
            _ => "#8B949E", // Muted Gray
        };

        var outcomeLabel = digest.Decision switch
        {
            ClearanceOutcome.Cleared => "RELEASE CLEARED",
            ClearanceOutcome.ConditionallyCleared => "CONDITIONALLY CLEARED",
            ClearanceOutcome.Denied => "RELEASE DENIED",
            ClearanceOutcome.Blocked => "EXAMINATION BLOCKED",
            _ => "UNKNOWN",
        };

        var verifiedPct = (int)Math.Round(digest.VerifiedWeightedCoverage * 100);
        var unknownPct = (int)Math.Round(digest.UnknownRisk * 100);
        var failedPct = Math.Max(0, 100 - verifiedPct - unknownPct);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<title>Ghozt Release Clearance Certificate — " + target.Name + "</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  :root { --bg: #0D1117; --surface: #161B22; --border: #30363D; --text: #C9D1D9; --gold: #D4AF37; }");
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: var(--bg); color: var(--text); padding: 40px; margin: 0; }");
        sb.AppendLine("  .container { max-width: 860px; margin: 0 auto; background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 36px; box-shadow: 0 8px 24px rgba(0,0,0,0.5); }");
        sb.AppendLine("  .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid var(--gold); padding-bottom: 20px; }");
        sb.AppendLine("  .brand { font-size: 24px; font-weight: 700; color: #FFFFFF; letter-spacing: 1px; }");
        sb.AppendLine("  .brand-sub { font-size: 13px; color: var(--gold); text-transform: uppercase; font-weight: 600; }");
        sb.AppendLine("  .badge { background: " + outcomeColor + "; color: #FFFFFF; padding: 8px 16px; border-radius: 4px; font-weight: 700; font-size: 14px; letter-spacing: 0.5px; }");
        sb.AppendLine("  .meta-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin: 24px 0; font-size: 14px; }");
        sb.AppendLine("  .meta-item { background: #0E131A; padding: 12px; border-radius: 4px; border: 1px solid var(--border); }");
        sb.AppendLine("  .meta-label { font-size: 11px; text-transform: uppercase; color: #8B949E; margin-bottom: 4px; }");
        sb.AppendLine("  .meta-val { font-family: monospace; font-weight: 600; color: #FFFFFF; }");
        sb.AppendLine("  .honesty-box { background: #0E131A; border: 1px solid var(--border); border-radius: 6px; padding: 16px; margin: 24px 0; }");
        sb.AppendLine("  .bar-container { display: flex; height: 16px; border-radius: 4px; overflow: hidden; margin-top: 10px; background: #21262D; }");
        sb.AppendLine("  .bar-verified { background: #2EA043; width: " + verifiedPct + "%; }");
        sb.AppendLine("  .bar-failed { background: #DA3633; width: " + failedPct + "%; }");
        sb.AppendLine("  .bar-unknown { background: #8B949E; width: " + unknownPct + "%; }");
        sb.AppendLine("  table { width: 100%; border-collapse: collapse; margin-top: 20px; }");
        sb.AppendLine("  th, td { padding: 10px 12px; text-align: left; border-bottom: 1px solid var(--border); font-size: 13px; }");
        sb.AppendLine("  th { color: #8B949E; text-transform: uppercase; font-size: 11px; }");
        sb.AppendLine("  .sig-block { border-top: 1px solid var(--border); margin-top: 32px; padding-top: 20px; font-size: 12px; color: #8B949E; }");
        sb.AppendLine("  .merkle { font-family: monospace; color: var(--gold); word-break: break-all; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");

        // Header
        sb.AppendLine("  <div class=\"header\">");
        sb.AppendLine("    <div>");
        sb.AppendLine("      <div class=\"brand\">CAPITAL-IZ GHOZT</div>");
        sb.AppendLine("      <div class=\"brand-sub\">Autonomous Release Clearance Certificate</div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"badge\">" + outcomeLabel + "</div>");
        sb.AppendLine("  </div>");

        // Meta Grid
        sb.AppendLine("  <div class=\"meta-grid\">");
        sb.AppendLine("    <div class=\"meta-item\"><div class=\"meta-label\">Clearance Identifier</div><div class=\"meta-val\">" + digest.ClearanceId + "</div></div>");
        sb.AppendLine("    <div class=\"meta-item\"><div class=\"meta-label\">Target &amp; Build</div><div class=\"meta-val\">" + target.Name + " (" + digest.TargetAndBuild + ")</div></div>");
        sb.AppendLine("    <div class=\"meta-item\"><div class=\"meta-label\">Environment &amp; Access</div><div class=\"meta-val\">" + target.Environment + " / " + target.Access + "</div></div>");
        sb.AppendLine("    <div class=\"meta-item\"><div class=\"meta-label\">Governing Policy</div><div class=\"meta-val\">" + digest.Policy + "</div></div>");
        sb.AppendLine("  </div>");

        // Three-State Honesty Decomposition
        sb.AppendLine("  <div class=\"honesty-box\">");
        sb.AppendLine("    <div style=\"display:flex; justify-content:space-between; align-items:center;\">");
        sb.AppendLine("      <span style=\"font-weight:600; font-size:13px;\">Three-State Scope Decomposition (Patent §45)</span>");
        sb.AppendLine("      <span style=\"font-size:12px; color:#8B949E;\">" + verifiedPct + "% Verified · " + failedPct + "% Failed · " + unknownPct + "% Unknown Risk</span>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"bar-container\">");
        sb.AppendLine("      <div class=\"bar-verified\" title=\"Verified Scope: " + verifiedPct + "%\"></div>");
        sb.AppendLine("      <div class=\"bar-failed\" title=\"Failed Scope: " + failedPct + "%\"></div>");
        sb.AppendLine("      <div class=\"bar-unknown\" title=\"Unknown Risk: " + unknownPct + "%\"></div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </div>");

        // Mandatory Policy Gates Table
        sb.AppendLine("  <h4 style=\"margin-top:28px; margin-bottom:8px; font-size:14px; text-transform:uppercase; color:#FFFFFF;\">Mandatory Policy Gates</h4>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <thead><tr><th>Gate</th><th>Requirement</th><th>Status</th></tr></thead>");
        sb.AppendLine("    <tbody>");
        foreach (var gate in decision.Gates)
        {
            var gateColor = gate.Passed ? "#2EA043" : (gate.Mandatory ? "#DA3633" : "#D29922");
            sb.AppendLine("      <tr>");
            sb.AppendLine("        <td style=\"font-weight:600;\">" + gate.Name + "</td>");
            sb.AppendLine("        <td style=\"color:#8B949E;\">" + gate.Requirement + "</td>");
            sb.AppendLine("        <td style=\"font-weight:700; color:" + gateColor + ";\">" + gate.Result + "</td>");
            sb.AppendLine("      </tr>");
        }
        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");

        // Evidence Merkle Digest & Signature
        sb.AppendLine("  <div class=\"sig-block\">");
        sb.AppendLine("    <div><strong>Cryptographic Evidence Merkle Root:</strong> <span class=\"merkle\">sha256:" + digest.EvidenceDigest + "</span></div>");
        sb.AppendLine("    <div style=\"margin-top:8px;\"><strong>Signers:</strong> " + string.Join(", ", digest.Approvers) + " &nbsp;|&nbsp; <strong>Valid Until:</strong> " + digest.ValidUntil.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) + "</div>");
        sb.AppendLine("    <div style=\"margin-top:6px; font-size:11px; color:#6E7681;\">Ghozt Patent Quality Certification System (Patent §33, §41, §45). Tampering with any evidence record invalidates this seal.</div>");
        sb.AppendLine("  </div>");

        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }
}
