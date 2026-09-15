using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ghozt.Core;

/// <summary>
/// Structured item representing an external observability log record or trace span (Patent Table 75).
/// </summary>
public sealed record ExternalLogEntry
{
    public DateTimeOffset? Timestamp { get; init; }

    public string Level { get; init; } = "INFO";

    public string Message { get; init; } = "";

    public string Source { get; init; } = "External";

    public string TraceId { get; init; } = "";

    public string SpanId { get; init; } = "";

    public Dictionary<string, string> Attributes { get; init; } = new();
}

/// <summary>
/// Ingestion Gateway for external observability streams (OpenTelemetry, Syslog, JSON, Windows Event Log)
/// with automated PII/credential redaction and BlackBox ledger persistence (Patent §22, §35, Table 75).
/// </summary>
public static class ObservabilityIngestGateway
{
    private static readonly Regex KeyValueLogRegex = new(
        @"(?:level=(?<level>[a-zA-Z]+))|(?:msg=""(?<msg>[^""]+)"")|(?:trace_id=(?<trace>[a-zA-Z0-9_\-]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses and ingests a raw multiline log stream or JSON-lines stream into Ghozt BlackBox events.
    /// </summary>
    public static async Task<ObservabilityIngestResult> IngestLogStreamAsync(
        GhoztStore store,
        string rawLogContent,
        GhoztId targetId,
        GhoztId? sweepId = null,
        string defaultSource = "ObservabilityGateway",
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(rawLogContent))
        {
            return new ObservabilityIngestResult
            {
                IngestedCount = 0,
                RedactedCount = 0,
                MalformedCount = 0,
                Duration = sw.Elapsed,
                BatchDigest = Hashing.Sha256("EMPTY_STREAM"),
            };
        }

        var trimmed = rawLogContent.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            var singleEntry = ParseLine(trimmed, defaultSource);
            if (singleEntry != null)
            {
                return await IngestEntriesAsync(store, [singleEntry], targetId, sweepId, ct).ConfigureAwait(false);
            }
        }

        var lines = rawLogContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var ingested = 0;
        var redacted = 0;
        var malformed = 0;
        var payloadDigests = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            ct.ThrowIfCancellationRequested();

            var entry = ParseLine(line, defaultSource);
            if (entry == null)
            {
                malformed++;
                continue;
            }

            // Check and apply RedactionEngine
            var rawText = entry.Message;
            var isSecret = RedactionEngine.ContainsSensitiveData(rawText);
            var sanitizedMsg = RedactionEngine.Redact(rawText);
            if (isSecret)
            {
                redacted++;
            }

            // Format diagnostic code from level
            var diagCode = MapDiagnosticCode(entry.Level, entry.TraceId);
            var severity = MapSeverity(entry.Level);

            // Serialize attributes for detail
            var sanitizedAttributes = entry.Attributes.ToDictionary(
                kvp => kvp.Key,
                kvp => RedactionEngine.Redact(kvp.Value)
            );
            var detailJson = JsonSerializer.Serialize(new
            {
                trace_id = entry.TraceId,
                span_id = entry.SpanId,
                source = entry.Source,
                attributes = sanitizedAttributes,
            });

            var evtId = store.NextId(IdKind.Event);
            var ledgerEvent = new LedgerEvent
            {
                Id = evtId,
                At = entry.Timestamp ?? DateTimeOffset.Now,
                Code = diagCode,
                Message = sanitizedMsg,
                Severity = severity,
                TargetId = targetId,
                SweepId = sweepId,
                RelatedId = entry.TraceId,
                Actor = entry.Source,
                Detail = detailJson,
            };

