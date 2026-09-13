using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class ProcessPickerDialog : Window
{
    private IReadOnlyList<ProcessInfoSnapshot> _allProcesses = [];

    public ProcessPickerDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadProcesses();
    }

    public ProcessInfoSnapshot? SelectedProcess { get; private set; }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void LoadProcesses()
    {
        _allProcesses = TargetSupervisor.GetActiveDesktopProcesses();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allProcesses
            : _allProcesses.Where(p =>
                p.MainWindowTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        ProcessList.ItemsSource = filtered;
        StatusCount.Text = $"{filtered.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {_allProcesses.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} running window(s)";

        if (filtered.Count > 0 && ProcessList.SelectedItem is null)
        {
            ProcessList.SelectedIndex = 0;
        }

        UpdateAttachState();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnRefresh(object sender, RoutedEventArgs e) => LoadProcesses();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAttachState();

    private void UpdateAttachState()
    {
        AttachButton.IsEnabled = ProcessList.SelectedItem is ProcessInfoSnapshot;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProcessList.SelectedItem is ProcessInfoSnapshot)
        {
            OnAttach(sender, e);
        }
    }

    private void OnAttach(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is ProcessInfoSnapshot snapshot)
        {
            SelectedProcess = snapshot;
            DialogResult = true;
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
