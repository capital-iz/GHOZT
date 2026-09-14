using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// A compiled forensic audit report with cryptographic provenance and evidence seals.
/// </summary>
public sealed record ForensicReport
{
    public required string ReportId { get; init; }
    public required GhoztId TargetId { get; init; }
    public required string TargetName { get; init; }
    public required string BuildLabel { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string GeneratedBy { get; init; }
    public required int TotalSweeps { get; init; }
    public required int TotalStatesExplored { get; init; }
    public required int TotalDeviations { get; init; }
    public required int TotalEvidenceArtefacts { get; init; }
    public required ClearanceOutcome ClearanceVerdict { get; init; }
    public required string Sha256Seal { get; init; }
    public required string MarkdownContent { get; init; }
    public required string HtmlContent { get; init; }
    public required string JsonContent { get; init; }
    public string SarifContent { get; init; } = "";
    public string JUnitXmlContent { get; init; } = "";
}

/// <summary>
/// Compiles technical, executive, and machine-readable forensic audit reports.
/// </summary>
public sealed class ReportGenerator
{
    private static readonly JsonSerializerOptions s_indentedJson = new() { WriteIndented = true };

    private readonly GhoztStore _store;

    public ReportGenerator(GhoztStore store) => _store = store;

    /// <summary>
    /// Generates a comprehensive forensic audit report for a registered target.
    /// </summary>
    public ForensicReport Generate(Target target, string author = "operator")
    {
        var sweeps = _store.Sweeps(target.Id);
        var findings = _store.Findings(target.Id);
        var evidence = _store.EvidenceItems(target.Id);
        var nodes = _store.Nodes(target.Id);
        var clearanceEngine = new ClearanceEngine(_store);
        var decision = clearanceEngine.EvaluateTarget(target, author);

        var totalStates = sweeps.Sum(s => s.StatesVisited);
        var reportId = $"RPT-{DateTimeOffset.UtcNow:yyyyMMdd}-{target.Id}";

        // Build Markdown
        var md = new StringBuilder();
        md.AppendLine($"# Forensic Examination Dossier: {target.Name}");
        md.AppendLine();
        md.AppendLine($"**Report ID:** `{reportId}`  ");
        md.AppendLine($"**Target ID:** `{target.Id}` | **Build:** `{target.BuildLabel}` | **Environment:** `{target.Environment}`  ");
        md.AppendLine($"**Generated:** {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} by `{author}`  ");
        md.AppendLine($"**Clearance Verdict:** `{decision.Outcome}`  ");
        md.AppendLine();
        md.AppendLine("---");
        md.AppendLine();
        md.AppendLine("## 1. Executive Summary & Verification Metrics");
        md.AppendLine();
        md.AppendLine($"- **Total Examination Sweeps:** {sweeps.Count}");
        md.AppendLine($"- **Total States Visited:** {totalStates}");
        md.AppendLine($"- **Total Deviations / Findings:** {findings.Count}");
        md.AppendLine($"- **Cryptographically Sealed Evidence Artefacts:** {evidence.Count}");
        md.AppendLine($"- **Discovered Lattice Nodes:** {nodes.Count}");
        md.AppendLine();
        md.AppendLine("## 2. Release Policy Clearance Gates");
        md.AppendLine();
        md.AppendLine("| Gate ID | Gate Name | Mandatory | Result | Evidence / Note |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var gate in decision.Gates)
        {
            var statusGlyph = gate.Passed ? "PASS" : (gate.Mandatory ? "FAIL" : "WARN");
            md.AppendLine($"| `{gate.Id}` | {gate.Name} | {(gate.Mandatory ? "Yes" : "No")} | **{statusGlyph}** | {gate.EvidenceNote} |");
        }
        md.AppendLine();
        md.AppendLine("## 3. Deviations & Faults Catalog");
        md.AppendLine();
        if (findings.Count == 0)
        {
            md.AppendLine("Zero deviations recorded. Target satisfies all examined oracles.");
        }
        else
        {
            md.AppendLine("| ID | Title | Severity | Reproducibility | 95% Wilson Bounds | Phantom? |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var f in findings)
            {
                var repro = f.Reproducibility is { } r ? $"{r:P0}" : "Unattempted";
                var wilson = f.WilsonConfidence;
                var bounds = $"[{wilson.Lower:P0}, {wilson.Upper:P0}]";
                var phantom = f.IsPhantom ? "Yes (Phantom)" : "No";
                md.AppendLine($"| `{f.Id}` | {f.Title} | **{f.Severity}** | {repro} | {bounds} | {phantom} |");
            }
        }
        md.AppendLine();
        md.AppendLine("---");
        md.AppendLine($"*Cryptographically Sealed Evidence Digest: `{decision.EvidenceDigest}`*");

        var mdContent = md.ToString();

        // Build HTML
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\"/>");
        html.AppendLine($"<title>Ghozt Forensic Report — {target.Name}</title>");
        html.AppendLine("<style>");
        html.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0b1118; color: #d8e2ea; padding: 32px; line-height: 1.5; }");
        html.AppendLine("h1, h2 { color: #f2f7fa; margin-top: 24px; }");
        html.AppendLine(".badge { display: inline-block; padding: 4px 10px; border-radius: 4px; font-weight: bold; font-size: 12px; }");
        html.AppendLine(".badge-cleared { background: rgba(30, 180, 100, 0.2); color: #32d74b; border: 1px solid #32d74b; }");
        html.AppendLine(".badge-denied { background: rgba(220, 50, 50, 0.2); color: #ff453a; border: 1px solid #ff453a; }");
        html.AppendLine(".card { background: #121c26; border: 1px solid #233444; border-radius: 8px; padding: 20px; margin-bottom: 20px; }");
        html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 12px; }");
        html.AppendLine("th, td { border-bottom: 1px solid #1e2e3d; padding: 10px; text-align: left; font-size: 13px; }");
        html.AppendLine("th { color: #7f9bb3; font-weight: 600; text-transform: uppercase; font-size: 11px; }");
        html.AppendLine(".mono { font-family: monospace; }");
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine($"<h1>Forensic Examination Dossier</h1>");
        html.AppendLine($"<div class=\"card\">");
        html.AppendLine($"<h2>{target.Name} &nbsp;<span class=\"badge {(decision.Outcome == ClearanceOutcome.Cleared ? "badge-cleared" : "badge-denied")}\">{decision.Outcome}</span></h2>");
        html.AppendLine($"<p><strong>Target ID:</strong> <span class=\"mono\">{target.Id}</span> &nbsp;|&nbsp; <strong>Build:</strong> {target.BuildLabel} &nbsp;|&nbsp; <strong>Environment:</strong> {target.Environment}</p>");
        html.AppendLine($"<p><strong>Report ID:</strong> <span class=\"mono\">{reportId}</span> &nbsp;|&nbsp; <strong>Generated:</strong> {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}</p>");
        html.AppendLine($"</div>");

        html.AppendLine($"<div class=\"card\">");
        html.AppendLine($"<h2>Release Policy Clearance Gates</h2>");
        html.AppendLine("<table>");
        html.AppendLine("<tr><th>Gate</th><th>Requirement</th><th>Status</th><th>Note</th></tr>");
        foreach (var gate in decision.Gates)
        {
            html.AppendLine($"<tr><td><strong>{gate.Name}</strong></td><td>{gate.Requirement}</td><td>{(gate.Passed ? "<span style='color:#32d74b'>PASS</span>" : "<span style='color:#ff453a'>FAIL</span>")}</td><td>{gate.EvidenceNote}</td></tr>");
        }
        html.AppendLine("</table>");
        html.AppendLine("</div>");
        html.AppendLine("</body></html>");

        var htmlContent = html.ToString();

        // Build JSON payload
        var jsonModel = new
        {
            ReportId = reportId,
            TargetId = target.Id.ToString(),
            TargetName = target.Name,
            BuildLabel = target.BuildLabel,
            GeneratedAt = DateTimeOffset.Now,
            Author = author,
            Outcome = decision.Outcome.ToString(),
            EvidenceDigest = decision.EvidenceDigest,
            TotalSweeps = sweeps.Count,
            TotalStatesVisited = totalStates,
            TotalFindings = findings.Count,
            TotalEvidence = evidence.Count,
            Gates = decision.Gates,
            Findings = findings.Select(f => new
            {
                f.Id,
                f.Title,
                Severity = f.Severity.ToString(),
                f.Reproducibility,
                f.IsPhantom,
                f.Actual,
                f.Expected,
            })
        };

        var jsonContent = JsonSerializer.Serialize(jsonModel, s_indentedJson);
        var seal = Hashing.Sha256(jsonContent);

        var sarifContent = BuildSarif(target, findings);
        var junitContent = BuildJUnitXml(target, sweeps, findings);

        return new ForensicReport
        {
            ReportId = reportId,
            TargetId = target.Id,
            TargetName = target.Name,
            BuildLabel = target.BuildLabel,
            GeneratedAt = DateTimeOffset.Now,
            GeneratedBy = author,
            TotalSweeps = sweeps.Count,
            TotalStatesExplored = totalStates,
            TotalDeviations = findings.Count,
            TotalEvidenceArtefacts = evidence.Count,
            ClearanceVerdict = decision.Outcome,
            Sha256Seal = seal,
            MarkdownContent = mdContent,
            HtmlContent = htmlContent,
            JsonContent = jsonContent,
            SarifContent = sarifContent,
            JUnitXmlContent = junitContent,
        };
    }