            store.Append(ledgerEvent);
            ingested++;
            payloadDigests.Add($"{evtId}:{diagCode}:{sanitizedMsg}");
        }

        sw.Stop();
        var batchDigest = Hashing.Sha256(string.Join("\n", payloadDigests));

        return new ObservabilityIngestResult
        {
            IngestedCount = ingested,
            RedactedCount = redacted,
            MalformedCount = malformed,
            Duration = sw.Elapsed,
            BatchDigest = batchDigest,
        };
    }

    /// <summary>
    /// Ingests pre-parsed structured log entries directly into the BlackBox store.
    /// </summary>
    public static async Task<ObservabilityIngestResult> IngestEntriesAsync(
        GhoztStore store,
        IEnumerable<ExternalLogEntry> entries,
        GhoztId targetId,
        GhoztId? sweepId = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var ingested = 0;
        var redacted = 0;
        var malformed = 0;
        var payloadDigests = new List<string>();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.Message))
            {
                malformed++;
                continue;
            }

            var isSecret = RedactionEngine.ContainsSensitiveData(entry.Message);
            var sanitizedMsg = RedactionEngine.Redact(entry.Message);

            var diagCode = MapDiagnosticCode(entry.Level, entry.TraceId);
            var severity = MapSeverity(entry.Level);

            var attrHasSecret = false;
            var sanitizedAttributes = entry.Attributes.ToDictionary(
                kvp => kvp.Key,
                kvp =>
                {
                    if (RedactionEngine.ContainsSensitiveData(kvp.Value))
                    {
                        attrHasSecret = true;
                    }
                    return RedactionEngine.Redact(kvp.Value);
                }
            );

            if (isSecret || attrHasSecret)
            {
                redacted++;
            }

            var detailJson = JsonSerializer.Serialize(new
            {
                trace_id = entry.TraceId,
                span_id = entry.SpanId,
                source = entry.Source,
                attributes = sanitizedAttributes,
            });

            var evtId = store.NextId(IdKind.Event);
            var ledgerEvent = new LedgerEvent
            {
                Id = evtId,
                At = entry.Timestamp ?? DateTimeOffset.Now,
                Code = diagCode,
                Message = sanitizedMsg,
                Severity = severity,
                TargetId = targetId,
                SweepId = sweepId,
                RelatedId = entry.TraceId,
                Actor = entry.Source,
                Detail = detailJson,
            };

            store.Append(ledgerEvent);
            ingested++;
            payloadDigests.Add($"{evtId}:{diagCode}:{sanitizedMsg}");
        }

        sw.Stop();
        var batchDigest = Hashing.Sha256(string.Join("\n", payloadDigests));

        return new ObservabilityIngestResult
        {
            IngestedCount = ingested,
            RedactedCount = redacted,
            MalformedCount = malformed,
            Duration = sw.Elapsed,
            BatchDigest = batchDigest,
        };
    }

    private static ExternalLogEntry? ParseLine(string line, string defaultSource)
    {
        var trimmed = line.Trim();

        // 1. Try parsing JSON format: {"timestamp":"...","level":"...","message":"..."}
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var msg = GetJsonString(root, "message") ?? GetJsonString(root, "msg") ?? GetJsonString(root, "error") ?? trimmed;
                var level = GetJsonString(root, "level") ?? GetJsonString(root, "severity") ?? "INFO";
                var source = GetJsonString(root, "source") ?? GetJsonString(root, "service") ?? defaultSource;
                var trace = GetJsonString(root, "trace_id") ?? GetJsonString(root, "traceId") ?? "";
                var span = GetJsonString(root, "span_id") ?? GetJsonString(root, "spanId") ?? "";

                DateTimeOffset? ts = null;
                if (root.TryGetProperty("timestamp", out var tsProp) && tsProp.TryGetDateTimeOffset(out var parsedTs))
                {
                    ts = parsedTs;
                }

                var attrs = new Dictionary<string, string>();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name is not ("message" or "msg" or "level" or "severity" or "source" or "service" or "trace_id" or "traceId" or "span_id" or "spanId" or "timestamp"))
                    {
                        attrs[prop.Name] = prop.Value.ToString();
                    }
                }

                return new ExternalLogEntry
                {
                    Timestamp = ts,
                    Level = level.ToUpperInvariant(),
                    Message = msg,
                    Source = source,
                    TraceId = trace,
                    SpanId = span,
                    Attributes = attrs,
                };
            }
            catch (JsonException)
            {
                // Fallback to text parsing
            }
        }

        // 2. Fallback to standard text / key-value format
        var match = KeyValueLogRegex.Match(trimmed);
        var textLevel = match.Groups["level"].Success ? match.Groups["level"].Value.ToUpperInvariant() : "INFO";
        var textMsg = match.Groups["msg"].Success ? match.Groups["msg"].Value : trimmed;
        var textTrace = match.Groups["trace"].Success ? match.Groups["trace"].Value : "";

        return new ExternalLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = textLevel,
            Message = textMsg,
            Source = defaultSource,
            TraceId = textTrace,
        };
    }

    private static string? GetJsonString(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static string MapDiagnosticCode(string level, string traceId)
    {
        var baseLevel = level.ToUpperInvariant() switch
        {
            "ERROR" or "CRITICAL" or "FATAL" or "EMERG" => "ERR",
            "WARN" or "WARNING" => "WRN",
            "INFO" or "NOTICE" => "INF",
            "DEBUG" or "TRACE" => "DBG",
            _ => "LOG",
        };

        return string.IsNullOrWhiteSpace(traceId) ? $"GZT-OBS-{baseLevel}" : $"GZT-OBS-SPAN-{baseLevel}";
    }

    private static Severity MapSeverity(string level) => level.ToUpperInvariant() switch
    {
        "CRITICAL" or "FATAL" or "EMERG" => Severity.Critical,
        "ERROR" => Severity.High,
        "WARN" or "WARNING" => Severity.Medium,
        "DEBUG" or "TRACE" => Severity.Info,
        _ => Severity.Info,
    };
}
