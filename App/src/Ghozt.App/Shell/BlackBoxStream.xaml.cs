using System.ComponentModel;
using System.Windows.Controls;

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
