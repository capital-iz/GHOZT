using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace Ghozt.Core;

public enum UiActionKind
{
    Click,
    SetText,
    Toggle,
    Select,
    Scroll,
    Focus,
}

public sealed record UiElementSnapshot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ClassName { get; init; }
    public required string ControlType { get; init; }
    public required Rect Bounds { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsOffscreen { get; init; }
    public string Value { get; init; } = "";
    public List<string> SupportedPatterns { get; init; } = [];
    public List<UiElementSnapshot> Children { get; init; } = [];
}

public sealed record UiActionIntent
{
    public required UiActionKind Kind { get; init; }
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public string TargetControlType { get; init; } = "";
    public string InputValue { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed record UiActionResult(
    bool Success,
    string Message,
    TimeSpan Elapsed,
    string? Error = null);

public interface IUiaDriver
{
    Task<UiElementSnapshot?> CaptureTreeAsync(IntPtr windowHandle, int maxDepth = 6);
    Task<IReadOnlyList<UiActionIntent>> DiscoverActionsAsync(UiElementSnapshot root, bool allowDestructive);
    Task<UiActionResult> ExecuteActionAsync(IntPtr windowHandle, UiActionIntent intent, bool allowDestructive);
}

public sealed class UiaDriver : IUiaDriver
{
    private readonly HashSet<string> _destructiveKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "remove", "destroy", "purge", "format", "drop table", "truncate", "wipe", "uninstall"
    };

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public Task<UiElementSnapshot?> CaptureTreeAsync(IntPtr windowHandle, int maxDepth = 6)
    {
        return RunOnMtaThread(() =>
        {
            if (windowHandle == IntPtr.Zero) return null;

            AutomationElement? windowElement;
            try
            {
                windowElement = AutomationElement.FromHandle(windowHandle);
            }
            catch (Exception)
            {
                return null;
            }

            if (windowElement is null) return null;

            return BuildSnapshot(windowElement, 0, maxDepth);
        });
    }

    public Task<IReadOnlyList<UiActionIntent>> DiscoverActionsAsync(UiElementSnapshot root, bool allowDestructive)
    {
        return Task.Run<IReadOnlyList<UiActionIntent>>(() =>
        {
            var actions = new List<UiActionIntent>();
            CollectActions(root, actions, allowDestructive);
            return actions;
        });
    }

    public Task<UiActionResult> ExecuteActionAsync(IntPtr windowHandle, UiActionIntent intent, bool allowDestructive)
    {
        return RunOnMtaThread(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();

            if (IsDestructive(intent) && !allowDestructive)
            {
                return new UiActionResult(
                    Success: false,
                    Message: $"Action '{intent.Description}' blocked by safety policy: destructive actions not permitted.",
                    Elapsed: clock.Elapsed,
                    Error: "PolicyGate: DestructiveNotAllowed");
            }

            if (windowHandle == IntPtr.Zero)
            {
                return new UiActionResult(false, "Window handle invalid.", clock.Elapsed, "InvalidWindowHandle");
            }

            SetForegroundWindow(windowHandle);

            AutomationElement? windowElement;
            try
            {
                windowElement = AutomationElement.FromHandle(windowHandle);
            }
            catch (Exception ex)
            {
                return new UiActionResult(false, $"Failed to bind window element: {ex.Message}", clock.Elapsed, ex.GetType().Name);
            }

            if (windowElement is null)
            {
                return new UiActionResult(false, "Window element not found.", clock.Elapsed, "NullWindowElement");
            }

            AutomationElement? targetElement = FindElement(windowElement, intent.TargetId, intent.TargetName);
            if (targetElement is null)
            {
                return new UiActionResult(false, $"Target element '{intent.TargetName}' (id: {intent.TargetId}) not found in tree.", clock.Elapsed, "ElementNotFound");
            }

            try
            {
                switch (intent.Kind)
                {
                    case UiActionKind.Click:
                        if (targetElement.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObj) && invokeObj is InvokePattern invokePattern)
                        {
                            invokePattern.Invoke();
                            return new UiActionResult(true, $"Invoked {intent.TargetName}", clock.Elapsed);
                        }
                        else if (targetElement.TryGetCurrentPattern(TogglePattern.Pattern, out var toggleObj) && toggleObj is TogglePattern togglePattern)
                        {
                            togglePattern.Toggle();
                            return new UiActionResult(true, $"Toggled {intent.TargetName}", clock.Elapsed);
                        }
                        else if (targetElement.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selObj) && selObj is SelectionItemPattern selPattern)
                        {
                            selPattern.Select();
                            return new UiActionResult(true, $"Selected {intent.TargetName}", clock.Elapsed);
                        }
                        break;

                    case UiActionKind.SetText:
                        if (targetElement.TryGetCurrentPattern(ValuePattern.Pattern, out var valObj) && valObj is ValuePattern valPattern)
                        {
                            valPattern.SetValue(intent.InputValue);
                            return new UiActionResult(true, $"Set text of {intent.TargetName} to '{intent.InputValue}'", clock.Elapsed);
                        }
                        break;

                    case UiActionKind.Toggle:
                        if (targetElement.TryGetCurrentPattern(TogglePattern.Pattern, out var togObj) && togObj is TogglePattern togPattern)
                        {
                            togPattern.Toggle();
                            return new UiActionResult(true, $"Toggled {intent.TargetName}", clock.Elapsed);
                        }
                        break;

                    case UiActionKind.Select:
                        if (targetElement.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sObj) && sObj is SelectionItemPattern sPattern)
                        {
                            sPattern.Select();
                            return new UiActionResult(true, $"Selected {intent.TargetName}", clock.Elapsed);
                        }
                        break;
                }

                return new UiActionResult(false, $"Element does not support requested pattern for {intent.Kind}", clock.Elapsed, "PatternNotSupported");
            }
            catch (Exception ex)
            {
                return new UiActionResult(false, $"Execution failed: {ex.Message}", clock.Elapsed, ex.GetType().Name);
            }
        });
    }

    private void CollectActions(UiElementSnapshot node, List<UiActionIntent> actions, bool allowDestructive)
    {
        if (node.IsEnabled && !node.IsOffscreen)
        {
            var isButton = node.ControlType.Equals("Button", StringComparison.OrdinalIgnoreCase) ||
                           node.ControlType.Equals("Hyperlink", StringComparison.OrdinalIgnoreCase);
            var isEdit = node.ControlType.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                         node.ControlType.Equals("Document", StringComparison.OrdinalIgnoreCase);
            var isCheckBox = node.ControlType.Equals("CheckBox", StringComparison.OrdinalIgnoreCase) ||
                             node.ControlType.Equals("RadioButton", StringComparison.OrdinalIgnoreCase);
            var isListItem = node.ControlType.Equals("ListItem", StringComparison.OrdinalIgnoreCase) ||
                             node.ControlType.Equals("TreeItem", StringComparison.OrdinalIgnoreCase) ||
                             node.ControlType.Equals("MenuItem", StringComparison.OrdinalIgnoreCase);

            if (isButton && (node.SupportedPatterns.Contains("InvokePattern") || node.SupportedPatterns.Contains("TogglePattern")))
            {
                var intent = new UiActionIntent
                {
                    Kind = UiActionKind.Click,
                    TargetId = node.Id,
                    TargetName = string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name,
                    TargetControlType = node.ControlType,
                    Description = $"Click button '{node.Name}'"
                };
                if (allowDestructive || !IsDestructive(intent)) actions.Add(intent);
            }
            else if (isEdit && node.SupportedPatterns.Contains("ValuePattern"))
            {
                actions.Add(new UiActionIntent
                {
                    Kind = UiActionKind.SetText,
                    TargetId = node.Id,
                    TargetName = string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name,
                    TargetControlType = node.ControlType,
                    InputValue = "test_input",
                    Description = $"Type into '{node.Name}'"
                });
            }
            else if (isCheckBox && node.SupportedPatterns.Contains("TogglePattern"))
            {
                actions.Add(new UiActionIntent
                {
                    Kind = UiActionKind.Toggle,
                    TargetId = node.Id,
                    TargetName = string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name,
                    TargetControlType = node.ControlType,
                    Description = $"Toggle '{node.Name}'"
                });
            }
            else if (isListItem && node.SupportedPatterns.Contains("SelectionItemPattern"))
            {
                actions.Add(new UiActionIntent
                {
                    Kind = UiActionKind.Select,
                    TargetId = node.Id,
                    TargetName = string.IsNullOrWhiteSpace(node.Name) ? node.Id : node.Name,
                    TargetControlType = node.ControlType,
                    Description = $"Select '{node.Name}'"
                });
            }
        }

        foreach (var child in node.Children)
        {
            CollectActions(child, actions, allowDestructive);
        }
    }

    private bool IsDestructive(UiActionIntent intent)
    {
        return _destructiveKeywords.Any(kw => 
            intent.TargetName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            intent.TargetId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            intent.Description.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static AutomationElement? FindElement(AutomationElement root, string id, string name)
    {
        // 1. Try search by AutomationId
        if (!string.IsNullOrWhiteSpace(id))
        {
            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            var el = root.FindFirst(TreeScope.Subtree, cond);
            if (el is not null) return el;
        }

        // 2. Try search by Name
        if (!string.IsNullOrWhiteSpace(name))
        {
            var cond = new PropertyCondition(AutomationElement.NameProperty, name);
            var el = root.FindFirst(TreeScope.Subtree, cond);
            if (el is not null) return el;
        }

        return null;
    }

    private static UiElementSnapshot BuildSnapshot(AutomationElement el, int currentDepth, int maxDepth)
    {
        var id = SafeGet(() => el.Current.AutomationId, "");
        var name = SafeGet(() => el.Current.Name, "");
        var className = SafeGet(() => el.Current.ClassName, "");
        var controlType = SafeGet(() => el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""), "Unknown");
        var bounds = SafeGet(() => el.Current.BoundingRectangle, Rect.Empty);
        var isEnabled = SafeGet(() => el.Current.IsEnabled, false);
        var isOffscreen = SafeGet(() => el.Current.IsOffscreen, false);

        var patterns = new List<string>();
        if (el.TryGetCurrentPattern(InvokePattern.Pattern, out _)) patterns.Add("InvokePattern");
        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) && vp is ValuePattern valPat)
        {
            patterns.Add("ValuePattern");
        }
        if (el.TryGetCurrentPattern(TogglePattern.Pattern, out _)) patterns.Add("TogglePattern");
        if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _)) patterns.Add("SelectionItemPattern");

        var snapshot = new UiElementSnapshot
        {
            Id = id,
            Name = name,
            ClassName = className,
            ControlType = controlType,
            Bounds = bounds,
            IsEnabled = isEnabled,
            IsOffscreen = isOffscreen,
            SupportedPatterns = patterns,
        };

        if (currentDepth < maxDepth)
        {
            try
            {
                var children = el.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
                foreach (AutomationElement child in children)
                {
                    snapshot.Children.Add(BuildSnapshot(child, currentDepth + 1, maxDepth));
                }
            }
            catch (Exception)
            {
                // UI tree may mutate dynamically during inspection
            }
        }

        return snapshot;
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static Task<T> RunOnMtaThread<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}
