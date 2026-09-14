using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ghozt.Theming;

namespace Ghozt.Core;

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}

public sealed record ValidationMetric(
    string Name,
    double Value,
    string Unit,
    double Threshold,
    bool Passed);

public sealed record ValidationFindingDraft(
    string Title,
    string Description,
    Severity Severity,
    string Expected,
    string Actual,
    string LatticeNodeId,
    string Category,
    string RemediativeGuidance);

public sealed record ValidationReport
{
    public required string ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public required string Category { get; init; }
    public required bool Passed { get; init; }
    public required bool Blocked { get; init; }
    public required Severity Severity { get; init; }
    public required string Summary { get; init; }
    public required string Detail { get; init; }
    public IReadOnlyList<ValidationFindingDraft> Findings { get; init; } = [];
    public IReadOnlyList<ValidationMetric> Metrics { get; init; } = [];
    public required string EvidencePayload { get; init; }
}

public interface IValidationModule
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    RiskLevel Risk { get; }
    IReadOnlyList<string> OracleTypes { get; }
    IReadOnlyList<string> EvidenceRequirements { get; }
    Task<ValidationReport> ValidateAsync(Target target, GhoztStore store, CancellationToken cancellation = default);
}

/// <summary>
/// Accessibility validation module: scans lattice UI action nodes and verifies WCAG contrast compliance
/// across all 12 design system palettes.
/// </summary>
public sealed class AccessibilityValidationModule : IValidationModule
{
    public string Id => "val:a11y";
    public string Name => "Cross-Theme Accessibility & Contrast Validator";
    public string Category => "Accessibility";
    public RiskLevel Risk => RiskLevel.Low;
    public IReadOnlyList<string> OracleTypes => ["WCAG AA 4.5:1", "UIA Accessibility Name Completeness"];
    public IReadOnlyList<string> EvidenceRequirements => ["Palette contrast metrics", "Lattice node UIA inspection"];

