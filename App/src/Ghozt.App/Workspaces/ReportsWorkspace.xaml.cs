using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class ReportsWorkspace : UserControl
{
    private ForensicReport? _currentReport;

    public ReportsWorkspace()
    {
        InitializeComponent();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            StatusText.Text = "No target selected";
            return;
        }

        var generator = new ReportGenerator(App.Store);
        _currentReport = generator.Generate(target, "operator");

        RenderReport(_currentReport);

        ExportMdButton.IsEnabled = true;
        ExportHtmlButton.IsEnabled = true;
        ExportJsonButton.IsEnabled = true;
    }

    private void RenderReport(ForensicReport report)
    {
        ReportContainer.Children.Clear();
        StatusText.Text = $"Report {report.ReportId} generated · SHA-256 sealed";

        // Summary Card
        var isCleared = report.ClearanceVerdict == ClearanceOutcome.Cleared;
        var summaryCard = new Border
        {
            Background = (Brush)TryFindResource("Surface.Card"),
            BorderBrush = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("Border.Card")),
            BorderThickness = new Thickness(isCleared ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var stack = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = report.TargetName,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)TryFindResource("Text.Primary"),
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"Report ID: {report.ReportId} · Target: {report.TargetId} · Build: {report.BuildLabel}",
            Style = (Style)TryFindResource("Type.CaptionStyle"),
            Foreground = (Brush)TryFindResource("Text.Muted"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var verdictBadge = new Border
        {
            Background = (Brush)(isCleared ? TryFindResource("Authority.Soft") : TryFindResource("State.FaultSoft")),
            BorderBrush = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        verdictBadge.Child = new TextBlock
        {
            Text = report.ClearanceVerdict.ToString().ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
        };
        Grid.SetColumn(verdictBadge, 1);
        header.Children.Add(verdictBadge);

        stack.Children.Add(header);

        // Metrics row
        var metricsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        AddMetric(metricsRow, "Sweeps", report.TotalSweeps.ToString());
        AddMetric(metricsRow, "States Explored", report.TotalStatesExplored.ToString());
        AddMetric(metricsRow, "Deviations", report.TotalDeviations.ToString());
        AddMetric(metricsRow, "Sealed Artefacts", report.TotalEvidenceArtefacts.ToString());
        stack.Children.Add(metricsRow);

        summaryCard.Child = stack;
        ReportContainer.Children.Add(summaryCard);

        // Markdown preview card
        var mdCard = new Border
        {
            Background = (Brush)TryFindResource("Surface.Card"),
            BorderBrush = (Brush)TryFindResource("Border.Card"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var mdBox = new TextBox
        {
            Text = report.MarkdownContent,
            IsReadOnly = true,
            Background = Brushes.Transparent,
            Foreground = (Brush)TryFindResource("Text.Secondary"),
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily)TryFindResource("Font.Mono"),
            FontSize = 11,
        };
        mdCard.Child = mdBox;
        ReportContainer.Children.Add(mdCard);
    }

    private void AddMetric(StackPanel parent, string label, string value)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        p.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 9, Foreground = (Brush)TryFindResource("Text.Muted") });
        p.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = (Brush)TryFindResource("Text.Primary") });
        parent.Children.Add(p);
    }

    private string? _lastExportedPath;

    private static string GetReportsDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Capital-Iz", "Ghozt", "Reports");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void OnExportMarkdown(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        _lastExportedPath = Path.Combine(GetReportsDirectory(), $"{_currentReport.ReportId}.md");
        File.WriteAllText(_lastExportedPath, _currentReport.MarkdownContent);
        StatusText.Text = $"Exported to {_lastExportedPath}";
        Shell?.Log("RPT.EXPORT.MD", $"Exported markdown dossier {_currentReport.ReportId} to disk.", Severity.Info,
            detail: _lastExportedPath, related: _currentReport.TargetId.ToString());
        OpenViewerButton.IsEnabled = true;
    }

    private void OnExportHtml(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        _lastExportedPath = Path.Combine(GetReportsDirectory(), $"{_currentReport.ReportId}.html");
        File.WriteAllText(_lastExportedPath, _currentReport.HtmlContent);
        StatusText.Text = $"Exported to {_lastExportedPath}";
        Shell?.Log("RPT.EXPORT.HTML", $"Exported HTML dossier {_currentReport.ReportId} to disk.", Severity.Info,
            detail: _lastExportedPath, related: _currentReport.TargetId.ToString());
        OpenViewerButton.IsEnabled = true;
    }

    private void OnExportJson(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        _lastExportedPath = Path.Combine(GetReportsDirectory(), $"{_currentReport.ReportId}.json");
        File.WriteAllText(_lastExportedPath, _currentReport.JsonContent);
        StatusText.Text = $"Exported to {_lastExportedPath}";
        Shell?.Log("RPT.EXPORT.JSON", $"Exported JSON dataset {_currentReport.ReportId} to disk.", Severity.Info,
            detail: _lastExportedPath, related: _currentReport.TargetId.ToString());
        OpenViewerButton.IsEnabled = true;
    }

    private void OnOpenViewer(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastExportedPath) || !File.Exists(_lastExportedPath))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _lastExportedPath,
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Fallback if no association exists
        }
    }
}

