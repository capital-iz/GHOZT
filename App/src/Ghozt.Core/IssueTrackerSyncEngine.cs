using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ghozt.Core;

/// <summary>
/// Synchronizes and formats Ghozt findings into issue trackers (GitHub, Jira, GitLab)
/// with Table 46 fingerprint deduplication (Patent Table 75).
/// </summary>
public static class IssueTrackerSyncEngine
{
    private static readonly Regex FingerprintCommentRegex = new(
        @"<!--\s*ghozt:finding_id:([A-Z]{3}-[0-9]{6})\s+fingerprint:(FP-[A-F0-9]+)\s*-->",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Generates a comprehensive GitHub Issue Markdown document from a Finding and its forensic context.
    /// </summary>
    public static string FormatGitHubIssueMarkdown(
        Finding finding,
        Target? target = null,
        CausalHypothesis? hypothesis = null,
        IEnumerable<Evidence>? evidenceRecords = null,
        RegressionGuard? guard = null)
    {
        var sb = new StringBuilder();

        // 1. Header & Fingerprint Token (Patent Table 46)
        sb.AppendLine($"<!-- ghozt:finding_id:{finding.Id} fingerprint:{finding.Fingerprint} -->");
        sb.AppendLine($"> [!NOTE]");
        sb.AppendLine($"> **Ghozt Autonomous Verification Finding** — `{finding.Id}` | Fingerprint: `{finding.Fingerprint}`");
        sb.AppendLine($"> Target: `{target?.Name ?? "Unknown Target"}` (`{finding.TargetId}`) | Sweep: `{finding.SweepId}` | Severity: **{finding.Severity}** | Maturity: **{finding.Maturity}**");
        sb.AppendLine($"# [{finding.Severity.ToString().ToUpperInvariant()}] {finding.Title}");
        sb.AppendLine();

        // 2. Semantic Deviation Overview
        sb.AppendLine("## 1. Deviation Overview");
        sb.AppendLine($"**Semantic Category**: `{finding.SemanticCategory?.ToString() ?? "Deviation"}`  ");
        sb.AppendLine($"**Reproduction Attempts**: `{finding.ReproAttempts}` (Successes: `{finding.ReproSuccesses}`)  ");
        sb.AppendLine($"**Laplace Reproducibility Prior**: `{finding.BayesianReproducibility:P1}` (95% Wilson Interval: `[{finding.WilsonConfidence.Lower:P1}..{finding.WilsonConfidence.Upper:P1}]`)  ");
        sb.AppendLine();
        sb.AppendLine("### Expected Behavior");
        sb.AppendLine(string.IsNullOrWhiteSpace(finding.Expected) ? "_No formal expectation specified._" : finding.Expected);
        sb.AppendLine();
        sb.AppendLine("### Observed Reality");
        sb.AppendLine(string.IsNullOrWhiteSpace(finding.Actual) ? "_No actual output captured._" : finding.Actual);
        sb.AppendLine();

        // 3. Causal Forensic Hypothesis (Patent §29)
        if (hypothesis != null)
        {
            sb.AppendLine("## 2. Causal Forensics & Counterfactual Evidence");
            sb.AppendLine($"**Hypothesis ID**: `{hypothesis.Id}`  ");
            sb.AppendLine($"**Causal Mechanism**: {hypothesis.Mechanism}  ");
            sb.AppendLine($"**Causal Confidence Score**: `{hypothesis.Confidence:P1}` ({hypothesis.Status})  ");
            sb.AppendLine();
        }

        // 4. Sealed Evidence Hashes (Patent §23, Table 40)
        if (evidenceRecords != null && evidenceRecords.Any())
        {
            sb.AppendLine("## 3. Cryptographically Sealed Evidence");
            sb.AppendLine("| Evidence ID | Kind | SHA-256 Digest | Source Adapter |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var ev in evidenceRecords)
            {
                sb.AppendLine($"| `{ev.Id}` | {ev.Kind} | `{ev.Sha256}` | {ev.SourceAdapter} |");
            }
            sb.AppendLine();
        }

        // 5. Recommended Regression Guard (Patent §31)
        if (guard != null)
        {
            sb.AppendLine("## 4. Active Regression Guard");
            sb.AppendLine($"**Guard ID**: `{guard.Id}` | Title: **{guard.Title}**  ");
            sb.AppendLine($"**Expected Behavior**: `{guard.ExpectedBehavior}`  ");
            sb.AppendLine("```yaml");
            sb.AppendLine($"guard_id: {guard.Id}");
            sb.AppendLine($"target_id: {guard.TargetId}");
            sb.AppendLine($"created_at: {guard.CreatedAt:O}");
            sb.AppendLine($"details: {guard.Details}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // 6. Footer Provenance
        sb.AppendLine("---");
        sb.AppendLine($"*Generated autonomously by [Ghozt - Dev Tester](https://capital-iz.com) at {DateTimeOffset.Now:O}. Tamper-evident record.*");

        return sb.ToString();
    }

    /// <summary>
    /// Generates Jira JSON issue creation payload (Patent Table 75).
    /// </summary>
    public static string FormatJiraJson(Finding finding, string projectKey = "GZT")
    {
        var summary = $"[{finding.Severity.ToString().ToUpperInvariant()}] {finding.Title} ({finding.Id})";
        var escapedSummary = EscapeJson(summary);
        var escapedDesc = EscapeJson($"Ghozt Finding {finding.Id}\nFingerprint: {finding.Fingerprint}\n\nExpected: {finding.Expected}\nActual: {finding.Actual}\nAttempts: {finding.ReproAttempts}");

        return $$"""
        {
          "fields": {
            "project": { "key": "{{projectKey}}" },
            "summary": "{{escapedSummary}}",
            "description": "{{escapedDesc}}",
            "issuetype": { "name": "Bug" },
            "labels": ["ghozt", "{{finding.Fingerprint}}", "{{finding.SemanticCategory}}", "{{finding.Severity}}"]
          }
        }
        """;
    }

    /// <summary>
    /// Evaluates existing tickets against an incoming Finding to prevent duplicate ticket proliferation (Patent Table 46, Table 75).
    /// </summary>
    public static IssueSyncResult DeduplicateAndSync(
        Finding finding,
        IEnumerable<string> existingTicketBodies,
        Target? target = null,
        string provider = "GitHub")
    {
        // Search existing ticket bodies for matching Table 46 fingerprint token
        foreach (var body in existingTicketBodies)
        {
            var match = FingerprintCommentRegex.Match(body);
            if (match.Success)
            {
                var existingFp = match.Groups[2].Value;
                if (string.Equals(existingFp, finding.Fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    // Existing ticket match found: generate recurrence update payload
                    var existingId = match.Groups[1].Value;
                    var updateMarkdown = FormatRecurrenceUpdateComment(finding);

                    return new IssueSyncResult
                    {
                        Provider = provider,
                        IssueIdentifier = existingId,
                        Title = finding.Title,
                        PayloadMarkdown = updateMarkdown,
                        FingerprintToken = finding.Fingerprint,
                        IsUpdate = true,
                        RecurrenceCount = finding.ReproAttempts,
                        SyncedAt = DateTimeOffset.Now,
                    };
                }
            }
        }

        // No existing ticket found: generate new ticket payload
        var newMarkdown = FormatGitHubIssueMarkdown(finding, target);
        return new IssueSyncResult
        {
            Provider = provider,
            IssueIdentifier = finding.Id.ToString(),
            Title = $"[{finding.Severity.ToString().ToUpperInvariant()}] {finding.Title} ({finding.Id})",
            PayloadMarkdown = newMarkdown,
            FingerprintToken = finding.Fingerprint,
            IsUpdate = false,
            RecurrenceCount = finding.ReproAttempts,
            SyncedAt = DateTimeOffset.Now,
        };
    }

    /// <summary>
    /// Generates a GitHub issue comment for recurring occurrences of a known fault fingerprint.
    /// </summary>
    public static string FormatRecurrenceUpdateComment(Finding finding)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"> [!WARNING]");
        sb.AppendLine($"> **Ghozt Recurrence Notice** — Finding `{finding.Id}` re-observed in sweep `{finding.SweepId}`.");
        sb.AppendLine($"> **Updated Repro Attempts**: `{finding.ReproAttempts}` (Successes: `{finding.ReproSuccesses}`)");
        sb.AppendLine($"> **Bayesian Laplace Prior**: `{finding.BayesianReproducibility:P1}` | **95% Wilson Bounds**: `[{finding.WilsonConfidence.Lower:P1}..{finding.WilsonConfidence.Upper:P1}]`");
        sb.AppendLine($"> **Fingerprint**: `{finding.Fingerprint}`");
        sb.AppendLine();
        sb.AppendLine($"Timestamp: `{DateTimeOffset.Now:O}`");
        return sb.ToString();
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\r", "")
         .Replace("\n", "\\n");
}