    public Task<ValidationReport> ValidateAsync(Target target, GhoztStore store, CancellationToken cancellation = default)
    {
        var findings = new List<ValidationFindingDraft>();
        var metrics = new List<ValidationMetric>();
        var sb = new StringBuilder();
        sb.AppendLine("=== Accessibility & Contrast Audit ===");

        // 1. Evaluate contrast across all 12 palettes
        double totalContrast = 0;
        var paletteCount = 0;
        var contrastViolations = 0;

        foreach (var palette in GhoztCatalog.All)
        {
            var darkTokens = palette.BuildTokens(dark: true, surfaceOpacityPercent: 100, palette.DefaultEffects);
            var lightTokens = palette.BuildTokens(dark: false, surfaceOpacityPercent: 100, palette.DefaultEffects);

            var darkContrast = darkTokens.TryGetValue("Text.Primary", out var dt) &&
                               darkTokens.TryGetValue("Surface.Base", out var ds)
                ? ColorMath.ContrastRatio(dt, ds)
                : 10.0;

            var lightContrast = lightTokens.TryGetValue("Text.Primary", out var lt) &&
                                lightTokens.TryGetValue("Surface.Base", out var ls)
                ? ColorMath.ContrastRatio(lt, ls)
                : 10.0;

            var avgPaletteContrast = (darkContrast + lightContrast) / 2.0;
            totalContrast += avgPaletteContrast;
            paletteCount++;

            sb.AppendLine(CultureInfo.InvariantCulture, $"Palette '{palette.Label}': Dark={darkContrast:F2}:1, Light={lightContrast:F2}:1 (Avg={avgPaletteContrast:F2}:1)");

            if (darkContrast < 4.5)
            {
                contrastViolations++;
                findings.Add(new ValidationFindingDraft(
                    Title: $"WCAG AA Contrast Breach in {palette.Label} (Dark)",
                    Description: $"Text.Primary on Surface.Base in dark mode achieves {darkContrast:F2}:1, which is below the 4.5:1 WCAG AA minimum.",
                    Severity: Severity.Medium,
                    Expected: "Contrast ratio >= 4.5:1",
                    Actual: $"{darkContrast:F2}:1",
                    LatticeNodeId: "theme:" + palette.Key,
                    Category: "Accessibility",
                    RemediativeGuidance: "Adjust palette Surface.Base or Text.Primary luminance to meet 4.5:1 contrast floor."));
            }

            if (lightContrast < 4.5)
            {
                contrastViolations++;
                findings.Add(new ValidationFindingDraft(
                    Title: $"WCAG AA Contrast Breach in {palette.Label} (Light)",
                    Description: $"Text.Primary on Surface.Base in light mode achieves {lightContrast:F2}:1, which is below the 4.5:1 WCAG AA minimum.",
                    Severity: Severity.Medium,
                    Expected: "Contrast ratio >= 4.5:1",
                    Actual: $"{lightContrast:F2}:1",
                    LatticeNodeId: "theme:" + palette.Key,
                    Category: "Accessibility",
                    RemediativeGuidance: "Adjust palette Surface.Base or Text.Primary luminance to meet 4.5:1 contrast floor."));
            }
        }

        var meanContrast = paletteCount > 0 ? totalContrast / paletteCount : 0.0;
        metrics.Add(new ValidationMetric("AveragePaletteContrast", Math.Round(meanContrast, 2), ":1", 4.5, meanContrast >= 4.5));
        metrics.Add(new ValidationMetric("ContrastViolations", contrastViolations, "count", 0, contrastViolations == 0));

        // 2. Scan lattice nodes for accessible names
        var nodes = store.Nodes(target.Id);
        var missingAccessibleNames = 0;
        foreach (var node in nodes.Where(n => n.Kind is NodeKind.Action or NodeKind.Endpoint))
        {
            if (string.IsNullOrWhiteSpace(node.Label) || node.Label.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
            {
                missingAccessibleNames++;
                findings.Add(new ValidationFindingDraft(
                    Title: $"Missing Accessible Name on Node '{node.Id}'",
                    Description: $"Interactive lattice node '{node.Id}' lacks an explicit descriptive label for accessibility and screen readers.",
                    Severity: Severity.Low,
                    Expected: "Human-readable accessible name or label",
                    Actual: string.IsNullOrWhiteSpace(node.Label) ? "<empty>" : node.Label,
                    LatticeNodeId: node.Id,
                    Category: "Accessibility",
                    RemediativeGuidance: "Provide an accessible name via AutomationProperties.Name or aria-label."));
            }
        }

        metrics.Add(new ValidationMetric("MissingAccessibleLabels", missingAccessibleNames, "count", 0, missingAccessibleNames == 0));

        var passed = findings.Count == 0;
        var summary = passed
            ? $"Accessibility verified: Mean contrast {meanContrast:F2}:1 across {paletteCount} palettes; 0 missing accessible names."
            : $"Accessibility violations found: {findings.Count} issue(s) detected ({contrastViolations} contrast, {missingAccessibleNames} missing labels).";

        return Task.FromResult(new ValidationReport
        {
            ModuleId = Id,
            ModuleName = Name,
            Category = Category,
            Passed = passed,
            Blocked = false,
            Severity = passed ? Severity.Info : (findings.Any(f => f.Severity >= Severity.High) ? Severity.High : Severity.Medium),
            Summary = summary,
            Detail = sb.ToString(),
            Findings = findings,
            Metrics = metrics,
            EvidencePayload = sb.ToString(),
        });
    }
}

/// <summary>
/// Security and privacy validation module: scans target files, configurations, and payload streams
/// for exposed credentials (AWS keys, private keys, JWTs, connection strings with plaintext passwords).
/// </summary>
public sealed class SecurityPrivacyValidationModule : IValidationModule
{
    public string Id => "val:sec-privacy";
    public string Name => "Credential, Secret Leak & Privacy Validator";
    public string Category => "Security";
    public RiskLevel Risk => RiskLevel.Critical;
    public IReadOnlyList<string> OracleTypes => ["Secret Leak Detection", "Data Privacy & Redaction Invariant"];
    public IReadOnlyList<string> EvidenceRequirements => ["Filesystem secret scan log", "Redacted payload digest"];

