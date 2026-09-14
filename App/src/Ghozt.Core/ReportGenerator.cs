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
        var htmlContent = BuildHtml(target, decision, sweeps, findings, evidence, nodes, reportId, author);

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
    /// Gets the running assembly product version or defaults to 0.5.0.
    /// </summary>
    public static string GetProductVersion() =>
        typeof(ReportGenerator).Assembly.GetName().Version?.ToString(3) ?? "0.5.0";

    /// <summary>
    /// Builds a patent-grade, self-contained HTML forensic dossier conforming to Capital-Iz Design System v2.0 (Patent §32).
    /// Zero external CDN or script dependencies: fully air-gapped and offline capable.
    /// </summary>
    public static string BuildHtml(
        Target target,
        ClearanceDecision decision,
        IReadOnlyList<Sweep> sweeps,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<LatticeNode> nodes,
        string reportId,
        string author)
    {
        var isCleared = decision.Outcome == ClearanceOutcome.Cleared;
        var isCond = decision.Outcome == ClearanceOutcome.ConditionallyCleared;
        var isDenied = decision.Outcome == ClearanceOutcome.Denied;
        var verdictClass = isCleared ? "badge-cleared" : isCond ? "badge-warn" : isDenied ? "badge-denied" : "badge-blocked";

        var totalStates = sweeps.Sum(s => s.StatesVisited);
        var passedGates = decision.Gates.Count(g => g.Passed);
        var totalGates = Math.Max(1, decision.Gates.Count);
        var passPercent = (passedGates / (double)totalGates) * 100.0;

        var critCount = findings.Count(f => f.Severity == Severity.Critical);
        var highCount = findings.Count(f => f.Severity == Severity.High);
        var medCount = findings.Count(f => f.Severity == Severity.Medium);
        var lowCount = findings.Count(f => f.Severity == Severity.Low);

        // SVG Donut calculation
        const double circumference = 339.292; // 2 * pi * 54
        var passDash = (passedGates / (double)totalGates) * circumference;
        var remainingDash = circumference - passDash;

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\"/>");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"/>");
        html.AppendLine(CultureInfo.InvariantCulture, $"<title>Ghozt Forensic Audit Dossier — {SecurityElement.Escape(target.Name)}</title>");
        html.AppendLine("<style>");
        html.AppendLine(":root {");
        html.AppendLine("  --bg-deep: #070c12; --bg-card: #0f1722; --bg-card-subtle: #141f2d;");
        html.AppendLine("  --border-subtle: #1c2b3d; --border-card: #22374e;");
        html.AppendLine("  --text-primary: #e6edf3; --text-secondary: #8b9eb5; --text-muted: #576d85;");
        html.AppendLine("  --accent-gold: #d4af37; --accent-gold-soft: rgba(212, 175, 55, 0.15);");
        html.AppendLine("  --state-verified: #32d74b; --state-verified-soft: rgba(50, 215, 75, 0.15);");
        html.AppendLine("  --state-fault: #ff453a; --state-fault-soft: rgba(255, 69, 58, 0.15);");
        html.AppendLine("  --state-warn: #ff9f0a; --state-warn-soft: rgba(255, 159, 10, 0.15);");
        html.AppendLine("  --state-info: #64d2ff; --state-info-soft: rgba(100, 210, 255, 0.15);");
        html.AppendLine("  --mono-font: ui-monospace, SFMono-Regular, 'SF Mono', Menlo, Consolas, monospace;");
        html.AppendLine("}");
        html.AppendLine("* { box-sizing: border-box; margin: 0; padding: 0; }");
        html.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: var(--bg-deep); color: var(--text-primary); padding: 36px 28px; line-height: 1.55; }");
        html.AppendLine(".container { max-width: 1100px; margin: 0 auto; }");
        html.AppendLine("header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 24px; padding-bottom: 20px; border-bottom: 1px solid var(--border-subtle); }");
        html.AppendLine("h1 { font-size: 26px; font-weight: 700; color: #fff; letter-spacing: -0.5px; }");
        html.AppendLine(".header-meta { font-size: 13px; color: var(--text-secondary); margin-top: 6px; font-family: var(--mono-font); }");
        html.AppendLine(".badge { display: inline-block; padding: 6px 14px; border-radius: 6px; font-weight: 700; font-size: 13px; text-transform: uppercase; letter-spacing: 0.5px; }");
        html.AppendLine(".badge-cleared { background: var(--accent-gold-soft); color: var(--accent-gold); border: 1px solid var(--accent-gold); box-shadow: 0 0 16px rgba(212,175,55,0.25); }");
        html.AppendLine(".badge-warn { background: var(--state-warn-soft); color: var(--state-warn); border: 1px solid var(--state-warn); }");
        html.AppendLine(".badge-denied { background: var(--state-fault-soft); color: var(--state-fault); border: 1px solid var(--state-fault); }");
        html.AppendLine(".badge-blocked { background: rgba(120,120,120,0.2); color: #aaa; border: 1px solid #777; }");
        html.AppendLine(".kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 14px; margin-bottom: 28px; }");
        html.AppendLine(".kpi-card { background: var(--bg-card); border: 1px solid var(--border-card); border-radius: 8px; padding: 18px 16px; }");
        html.AppendLine(".kpi-label { font-size: 11px; font-weight: 600; text-transform: uppercase; color: var(--text-muted); letter-spacing: 0.5px; }");
        html.AppendLine(".kpi-value { font-size: 24px; font-weight: 700; margin-top: 6px; color: var(--text-primary); font-family: var(--mono-font); }");
        html.AppendLine(".charts-row { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 28px; }");
        html.AppendLine("@media(max-width: 768px) { .charts-row { grid-template-columns: 1fr; } }");
        html.AppendLine(".chart-card { background: var(--bg-card); border: 1px solid var(--border-card); border-radius: 8px; padding: 20px; display: flex; flex-direction: column; align-items: center; }");
        html.AppendLine(".chart-title { width: 100%; font-size: 13px; font-weight: 600; text-transform: uppercase; color: var(--text-muted); margin-bottom: 12px; }");
        html.AppendLine(".card { background: var(--bg-card); border: 1px solid var(--border-card); border-radius: 8px; padding: 22px; margin-bottom: 24px; }");
        html.AppendLine("h2 { font-size: 17px; font-weight: 600; color: #fff; margin-bottom: 16px; display: flex; align-items: center; justify-content: space-between; }");
        html.AppendLine("table { width: 100%; border-collapse: collapse; font-size: 13px; }");
        html.AppendLine("th, td { padding: 10px 12px; text-align: left; border-bottom: 1px solid var(--border-subtle); vertical-align: middle; }");
        html.AppendLine("th { color: var(--text-muted); font-weight: 600; text-transform: uppercase; font-size: 11px; letter-spacing: 0.5px; background: rgba(0,0,0,0.15); }");
        html.AppendLine("tr:hover td { background: var(--bg-card-subtle); }");
        html.AppendLine(".mono { font-family: var(--mono-font); font-size: 12px; }");
        html.AppendLine(".tag { display: inline-block; padding: 2px 7px; border-radius: 4px; font-size: 10px; font-weight: 700; }");
        html.AppendLine(".tag-pass { background: var(--state-verified-soft); color: var(--state-verified); border: 1px solid var(--state-verified); }");
        html.AppendLine(".tag-fail { background: var(--state-fault-soft); color: var(--state-fault); border: 1px solid var(--state-fault); }");
        html.AppendLine(".tag-warn { background: var(--state-warn-soft); color: var(--state-warn); border: 1px solid var(--state-warn); }");
        html.AppendLine(".sev-crit { color: var(--state-fault); font-weight: 700; }");
        html.AppendLine(".sev-high { color: var(--state-warn); font-weight: 700; }");
        html.AppendLine(".sev-med { color: #ffd60a; }");
        html.AppendLine(".sev-low { color: var(--state-info); }");
        html.AppendLine("footer { text-align: center; margin-top: 36px; padding-top: 20px; border-top: 1px solid var(--border-subtle); font-size: 12px; color: var(--text-muted); }");
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("<div class=\"container\">");

        // Header
        html.AppendLine("<header>");
        html.AppendLine("  <div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"    <h1>Forensic Examination Dossier: {SecurityElement.Escape(target.Name)}</h1>");
        html.AppendLine(CultureInfo.InvariantCulture, $"    <div class=\"header-meta\">Target ID: {SecurityElement.Escape(target.Id.ToString())} · Build: {SecurityElement.Escape(target.BuildLabel)} · Env: {SecurityElement.Escape(target.Environment.ToString())} · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} zzz</div>");
        html.AppendLine("  </div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <div class=\"badge {verdictClass}\">{decision.Outcome}</div>");
        html.AppendLine("</header>");

        // KPI Grid
        html.AppendLine("<div class=\"kpi-grid\">");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <div class=\"kpi-card\"><div class=\"kpi-label\">Clearance Verdict</div><div class=\"kpi-value\" style=\"color:{(isCleared ? "var(--accent-gold)" : "var(--state-fault)")}\">{decision.Outcome}</div></div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <div class=\"kpi-card\"><div class=\"kpi-label\">Policy Gates</div><div class=\"kpi-value\">{passedGates} / {decision.Gates.Count}</div></div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <div class=\"kpi-card\"><div class=\"kpi-label\">States Visited</div><div class=\"kpi-value\">{totalStates}</div></div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <div class=\"kpi-card\"><div class=\"kpi-label\">Deviations</div><div class=\"kpi-value\" style=\"color:{(findings.Count == 0 ? "var(--state-verified)" : "var(--state-fault)")}\">{findings.Count}</div></div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <div class=\"kpi-card\"><div class=\"kpi-label\">Sealed Artefacts</div><div class=\"kpi-value\">{evidence.Count}</div></div>");
        html.AppendLine("</div>");

        // Charts Row
        html.AppendLine("<div class=\"charts-row\">");
        // Donut Chart
        html.AppendLine("  <div class=\"chart-card\">");
        html.AppendLine("    <div class=\"chart-title\">Release Clearance Gates Fulfillment</div>");
        html.AppendLine("    <svg width=\"140\" height=\"140\" viewBox=\"0 0 140 140\">");
        html.AppendLine("      <circle cx=\"70\" cy=\"70\" r=\"54\" fill=\"none\" stroke=\"var(--border-subtle)\" stroke-width=\"12\"/>");
        html.AppendLine(CultureInfo.InvariantCulture, $"      <circle cx=\"70\" cy=\"70\" r=\"54\" fill=\"none\" stroke=\"{(passedGates == decision.Gates.Count ? "var(--state-verified)" : "var(--state-fault)")}\" stroke-width=\"12\" stroke-dasharray=\"{passDash:F1} {remainingDash:F1}\" stroke-dashoffset=\"0\" transform=\"rotate(-90 70 70)\"/>");
        html.AppendLine(CultureInfo.InvariantCulture, $"      <text x=\"70\" y=\"66\" text-anchor=\"middle\" font-size=\"20\" font-weight=\"bold\" fill=\"var(--text-primary)\" font-family=\"var(--mono-font)\">{passPercent:F0}%</text>");
        html.AppendLine(CultureInfo.InvariantCulture, $"      <text x=\"70\" y=\"82\" text-anchor=\"middle\" font-size=\"10\" fill=\"var(--text-secondary)\">{passedGates}/{decision.Gates.Count} GATES</text>");
        html.AppendLine("    </svg>");
        html.AppendLine("  </div>");

        // Severity Distribution
        html.AppendLine("  <div class=\"chart-card\">");
        html.AppendLine("    <div class=\"chart-title\">Deviation Severity Breakdown</div>");
        html.AppendLine("    <div style=\"width:100%; display:flex; flex-direction:column; justify-content:center; height:140px; gap:8px;\">");
        var totalFind = Math.Max(1, findings.Count);
        html.AppendLine(CultureInfo.InvariantCulture, $"      <div style=\"display:flex; justify-content:space-between; font-size:11px;\"><span>Critical</span><span class=\"mono sev-crit\">{critCount}</span></div><div style=\"height:6px; background:var(--border-subtle); border-radius:3px;\"><div style=\"width:{(critCount / (double)totalFind) * 100:F1}%; height:100%; background:var(--state-fault); border-radius:3px;\"></div></div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"      <div style=\"display:flex; justify-content:space-between; font-size:11px;\"><span>High</span><span class=\"mono sev-high\">{highCount}</span></div><div style=\"height:6px; background:var(--border-subtle); border-radius:3px;\"><div style=\"width:{(highCount / (double)totalFind) * 100:F1}%; height:100%; background:var(--state-warn); border-radius:3px;\"></div></div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"      <div style=\"display:flex; justify-content:space-between; font-size:11px;\"><span>Medium</span><span class=\"mono sev-med\">{medCount}</span></div><div style=\"height:6px; background:var(--border-subtle); border-radius:3px;\"><div style=\"width:{(medCount / (double)totalFind) * 100:F1}%; height:100%; background:#ffd60a; border-radius:3px;\"></div></div>");
        html.AppendLine(CultureInfo.InvariantCulture, $"      <div style=\"display:flex; justify-content:space-between; font-size:11px;\"><span>Low</span><span class=\"mono sev-low\">{lowCount}</span></div><div style=\"height:6px; background:var(--border-subtle); border-radius:3px;\"><div style=\"width:{(lowCount / (double)totalFind) * 100:F1}%; height:100%; background:var(--state-info); border-radius:3px;\"></div></div>");
        html.AppendLine("    </div>");
        html.AppendLine("  </div>");
        html.AppendLine("</div>");

        // Section 1: Gates Table
        html.AppendLine("<div class=\"card\">");
        html.AppendLine("  <h2>Release Policy Clearance Gates</h2>");
        html.AppendLine("  <table>");
        html.AppendLine("    <thead><tr><th>ID</th><th>Gate Name &amp; Requirement</th><th>Policy Source</th><th>Status</th><th>Evidence Note</th></tr></thead>");
        html.AppendLine("    <tbody>");
        foreach (var g in decision.Gates)
        {
            var tag = g.Passed ? "tag-pass" : (g.Mandatory ? "tag-fail" : "tag-warn");
            var res = g.Passed ? "PASS" : (g.Mandatory ? "FAIL" : "WARN");
            html.AppendLine(CultureInfo.InvariantCulture, $"      <tr><td class=\"mono\">{SecurityElement.Escape(g.Id)}</td><td><strong>{SecurityElement.Escape(g.Name)}</strong><br/><span style=\"color:var(--text-secondary); font-size:11px;\">{SecurityElement.Escape(g.Requirement)}</span></td><td style=\"font-size:11px; color:var(--text-muted);\">{SecurityElement.Escape(g.PolicySource)}</td><td><span class=\"tag {tag}\">{res}</span></td><td>{SecurityElement.Escape(g.EvidenceNote)}</td></tr>");
        }
        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");
        html.AppendLine("</div>");

        // Section 2: Findings Table
        html.AppendLine("<div class=\"card\">");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <h2>Deviations &amp; Faults Catalog ({findings.Count})</h2>");
        if (findings.Count == 0)
        {
            html.AppendLine("  <p style=\"color:var(--state-verified); font-size:13px;\">Zero deviations recorded. Target satisfies all examined behavioural oracles.</p>");
        }
        else
        {
            html.AppendLine("  <table>");
            html.AppendLine("    <thead><tr><th>ID</th><th>Title &amp; Delta</th><th>Severity</th><th>Repro / 95% Wilson</th><th>Phantom?</th></tr></thead>");
            html.AppendLine("    <tbody>");
            foreach (var f in findings)
            {
                var sevClass = f.Severity == Severity.Critical ? "sev-crit" : f.Severity == Severity.High ? "sev-high" : f.Severity == Severity.Medium ? "sev-med" : "sev-low";
                var reproStr = f.Reproducibility is { } r ? $"{r:P0}" : "Unattempted";
                var wilsonStr = $"[{f.WilsonConfidence.Lower:P0}, {f.WilsonConfidence.Upper:P0}]";
                html.AppendLine(CultureInfo.InvariantCulture, $"      <tr><td class=\"mono\">{SecurityElement.Escape(f.Id.ToString())}</td><td><strong>{SecurityElement.Escape(f.Title)}</strong><br/><span style=\"color:var(--text-secondary); font-size:11px;\">Observed: {SecurityElement.Escape(f.Actual)} | Expected: {SecurityElement.Escape(f.Expected)}</span></td><td class=\"{sevClass}\">{f.Severity}</td><td class=\"mono\" style=\"font-size:11px;\">{reproStr}<br/><span style=\"color:var(--text-muted);\">{wilsonStr}</span></td><td>{(f.IsPhantom ? "<span style='color:var(--state-warn); font-weight:700;'>Yes</span>" : "No")}</td></tr>");
            }
            html.AppendLine("    </tbody>");
            html.AppendLine("  </table>");
        }
        html.AppendLine("</div>");

        // Section 3: Evidence Artefacts Table
        html.AppendLine("<div class=\"card\">");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <h2>Cryptographically Sealed Evidence Plane ({evidence.Count})</h2>");
        html.AppendLine("  <table>");
        html.AppendLine("    <thead><tr><th>ID</th><th>Kind</th><th>Summary</th><th>SHA-256 Digest</th><th>Sealed By</th><th>Timestamp</th></tr></thead>");
        html.AppendLine("    <tbody>");
        foreach (var ev in evidence.Take(50))
        {
            var hashShort = !string.IsNullOrWhiteSpace(ev.Sha256) && ev.Sha256.Length >= 16 ? ev.Sha256[..16] + "…" : "unhashed";
            html.AppendLine(CultureInfo.InvariantCulture, $"      <tr><td class=\"mono\">{SecurityElement.Escape(ev.Id.ToString())}</td><td class=\"mono\">{SecurityElement.Escape(ev.Kind)}</td><td>{SecurityElement.Escape(ev.Summary)}</td><td class=\"mono\" title=\"{SecurityElement.Escape(ev.Sha256)}\">{hashShort}</td><td>{SecurityElement.Escape(ev.SealedBy ?? "unsealed")}</td><td style=\"font-size:11px; color:var(--text-muted);\">{ev.CapturedAt:yyyy-MM-dd HH:mm}</td></tr>");
        }
        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");
        html.AppendLine("</div>");

        // Footer
        html.AppendLine("<footer>");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <p>Evidence Digest: <span class=\"mono\">{SecurityElement.Escape(decision.EvidenceDigest)}</span></p>");
        html.AppendLine(CultureInfo.InvariantCulture, $"  <p style=\"margin-top:6px;\">Capital-Iz Ghozt - Dev Tester v{GetProductVersion()} · Universal Blueprint Development Doctrine · Authority Sealed</p>");
        html.AppendLine("</footer>");

        html.AppendLine("</div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
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
                            version = GetProductVersion(),
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
