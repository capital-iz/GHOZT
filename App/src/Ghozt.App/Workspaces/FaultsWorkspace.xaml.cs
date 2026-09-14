using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class FaultsWorkspace : UserControl
{
    private CausalHypothesis? _currentHypothesis;
    private FixBrief? _currentFixBrief;

    public FaultsWorkspace()
    {
        InitializeComponent();

        // Both hooks, deliberately. DataContextChanged fires before the first render, so the
        // workspace never paints an empty state over data it already has; Loaded covers the case
        // where the context was set before this control existed.
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
    }

    private bool _attached;

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }

        Refresh();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private Finding? Selected => Queue.SelectedItem as Finding;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target) or nameof(ShellViewModel.Findings))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var target = Shell?.Target;
        var findings = target is null ? [] : App.Store.Findings(target.Id);

        Queue.ItemsSource = findings;
        EmptyPanel.Visibility = findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (findings.Count > 0)
        {
            return;
        }

        var sweeps = target is null ? [] : App.Store.Sweeps(target.Id);

        if (target is null)
        {
            EmptyTitle.Text = "No target selected";
            EmptyBody.Text = "Select a target in Targets to see the findings recorded against it.";
        }
        else if (sweeps.Count == 0)
        {
            EmptyTitle.Text = "Nothing has been tested";
            EmptyBody.Text = $"No sweep has run against {target.Name}. An empty fault queue here means "
                             + "absence of testing, not absence of faults. Run a sweep to establish coverage.";
        }
        else
        {
            var partial = sweeps.Count(s => s.State is RunState.Partial or RunState.Cancelled);
            EmptyTitle.Text = "No faults in tested scope";
            EmptyBody.Text = partial > 0
                ? $"{sweeps.Count} sweep(s) have run and found no deviations, but {partial} ended early. "
                  + "The scope those runs did not reach remains untested rather than proven."
                : $"{sweeps.Count} sweep(s) have run against {target.Name} with no deviations. "
                  + "Coverage is bounded by the checks in those sweep modes.";
        }
    }

    private void OnSelect(object sender, SelectionChangedEventArgs e)
    {
        var finding = Selected;

        Shell.InspectorSubject = finding;

        ReproButton.IsEnabled = finding is not null;
        AdvanceButton.IsEnabled = finding is not null && NextMaturity(finding) is not null;
        CausalButton.IsEnabled = finding is not null;
        FixBriefButton.IsEnabled = finding is not null;
        AssignButton.IsEnabled = finding is not null;

        if (finding is null || Shell.Target is null)
        {
            NoSelectionBox.Visibility = Visibility.Visible;
            CausalCard.Visibility = Visibility.Collapsed;
            FixBriefCard.Visibility = Visibility.Collapsed;
            return;
        }

        NoSelectionBox.Visibility = Visibility.Collapsed;

        // Automatically formulate initial causal hypothesis
        var causalEngine = new CausalForensicsEngine(App.Store);
        _currentHypothesis = causalEngine.Investigate(finding, Shell.Target);
        RenderCausalHypothesis(_currentHypothesis);

        // Clear or hide previous fix brief until requested or ready
        if (_currentFixBrief?.FindingId != finding.Id)
        {
            _currentFixBrief = null;
            FixBriefCard.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderCausalHypothesis(CausalHypothesis hyp)
    {
        HypothesisIdText.Text = $"{hyp.Id} · Confidence: {hyp.Confidence:P0}";
        HypothesisClaimText.Text = hyp.Claim;
        HypothesisMechanismText.Text = hyp.Mechanism;

        InterventionsList.Children.Clear();
        foreach (var cfx in hyp.Interventions)
        {
            var p = new Border
            {
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(8, 6, 8, 6),
                Background = (Brush)TryFindResource("Surface.Inset"),
                BorderBrush = (Brush)TryFindResource("Border.Subtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
            };
            var itemSp = new StackPanel();
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var header = new TextBlock
            {
                Text = $"[{cfx.Id}] {cfx.Class}: {(cfx.OutcomePreventedDeviation ? "PREVENTED DEVIATION" : (cfx.Class == InterventionClass.NegativeControl ? "EXPECTED NEGATIVE" : "NO EFFECT"))}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(cfx.OutcomePreventedDeviation || cfx.Class == InterventionClass.NegativeControl ? TryFindResource("State.Verified") : TryFindResource("Text.Muted")),
                VerticalAlignment = VerticalAlignment.Center,
            };
            headerRow.Children.Add(header);

            var testBtn = new Button
            {
                Content = "Test Branch",
                FontSize = 9,
                Padding = new Thickness(8, 2, 8, 2),
                Tag = cfx,
            };
            Grid.SetColumn(testBtn, 1);
            testBtn.Click += async (s, ev) =>
            {
                if (Selected is not { } f || Shell.Target is not { } t) return;
                testBtn.IsEnabled = false;
                testBtn.Content = "Testing…";
                var engine = new CausalForensicsEngine(App.Store);
                var (updatedHyp, _) = await engine.ExecuteInterventionAsync(_currentHypothesis!, cfx, f, t);
                _currentHypothesis = updatedHyp;
                RenderCausalHypothesis(_currentHypothesis);
            };
            headerRow.Children.Add(testBtn);

            var desc = new TextBlock
            {
                Text = $"{cfx.Description}\n(Factor changed: {cfx.FactorChanged} · Held: {cfx.FactorHeldConstant})",
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            };
            itemSp.Children.Add(headerRow);
            itemSp.Children.Add(desc);
            if (!string.IsNullOrWhiteSpace(cfx.ObservationNote))
            {
                var note = new TextBlock
                {
                    Text = $"Evidence: {cfx.ObservationNote}",
                    Style = (Style)TryFindResource("Type.MonoStyle"),
                    FontSize = 9,
                    Foreground = (Brush)TryFindResource("Text.Secondary"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                };
                itemSp.Children.Add(note);
            }
            p.Child = itemSp;
            InterventionsList.Children.Add(p);
        }

        CausalStatusText.Text = $"STATUS: {hyp.Status.ToString().ToUpperInvariant()}";
        CausalCard.Visibility = Visibility.Visible;
    }

    private void OnInvestigateCausal(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target)
        {
            return;
        }

        var engine = new CausalForensicsEngine(App.Store);
        _currentHypothesis = engine.Investigate(finding, target);
        RenderCausalHypothesis(_currentHypothesis);

        Shell.Log("CFX.INVESTIGATE", $"Evaluated causal hypothesis {_currentHypothesis.Id} for {finding.Id}.",
            Severity.Info, related: finding.Id.ToString(), detail: _currentHypothesis.Claim);
    }

    private void OnGenerateFixBrief(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target)
        {
            return;
        }

        var engine = new FixBriefEngine(App.Store);
        _currentFixBrief = engine.Generate(finding, target, _currentHypothesis);

        FixBriefIdText.Text = _currentFixBrief.Id;
        FixBriefTitleText.Text = _currentFixBrief.Title;
        FixBriefScopeText.Text = string.Join("\n", _currentFixBrief.AllowedScope);
        FixBriefGuardText.Text = _currentFixBrief.RegressionGuardSpecification;

        FixBriefCard.Visibility = Visibility.Visible;

        Shell.Log("FXB.GENERATE", $"Generated bounded Fix Brief {_currentFixBrief.Id} for {finding.Id}.",
            Severity.Info, related: finding.Id.ToString(), detail: _currentFixBrief.RootMechanism);
    }

    private void OnCopyFixBrief(object sender, RoutedEventArgs e)
    {
        if (_currentFixBrief is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_currentFixBrief.ToMarkdown());
            Shell.Log("FXB.COPY", $"Copied Fix Brief {_currentFixBrief.Id} to clipboard.", Severity.Info);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard access may fail in sandboxed/headless scenarios
        }
    }

    private void OnExportFixBrief(object sender, RoutedEventArgs e)
    {
        if (_currentFixBrief is null) return;

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz", "Ghozt", "FixBriefs");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, $"{_currentFixBrief.Id}.md");
            File.WriteAllText(path, _currentFixBrief.ToMarkdown());

            Shell.Log("FXB.EXPORT", $"Exported Fix Brief {_currentFixBrief.Id} to {path}.", Severity.Info,
                related: _currentFixBrief.FindingId.ToString());
        }
        catch (Exception ex)
        {
            Shell.Log("FXB.EXPORT.FAIL", $"Failed to export Fix Brief {_currentFixBrief.Id}: {ex.Message}", Severity.Medium);
        }
    }

    private void OnDeriveGuard(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target) return;

        var engine = new RegressionEngine(App.Store);
        var guards = engine.GenerateGuards(target.Id);
        var matchingGuard = guards.FirstOrDefault(g => g.FindingId == finding.Id);

        Shell.Log("REG.GUARD.DERIVE",
            $"Derived regression guard {(matchingGuard != null ? matchingGuard.Id : "GRD-DEFECT")} for finding {finding.Id}.",
            Severity.Info, related: finding.Id.ToString(),
            detail: $"Expected: {matchingGuard?.ExpectedBehavior ?? "No deviation"}");

        if (finding.Maturity < FindingMaturity.CauseSupported)
        {
            App.Store.Save(finding with { Maturity = FindingMaturity.CauseSupported });
            Refresh();
            Shell.Refresh();
        }
    }

    private static FindingMaturity? NextMaturity(Finding f) => f.Maturity switch
    {
        FindingMaturity.New => FindingMaturity.Confirming,
        FindingMaturity.Confirming => f.ReproSuccesses > 0 ? FindingMaturity.Reproduced : null,
        FindingMaturity.Reproduced => FindingMaturity.CauseSupported,
        FindingMaturity.CauseSupported => FindingMaturity.FixReady,
        FindingMaturity.FixReady => FindingMaturity.ReTest,
        FindingMaturity.ReTest => f.EvidenceCount > 0 ? FindingMaturity.Verified : null,
        _ => null,
    };

    private async void OnReproduce(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target)
        {
            return;
        }

        ReproButton.IsEnabled = false;
        Shell.Log("RPR.START", $"Attempting reproduction of {finding.Id}.", Severity.Info,
            related: finding.Id.ToString());

        var originalSweep = App.Store.Sweeps(target.Id).FirstOrDefault(s => s.Id == finding.SweepId);
        var reproMode = originalSweep?.Mode ?? "Full sweep";

        var sweep = new Sweep
        {
            Id = App.Store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = $"Reproduction of {finding.Id}",
            Mode = reproMode,
            MaxStates = originalSweep?.MaxStates ?? 30,
            MaxSeconds = originalSweep?.MaxSeconds ?? 45,
            MaxDepth = originalSweep?.MaxDepth ?? 4,
            AllowDestructive = originalSweep?.AllowDestructive ?? false,
        };

        App.Store.Save(sweep);

        var before = App.Store.Findings(target.Id).FirstOrDefault(f => f.Id == finding.Id);
        var engine = new SweepEngine(App.Store);

        await engine.RunAsync(sweep, target, null, CancellationToken.None).ConfigureAwait(true);

        var after = App.Store.Findings(target.Id).FirstOrDefault(f => f.Id == finding.Id);
        var recurred = after is not null && before is not null && after.ReproSuccesses > before.ReproSuccesses;

        if (!recurred && after is not null)
        {
            App.Store.Save(after with { ReproAttempts = after.ReproAttempts + 1 });
        }

        Shell.Log(recurred ? "RPR.RECURRED" : "RPR.ABSENT",
            recurred
                ? $"{finding.Id} reproduced under the same conditions."
                : $"{finding.Id} did not recur. It is now less reliably reproducible, not resolved.",
            recurred ? Severity.Medium : Severity.Info,
            related: finding.Id.ToString());

        Refresh();
        Shell.Refresh();
        ReproButton.IsEnabled = true;
    }

    private void OnAdvance(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || NextMaturity(finding) is not { } next)
        {
            return;
        }

        App.Store.Save(finding with { Maturity = next, LastSeenAt = DateTimeOffset.Now });

        Shell.Log("FLT.MATURITY", $"{finding.Id} advanced from {finding.Maturity} to {next}.",
            Severity.Info, related: finding.Id.ToString(),
            detail: $"Advanced by {Environment.UserName}.");

        Refresh();
        Shell.Refresh();
    }

    private void OnAssign(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding)
        {
            return;
        }

        var owner = Environment.UserName;
        App.Store.Save(finding with { Owner = owner });

        Shell.Log("FLT.OWNER", $"{finding.Id} assigned to {owner}.", Severity.Info,
            related: finding.Id.ToString());

        Refresh();
    }
}