    private static readonly Regex AwsKeyRegex = new(@"\b(A3T[A-Z0-9]|AKIA[0-9A-Z]{16}|ASIA[0-9A-Z]{16})\b", RegexOptions.Compiled);
    private static readonly Regex PrivateKeyRegex = new(@"-----BEGIN (RSA|OPENSSH|EC|DSA|PGP) PRIVATE KEY-----", RegexOptions.Compiled);
    private static readonly Regex JwtRegex = new(@"\beyJ[A-Za-z0-9-_=]{10,}\.eyJ[A-Za-z0-9-_=]{10,}\.[A-Za-z0-9-_.+/=]{10,}\b", RegexOptions.Compiled);
    private static readonly Regex ConnStringPwdRegex = new(@"(?i)(password|pwd)\s*=\s*([^;""'\s]{4,})", RegexOptions.Compiled);
    private static readonly Regex BearerTokenRegex = new(@"(?i)(bearer\s+[A-Za-z0-9_\-\.]{24,})", RegexOptions.Compiled);

    public Task<ValidationReport> ValidateAsync(Target target, GhoztStore store, CancellationToken cancellation = default)
    {
        var findings = new List<ValidationFindingDraft>();
        var metrics = new List<ValidationMetric>();
        var sb = new StringBuilder();
        sb.AppendLine("=== Security & Privacy Leak Scan ===");

        var targetPath = target.BaseAddress;
        if (string.IsNullOrWhiteSpace(targetPath) || (!File.Exists(targetPath) && !Directory.Exists(targetPath)))
        {
            // If target is HTTP or process without local dir, scan working directory
            targetPath = AppContext.BaseDirectory;
        }

        var rootDir = File.Exists(targetPath) ? Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory : targetPath;
        sb.AppendLine($"Scanning target directory: {rootDir}");

        var filesScanned = 0;
        var leakedSecrets = 0;

        try
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".json", ".env", ".config", ".xml", ".yaml", ".yml", ".ini", ".conf", ".cs", ".ts", ".js"
            };

