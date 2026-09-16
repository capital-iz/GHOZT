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
