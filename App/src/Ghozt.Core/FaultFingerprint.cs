using System.Text.RegularExpressions;

namespace Ghozt.Core;

/// <summary>
/// Composite fault fingerprinting and deduplication engine (Patent §26).
/// Computes normalized signatures to cluster equivalent failures across runs and environments.
/// </summary>
public static partial class FaultFingerprint
{
    // Regex patterns for volatile tokens that change across runs without changing the underlying defect
    private static readonly Regex MemoryAddressRegex = new(@"\b0x[0-9a-fA-F]{4,16}\b", RegexOptions.Compiled);
    private static readonly Regex GuidRegex = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex TimestampRegex = new(@"\b\d{4}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?\b", RegexOptions.Compiled);
    private static readonly Regex ProcessIdRegex = new(@"\b(pid|processid|process\s*id)\s*[:=]?\s*\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LocalPortRegex = new(@"(127\.0\.0\.1|localhost):\d{4,5}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LineNumberRegex = new(@":\s*line\s+\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Normalizes an error message or stack trace by stripping volatile runtime tokens (addresses, GUIDs, PIDs, timestamps).
    /// </summary>
    public static string NormalizeMessage(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = input;
        text = MemoryAddressRegex.Replace(text, "<ADDR>");
        text = GuidRegex.Replace(text, "<GUID>");
        text = TimestampRegex.Replace(text, "<TIMESTAMP>");
        text = ProcessIdRegex.Replace(text, "<PID>");
        text = LocalPortRegex.Replace(text, "$1:<PORT>");
        text = LineNumberRegex.Replace(text, ":line <N>");

        return text.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Computes a deterministic 32-character composite fingerprint (SHA-256) for a fault observation.
    /// </summary>
    public static string Compute(
        string targetId,
        string exceptionClass,
        string errorCode,
        string messageOrDetail,
        string? latticeNodeId = null,
        string? deltaSignature = null)
    {
        var normTarget = (targetId ?? string.Empty).Trim().ToLowerInvariant();
        var normClass = (exceptionClass ?? string.Empty).Trim().ToLowerInvariant();
        var normCode = (errorCode ?? string.Empty).Trim().ToLowerInvariant();
        var normMsg = NormalizeMessage(messageOrDetail);
        var normNode = (latticeNodeId ?? string.Empty).Trim().ToLowerInvariant();
        var normDelta = (deltaSignature ?? string.Empty).Trim().ToLowerInvariant();

        // Features joined with unit separator '\u001F'
        var composite = string.Join('\u001F', normTarget, normClass, normCode, normNode, normDelta, normMsg);
        return Hashing.Sha256(composite)[..32];
    }

    /// <summary>
    /// Deduplicates a finding against the evidence store. If a matching composite fingerprint exists,
    /// increments reproduction counters and updates Bayesian / Wilson statistics rather than creating duplicates.
    /// </summary>
    public static (Finding Finding, bool IsRecurrence) ClusterFinding(
        GhoztStore store,
        Finding newFinding,
        Target target,
        Sweep? sweep = null)
    {
        var existing = store.FindByFingerprint(target.Id, newFinding.Fingerprint);

        if (existing is not null)
        {
            var attempts = existing.ReproAttempts + 1;
            var successes = existing.ReproSuccesses + 1;

            // Maturity progression: reproducible after 2 successful observations
            var maturity = existing.Maturity;
            if (maturity == FindingMaturity.New && attempts >= 2)
            {
                maturity = FindingMaturity.Reproduced;
            }

            var updated = existing with
            {
                LastSeenAt = DateTimeOffset.Now,
                ReproAttempts = attempts,
                ReproSuccesses = successes,
                EvidenceCount = existing.EvidenceCount + newFinding.EvidenceCount,
                Maturity = maturity,
                Actual = !string.IsNullOrWhiteSpace(newFinding.Actual) ? newFinding.Actual : existing.Actual,
            };

            store.Save(updated);

            store.Append(new LedgerEvent
            {
                Id = store.NextId(IdKind.Event),
                At = DateTimeOffset.Now,
                Code = "FLT.RECUR",
                Message = $"Deviation recurred: {updated.Title}",
                Severity = updated.Severity,
                TargetId = target.Id,
                SweepId = sweep?.Id,
                RelatedId = updated.Id.ToString(),
                Actor = "FaultFingerprintEngine",
                Detail = $"Finding {updated.Id} (fingerprint {updated.Fingerprint}) seen {successes} of {attempts} attempts. Bayes={updated.BayesianReproducibility:P0}, Wilson=[{updated.WilsonConfidence.Lower:P0}..{updated.WilsonConfidence.Upper:P0}].",
            });

            return (updated, true);
        }

        var saved = newFinding with
        {
            ReproAttempts = Math.Max(1, newFinding.ReproAttempts),
            ReproSuccesses = Math.Max(1, newFinding.ReproSuccesses),
            FirstSeenAt = DateTimeOffset.Now,
            LastSeenAt = DateTimeOffset.Now,
        };

        store.Save(saved);

        store.Append(new LedgerEvent
        {
            Id = store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "FLT.NEW",
            Message = $"New deviation recorded: {saved.Title}",
            Severity = saved.Severity,
            TargetId = target.Id,
            SweepId = sweep?.Id,
            RelatedId = saved.Id.ToString(),
            Actor = "FaultFingerprintEngine",
            Detail = $"Registered finding {saved.Id} with composite fingerprint: {saved.Fingerprint}",
        });

        return (saved, false);
    }
}
