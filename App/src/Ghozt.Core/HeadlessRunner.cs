using System.IO;
using System.Text.Json;

namespace Ghozt.Core;

public sealed record HeadlessOptions
{
    public string? TargetIdentifier { get; init; }
    public string Mode { get; init; } = "smoke";
    public string? SarifPath { get; init; }
    public string? JunitPath { get; init; }
    public string? ClearanceCertPath { get; init; }
    public string? DatabasePath { get; init; }
    public bool RunSelfTest { get; init; }
}

/// <summary>
/// Autonomous Headless Execution Engine for CLI, scripts, and CI/CD pipelines (Patent §38).
/// Executes sweeps, evaluates clearance gates, exports standard reports, and emits deterministic exit codes.
/// </summary>
public sealed class HeadlessRunner
{
    private readonly GhoztStore _store;
    private readonly TextWriter _out;
    private readonly TextWriter _err;

    public HeadlessRunner(GhoztStore store, TextWriter? standardOutput = null, TextWriter? standardError = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _out = standardOutput ?? Console.Out;
        _err = standardError ?? Console.Error;
    }

    /// <summary>
    /// Parses string array into HeadlessOptions.
    /// </summary>
    public static HeadlessOptions ParseArgs(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? targetId = null;
        string mode = "smoke";
        string? sarif = null;
        string? junit = null;
        string? cert = null;
        string? db = null;
        bool selfTest = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--sweep", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                targetId = args[++i];
            }
            else if (arg.Equals("--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                mode = args[++i];
            }
            else if (arg.Equals("--sarif", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                sarif = args[++i];
            }
            else if (arg.Equals("--junit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                junit = args[++i];
            }
            else if (arg.Equals("--clearance-cert", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cert = args[++i];
            }
            else if ((arg.Equals("--database", StringComparison.OrdinalIgnoreCase) || arg.Equals("--db", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                db = args[++i];
            }
            else if (arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase))
            {
                selfTest = true;
            }
        }

        return new HeadlessOptions
        {
            TargetIdentifier = targetId,
            Mode = mode,
            SarifPath = sarif,
            JunitPath = junit,
            ClearanceCertPath = cert,
            DatabasePath = db,
            RunSelfTest = selfTest,
        };
    }

    /// <summary>
    /// Runs the headless execution workflow and returns the process exit code:
    /// 0: Success / Cleared
    /// 1: Defects Found / Clearance Refused
    /// 2: Target Unreachable or Missing
    /// 3: Invalid Arguments
    /// </summary>
    public async Task<int> RunAsync(HeadlessOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        _out.WriteLine("================================================================================");
        _out.WriteLine("  GHOZT - DEV TESTER :: AUTONOMOUS HEADLESS EXAMINATION RUNNER");
        _out.WriteLine("  Publisher: Capital-Iz | Sovereign Quality Verification Engine");
        _out.WriteLine("================================================================================");

        // Branch 1: Autonomous Self-Test Execution
        if (options.RunSelfTest)
        {
            _out.WriteLine("==> Executing Autonomous 12-Domain Self-Test & Quality Certification...");
            var selfTestEngine = new SelfTestEngine(_store);
            var cert = await selfTestEngine.RunFullSelfTestAsync(ct).ConfigureAwait(false);

            _out.WriteLine($"==> Self-Test Result: {cert.Verdict} ({cert.PassedDomains}/{cert.TotalDomains} domains passed)");
            _out.WriteLine($"    Certificate ID: {cert.CertificateId}");
            _out.WriteLine($"    Digest SHA-256: {cert.Sha256Digest}");

            if (!string.IsNullOrWhiteSpace(options.ClearanceCertPath))
            {
                var dir = Path.GetDirectoryName(options.ClearanceCertPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(options.ClearanceCertPath, JsonSerializer.Serialize(cert, new JsonSerializerOptions { WriteIndented = true }));
                _out.WriteLine($"    Exported Certificate: {options.ClearanceCertPath}");
            }

            return cert.PassedDomains == cert.TotalDomains ? 0 : 1;
        }

        // Branch 2: Target Sweep
        if (string.IsNullOrWhiteSpace(options.TargetIdentifier))
        {
            _err.WriteLine("ERROR: Target identifier missing. Specify --sweep <target-id-or-name> or --self-test.");
            return 3;
        }

        var targets = _store.Targets();
        var target = targets.FirstOrDefault(t =>
            t.Id.ToString().Equals(options.TargetIdentifier, StringComparison.OrdinalIgnoreCase) ||
            t.Name.Equals(options.TargetIdentifier, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            _err.WriteLine($"ERROR: Target '{options.TargetIdentifier}' not found in evidence store.");
            return 2;
        }

        _out.WriteLine($"==> Target: {target.Name} ({target.Id})");
        _out.WriteLine($"    Environment: {target.Environment} | Access: {target.Access}");
        _out.WriteLine($"    Base Address: {target.BaseAddress ?? "(none)"}");

        if (!target.Reachable)
        {
            _err.WriteLine($"ERROR: Target '{target.Name}' is marked unreachable.");
            return 2;
        }

        // Execute Sweep
        _out.WriteLine($"==> Starting autonomous {options.Mode} sweep...");
        var sweepEngine = new SweepEngine(_store);
        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = $"Headless {options.Mode} sweep",
            Mode = options.Mode,
            State = RunState.Running,
            StartedAt = DateTimeOffset.Now,
        };

        var completedSweep = await sweepEngine.RunAsync(sweep, target, progress: null, ct).ConfigureAwait(false);
        _out.WriteLine($"==> Sweep completed: State={completedSweep.State} | Visited={completedSweep.StatesVisited} | Deviations={completedSweep.DeviationsFound}");

        // Retrieve findings and all target sweeps
        var findings = _store.Findings(target.Id);
        var allSweeps = _store.Sweeps(target.Id);

        // Export SARIF if requested
        if (!string.IsNullOrWhiteSpace(options.SarifPath))
        {
            var dir = Path.GetDirectoryName(options.SarifPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            var sarifContent = ReportGenerator.BuildSarif(target, findings);
            File.WriteAllText(options.SarifPath, sarifContent);
            _out.WriteLine($"==> Exported SARIF v2.1.0 report: {options.SarifPath}");
        }

        // Export JUnit if requested
        if (!string.IsNullOrWhiteSpace(options.JunitPath))
        {
            var dir = Path.GetDirectoryName(options.JunitPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            var junitContent = ReportGenerator.BuildJUnitXml(target, allSweeps, findings);
            File.WriteAllText(options.JunitPath, junitContent);
            _out.WriteLine($"==> Exported JUnit XML report: {options.JunitPath}");
        }

        // Evaluate Release Clearance Gates
        _out.WriteLine("==> Evaluating Release Clearance Gates...");
        var clearanceEngine = new ClearanceEngine(_store);
        var decision = clearanceEngine.EvaluateTarget(target, "Ghozt.HeadlessRunner@v1");

        var passedGates = decision.Gates.Count(g => g.Passed);
        var totalGates = Math.Max(1, decision.Gates.Count);
        var scorePercent = (double)passedGates / totalGates * 100.0;
        _out.WriteLine($"==> Clearance Outcome: {decision.Outcome} (Score: {scorePercent:F1}%)");
        foreach (var gate in decision.Gates)
        {
            _out.WriteLine($"    [{ (gate.Passed ? "PASS" : (gate.Mandatory ? "FAIL" : "WARN")) }] {gate.Name}: {gate.Result}");
        }

        // Export Clearance Certificate if requested
        if (!string.IsNullOrWhiteSpace(options.ClearanceCertPath))
        {
            var dir = Path.GetDirectoryName(options.ClearanceCertPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(decision, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(options.ClearanceCertPath, json);
            _out.WriteLine($"==> Exported Clearance Digest: {options.ClearanceCertPath}");
        }

        var isCleared = decision.Outcome is ClearanceOutcome.Cleared or ClearanceOutcome.ConditionallyCleared;
        _out.WriteLine($"==> Headless Run Complete: Result={ (isCleared ? "CLEARED" : "FAILED") }");

        return isCleared ? 0 : 1;
    }
}
