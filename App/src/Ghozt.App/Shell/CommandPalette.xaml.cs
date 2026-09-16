using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ghozt.App.Shell;

/// <summary>One row in the command palette.</summary>
public sealed record PaletteEntry(
    string Glyph,
    string Title,
    string Subtitle,
    string Shortcut,
    Action Invoke,
    bool Available = true);

public partial class CommandPalette : UserControl
{
    private ShellViewModel? _shell;

    public CommandPalette()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => _shell = DataContext as ShellViewModel;

        // Focus the query whenever the palette is shown, so it can be typed into immediately rather
        // than needing a click first.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Query.Clear();
                Populate("");
                Dispatcher.BeginInvoke(() => Query.Focus());
            }
        };
    }

    private void Populate(string query)
    {
        if (_shell is null)
        {
            return;
        }

        var entries = new List<PaletteEntry>();

        foreach (var workspace in WorkspaceDefinition.All)
        {
            var definition = workspace;
            entries.Add(new PaletteEntry(
                definition.Glyph,
                definition.Title,
                definition.Purpose,
                definition.Shortcut,
                () => _shell.Workspace = definition.Kind));
        }

        foreach (var target in _shell.Targets)
        {
            var captured = target;
            entries.Add(new PaletteEntry(
                "",
                $"Switch to {captured.Name}",
                $"{captured.Id} · {captured.Environment} · {captured.Access}",
                "Ctrl+P",
                () => _shell.Target = captured));
        }

        // Governed actions appear whether or not they are currently permitted. A palette that hides
        // what you cannot do teaches you the action does not exist; one that shows it disabled, with
        // the reason, teaches you what to arrange.
        entries.Add(new PaletteEntry(
            "",
            "Stop all execution",
            _shell.IsAnyRunActive
                ? "Contain every running sweep; evidence is preserved."
                : "Unavailable — nothing is currently running.",
            "",
            () => _shell.StopAll.Execute(null),
            _shell.IsAnyRunActive));

        entries.Add(new PaletteEntry(
            "",
            _shell.Recording ? "Stop the evidence recorder" : "Arm the evidence recorder",
            _shell.Recording
                ? "New artefacts will stop being captured."
                : "Artefacts will be captured and hashed as they arrive.",
            "",
            () => _shell.ToggleRecorder.Execute(null)));

        // Operational workflows & forensic shortcuts
        entries.Add(new PaletteEntry(
            "",
            "Refresh active workspace",
            "Re-query the evidence store, reload states, and re-evaluate active guards.",
            "F5",
            () => _shell.RequestRefresh()));

        entries.Add(new PaletteEntry(
            "",
            "Focus workspace search",
            "Focus the active workspace search input or filter box.",
            "Ctrl+F",
            () => _shell.RequestFocusSearch()));

        entries.Add(new PaletteEntry(
            "",
            "Toggle black-box stream drawer",
            "Expand or collapse the live event trace and forensic telemetry tray.",
            "Ctrl+L",
            () => _shell.ToggleTray.Execute(null)));

        entries.Add(new PaletteEntry(
            "",
            "Copy visible event stream",
            "Format and copy all visible black-box events to the clipboard.",
            "",
            () => _shell.CopyVisibleEvents.Execute(null)));

        entries.Add(new PaletteEntry(
            "",
            "Evaluate clearance gates",
            "Formally verify production release readiness and integrity gates across active target.",
            "",
            () =>
            {
                _shell.Workspace = WorkspaceKind.Clearance;
                _shell.RequestRefresh();
            }));

        entries.Add(new PaletteEntry(
            "",
            "Run invariant algorithm suite",
            "Execute ddmin, metatesting, dual-state differential, and causal harnesses.",
            "",
            () =>
            {
                _shell.Workspace = WorkspaceKind.Algorithms;
                _shell.RequestRefresh();
            }));

        entries.Add(new PaletteEntry(
            "",
            "Start autonomous state sweep",
            "Open autonomous state machine exploration and invariant probing.",
            "",
            () => _shell.Workspace = WorkspaceKind.Sweep));

        entries.Add(new PaletteEntry(
            "",
            "Auto-arrange state lattice",
            "Recalculate clean radial layout and reset node spacing on the active state map.",
            "",
            () => _shell.NavigateToStateMap()));

        // Multi-Domain Autonomous Audit Runners
        entries.Add(new PaletteEntry(
            "",
            "Run scientific mathematical audit",
            "Execute first-principles formula audits, 4-layer DAG invariants, and floating-point epsilon damping.",
            "",
            () =>
            {
                _shell.Workspace = WorkspaceKind.Algorithms;
                _shell.RequestRefresh();
            }));

        entries.Add(new PaletteEntry(
            "",
            "Run agentic AI & machine learning hardening audit",
            "Audit zero lookahead data contracts, day-clustered t >= 2.0 significance, and UTCP tool schemas.",
            "",
            () =>
            {
                _shell.Workspace = WorkspaceKind.Algorithms;
                _shell.RequestRefresh();
            }));

        entries.Add(new PaletteEntry(
            "",
            "Run mobile emulator runtime audit",
            "Verify ADB emulator presence, touch gestures, Logcat row-execSQL and RenderFlex overflow sentries.",
            "",
            () =>
            {
                _shell.Workspace = WorkspaceKind.Algorithms;
                _shell.RequestRefresh();
            }));

        entries.Add(new PaletteEntry(
            "",
            "Run desktop native runtime audit",
            "Verify process telemetry bounds (< 250 MB), Windows UIA tree integrity, and SQLite WAL storage.",
            "",
            () =>
            {
                _shell.Workspace = WorkspaceKind.Algorithms;
                _shell.RequestRefresh();
            }));

        // Fast theme switching
        entries.Add(new PaletteEntry(
            "",
            "Switch theme to Gold Standard",
            "High-contrast authoritative gold & deep obsidian finish (Patent clearance standard).",
            "",
            () => Theming.ThemeManager.Current.ApplyPaletteWithItsDefaults("gold_standard")));

        entries.Add(new PaletteEntry(
            "",
            "Switch theme to Spectral Black",
            "Zero-reflection deep OLED black with phosphor cyan highlights.",
            "",
            () => Theming.ThemeManager.Current.ApplyPaletteWithItsDefaults("spectral_black")));

        entries.Add(new PaletteEntry(
            "",
            "Switch theme to Ghostwire",
            "Clean phosphor wireframe telemetry finish.",
            "",
            () => Theming.ThemeManager.Current.ApplyPaletteWithItsDefaults("ghostwire")));

        entries.Add(new PaletteEntry(
            "",
            "Switch theme to Blacksite",
            "Restricted amber terminal finish for high-security isolation.",
            "",
            () => Theming.ThemeManager.Current.ApplyPaletteWithItsDefaults("blacksite")));

        entries.Add(new PaletteEntry(
            "",
            "Switch theme to Operator Green",
            "High-visibility phosphor CRT terminal finish.",
            "",
            () => Theming.ThemeManager.Current.ApplyPaletteWithItsDefaults("operator_green")));

        entries.Add(new PaletteEntry(
            "",
            "Switch theme to Phantom Violet",
            "Deep electromagnetic violet spectrum finish.",
            "",
            () => Theming.ThemeManager.Current.ApplyPaletteWithItsDefaults("phantom_violet")));

        if (!string.IsNullOrWhiteSpace(query))
        {
            entries = entries
                .Where(e => e.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || e.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Results.ItemsSource = entries;
        if (NoMatchesNotice is not null)
        {
            NoMatchesNotice.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (entries.Count > 0)
        {
            Results.SelectedIndex = 0;
        }
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e) => Populate(Query.Text);

    private void OnQueryKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                e.Handled = true;
                break;

            case Key.Enter:
                Invoke();
                e.Handled = true;
                break;

            case Key.Down:
                if (Results.SelectedIndex < Results.Items.Count - 1)
                {
                    Results.SelectedIndex++;
                }

                e.Handled = true;
                break;

            case Key.Up:
                if (Results.SelectedIndex > 0)
                {
                    Results.SelectedIndex--;
                }

                e.Handled = true;
                break;
        }
    }

    private void OnResultsKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Dismiss();
            e.Handled = true;
        }
    }

    private void OnInvoke(object sender, MouseButtonEventArgs e) => Invoke();

    private void Invoke()
    {
        if (Results.SelectedItem is PaletteEntry { Available: true } entry)
        {
            Dismiss();
            entry.Invoke();
        }
    }

    private void OnDismiss(object sender, MouseButtonEventArgs e) => Dismiss();

    /// <summary>Stops a click inside the panel from dismissing it.</summary>
    private void OnSwallow(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Dismiss()
    {
        if (_shell is not null)
        {
            _shell.CommandPaletteOpen = false;
        }
    }
}
