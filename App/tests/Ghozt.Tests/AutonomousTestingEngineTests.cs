using System.IO;
using System.Text;
using System.Windows;
using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class AutonomousTestingEngineTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public AutonomousTestingEngineTests()
    {
        // Sandboxed test isolation: doctrine §25.2 - never pollute live profile databases
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"ghozt_test_{Guid.NewGuid():N}.db");
        _store = new GhoztStore(_tempDbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
            var wal = _tempDbPath + "-wal";
            if (File.Exists(wal)) File.Delete(wal);
            var shm = _tempDbPath + "-shm";
            if (File.Exists(shm)) File.Delete(shm);
        }
        catch
        {
            // Best-effort cleanup of temp test files
        }
    }

    [Fact]
    public void OracleEngineDetectsUnhandledExceptionPopup()
    {
        var errorTree = new UiElementSnapshot
        {
            Id = "dlg1",
            Name = "Unhandled Exception Dialog",
            ClassName = "Dialog",
            ControlType = "Window",
            Bounds = new Rect(100, 100, 400, 250),
            IsEnabled = true,
            IsOffscreen = false,
            Children =
            [
                new UiElementSnapshot
                {
                    Id = "lblError",
                    Name = "NullReferenceException in WorkerTask",
                    ClassName = "TextBlock",
                    ControlType = "Text",
                    Bounds = new Rect(120, 120, 360, 40),
                    Value = "System.NullReferenceException: Object reference not set to an instance of an object.",
                }
            ]
        };

        var results = OracleEngine.Evaluate(supervisor: null, uiTree: errorTree);

        Assert.NotEmpty(results);
        var unhandled = results.FirstOrDefault(r => r.RuleName == "UI.UnhandledException");
        Assert.NotNull(unhandled);
        Assert.False(unhandled.Passed);
        Assert.Equal(Severity.Critical, unhandled.Severity);
        Assert.Contains("Unhandled Exception", unhandled.Title);
        Assert.Contains("NullReferenceException", unhandled.Actual);
    }

    [Fact]
    public void OracleEngineDetectsCollapsedUiSurface()
    {
        var blankWindow = new UiElementSnapshot
        {
            Id = "w1",
            Name = "Blank MainWindow",
            ClassName = "Window",
            ControlType = "Window",
            Bounds = new Rect(0, 0, 800, 600),
            Children = [] // Zero children, zero patterns
        };

        var results = OracleEngine.Evaluate(supervisor: null, uiTree: blankWindow);

        var surfaceResult = results.FirstOrDefault(r => r.RuleName == "UI.SurfaceIntegrity");
        Assert.NotNull(surfaceResult);
        Assert.False(surfaceResult.Passed);
        Assert.Equal(Severity.Medium, surfaceResult.Severity);
        Assert.Contains("Collapsed", surfaceResult.Title);
    }

    [Fact]
    public void OracleEngineDetectsFailedActionExecution()
    {
        var intent = new UiActionIntent
        {
            Kind = UiActionKind.Click,
            TargetId = "btnSubmit",
            TargetName = "Submit",
            Description = "Click Submit button",
        };

        var failedResult = new UiActionResult(
            Success: false,
            Message: "Element not found or invoke pattern unavailable",
            Elapsed: TimeSpan.FromMilliseconds(450),
            Error: "ElementNotFoundException");

        var healthyTree = new UiElementSnapshot
        {
            Id = "root",
            Name = "MainWindow",
            ClassName = "Window",
            ControlType = "Window",
            Bounds = new Rect(0, 0, 800, 600),
            SupportedPatterns = ["InvokePattern"],
        };

        var results = OracleEngine.Evaluate(supervisor: null, uiTree: healthyTree, lastAction: intent, lastResult: failedResult);

        var actionResult = results.FirstOrDefault(r => r.RuleName == "Action.ExecutionFailed");
        Assert.NotNull(actionResult);
        Assert.False(actionResult.Passed);
        Assert.Equal(Severity.Medium, actionResult.Severity);
        Assert.Contains("Submit", actionResult.Title);
    }

    [Fact]
    public void BlackBoxRecorderGeneratesDiagnosticFrameAndSealsEvidence()
    {
        var recorder = new BlackBoxRecorder(_store);

        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Sample Target",
            Kind = "Application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Autonomous Test Sweep",
            Mode = "Autonomous E2E",
        };
        _store.Save(sweep);

        var uiTree = new UiElementSnapshot
        {
            Id = "winMain",
            Name = "Sample App Shell",
            ClassName = "Window",
            ControlType = "Window",
            Bounds = new Rect(0, 0, 1024, 768),
            SupportedPatterns = ["WindowPattern"],
        };

        var step = recorder.RecordStep(
            stepIndex: 1,
            target: target,
            sweep: sweep,
            stateId: "state:001",
            actionDescription: "Verified dashboard render",
            windowHandle: IntPtr.Zero, // Generates diagnostic frame
            uiTree: uiTree);

        Assert.NotNull(step);
        Assert.NotNull(step.ScreenshotEvidenceId);
        Assert.NotNull(step.UiTreeEvidenceId);
        Assert.NotEmpty(step.ScreenshotBase64);
        Assert.True(step.ScreenshotBytes?.Length > 0);

        // Verify artefacts persisted to SQLite store
        var storedEvidence = _store.EvidenceItems(target.Id);
        Assert.True(storedEvidence.Count >= 2);

        var screenshotItem = storedEvidence.First(e => e.Kind == "ui:screenshot/png");
        Assert.True(screenshotItem.Sealed);
        Assert.Equal("Ghozt.AutonomousEngine", screenshotItem.SealedBy);
        Assert.Equal(Hashing.Sha256(step.ScreenshotBytes!), screenshotItem.Sha256);

        var treeItem = storedEvidence.First(e => e.Kind == "ui:snapshot/json");
        Assert.True(treeItem.Sealed);
        Assert.Contains("Sample App Shell", treeItem.Payload);

        // Verify ledger events
        var events = _store.RecentEvents(targetId: target.Id);
        Assert.Contains(events, ev => ev.Code == "UI.STATE" && ev.Message.Contains("Step 001"));
    }

    [Fact]
    public async Task UiaDriverDestructiveKeywordsAreGuarded()
    {
        var driver = new UiaDriver();

        var destructiveIntent = new UiActionIntent
        {
            Kind = UiActionKind.Click,
            TargetId = "btnDeleteAccount",
            TargetName = "Purge Database",
            Description = "Permanently delete database records",
        };

        // When allowDestructive is false, execution must be blocked by safety policy
        var result = await driver.ExecuteActionAsync(IntPtr.Zero, destructiveIntent, allowDestructive: false);

        Assert.False(result.Success);
        Assert.Equal("PolicyGate: DestructiveNotAllowed", result.Error);
        Assert.Contains("blocked by safety policy", result.Message);
    }

    [Fact]
    public void AgenticSweepRunnerResolveExecutablePathWorks()
    {
        // System notepad is guaranteed to exist on Windows
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

        var targetWithCommand = new Target
        {
            Id = GhoztId.Create(IdKind.Target, 99),
            Name = "Notepad App",
            Kind = "Application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            LaunchCommand = $"{notepad} --arg",
        };

        var resolvedPath = AgenticSweepRunner.ResolveExecutablePath(targetWithCommand);
        Assert.Equal(notepad, resolvedPath);

        var targetWithBaseAddress = new Target
        {
            Id = GhoztId.Create(IdKind.Target, 100),
            Name = "Notepad BaseAddress",
            Kind = "Application",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.Interact,
            BaseAddress = notepad,
        };

        var resolvedPath2 = AgenticSweepRunner.ResolveExecutablePath(targetWithBaseAddress);
        Assert.Equal(notepad, resolvedPath2);
    }
}
