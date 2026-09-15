using System.Globalization;
using System.IO;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class SecurityAuditAndCompatibilityMatrixTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public SecurityAuditAndCompatibilityMatrixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_sec_compat_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_sec_compat_test.db");
        _store = new GhoztStore(_tempDbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void SecurityAuditEngine_All8Areas_EvaluatesAndRecordsFindings()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Compliant Test Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.StateChanging,
            BaseAddress = "https://localhost:5001",
            LaunchCommand = "ghozt.exe --test-run",
            Notes = "Standard test target without secrets",
        };
        _store.Save(target);

        // Issue a valid authority grant
        var grantId = _store.NextId(IdKind.Authority);
        var grant = AuthorityKernel.IssueGrant(
            grantId: grantId,
            targetId: targetId,
            actor: "operator",
            maxLevel: ExecutionIsolationLevel.L2_StateChanging,
            allowedActionClasses: new[] { "read", "mutate" },
            expiresAt: DateTimeOffset.Now.AddHours(2),
            approver: "Rea",
            scopeJustification: "Security audit compliance validation",
            sensitivity: DataSensitivityTier.Internal);
        _store.Save(grant);

        var report = SecurityAuditEngine.RunAudit(target, _store, grant);

        Assert.Equal(8, report.TotalAreasChecked);
        Assert.Equal(8, report.PassedAreas);
        Assert.Equal(0, report.FailedAreas);
        Assert.True(report.Passed);
        Assert.NotEmpty(report.AuditSignature);
        Assert.Equal(8, report.AreaResults.Count);
    }

    [Fact]
    public void SecurityAuditEngine_InsecureHttpEndpoint_EmitsTransportFinding()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Insecure Staging Target",
            Kind = "API",
            Environment = TargetEnvironment.Staging,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "http://insecure.staging.example.com",
            LaunchCommand = "api-runner --port 80",
            Notes = "Testing plaintext transport failure",
        };
        _store.Save(target);

        var report = SecurityAuditEngine.RunAudit(target, _store, null);

        Assert.False(report.Passed);
        var transportArea = Assert.Single(report.AreaResults, a => a.Area == SecurityValidationArea.TransportAndTrust);
        Assert.False(transportArea.Passed);
        Assert.Equal(Severity.High, transportArea.Severity);
        Assert.Equal("SEC.TRN.PLAINTEXT_HTTP", transportArea.DiagnosticCode);

        // Verify finding was recorded in the evidence store
        var findings = _store.Findings(targetId);
        Assert.Contains(findings, f => f.Title.Contains("Transport Security", StringComparison.OrdinalIgnoreCase) && f.Severity == Severity.High);
    }

    [Fact]
    public void SecurityAuditEngine_UnsanitizedSecret_FlagsCredentialFinding()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Leaky Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "https://localhost:8080",
            LaunchCommand = "app.exe",
            Notes = "Target configured with api_key = sk_live_9999888877776666555544443333",
        };
        _store.Save(target);

        var report = SecurityAuditEngine.RunAudit(target, _store, null);

        Assert.False(report.Passed);
        var secretsArea = Assert.Single(report.AreaResults, a => a.Area == SecurityValidationArea.SecretsAndCredentials);
        Assert.False(secretsArea.Passed);
        Assert.Equal("SEC.SCR.LEAK_DETECTED", secretsArea.DiagnosticCode);
    }

    [Fact]
    public void SecurityAuditEngine_MaliciousCommandInjection_FlagsCriticalInput()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Injection Vulnerable Target",
            Kind = "CLI",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "https://localhost:9000",
            LaunchCommand = "runner.exe && rm -rf /",
            Notes = "Command contains shell injection delimiter",
        };
        _store.Save(target);

        var report = SecurityAuditEngine.RunAudit(target, _store, null);

        Assert.False(report.Passed);
        var inputArea = Assert.Single(report.AreaResults, a => a.Area == SecurityValidationArea.InputHandling);
        Assert.False(inputArea.Passed);
        Assert.Equal(Severity.Critical, inputArea.Severity);
        Assert.Equal("SEC.INP.INJECTION_SUSPECT", inputArea.DiagnosticCode);
    }

    [Fact]
    public void CompatibilityMatrixEngine_All5Axes_PassOnStandardHost()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Standard Compatibility Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "https://localhost:5000",
        };
        _store.Save(target);

        var report = CompatibilityMatrixEngine.EvaluateCompatibility(target, _store);

        Assert.Equal(5, report.TotalAxesAssayed);
        Assert.Equal(5, report.PassedAxes);
        Assert.True(report.Passed);
        Assert.NotEmpty(report.MatrixDigest);
        Assert.All(report.AxisResults, a => Assert.True(a.Passed));
    }

    [Fact]
    public void CompatibilityMatrixEngine_LocaleInvariance_SurvivesMultipleCultures()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Internationalization Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
        };
        _store.Save(target);

        var report = CompatibilityMatrixEngine.EvaluateCompatibility(target, _store);
        var localeAxis = Assert.Single(report.AxisResults, a => a.Axis == CompatibilityAxis.LocaleAndCulture);

        Assert.True(localeAxis.Passed);
        Assert.Contains("Numeric format invariance", localeAxis.Assessment);
        Assert.Equal("OK", localeAxis.Diagnostics["InvariantNumberRoundtrip"]);
        Assert.Equal("OK", localeAxis.Diagnostics["Iso8601TimestampRoundtrip"]);
    }

    [Fact]
    public void ClearanceEngine_IncludesGates11And12_AndEnforcesZeroViolations()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Clearance Gates Target",
            Kind = "Desktop",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            BaseAddress = "https://localhost:8080",
            LaunchCommand = "ghozt.exe",
        };
        _store.Save(target);

        var engine = new ClearanceEngine(_store);
        var decision = engine.EvaluateTarget(target, "operator");

        // Assert Gate 11 (Compatibility Matrix) is present and mandatory
        var compatGate = Assert.Single(decision.Gates, g => g.Id == "GATE.COMPATIBILITY_MATRIX");
        Assert.True(compatGate.Mandatory);
        Assert.True(compatGate.Passed);
        Assert.Contains("Table 38", compatGate.PolicySource);

        // Assert Gate 12 (Security Audit) is present and mandatory
        var secGate = Assert.Single(decision.Gates, g => g.Id == "GATE.SECURITY_AUDIT");
        Assert.True(secGate.Mandatory);
        Assert.True(secGate.Passed);
        Assert.Contains("Table 36", secGate.PolicySource);
    }

    [Fact]
    public void ClearanceEngine_Gate12_BlocksClearanceWhenSecurityBreached()
    {
        var targetId = _store.NextId(IdKind.Target);
        var target = new Target
        {
            Id = targetId,
            Name = "Breached Production Target",
            Kind = "API",
            Environment = TargetEnvironment.Production,
            Access = AccessClass.StateChanging, // Requires grant, but none provided -> Auth breach
            BaseAddress = "http://insecure.production.internal", // HTTP in production -> Transport breach
            LaunchCommand = "api.exe; format c:", // Injection delimiter -> Input breach
            Notes = "Exposed key api_key = sk_live_999999999999999999999999", // Leaked key -> Secrets breach
            Reachable = true,
        };
        _store.Save(target);

        var engine = new ClearanceEngine(_store);
        var decision = engine.EvaluateTarget(target, "security-reviewer");

        var secGate = Assert.Single(decision.Gates, g => g.Id == "GATE.SECURITY_AUDIT");
        Assert.False(secGate.Passed);
        Assert.Contains("Failed", secGate.Result);

        // Overall outcome must be Denied due to mandatory gate failure
        Assert.Equal(ClearanceOutcome.Denied, decision.Outcome);
    }
}