    /// <summary>
    /// Generates an OASIS SARIF v2.1.0 compliant report for CI/CD and security scanners (Patent §32).
    /// </summary>
    public static string BuildSarif(Target target, IReadOnlyList<Finding> findings)
    {
        var rules = findings.GroupBy(f => f.Fingerprint.Length >= 8 ? f.Fingerprint[..8] : f.Id.ToString())
            .Select(g => new
            {
                id = "GHOZT-" + g.Key,
                name = g.First().Title,
                shortDescription = new { text = g.First().Title },
                fullDescription = new { text = $"Expected: {g.First().Expected}. Observed: {g.First().Actual}" },
                defaultConfiguration = new { level = g.First().Severity >= Severity.High ? "error" : "warning" }
            }).ToList();

        var results = findings.Select(f => new
        {
            ruleId = "GHOZT-" + (f.Fingerprint.Length >= 8 ? f.Fingerprint[..8] : f.Id.ToString()),
            level = f.Severity == Severity.Critical ? "error" : f.Severity == Severity.High ? "error" : f.Severity == Severity.Medium ? "warning" : "note",
            message = new { text = $"{f.Title} | Observed: {f.Actual} | Expected: {f.Expected}" },
            locations = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = !string.IsNullOrWhiteSpace(target.BaseAddress) ? target.BaseAddress.Replace('\\', '/') : target.Name },
                        region = new { startLine = 1 }
                    }
                }
            },
            properties = new
            {
                findingId = f.Id.ToString(),
                nodeId = f.NodeId,
                reproducibility = f.Reproducibility,
                isPhantom = f.IsPhantom,
                maturity = f.Maturity.ToString()
            }
        }).ToList();

        var sarif = new
        {
            schema = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
            version = "2.1.0",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "Ghozt - Dev Tester",
                            version = "0.2.7",
                            informationUri = "https://github.com/capital-iz/GHOZT",
                            rules
                        }
                    },
                    results
                }
            }
        };

        return JsonSerializer.Serialize(sarif, s_indentedJson);
    }

    /// <summary>
    /// Generates standard JUnit XML format for CI test reporting engines (Jenkins, GitLab CI) (Patent §32).
    /// </summary>
    public static string BuildJUnitXml(Target target, IReadOnlyList<Sweep> sweeps, IReadOnlyList<Finding> findings)
    {
        var sb = new StringBuilder();
        var totalTests = Math.Max(1, sweeps.Sum(s => s.StatesVisited));
        var failures = findings.Count;
        var totalSeconds = sweeps.Sum(s => s.FinishedAt is { } fin && s.StartedAt is { } start && fin > start ? (fin - start).TotalSeconds : 0.0);

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<testsuites name=\"Ghozt Forensic Audit: {SecurityElement.Escape(target.Name)}\" tests=\"{totalTests}\" failures=\"{failures}\" errors=\"0\" time=\"{totalSeconds:F3}\">");

        foreach (var sweep in sweeps)
        {
            var sweepFindings = findings.Where(f => f.SweepId == sweep.Id).ToList();
            var sweepDuration = sweep.FinishedAt is { } sfin && sweep.StartedAt is { } sstart && sfin > sstart ? (sfin - sstart).TotalSeconds : 0.0;
            sb.AppendLine(CultureInfo.InvariantCulture, $"  <testsuite name=\"Sweep.{SecurityElement.Escape(sweep.Id.ToString())}\" tests=\"{Math.Max(1, sweep.StatesVisited)}\" failures=\"{sweepFindings.Count}\" errors=\"0\" time=\"{sweepDuration:F3}\">");

            if (sweepFindings.Count == 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"    <testcase classname=\"{SecurityElement.Escape(target.Name)}\" name=\"Sweep_{SecurityElement.Escape(sweep.Mode)}\" time=\"{sweepDuration:F3}\" />");
            }
            else
            {
                foreach (var f in sweepFindings)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    <testcase classname=\"{SecurityElement.Escape(target.Name)}\" name=\"{SecurityElement.Escape(f.Title)}\" time=\"0.010\">");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"      <failure message=\"{SecurityElement.Escape(f.Actual)}\" type=\"{f.Severity}\">{SecurityElement.Escape(f.Expected)}</failure>");
                    sb.AppendLine("    </testcase>");
                }
            }

            sb.AppendLine("  </testsuite>");
        }

        sb.AppendLine("</testsuites>");
        return sb.ToString();
    }
}
