using System.Text;
using System.Text.RegularExpressions;

namespace Ghozt.Core;

/// <summary>
/// Evidence Privacy & Redaction Engine (Patent §22, §35).
/// Deterministically detects and masks credentials, tokens, connection strings, keys,
/// and PII while preserving verification honesty and cryptographic audit trails.
/// </summary>
public static class RedactionEngine
{
    private static readonly (string Category, Regex Pattern, string Replacement)[] RedactionRules =
    [
        // PEM Private Keys
        (
            "PrivateKey",
            new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled),
            "[REDACTED:PRIVATE_KEY]"
        ),

        // Bearer / OAuth authorization tokens
        (
            "BearerToken",
            new Regex(@"(?i)\bBearer\s+[a-zA-Z0-9_\-\.]{12,}\b", RegexOptions.Compiled),
            "Bearer [REDACTED:TOKEN]"
        ),

        // JSON Web Tokens (JWT)
        (
            "JwtToken",
            new Regex(@"\beyJ[a-zA-Z0-9_-]{10,}\.eyJ[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_\-\.]+\b", RegexOptions.Compiled),
            "[REDACTED:JWT]"
        ),

        // API Keys, Client Secrets, Access Tokens
        (
            "ApiKey",
            new Regex(@"(?i)\b(?<prefix>(?:api[_-]?key|client[_-]?secret|access[_-]?token|app[_-]?secret|auth[_-]?token)\s*[:=]\s*[""']?)(?!\[REDACTED:)[a-zA-Z0-9_\-]{16,}(?<suffix>[""']?)", RegexOptions.Compiled),
            "${prefix}[REDACTED:API_KEY]${suffix}"
        ),

        // Database connection strings containing passwords
        (
            "ConnectionStringPassword",
            new Regex(@"(?i)(?<prefix>(?:Password|Pwd)\s*=\s*)(?!\[REDACTED:)[^;]+(?<suffix>;|$)", RegexOptions.Compiled),
            "${prefix}[REDACTED:DB_PASSWORD]${suffix}"
        ),

        // Passwords & generic credentials in key-value pairs or query strings
        (
            "Password",
            new Regex(@"(?i)\b(?<prefix>(?:password|pwd)\s*[:=]\s*[""']?)(?!\[REDACTED:)[^""'\s,;]{4,}(?<suffix>[""']?)", RegexOptions.Compiled),
            "${prefix}[REDACTED:PASSWORD]${suffix}"
        ),

        // Email addresses
        (
            "Email",
            new Regex(@"\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b", RegexOptions.Compiled),
            "[REDACTED:EMAIL]"
        ),
    ];

    /// <summary>
    /// Checks whether the payload contains any detectable sensitive credentials or PII.
    /// </summary>
    public static bool ContainsSensitiveData(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        foreach (var (_, pattern, _) in RedactionRules)
        {
            if (pattern.IsMatch(input))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the detected secret categories present within the payload.
    /// </summary>
    public static IReadOnlyList<string> DetectSecretCategories(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var categories = new List<string>();
        foreach (var (category, pattern, _) in RedactionRules)
        {
            if (pattern.IsMatch(input) && !categories.Contains(category))
            {
                categories.Add(category);
            }
        }

        return categories;
    }

    /// <summary>
    /// Sanitizes the input string by replacing all discovered secrets with deterministic replacement tokens.
    /// </summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "";
        }

        var sanitized = input;
        foreach (var (_, pattern, replacement) in RedactionRules)
        {
            sanitized = pattern.Replace(sanitized, replacement);
        }

        return sanitized;
    }

    /// <summary>
    /// Redacts a single Evidence record, updating its payload with sanitized tokens,
    /// recomputing its SHA-256 seal, setting Redacted=true, persisting to GhoztStore,
    /// and logging an EVD.REDACT ledger event.
    /// </summary>
    public static Evidence RedactEvidence(Evidence evidence, GhoztStore store, string actor = "RedactionEngine")
    {
        if (evidence.Redacted)
        {
            return evidence;
        }

        var originalSha = evidence.Sha256;
        var sanitizedPayload = Redact(evidence.Payload);
        var newSha = Hashing.Sha256(sanitizedPayload);
        var newSize = Encoding.UTF8.GetByteCount(sanitizedPayload);

        var summarySuffix = evidence.Summary.EndsWith("[Redacted]", StringComparison.OrdinalIgnoreCase)
            ? evidence.Summary
            : $"{evidence.Summary} [Redacted]";

        var redacted = evidence with
        {
            Payload = sanitizedPayload,
            Sha256 = newSha,
            SizeBytes = newSize,
            Redacted = true,
            Summary = summarySuffix,
        };

        store.Save(redacted);

        store.Append(new LedgerEvent
        {
            Id = store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "EVD.REDACT",
            Message = $"Evidence {evidence.Id} redacted by {actor}.",
            Severity = Severity.Medium,
            TargetId = evidence.TargetId,
            RelatedId = evidence.Id.ToString(),
            Actor = actor,
            Detail = $"Original SHA-256: {originalSha} | Sanitized SHA-256: {newSha} | Size: {evidence.SizeBytes} -> {newSize} bytes",
        });

        return redacted;
    }

    /// <summary>
    /// Scans and redacts all unredacted evidence items for the specified target that contain sensitive data.
    /// Returns the number of redacted artefacts.
    /// </summary>
    public static int RedactTargetEvidence(GhoztStore store, GhoztId targetId, string actor = "RedactionEngine")
    {
        var items = store.EvidenceItems(targetId);
        var count = 0;

        foreach (var item in items)
        {
            if (!item.Redacted && ContainsSensitiveData(item.Payload))
            {
                RedactEvidence(item, store, actor);
                count++;
            }
        }

        return count;
    }
}
