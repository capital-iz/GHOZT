using System.Diagnostics;
using System.Text.Json;

namespace Ghozt.Core;

public sealed record OracleResult
{
    public required bool Passed { get; init; }
    public required string RuleName { get; init; }
    public required Severity Severity { get; init; }
    public required string Title { get; init; }
    public required string Expected { get; init; }
    public required string Actual { get; init; }
    public required string Fingerprint { get; init; }
    public string Detail { get; init; } = "";
}

/// <summary>
/// Real-time examination oracles that evaluate target health, UI integrity,
/// unhandled exception popups, process responsiveness, and functional expectations.
/// </summary>
public sealed class OracleEngine
{
    private static readonly string[] ExceptionKeywords =
    [
        "NullReferenceException",
        "System.Exception",
        "IndexOutOfRangeException",
        "ArgumentException",
        "AccessViolationException",
        "StackOverflowException",
        "Unhandled exception",
        "Fatal error",
        "Fatal error occurred",
        "Application error",
        "has stopped working",
        "crash",
    ];

    private static readonly string[] ErrorDialogTitles =
    [
        "Error",
        "Exception",
        "Unhandled Exception",
        "Fatal Error",
        "Application Error",
        "Crash",
        "Fault",
        "WerFault",
    ];

    /// <summary>
    /// Evaluates target health, UI state, and process vitality against standard invariants.
    /// </summary>
    public static IReadOnlyList<OracleResult> Evaluate(
        TargetSupervisor? supervisor,
        UiElementSnapshot? uiTree,
        UiActionIntent? lastAction = null,
        UiActionResult? lastResult = null)
    {
        var results = new List<OracleResult>();

        // 1. Process Vitality Oracle
        if (supervisor is not null)
        {
            var procResult = CheckProcessVitality(supervisor);
            if (procResult is not null)
            {
                results.Add(procResult);
            }
        }

        // 2. UI Tree Error Dialog / Crash Dialog Oracle
        if (uiTree is not null)
        {
            var dialogResult = CheckErrorDialogs(uiTree);
            if (dialogResult is not null)
            {
                results.Add(dialogResult);
            }

            // 3. UI Surface Collapse / Empty Tree Oracle
            var surfaceResult = CheckSurfaceIntegrity(uiTree);
            if (surfaceResult is not null)
            {
                results.Add(surfaceResult);
            }
        }

        // 4. Action Execution Oracle
        if (lastAction is not null && lastResult is not null)
        {
            var actionOracle = CheckActionExecution(lastAction, lastResult);
            if (actionOracle is not null)
            {
                results.Add(actionOracle);
            }
        }

        return results;
    }

    private static OracleResult? CheckProcessVitality(TargetSupervisor supervisor)
    {
        if (supervisor.HasExited)
        {
            var exitCode = supervisor.ExitCode ?? -1;
            return new OracleResult
            {
                Passed = false,
                RuleName = "Process.Vitality",
                Severity = Severity.Critical,
                Title = $"Target Process Terminated Unexpectedly (Exit Code: {exitCode})",
                Expected = "Target process remains running and healthy throughout the examination sweep.",
                Actual = $"Target process (PID {supervisor.ProcessId}) exited prematurely with exit code {exitCode}.",
                Fingerprint = $"crash:exit_code_{exitCode}",
                Detail = supervisor.StandardError.Length > 0 
                    ? $"Standard Error:\n{supervisor.StandardError}" 
                    : $"Process exited. Standard output length: {supervisor.StandardOutput.Length} chars.",
            };
        }

        if (!supervisor.IsResponding)
        {
            return new OracleResult
            {
                Passed = false,
                RuleName = "Process.Responsiveness",
                Severity = Severity.High,
                Title = "Target Main Thread Unresponsive / UI Hang Detected",
                Expected = "Target process window message queue responds promptly to operating system messages.",
                Actual = $"Process PID {supervisor.ProcessId} has stopped processing messages (Responding == false).",
                Fingerprint = "hang:window_unresponsive",
                Detail = "The target window is hanging or deadlocked in a synchronous wait or heavy compute loop.",
            };
        }

        return null;
    }

