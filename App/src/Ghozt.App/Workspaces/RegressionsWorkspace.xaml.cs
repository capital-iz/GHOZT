using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class RegressionsWorkspace : UserControl, IRefreshableWorkspace
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _attached;

    public RegressionsWorkspace()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }

        Refresh();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        if (MutationScoreBadge is not null)
        {
            MutationScoreBadge.Visibility = Visibility.Collapsed;
        }

        var target = Shell?.Target;
        if (target is null)
        {
            GuardsPanel.Children.Clear();
            GuardsPanel.Children.Add(EmptyState);
            GuardSummaryText.Text = "No target selected";
            if (RunSuiteButton is not null) RunSuiteButton.IsEnabled = false;
            if (MutationAssaysButton is not null) MutationAssaysButton.IsEnabled = false;
            if (ExportSuiteButton is not null) ExportSuiteButton.IsEnabled = false;
            SelectGuard(null);
            return;
        }

        if (RunSuiteButton is not null) RunSuiteButton.IsEnabled = true;
        if (MutationAssaysButton is not null) MutationAssaysButton.IsEnabled = true;

        var engine = new RegressionEngine(App.Store);
        var guards = engine.GenerateGuards(target.Id);

        _currentGuards = guards;
        if (ExportSuiteButton is not null) ExportSuiteButton.IsEnabled = guards.Count > 0;
        RenderGuards(guards);
    }

    private IReadOnlyList<RegressionGuard> _currentGuards = [];
    private RegressionGuard? _selectedGuard;

    private void OnGenerateGuards(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void OnExportSuite(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null || _currentGuards.Count == 0)
        {
            return;
        }

        try
        {
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CAPITAL-IZ",
                "Ghozt",
                "Guards");
            Directory.CreateDirectory(exportDir);

            var filePath = Path.Combine(exportDir, $"REG_SUITE_{target.Id}.json");
            var json = JsonSerializer.Serialize(_currentGuards, JsonOptions);
            File.WriteAllText(filePath, json);

            Shell?.Log("REG.EXPORT", $"Regression guard suite exported to {filePath}", Severity.Info,
                detail: $"count={_currentGuards.Count}", related: target.Id.ToString());

            ShowTemporaryButtonConfirmation(ExportSuiteButton, "Exported!", "Export Suite (.json)");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Shell?.Log("REG.EXPORT.ERR", $"Failed to export regression suite: {ex.Message}", Severity.High);
        }
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

    private async void OnRunSuite(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            GuardSummaryText.Text = "No target selected";
            return;
        }

        RunSuiteButton.IsEnabled = false;
        GuardSummaryText.Text = "Executing regression guards...";

        try
        {
            var engine = new RegressionEngine(App.Store);
            var evaluated = await engine.ExecuteGuardsAsync(target).ConfigureAwait(true);
            _currentGuards = evaluated;
            ExportSuiteButton.IsEnabled = evaluated.Count > 0;
            RenderGuards(evaluated);
            Shell?.Refresh();
        }
        finally
        {
            RunSuiteButton.IsEnabled = true;
        }
    }

    private async void OnRunMutationAssays(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            GuardSummaryText.Text = "No target selected";
            return;
        }

        if (MutationAssaysButton is not null) MutationAssaysButton.IsEnabled = false;
        GuardSummaryText.Text = "Executing mutation assays...";

        try
        {
            var engine = new RegressionEngine(App.Store);
            var report = await engine.VerifyGuardEfficacyAsync(target).ConfigureAwait(true);

            var isPassable = report.MutationScore >= 0.80;
            var brushKey = isPassable ? "State.Verified" : "State.Fault";
            var softBrushKey = isPassable ? "State.VerifiedSoft" : "State.FaultSoft";

            if (MutationScoreBadge is not null && MutationScoreText is not null)
            {
                MutationScoreBadge.Visibility = Visibility.Visible;
                MutationScoreBadge.BorderBrush = (Brush)TryFindResource(brushKey);
                MutationScoreBadge.Background = (Brush)TryFindResource(softBrushKey);
                MutationScoreText.Foreground = (Brush)TryFindResource(brushKey);
                MutationScoreText.Text = $"μ = {report.MutationScore:P0} ({report.MutantsKilled}/{report.TotalMutants} killed)";
            }

            if (InspectorMutationScoreBadge is not null && InspectorMutationScoreText is not null)
            {
                InspectorMutationScoreBadge.BorderBrush = (Brush)TryFindResource(brushKey);
                InspectorMutationScoreBadge.Background = (Brush)TryFindResource(softBrushKey);
                InspectorMutationScoreText.Foreground = (Brush)TryFindResource(brushKey);
                InspectorMutationScoreText.Text = $"μ = {report.MutationScore:P0}";
            }

            if (InspectorMutantsKilledText is not null)
            {
                InspectorMutantsKilledText.Text = $"{report.MutantsKilled}/{report.TotalMutants} Killed";
            }

            if (InspectorMutantsSurvivedText is not null)
            {
                InspectorMutantsSurvivedText.Text = $"{report.MutantsSurvived} Survived";
                InspectorMutantsSurvivedText.Foreground = (Brush)(report.MutantsSurvived == 0 ? TryFindResource("State.Verified") : TryFindResource("State.Fault"));
            }

            if (InspectorEfficacyVerdictText is not null)
            {
                InspectorEfficacyVerdictText.Text = report.IneffectiveGuardIds.Count == 0
                    ? "High Efficacy: 100% of seeded artificial invariants detected and killed without false-confidence."
                    : $"Warning: {report.IneffectiveGuardIds.Count} guard(s) exhibit false-confidence risk!";
            }

            GuardSummaryText.Text = report.IneffectiveGuardIds.Count == 0
                ? $"All {report.TotalGuards} guards verified effective against artificial mutations"
                : $"{report.IneffectiveGuardIds.Count} guard(s) flagged ineffective / false-confidence!";

            Shell?.Refresh();
        }
        finally
        {
            if (MutationAssaysButton is not null) MutationAssaysButton.IsEnabled = true;
        }
    }

    private void RenderGuards(IReadOnlyList<RegressionGuard> guards)
    {
        GuardsPanel.Children.Clear();

        if (guards.Count == 0)
        {
            GuardsPanel.Children.Add(new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)TryFindResource("Border.Card"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24),
                Child = new TextBlock
                {
                    Text = "No regression guards generated yet. Confirmed findings in the Faults workspace automatically populate guards.",
                    Style = (Style)TryFindResource("Type.CaptionStyle"),
                    TextAlignment = TextAlignment.Center,
                }
            });
            GuardSummaryText.Text = "0 active regression guards";
            SelectGuard(null);
            return;
        }

        var passing = guards.Count(g => g.Status == GuardStatus.Passing);
        var failing = guards.Count(g => g.Status == GuardStatus.Failing);
        GuardSummaryText.Text = $"{guards.Count} total guards · {passing} passing · {failing} failing";

        foreach (var guard in guards)
        {
            var isPassing = guard.Status == GuardStatus.Passing;
            var card = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = guard,
            };

            var captured = guard;
            card.MouseLeftButtonUp += (_, _) => SelectGuard(captured);

            var stack = new StackPanel();

            // Header: Guard ID + Title + Status Badge
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = guard.Id,
                Style = (Style)TryFindResource("Type.MonoStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = guard.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(titlePanel, 0);
            header.Children.Add(titlePanel);

            var statusBadge = new Border
            {
                Background = (Brush)(isPassing ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                BorderBrush = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            statusBadge.Child = new TextBlock
            {
                Text = isPassing ? "PASSING" : "FAILING",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(statusBadge, 1);
            header.Children.Add(statusBadge);

            stack.Children.Add(header);

            // Invariant & Expected
            stack.Children.Add(new TextBlock
            {
                Text = $"Expected: {guard.ExpectedBehavior}",
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 6, 0, 4),
            });

            stack.Children.Add(new TextBlock
            {
                Text = guard.Details,
                FontSize = 11,
                Foreground = (Brush)TryFindResource("Text.Muted"),
            });

            card.Child = stack;
            GuardsPanel.Children.Add(card);
        }

        var toSelect = guards.FirstOrDefault(g => g.Id == _selectedGuard?.Id) ?? guards.FirstOrDefault();
        SelectGuard(toSelect);
    }

    private void SelectGuard(RegressionGuard? guard)
    {
        _selectedGuard = guard;

        if (InspectorGuardIdText is null) return;

        if (guard is null)
        {
            InspectorGuardIdText.Text = "NO GUARD SELECTED";
            InspectorGuardTitleText.Text = "Select a guard card on the left to inspect";
            InspectorStatusBadge.Background = (Brush)TryFindResource("Surface.CardFaint");
            InspectorStatusBadge.BorderBrush = (Brush)TryFindResource("Border.Subtle");
            InspectorStatusText.Text = "IDLE";
            InspectorStatusText.Foreground = (Brush)TryFindResource("Text.Muted");
            InspectorExpectedText.Text = "Expected behavior will appear here when a guard is selected.";
            InspectorObservedText.Text = "Source defect observation details will appear here.";
            InspectorSeverityText.Text = "None";
            InspectorLastEvaluatedText.Text = "Never";
            if (BtnExecuteSingleGuard is not null) BtnExecuteSingleGuard.IsEnabled = false;
            if (GuardDossierCard is not null) GuardDossierCard.BorderBrush = (Brush)TryFindResource("Border.Card");
            return;
        }

        var isPassing = guard.Status == GuardStatus.Passing;
        InspectorGuardIdText.Text = guard.Id;
        InspectorGuardTitleText.Text = guard.Title;
        InspectorStatusBadge.Background = (Brush)(isPassing ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft"));
        InspectorStatusBadge.BorderBrush = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault"));
        InspectorStatusText.Text = isPassing ? "PASSING" : "FAILING";
        InspectorStatusText.Foreground = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault"));
        InspectorExpectedText.Text = string.IsNullOrWhiteSpace(guard.ExpectedBehavior) ? "Invariant behavior contract not defined." : guard.ExpectedBehavior;
        InspectorObservedText.Text = string.IsNullOrWhiteSpace(guard.Details) ? "No observed defect note available." : guard.Details;
        InspectorSeverityText.Text = guard.Severity.ToString();
        InspectorLastEvaluatedText.Text = guard.LastEvaluatedAt.HasValue
            ? guard.LastEvaluatedAt.Value.ToString("yyyy-MM-dd HH:mm")
            : (guard.EvaluationCount > 0 ? $"{guard.EvaluationCount} runs" : "Never");

        if (BtnExecuteSingleGuard is not null) BtnExecuteSingleGuard.IsEnabled = true;
        if (GuardDossierCard is not null) GuardDossierCard.BorderBrush = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault"));

        // Highlight selected card in list
        foreach (var child in GuardsPanel.Children)
        {
            if (child is Border cardBorder && cardBorder.Tag is RegressionGuard cardGuard)
            {
                if (cardGuard.Id == guard.Id)
                {
                    cardBorder.BorderBrush = (Brush)TryFindResource("Accent.Fill");
                    cardBorder.BorderThickness = new Thickness(2);
                }
                else
                {
                    var pass = cardGuard.Status == GuardStatus.Passing;
                    cardBorder.BorderBrush = (Brush)(pass ? TryFindResource("State.Verified") : TryFindResource("State.Fault"));
                    cardBorder.BorderThickness = new Thickness(1);
                }
            }
        }
    }

    private void OnExecuteSelectedGuard(object sender, RoutedEventArgs e)
    {
        if (_selectedGuard is null || Shell?.Target is not { } target) return;

        var engine = new RegressionEngine(App.Store);
        var (passes, note) = engine.EvaluateGuard(_selectedGuard, target);

        var updatedGuard = _selectedGuard with
        {
            Status = passes ? GuardStatus.Passing : GuardStatus.Failing,
            LastEvaluatedAt = DateTimeOffset.Now,
            EvaluationCount = _selectedGuard.EvaluationCount + 1,
            Details = $"Evaluated: {note} · {DateTimeOffset.Now:HH:mm:ss}",
        };

        _currentGuards = _currentGuards.Select(g => g.Id == updatedGuard.Id ? updatedGuard : g).ToList();
        SelectGuard(updatedGuard);
        RenderGuards(_currentGuards);

        Shell.Log("REG.GUARD.EXEC", $"Executed {updatedGuard.Id}: {(passes ? "PASS" : "FAIL")} — {note}",
            passes ? Severity.Info : Severity.Medium,
            related: target.Id.ToString(), detail: updatedGuard.ExpectedBehavior);

        ShowTemporaryButtonConfirmation(BtnExecuteSingleGuard, "Executed!", "Execute Selected Guard");
    }

    private void OnAnalyzeChangeImpact(object sender, RoutedEventArgs e)
    {
        if (Shell?.Target is not { } target) return;

        var engine = new ChangeImpactEngine(App.Store);
        var baseDir = !string.IsNullOrWhiteSpace(target.BaseAddress) && Directory.Exists(target.BaseAddress)
            ? target.BaseAddress
            : "App";

        var sampleFiles = new[]
        {
            Path.Combine(baseDir, "Services", "AuthService.cs"),
            Path.Combine(baseDir, "Data", "GhoztStore.cs"),
            Path.Combine(baseDir, "Workspaces", "ClearanceWorkspace.xaml"),
        };

        var assessment = engine.AssessImpact(target, sampleFiles);
        GuardSummaryText.Text = $"Impact: {assessment.ImpactedGuards.Count} guard(s) affected · Risk: {assessment.RiskLevel}";

        if (InspectorRiskBadge is not null && InspectorRiskBadgeText is not null)
        {
            InspectorRiskBadgeText.Text = $"{assessment.RiskLevel} Risk".ToUpperInvariant();
            var riskBrushKey = assessment.RiskLevel switch
            {
                ChangeRiskLevel.Critical => "State.Critical",
                ChangeRiskLevel.High => "State.Fault",
                ChangeRiskLevel.Medium => "State.Warning",
                _ => "State.Verified"
            };
            InspectorRiskBadge.BorderBrush = (Brush)TryFindResource(riskBrushKey);
            InspectorRiskBadgeText.Foreground = (Brush)TryFindResource(riskBrushKey);
        }

        if (InspectorImpactDetailsText is not null)
        {
            InspectorImpactDetailsText.Text = $"Assessment complete: {assessment.ImpactedGuards.Count} guard(s) and {assessment.ImpactedNodes.Count} lattice node(s) in change envelope. Summary: {assessment.Summary}";
        }

        ShowTemporaryButtonConfirmation(ChangeImpactButton, "Analyzed!", "Change Impact");
        Shell.Log("CHANGE_IMPACT.ASSESSED", assessment.Summary,
            assessment.RiskLevel >= ChangeRiskLevel.High ? Severity.High : Severity.Info,
            detail: $"Recommended checks: {string.Join(", ", assessment.RecommendedSweepChecks)}");
    }
}
