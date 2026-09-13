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
}

/// <summary>
/// Compiles technical, executive, and machine-readable forensic audit reports.
/// </summary>
public sealed class ReportGenerator
{
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

        var jsonContent = JsonSerializer.Serialize(jsonModel, new JsonSerializerOptions { WriteIndented = true });
        var seal = Hashing.Sha256(jsonContent);

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
        };
    }
}