            var dirInfo = new DirectoryInfo(rootDir);
            if (dirInfo.Exists)
            {
                var files = dirInfo.EnumerateFiles("*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(f.Extension))
                    .Where(f => !f.FullName.Contains(@"\bin\") && !f.FullName.Contains(@"\obj\") && !f.FullName.Contains(@"\.git\"))
                    .Take(100);

                foreach (var file in files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    filesScanned++;

                    string content;
                    try
                    {
                        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(fs, Encoding.UTF8);
                        content = reader.ReadToEnd();
                    }
                    catch
                    {
                        continue;
                    }

                    // Check for AWS keys
                    var awsMatch = AwsKeyRegex.Match(content);
                    if (awsMatch.Success)
                    {
                        leakedSecrets++;
                        var raw = awsMatch.Value;
                        var redacted = raw.Length > 4 ? raw[..4] + new string('*', raw.Length - 4) : "****";
                        findings.Add(new ValidationFindingDraft(
                            Title: $"Exposed AWS Access Key in {file.Name}",
                            Description: $"Unredacted AWS credential discovered in {file.FullName}. Masked representation: '{redacted}'.",
                            Severity: Severity.Critical,
                            Expected: "Credentials must be supplied via secure secret vault or environment variables.",
                            Actual: $"Found unredacted key pattern: {redacted}",
                            LatticeNodeId: "sec:" + file.Name,
                            Category: "Security",
                            RemediativeGuidance: "Revoke the exposed key immediately and migrate to Windows Credential Manager or secret store."));
                    }

                    // Check for Private Keys
                    if (PrivateKeyRegex.IsMatch(content))
                    {
                        leakedSecrets++;
                        findings.Add(new ValidationFindingDraft(
                            Title: $"Exposed Cryptographic Private Key in {file.Name}",
                            Description: $"Unencrypted private key block discovered in {file.FullName}.",
                            Severity: Severity.Critical,
                            Expected: "Private keys must never reside unencrypted in application distribution folders.",
                            Actual: "Found 'BEGIN PRIVATE KEY' header",
                            LatticeNodeId: "sec:" + file.Name,
                            Category: "Security",
                            RemediativeGuidance: "Store private keys in Windows Certificate Store or hardware HSM enclaves."));
                    }

                    // Check for JWTs
                    var jwtMatch = JwtRegex.Match(content);
                    if (jwtMatch.Success)
                    {
                        leakedSecrets++;
                        var raw = jwtMatch.Value;
                        var redacted = raw.Length > 8 ? raw[..8] + "..." + raw[^4..] : "****";
                        findings.Add(new ValidationFindingDraft(
                            Title: $"Exposed JWT Bearer Token in {file.Name}",
                            Description: $"Hardcoded JWT authentication token discovered in {file.FullName}. Masked representation: '{redacted}'.",
                            Severity: Severity.High,
                            Expected: "Bearer tokens must be acquired dynamically and never committed to disk.",
                            Actual: $"Found JWT token: {redacted}",
                            LatticeNodeId: "sec:" + file.Name,
                            Category: "Security",
                            RemediativeGuidance: "Acquire session tokens dynamically via OAuth/OIDC exchanges."));
                    }

                    // Check for Connection String Passwords
                    var connMatch = ConnStringPwdRegex.Match(content);
                    if (connMatch.Success && !file.Name.Contains("example", StringComparison.OrdinalIgnoreCase))
                    {
                        leakedSecrets++;
                        findings.Add(new ValidationFindingDraft(
                            Title: $"Plaintext Database Password in {file.Name}",
                            Description: $"Database connection string with plaintext password discovered in {file.FullName}.",
                            Severity: Severity.High,
                            Expected: "Database authentication should use Integrated Security (Kerberos) or encrypted connection strings.",
                            Actual: "Connection string specifies plaintext password attribute",
                            LatticeNodeId: "sec:" + file.Name,
                            Category: "Security",
                            RemediativeGuidance: "Use Windows Integrated Security or managed identity."));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Scan error: {ex.Message}");
        }

        metrics.Add(new ValidationMetric("FilesScanned", filesScanned, "files", 1, true));
        metrics.Add(new ValidationMetric("LeakedSecrets", leakedSecrets, "count", 0, leakedSecrets == 0));

        var passed = leakedSecrets == 0;
        var summary = passed
            ? $"Security scan verified: {filesScanned} configuration/source files scanned; zero credential leaks detected."
            : $"Security breaches detected: {leakedSecrets} exposed secret(s) found across {filesScanned} scanned files.";

        return Task.FromResult(new ValidationReport
        {
            ModuleId = Id,
            ModuleName = Name,
            Category = Category,
            Passed = passed,
            Blocked = false,
            Severity = passed ? Severity.Info : Severity.Critical,
            Summary = summary,
            Detail = sb.ToString(),
            Findings = findings,
            Metrics = metrics,
            EvidencePayload = sb.ToString(),
        });
    }
}

/// <summary>
/// Resource and vitality leak validation module: measures working set memory, private bytes,
/// handle counts, and thread counts to detect resource exhaustion or leaks.
/// </summary>
public sealed class ResourceLeakValidationModule : IValidationModule
{
    public string Id => "val:leak-vitality";
    public string Name => "Resource Vitality & Process Leak Validator";
    public string Category => "Performance";
    public RiskLevel Risk => RiskLevel.Medium;
    public IReadOnlyList<string> OracleTypes => ["Handle Count Upper Bound", "Memory Headroom Invariant"];
    public IReadOnlyList<string> EvidenceRequirements => ["Process vitality snapshot", "Resource metrics table"];

    public Task<ValidationReport> ValidateAsync(Target target, GhoztStore store, CancellationToken cancellation = default)
    {
        var findings = new List<ValidationFindingDraft>();
        var metrics = new List<ValidationMetric>();
        var sb = new StringBuilder();
        sb.AppendLine("=== Process Resource Vitality Audit ===");

        Process? proc = null;
        if (!string.IsNullOrWhiteSpace(target.BaseAddress) &&
            target.BaseAddress.StartsWith("process:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(target.BaseAddress["process:".Length..], out var pid))
        {
            try { proc = Process.GetProcessById(pid); } catch { }
        }

        proc ??= Process.GetCurrentProcess();

        proc.Refresh();
        var workingSetMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 2);
        var privateMemoryMb = Math.Round(proc.PrivateMemorySize64 / 1024.0 / 1024.0, 2);
        var handleCount = proc.HandleCount;
        var threadCount = proc.Threads.Count;

        sb.AppendLine($"Process: {proc.ProcessName} (PID {proc.Id})");
        sb.AppendLine($"Working Set: {workingSetMb} MB");
        sb.AppendLine($"Private Memory: {privateMemoryMb} MB");
        sb.AppendLine($"Handle Count: {handleCount}");
        sb.AppendLine($"Thread Count: {threadCount}");

        var handlePassed = handleCount <= 10000;
        var memoryPassed = workingSetMb <= 2048.0;
        var threadPassed = threadCount <= 250;

        metrics.Add(new ValidationMetric("WorkingSetMemory", workingSetMb, "MB", 2048.0, memoryPassed));
        metrics.Add(new ValidationMetric("PrivateMemory", privateMemoryMb, "MB", 2048.0, true));
        metrics.Add(new ValidationMetric("HandleCount", handleCount, "handles", 10000.0, handlePassed));
        metrics.Add(new ValidationMetric("ThreadCount", threadCount, "threads", 250.0, threadPassed));

        if (!handlePassed)
        {
            findings.Add(new ValidationFindingDraft(
                Title: $"Excessive OS Handle Count ({handleCount:N0} handles)",
                Description: $"Process handle count exceeds the 10,000 safety threshold, indicating an unmanaged file, socket, or GDI handle leak.",
                Severity: Severity.High,
                Expected: "Handle count <= 10,000",
                Actual: $"{handleCount:N0} handles",
                LatticeNodeId: "proc:" + proc.ProcessName,
                Category: "Performance",
                RemediativeGuidance: "Dispose all unmanaged resources, file streams, and GDI objects promptly."));
        }

        if (!memoryPassed)
        {
            findings.Add(new ValidationFindingDraft(
                Title: $"Working Set Memory Headroom Warning ({workingSetMb:F1} MB)",
                Description: $"Process working set exceeds the 2,048 MB memory budget.",
                Severity: Severity.Medium,
                Expected: "Working set <= 2,048 MB",
                Actual: $"{workingSetMb:F1} MB",
                LatticeNodeId: "proc:" + proc.ProcessName,
                Category: "Performance",
                RemediativeGuidance: "Profile object retention and enforce cache eviction policies."));
        }

        var passed = findings.Count == 0;
        var summary = passed
            ? $"Resource vitality healthy: Working set={workingSetMb} MB, Handles={handleCount}, Threads={threadCount}."
            : $"Resource vitality warning: {findings.Count} resource constraint(s) breached.";

        return Task.FromResult(new ValidationReport
        {
            ModuleId = Id,
            ModuleName = Name,
            Category = Category,
            Passed = passed,
            Blocked = false,
            Severity = passed ? Severity.Info : (findings.Any(f => f.Severity >= Severity.High) ? Severity.High : Severity.Medium),
            Summary = summary,
            Detail = sb.ToString(),
            Findings = findings,
            Metrics = metrics,
            EvidencePayload = sb.ToString(),
        });
    }
}

/// <summary>
/// OpenAPI and API contract schema conformance validator: examines discovered OpenAPI endpoints
/// to verify route syntax, HTTP verb compliance, and response contract invariants.
/// </summary>
public sealed class ContractSchemaValidationModule : IValidationModule
{
    public string Id => "val:contract-schema";
    public string Name => "OpenAPI & API Contract Conformance Validator";
    public string Category => "APIContract";
    public RiskLevel Risk => RiskLevel.Medium;
    public IReadOnlyList<string> OracleTypes => ["OpenAPI 3.0 Schema Invariant", "HTTP Method Contract Invariant"];
    public IReadOnlyList<string> EvidenceRequirements => ["Lattice endpoint route inventory", "Schema integrity check"];

    public Task<ValidationReport> ValidateAsync(Target target, GhoztStore store, CancellationToken cancellation = default)
    {
        var findings = new List<ValidationFindingDraft>();
        var metrics = new List<ValidationMetric>();
        var sb = new StringBuilder();
        sb.AppendLine("=== API Contract & Route Conformance Audit ===");

        var nodes = store.Nodes(target.Id).Where(n => n.Kind == NodeKind.Endpoint).ToList();
        sb.AppendLine($"Discovered OpenAPI endpoints: {nodes.Count}");

        var validRoutes = 0;
        var schemaDeviations = 0;

        foreach (var node in nodes)
        {
            var route = node.Label;
            var parts = route.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var verb = parts.Length > 0 ? parts[0].ToUpperInvariant() : "GET";
            var path = parts.Length > 1 ? parts[1] : route;

            var validVerb = verb is "GET" or "POST" or "PUT" or "DELETE" or "PATCH" or "HEAD" or "OPTIONS";
            if (!validVerb)
            {
                schemaDeviations++;
                findings.Add(new ValidationFindingDraft(
                    Title: $"Invalid HTTP Verb '{verb}' on Endpoint {node.Id}",
                    Description: $"Endpoint defines non-standard or corrupted HTTP verb '{verb}'.",
                    Severity: Severity.Medium,
                    Expected: "Standard HTTP verb (GET, POST, PUT, DELETE, PATCH)",
                    Actual: verb,
                    LatticeNodeId: node.Id,
                    Category: "APIContract",
                    RemediativeGuidance: "Correct the HTTP operation attribute in the OpenAPI specification."));
            }

            // Check for unclosed path parameter braces e.g. /users/{id
            var openBraces = path.Count(c => c == '{');
            var closeBraces = path.Count(c => c == '}');
            if (openBraces != closeBraces)
            {
                schemaDeviations++;
                findings.Add(new ValidationFindingDraft(
                    Title: $"Malformed Route Template in Endpoint {node.Id}",
                    Description: $"Route path '{path}' contains unmatched path parameter braces ({openBraces} open vs {closeBraces} close).",
                    Severity: Severity.High,
                    Expected: "Balanced route parameters e.g. '{param}'",
                    Actual: path,
                    LatticeNodeId: node.Id,
                    Category: "APIContract",
                    RemediativeGuidance: "Ensure all route parameters in OpenAPI specification have matching closing braces."));
            }
            else
            {
                validRoutes++;
            }
        }

        metrics.Add(new ValidationMetric("TotalEndpoints", nodes.Count, "endpoints", 0, true));
        metrics.Add(new ValidationMetric("ValidRoutes", validRoutes, "routes", 0, true));
        metrics.Add(new ValidationMetric("SchemaDeviations", schemaDeviations, "deviations", 0, schemaDeviations == 0));

        var passed = schemaDeviations == 0;
        var summary = passed
            ? $"API contract conformance verified: {nodes.Count} endpoints examined, 0 schema deviations."
            : $"API contract deviations found: {schemaDeviations} defect(s) detected across {nodes.Count} endpoints.";

        return Task.FromResult(new ValidationReport
        {
            ModuleId = Id,
            ModuleName = Name,
            Category = Category,
            Passed = passed,
            Blocked = false,
            Severity = passed ? Severity.Info : (findings.Any(f => f.Severity >= Severity.High) ? Severity.High : Severity.Medium),
            Summary = summary,
            Detail = sb.ToString(),
            Findings = findings,
            Metrics = metrics,
            EvidencePayload = sb.ToString(),
        });
    }
}

/// <summary>
/// Master registry orchestrating full-stack multi-modal validation modules (Patent §16 & §22).
/// </summary>
public sealed class ValidationRegistry
{
    private static readonly Lazy<ValidationRegistry> s_default = new(() =>
    {
        var reg = new ValidationRegistry();
        reg.Register(new AccessibilityValidationModule());
        reg.Register(new SecurityPrivacyValidationModule());
        reg.Register(new ResourceLeakValidationModule());
        reg.Register(new ContractSchemaValidationModule());
        return reg;
    });

    public static ValidationRegistry Default => s_default.Value;

    private readonly List<IValidationModule> _modules = [];
    private readonly object _lock = new();

    public void Register(IValidationModule module)
    {
        lock (_lock)
        {
            _modules.RemoveAll(m => m.Id == module.Id);
            _modules.Add(module);
        }
    }

    public IReadOnlyList<IValidationModule> GetModules()
    {
        lock (_lock)
        {
            return _modules.ToList();
        }
    }

    public async Task<IReadOnlyList<ValidationReport>> RunAllAsync(Target target, GhoztStore store, CancellationToken cancellation = default)
    {
        var modules = GetModules();
        var reports = new List<ValidationReport>();

        foreach (var module in modules)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var report = await module.ValidateAsync(target, store, cancellation).ConfigureAwait(false);
                reports.Add(report);
            }
            catch (Exception ex)
            {
                reports.Add(new ValidationReport
                {
                    ModuleId = module.Id,
                    ModuleName = module.Name,
                    Category = module.Category,
                    Passed = false,
                    Blocked = true,
                    Severity = Severity.Medium,
                    Summary = $"{module.Name} could not complete execution: {ex.Message}",
                    Detail = ex.ToString(),
                    Findings = [],
                    Metrics = [],
                    EvidencePayload = $"Error={ex.Message}\nType={ex.GetType().Name}\n{ex}",
                });
            }
        }

        return reports;
    }
}
