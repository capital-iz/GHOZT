using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class AlgorithmsWorkspace : UserControl
{
    private AlgorithmSuiteReport? _lastReport;

    public AlgorithmsWorkspace()
    {
        InitializeComponent();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnRunSuite(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        try
        {
            _lastReport = AlgorithmHarness.RunSuite();
            RenderReport(_lastReport);
            SealButton.IsEnabled = Shell?.Target is not null;
            ExportDossierButton.IsEnabled = true;
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private void OnDomainFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_lastReport is not null)
        {
            RenderReport(_lastReport);
        }
    }

    private void OnExportDossier(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            return;
        }

        try
        {
            var dossier = AlgorithmHarness.GenerateMathematicalDossier(_lastReport);
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz",
                "Ghozt",
                "AlgorithmReports");
            Directory.CreateDirectory(exportDir);

            var filePath = Path.Combine(exportDir, $"ALG_DOSSIER_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
            File.WriteAllText(filePath, dossier);

            Shell?.Log("ALG.DOSSIER.EXPORT", $"Mathematical dossier exported to {filePath}", Severity.Info,
                detail: $"sha256={_lastReport.Sha256Digest}; passed={_lastReport.PassedCount}/{_lastReport.Results.Count}");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Shell?.Log("ALG.DOSSIER.ERR", $"Failed to export mathematical dossier: {ex.Message}", Severity.High);
        }
    }

    private void RenderReport(AlgorithmSuiteReport report)
    {
        ResultsPanel.Children.Clear();
        SuiteSummaryText.Text = $"{report.PassedCount}/{report.Results.Count} invariants verified · {report.FailedCount} failed · {report.TotalDuration.TotalMilliseconds:F1}ms";

        var selectedDomain = (DomainFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var filterDomain = string.IsNullOrEmpty(selectedDomain) || selectedDomain == "All Domains" ? null : selectedDomain;

        var items = report.Results.Where(r =>
            filterDomain is null || r.Domain.Contains(filterDomain, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var r in items)
        {
            var card = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(r.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var stack = new StackPanel();

            // Header line: ID + Name + Status Badge + Duration
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = r.Id,
                Style = (Style)TryFindResource("Type.MonoStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = r.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = $"[{r.Domain}]",
                FontSize = 11,
                Foreground = (Brush)TryFindResource("Accent.Text"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
            Grid.SetColumn(titlePanel, 0);
            header.Children.Add(titlePanel);

            var statusBadge = new Border
            {
                Background = (Brush)(r.Passed ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                BorderBrush = (Brush)(r.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            statusBadge.Child = new TextBlock
            {
                Text = r.Passed ? "VERIFIED" : "DEVIATION",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(r.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(statusBadge, 1);
            header.Children.Add(statusBadge);

            stack.Children.Add(header);

            // Invariant explanation
            stack.Children.Add(new TextBlock
            {
                Text = r.Invariant,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 6, 0, 8),
            });

            // Grid of Expected vs Actual
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var expPanel = new StackPanel();
            expPanel.Children.Add(new TextBlock { Text = "EXPECTED (CLOSED-FORM)", FontSize = 9, Foreground = (Brush)TryFindResource("Text.Muted") });
            expPanel.Children.Add(new TextBlock { Text = r.Expected, Style = (Style)TryFindResource("Type.MonoStyle"), FontSize = 11, Foreground = (Brush)TryFindResource("Text.Primary") });
            Grid.SetColumn(expPanel, 0);
            grid.Children.Add(expPanel);

            var actPanel = new StackPanel();
            actPanel.Children.Add(new TextBlock { Text = "ACTUAL CALCULATED", FontSize = 9, Foreground = (Brush)TryFindResource("Text.Muted") });
            actPanel.Children.Add(new TextBlock { Text = r.Actual, Style = (Style)TryFindResource("Type.MonoStyle"), FontSize = 11, Foreground = (Brush)TryFindResource("Text.Primary") });
            Grid.SetColumn(actPanel, 1);
            grid.Children.Add(actPanel);

            stack.Children.Add(grid);

            card.Child = stack;
            ResultsPanel.Children.Add(card);
        }
    }

    private void OnSealEvidence(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null || Shell?.Target is null)
        {
            return;
        }

        var target = Shell.Target;
        AlgorithmHarness.SealReportToEvidence(App.Store, target.Id, _lastReport);

        SealButton.IsEnabled = false;
        SealButton.Content = "Sealed";
        Shell.Refresh();
    }
}
