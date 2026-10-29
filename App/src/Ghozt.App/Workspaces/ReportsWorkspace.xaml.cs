using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class ReportsWorkspace : UserControl, IRefreshableWorkspace
{
    private ForensicReport? _currentReport;
    private bool _attached;

    public void Refresh()
    {
        if (Shell?.Target is null)
        {
            _currentReport = null;
            ReportContainer.Children.Clear();
            ReportContainer.Children.Add(EmptyState);
            StatusText.Text = "No target selected";
            if (ExportMdButton is not null) ExportMdButton.IsEnabled = false;
            if (ExportHtmlButton is not null) ExportHtmlButton.IsEnabled = false;
            if (ExportJsonButton is not null) ExportJsonButton.IsEnabled = false;
            if (ExportSarifButton is not null) ExportSarifButton.IsEnabled = false;
            if (ExportJUnitButton is not null) ExportJUnitButton.IsEnabled = false;
            if (OpenViewerButton is not null) OpenViewerButton.IsEnabled = false;
            if (PreviewInAppButton is not null) PreviewInAppButton.IsEnabled = false;
        }
        else
        {
            _currentReport = null;
            OnGenerate(this, new RoutedEventArgs());
        }
    }

    public ReportsWorkspace()
    {
        InitializeComponent();
        Loaded += OnReportsLoaded;
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            if (Shell?.Target is null)
            {
                _currentReport = null;
                ReportContainer.Children.Clear();
                ReportContainer.Children.Add(EmptyState);
                StatusText.Text = "No target selected";
                ExportMdButton.IsEnabled = false;
                ExportHtmlButton.IsEnabled = false;
                ExportJsonButton.IsEnabled = false;
                ExportSarifButton.IsEnabled = false;
                ExportJUnitButton.IsEnabled = false;
                OpenViewerButton.IsEnabled = false;
                PreviewInAppButton.IsEnabled = false;
                UpdateOperationsPanel(null);
            }
            else
            {
                _currentReport = null;
                OnGenerate(this, new RoutedEventArgs());
            }
        }
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnReportsLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        if (_currentReport is null && Shell?.Target is not null)
        {
            OnGenerate(this, new RoutedEventArgs());
        }
        else
        {
            UpdateOperationsPanel(_currentReport);
        }
    }

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            StatusText.Text = "No target selected";
            UpdateOperationsPanel(null);
            return;
        }

        var generator = new ReportGenerator(App.Store);
        _currentReport = generator.Generate(target, "operator");

        RenderReport(_currentReport);
        UpdateOperationsPanel(_currentReport);

        ExportMdButton.IsEnabled = true;
        ExportHtmlButton.IsEnabled = true;
        ExportJsonButton.IsEnabled = true;
        ExportSarifButton.IsEnabled = true;
        ExportJUnitButton.IsEnabled = true;
        PreviewInAppButton.IsEnabled = true;
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
            "CAPITAL-IZ", "Ghozt", "Reports");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ShowTemporaryButtonConfirmation(Button? button, string confirmationText, string originalText)
    {
        if (button is null) return;
        button.Content = confirmationText;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (s, ev) =>
        {
            timer.Stop();
            button.Content = originalText;
        };
        timer.Start();
    }

    private void SyncExportedPath()
    {
        if (!string.IsNullOrWhiteSpace(_lastExportedPath))
        {
            LastExportedPathText.Text = _lastExportedPath;
            BtnCopyExportedPath.IsEnabled = true;
            BtnLaunchSystemViewer.IsEnabled = true;
            OpenViewerButton.IsEnabled = true;
            BtnLaunchInAppPreview.IsEnabled = true;
            PreviewInAppButton.IsEnabled = true;
        }
    }

    private void OnExportMarkdown(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        _lastExportedPath = Path.Combine(GetReportsDirectory(), $"{_currentReport.ReportId}.md");
        File.WriteAllText(_lastExportedPath, _currentReport.MarkdownContent);
        StatusText.Text = $"Exported to {_lastExportedPath}";
        Shell?.Log("RPT.EXPORT.MD", $"Exported markdown dossier {_currentReport.ReportId} to disk.", Severity.Info,
            detail: _lastExportedPath, related: _currentReport.TargetId.ToString());
        ShowTemporaryButtonConfirmation(ExportMdButton, "Exported!", "Export Markdown");
        ShowTemporaryButtonConfirmation(BtnExportHubMarkdown, "Exported!", "Markdown (.md)");
        SyncExportedPath();
    }

    private void OnExportHtml(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        _lastExportedPath = Path.Combine(GetReportsDirectory(), $"{_currentReport.ReportId}.html");
        File.WriteAllText(_lastExportedPath, _currentReport.HtmlContent);
        StatusText.Text = $"Exported to {_lastExportedPath}";
        Shell?.Log("RPT.EXPORT.HTML", $"Exported HTML dossier {_currentReport.ReportId} to disk.", Severity.Info,
            detail: _lastExportedPath, related: _currentReport.TargetId.ToString());
        ShowTemporaryButtonConfirmation(ExportHtmlButton, "Exported!", "Export HTML");
        ShowTemporaryButtonConfirmation(BtnExportHubHtml, "Exported!", "HTML (.html)");
        SyncExportedPath();
    }

    private void OnExportJson(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        _lastExportedPath = Path.Combine(GetReportsDirectory(), $"{_currentReport.ReportId}.json");
        File.WriteAllText(_lastExportedPath, _currentReport.JsonContent);
        StatusText.Text = $"Exported to {_lastExportedPath}";
        Shell?.Log("RPT.EXPORT.JSON", $"Exported JSON dataset {_currentReport.ReportId} to disk.", Severity.Info,
            detail: _lastExportedPath, related: _currentReport.TargetId.ToString());
        ShowTemporaryButtonConfirmation(ExportJsonButton, "Exported!", "Export JSON");
        ShowTemporaryButtonConfirmation(BtnExportHubJson, "Exported!", "JSON (.json)");
        SyncExportedPath();
    }

    private void OnExportSarif(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        _lastExportedPath = Path.Combine(GetReportsDirectory(), $"{_currentReport.ReportId}.sarif.json");
        File.WriteAllText(_lastExportedPath, _currentReport.SarifContent);
        StatusText.Text = $"Exported SARIF to {_lastExportedPath}";
        Shell?.Log("RPT.EXPORT.SARIF", $"Exported OASIS SARIF v2.1.0 dossier {_currentReport.ReportId} to disk.", Severity.Info,
            detail: _lastExportedPath, related: _currentReport.TargetId.ToString());
        ShowTemporaryButtonConfirmation(ExportSarifButton, "Exported!", "Export SARIF");
        ShowTemporaryButtonConfirmation(BtnExportHubSarif, "Exported!", "SARIF (.sarif)");
        SyncExportedPath();
    }

    private void OnExportJUnit(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        _lastExportedPath = Path.Combine(GetReportsDirectory(), $"{_currentReport.ReportId}.junit.xml");
        File.WriteAllText(_lastExportedPath, _currentReport.JUnitXmlContent);
        StatusText.Text = $"Exported JUnit XML to {_lastExportedPath}";
        Shell?.Log("RPT.EXPORT.JUNIT", $"Exported JUnit XML report {_currentReport.ReportId} to disk.", Severity.Info,
            detail: _lastExportedPath, related: _currentReport.TargetId.ToString());
        ShowTemporaryButtonConfirmation(ExportJUnitButton, "Exported!", "Export JUnit");
        ShowTemporaryButtonConfirmation(BtnExportHubJUnit, "Exported!", "JUnit XML (.xml)");
        SyncExportedPath();
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

    private void OnPreviewInApp(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastExportedPath) && File.Exists(_lastExportedPath))
        {
            PreviewDocTitle.Text = Path.GetFileName(_lastExportedPath);
            PreviewDocContent.Text = File.ReadAllText(_lastExportedPath);
            DocumentPreviewDrawer.Visibility = Visibility.Visible;
            return;
        }

        if (_currentReport is not null)
        {
            PreviewDocTitle.Text = $"{_currentReport.ReportId} (Markdown)";
            PreviewDocContent.Text = _currentReport.MarkdownContent;
            DocumentPreviewDrawer.Visibility = Visibility.Visible;
        }
    }

    private void OnCopyPreviewText(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PreviewDocContent.Text);
            StatusText.Text = "Copied document content to clipboard.";
        }
        catch { }
    }

    private void OnCopyExportedPath(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastExportedPath)) return;
        try
        {
            Clipboard.SetText(_lastExportedPath);
            ShowTemporaryButtonConfirmation(BtnCopyExportedPath, "Copied!", "Copy Path");
            StatusText.Text = "Copied exported file path to clipboard.";
        }
        catch { }
    }

    private void OnCopyDossierMarkdown(object sender, RoutedEventArgs e)
    {
        if (_currentReport is null) return;
        try
        {
            Clipboard.SetText(_currentReport.MarkdownContent);
            ShowTemporaryButtonConfirmation(BtnCopyDossierMarkdown, "Copied!", "Copy Report Markdown");
            StatusText.Text = "Copied dossier markdown content to clipboard.";
        }
        catch { }
    }

    private void OnClosePreview(object sender, RoutedEventArgs e)
    {
        DocumentPreviewDrawer.Visibility = Visibility.Collapsed;
    }

    private void OnPortfolioRollup(object sender, RoutedEventArgs e)
    {
        var rollupEngine = new PortfolioRollupEngine(App.Store);
        var rollup = rollupEngine.GenerateRollup();

        RenderPortfolioRollup(rollup);
        UpdateOperationsPanelForRollup(rollup);

        Shell?.Log("RPT.PORTFOLIO.ROLLUP",
            $"Generated estate portfolio rollup for {rollup.TotalTargets} targets across estate. Readiness Index: {rollup.EstateReadinessIndex}",
            rollup.EstateReadinessIndex == "QUALIFIED" ? Severity.Info : Severity.High);
    }

    private void UpdateOperationsPanelForRollup(PortfolioRollupReport rollup)
    {
        EstateEriBadgeText.Text = $"ERI: {rollup.EstateReadinessIndex}";
        EstateTotalTargetsText.Text = $"{rollup.TotalTargets} Targets";
        EstateClearedTargetsText.Text = $"{rollup.ClearedTargets} / {rollup.DeniedOrBlockedTargets}";
        EstateCriticalFindingsText.Text = $"{rollup.TotalCriticalFindings} Faults";
    }

    private void UpdateOperationsPanel(ForensicReport? report = null)
    {
        var hasReport = report != null;
        if (hasReport)
        {
            ReportTargetInfoText.Text = $"{report!.TargetName} ({report.BuildLabel})";
            ReportIdInfoText.Text = $"Report: {report.ReportId} · Verdict: {report.ClearanceVerdict}";
            ReportDigestText.Text = $"SHA-256: {report.Sha256Seal}";
            ReportSealBadgeText.Text = "SEALED & AUDITED";
            ReportSealBadge.Background = (Brush)TryFindResource("State.VerifiedSoft");
            ReportSealBadge.BorderBrush = (Brush)TryFindResource("State.Verified");
            ReportSealBadgeText.Foreground = (Brush)TryFindResource("State.Verified");
        }
        else
        {
            var target = Shell?.Target;
            ReportTargetInfoText.Text = target != null ? $"{target.Name} ({target.BuildLabel})" : "No target selected";
            ReportIdInfoText.Text = "Report: Pending generation";
            ReportDigestText.Text = "SHA-256: Unsealed";
            ReportSealBadgeText.Text = "PENDING";
            ReportSealBadge.Background = (Brush)TryFindResource("Surface.CardFaint");
            ReportSealBadge.BorderBrush = (Brush)TryFindResource("Border.Subtle");
            ReportSealBadgeText.Foreground = (Brush)TryFindResource("Text.Muted");
        }

        // Synchronize export hub button states
        BtnExportHubMarkdown.IsEnabled = hasReport;
        BtnExportHubHtml.IsEnabled = hasReport;
        BtnExportHubJson.IsEnabled = hasReport;
        BtnExportHubSarif.IsEnabled = hasReport;
        BtnExportHubJUnit.IsEnabled = hasReport;
        BtnLaunchInAppPreview.IsEnabled = hasReport || (!string.IsNullOrWhiteSpace(_lastExportedPath) && File.Exists(_lastExportedPath));
        BtnLaunchSystemViewer.IsEnabled = !string.IsNullOrWhiteSpace(_lastExportedPath) && File.Exists(_lastExportedPath);
        BtnCopyDossierMarkdown.IsEnabled = hasReport;
        BtnCopyExportedPath.IsEnabled = !string.IsNullOrWhiteSpace(_lastExportedPath);

        // Update Estate Portfolio metrics from store
        try
        {
            var targets = App.Store.Targets();
            EstateTotalTargetsText.Text = $"{targets.Count} Targets";
            var clearanceEngine = new ClearanceEngine(App.Store);
            var cleared = 0;
            var blocked = 0;
            var critical = 0;
            foreach (var t in targets)
            {
                var dec = clearanceEngine.EvaluateTarget(t);
                if (dec.Outcome == ClearanceOutcome.Cleared) cleared++;
                else blocked++;
                var findings = App.Store.Findings(t.Id);
                critical += findings.Count(f => f.Severity == Severity.Critical && f.Maturity != FindingMaturity.Verified);
            }
            EstateClearedTargetsText.Text = $"{cleared} / {blocked}";
            EstateCriticalFindingsText.Text = $"{critical} Faults";
            var eriPercent = targets.Count > 0 ? (int)Math.Round((double)cleared / targets.Count * 100.0) : 100;
            EstateEriBadgeText.Text = $"ERI: {eriPercent}%";
        }
        catch
        {
            EstateTotalTargetsText.Text = "0 Targets";
            EstateClearedTargetsText.Text = "0 / 0";
            EstateCriticalFindingsText.Text = "0 Faults";
            EstateEriBadgeText.Text = "ERI: --";
        }
    }

    private void RenderPortfolioRollup(PortfolioRollupReport rollup)
    {
        ReportContainer.Children.Clear();
        StatusText.Text = $"Estate Rollup · Status: {rollup.EstateReadinessIndex} · {rollup.TotalTargets} targets";

        var isQualified = rollup.EstateReadinessIndex == "QUALIFIED";
        var isBlocked = rollup.EstateReadinessIndex == "BLOCKED";

        var summaryCard = new Border
        {
            Background = (Brush)TryFindResource("Surface.Card"),
            BorderBrush = (Brush)(isQualified
                ? TryFindResource("Authority.Clearance")
                : (isBlocked ? TryFindResource("State.Fault") : TryFindResource("State.Warning"))),
            BorderThickness = new Thickness(isQualified || isBlocked ? 2 : 1),
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
            Text = "CAPITAL-IZ Estate Quality & Release Readiness Rollup",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)TryFindResource("Text.Primary"),
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"Generated: {rollup.GeneratedAt:yyyy-MM-dd HH:mm:ss} · Patent §41 Multi-Target Portfolio Governance",
            Style = (Style)TryFindResource("Type.CaptionStyle"),
            Foreground = (Brush)TryFindResource("Text.Muted"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var readinessBadge = new Border
        {
            Background = (Brush)(isQualified
                ? TryFindResource("Authority.Soft")
                : (isBlocked ? TryFindResource("State.FaultSoft") : TryFindResource("State.WarningSoft"))),
            BorderBrush = (Brush)(isQualified
                ? TryFindResource("Authority.Clearance")
                : (isBlocked ? TryFindResource("State.Fault") : TryFindResource("State.Warning"))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        readinessBadge.Child = new TextBlock
        {
            Text = rollup.EstateReadinessIndex,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = (Brush)(isQualified
                ? TryFindResource("Authority.Clearance")
                : (isBlocked ? TryFindResource("State.Fault") : TryFindResource("State.Warning"))),
        };
        Grid.SetColumn(readinessBadge, 1);
        header.Children.Add(readinessBadge);

        stack.Children.Add(header);

        // Metrics row
        var metricsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        AddMetric(metricsRow, "Total Targets", rollup.TotalTargets.ToString());
        AddMetric(metricsRow, "Production", rollup.ProductionTargets.ToString());
        AddMetric(metricsRow, "Cleared", rollup.ClearedTargets.ToString());
        AddMetric(metricsRow, "Blocked/Denied", rollup.DeniedOrBlockedTargets.ToString());
        AddMetric(metricsRow, "Avg Coverage", $"{rollup.AverageCoverage}%");
        AddMetric(metricsRow, "Avg Unknown Risk", $"{rollup.AverageUnknownRisk}%");
        AddMetric(metricsRow, "Critical Findings", rollup.TotalCriticalFindings.ToString());
        stack.Children.Add(metricsRow);

        summaryCard.Child = stack;
        ReportContainer.Children.Add(summaryCard);

        // Risk warnings card
        if (rollup.StrategicRiskWarnings.Count > 0)
        {
            var warningCard = new Border
            {
                Background = (Brush)TryFindResource("State.FaultSoft"),
                BorderBrush = (Brush)TryFindResource("State.Fault"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16),
            };

            var warnStack = new StackPanel();
            warnStack.Children.Add(new TextBlock
            {
                Text = "STRATEGIC ESTATE RISK WARNINGS",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)TryFindResource("State.Fault"),
                Margin = new Thickness(0, 0, 0, 8),
            });

            foreach (var warning in rollup.StrategicRiskWarnings)
            {
                warnStack.Children.Add(new TextBlock
                {
                    Text = $"• {warning}",
                    Foreground = (Brush)TryFindResource("Text.Primary"),
                    FontSize = 11,
                    Margin = new Thickness(4, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            warningCard.Child = warnStack;
            ReportContainer.Children.Add(warningCard);
        }

        // Target Details
        var targetsHeader = new TextBlock
        {
            Text = "PORTFOLIO TARGET HEALTH BREAKDOWN",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)TryFindResource("Text.Primary"),
            Margin = new Thickness(4, 8, 0, 8),
        };
        ReportContainer.Children.Add(targetsHeader);

        foreach (var target in rollup.Targets)
        {
            var targetCard = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)TryFindResource("Border.Card"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var targetStack = new StackPanel();

            var tHeader = new Grid();
            tHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tTitle = new StackPanel { Orientation = Orientation.Horizontal };
            tTitle.Children.Add(new TextBlock
            {
                Text = target.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
            });
            tTitle.Children.Add(new TextBlock
            {
                Text = $"  [{target.Environment}]  ·  {target.Kind}  ·  Build: {target.BuildLabel}",
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(tTitle, 0);
            tHeader.Children.Add(tTitle);

            var isTargetCleared = target.ClearanceStatus == ClearanceOutcome.Cleared;
            var tBadge = new Border
            {
                Background = (Brush)(isTargetCleared ? TryFindResource("Authority.Soft") : TryFindResource("State.FaultSoft")),
                BorderBrush = (Brush)(isTargetCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
            };
            tBadge.Child = new TextBlock
            {
                Text = target.ClearanceStatus.ToString().ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(isTargetCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(tBadge, 1);
            tHeader.Children.Add(tBadge);

            targetStack.Children.Add(tHeader);

            var tMetrics = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            AddMetric(tMetrics, "Coverage", $"{target.CoveragePercentage}%");
            AddMetric(tMetrics, "Unknown Risk", $"{target.UnknownRiskPercentage}%");
            AddMetric(tMetrics, "Lattice Nodes", $"{target.VerifiedNodeCount} / {target.NodeCount}");
            AddMetric(tMetrics, "Open Defects", target.OpenFindingCount.ToString());
            AddMetric(tMetrics, "Critical", target.CriticalFindingCount.ToString());
            AddMetric(tMetrics, "Evidence", target.EvidenceCount.ToString());
            targetStack.Children.Add(tMetrics);

            targetCard.Child = targetStack;
            ReportContainer.Children.Add(targetCard);
        }
    }
}

