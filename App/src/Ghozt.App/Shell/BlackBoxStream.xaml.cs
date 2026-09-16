using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ghozt.Core;

namespace Ghozt.App.Shell;

public partial class BlackBoxStream : UserControl
{
    public BlackBoxStream()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        UpdateEmptyMessage();

        if (DataContext is ShellViewModel shell)
        {
            shell.PropertyChanged += OnShellChanged;
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.StreamFilter) or nameof(ShellViewModel.Target))
        {
            UpdateEmptyMessage();
        }
    }

    private LedgerEvent? SelectedEvent => Rows.SelectedItem as LedgerEvent;

    private void OnRowsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelectedLine();
            e.Handled = true;
        }
    }

    private void OnCopySelectedLine(object sender, RoutedEventArgs e) => CopySelectedLine();

    private void CopySelectedLine()
    {
        if (SelectedEvent is { } ev)
        {
            SetClipboardText($"{ev.At:HH:mm:ss.fff}\t{ev.Id}\t{ev.Code}\t{ev.Severity}\t{ev.Message}\t{ev.Actor}", $"event {ev.Id}");
        }
    }

    private void OnCopySelectedId(object sender, RoutedEventArgs e)
    {
        if (SelectedEvent is { } ev)
        {
            SetClipboardText(ev.Id.ToString(), $"event ID {ev.Id}");
        }
    }

    private void OnCopySelectedCode(object sender, RoutedEventArgs e)
    {
        if (SelectedEvent is { } ev)
        {
            SetClipboardText(ev.Code, $"code {ev.Code}");
        }
    }

    private void OnCopySelectedMessage(object sender, RoutedEventArgs e)
    {
        if (SelectedEvent is { } ev)
        {
            SetClipboardText(ev.Message, $"message for {ev.Id}");
        }
    }

    private void OnCopySelectedDetail(object sender, RoutedEventArgs e)
    {
        if (SelectedEvent is { } ev)
        {
            var detail = string.IsNullOrWhiteSpace(ev.Detail) ? ev.Message : ev.Detail;
            SetClipboardText(detail, $"details for {ev.Id}");
        }
    }

    private void OnCopyAllVisible(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.CopyVisibleEvents.Execute(null);
        }
    }

    private void SetClipboardText(string text, string description)
    {
        try
        {
            Clipboard.SetText(text);
            if (DataContext is ShellViewModel shell)
            {
                shell.Log("EVT.COPY", $"Copied {description} to clipboard.", Severity.Info);
            }
        }
        catch
        {
            // Safe native desktop clipboard access
        }
    }

    /// <summary>
    /// Says why the stream is empty, which is not always the same reason.
    /// </summary>
    /// <remarks>
    /// "Nothing has been recorded" and "your filter excluded everything" are different facts, and an
    /// operator who reads the second as the first concludes that a run produced no events. The same
    /// distinction the blueprint draws between "no faults in tested scope" and "nothing tested".
    /// </remarks>
    private void UpdateEmptyMessage()
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        EmptyMessage.Text = !string.IsNullOrWhiteSpace(shell.StreamFilter)
            ? $"No events match the code filter “{shell.StreamFilter}”. Clear the filter to see the full record."
            : shell.Target is null
                ? "No target selected. Register a target to begin recording."
                : "No events recorded for this target yet. Run a sweep to populate the record.";
    }
}