    private static OracleResult? CheckErrorDialogs(UiElementSnapshot uiTree)
    {
        var errorNodes = new List<UiElementSnapshot>();
        TraverseAndFindErrors(uiTree, errorNodes);

        if (errorNodes.Count > 0)
        {
            var detailNode = errorNodes.FirstOrDefault(n => n.ControlType != "Window" && n.ControlType != "Pane") ?? errorNodes[0];
            var summaryText = !string.IsNullOrWhiteSpace(detailNode.Value) ? detailNode.Value : detailNode.Name;
            var snippet = summaryText.Length > 80 ? summaryText[..80] + "…" : summaryText;

            return new OracleResult
            {
                Passed = false,
                RuleName = "UI.UnhandledException",
                Severity = Severity.Critical,
                Title = $"Unhandled Exception or Error Dialog Detected: {errorNodes[0].Name}",
                Expected = "Target executes without unhandled exception dialogs, crash alerts, or fatal fault windows.",
                Actual = $"Error indicator displayed: \"{snippet}\" in {detailNode.ControlType} '{detailNode.Name}'.",
                Fingerprint = $"ui_error:{errorNodes[0].Name}:{Hashing.Sha256(summaryText)[..32]}",
                Detail = $"Detected error control:\nID: {detailNode.Id}\nName: {detailNode.Name}\nClassName: {detailNode.ClassName}\nValue: {detailNode.Value}",
            };
        }

        return null;
    }

    private static void TraverseAndFindErrors(
        UiElementSnapshot node,
        List<UiElementSnapshot> findings,
        HashSet<UiElementSnapshot>? visited = null)
    {
        visited ??= [];
        if (!visited.Add(node))
        {
            return;
        }

        // Check window / dialog title
        if (node.ControlType is "Window" or "Pane" &&
            ErrorDialogTitles.Any(t => node.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(node);
        }
        else
        {
            // Check text content or name for exception keywords
            var textToCheck = $"{node.Name} {node.Value}";
            if (ExceptionKeywords.Any(kw => textToCheck.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(node);
            }
        }

        foreach (var child in node.Children)
        {
            TraverseAndFindErrors(child, findings, visited);
        }
    }

    private static OracleResult? CheckSurfaceIntegrity(UiElementSnapshot uiTree)
    {
        // An active window with 0 child elements and 0 interactable controls indicates blank/failed render
        if (uiTree.Children.Count == 0 && uiTree.SupportedPatterns.Count == 0)
        {
            return new OracleResult
            {
                Passed = false,
                RuleName = "UI.SurfaceIntegrity",
                Severity = Severity.Medium,
                Title = "UI Surface Collapsed or Render Failed",
                Expected = "Root window renders child elements and accessible controls.",
                Actual = $"Root element '{uiTree.Name}' ({uiTree.ControlType}) has 0 children and 0 supported patterns.",
                Fingerprint = "surface:collapsed_tree",
                Detail = $"Window Bounds: {uiTree.Bounds.Width}x{uiTree.Bounds.Height}, Class: {uiTree.ClassName}",
            };
        }

        return null;
    }

    private static OracleResult? CheckActionExecution(UiActionIntent intent, UiActionResult result)
    {
        if (!result.Success)
        {
            var durationMs = (int)result.Elapsed.TotalMilliseconds;
            var err = result.Error ?? result.Message;
            return new OracleResult
            {
                Passed = false,
                RuleName = "Action.ExecutionFailed",
                Severity = Severity.Medium,
                Title = $"Action Failed to Execute: {intent.Kind} on {intent.TargetName}",
                Expected = $"Action '{intent.Description}' executes successfully on target element.",
                Actual = $"Action execution failed after {durationMs}ms: {err}",
                Fingerprint = $"action_failed:{intent.Kind}:{intent.TargetId}:{intent.TargetName}",
                Detail = $"Action: {intent.Kind}\nTarget: {intent.TargetName} ({intent.TargetId})\nDuration: {durationMs}ms\nError: {err}",
            };
        }

        return null;
    }
}
