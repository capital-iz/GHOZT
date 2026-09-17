using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class FaultsWorkspace : UserControl, IRefreshableWorkspace
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
    private IReadOnlyList<Finding> _allFindings = [];

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target) or nameof(ShellViewModel.Findings) or nameof(ShellViewModel.RequestedFindingId) or nameof(ShellViewModel.RequestedNodeId))
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        var target = Shell?.Target;
        _allFindings = target is null ? [] : App.Store.Findings(target.Id);
        ApplyFilters();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilters();
        }
    }

    private void ApplyFilters()
    {
        if (_allFindings.Count == 0)
        {
            Queue.ItemsSource = Array.Empty<Finding>();
            if (QueueStatsText is not null) QueueStatsText.Text = "0 findings";
            UpdateEmptyPanel();
            return;
        }

        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var selectedSev = (SeverityFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Severities";
        var selectedState = (StateFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All States";

        var filtered = _allFindings.Where(f =>
        {
            // Query filter: title, ID, actual, expected, or fingerprint
            if (!string.IsNullOrEmpty(query))
            {
                var match = f.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            f.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(f.Actual) && f.Actual.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(f.Expected) && f.Expected.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(f.Fingerprint) && f.Fingerprint.Contains(query, StringComparison.OrdinalIgnoreCase));
                if (!match) return false;
            }

            // Severity filter
            if (selectedSev != "All Severities")
            {
                if (!f.Severity.ToString().Equals(selectedSev, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Lifecycle State filter
            if (selectedState != "All States")
            {
                if (!f.Maturity.ToString().Equals(selectedState, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }).ToList();

        Queue.ItemsSource = filtered;
        if (QueueStatsText is not null)
        {
            QueueStatsText.Text = filtered.Count == _allFindings.Count
                ? $"{filtered.Count} findings"
                : $"{filtered.Count} of {_allFindings.Count} findings";
        }

        EmptyPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (filtered.Count == 0 && _allFindings.Count > 0)
        {
            EmptyTitle.Text = "No matching findings";
            EmptyBody.Text = "No findings match the current search query or filter combination. Clear or adjust filters to view items.";
        }
        else if (_allFindings.Count == 0)
        {
            UpdateEmptyPanel();
        }

        if (filtered.Count > 0)
        {
            if (Shell?.RequestedFindingId is { } reqId)
            {
                var match = filtered.FirstOrDefault(f => f.Id == reqId);
                if (match is not null)
                {
                    Queue.SelectedItem = match;
                    Queue.ScrollIntoView(match);
                }
            }
            else if (!string.IsNullOrWhiteSpace(Shell?.RequestedNodeId))
            {
                var match = filtered.FirstOrDefault(f => f.NodeId.Equals(Shell.RequestedNodeId, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    Queue.SelectedItem = match;
                    Queue.ScrollIntoView(match);
                }
            }
            else if (Queue.SelectedItem is null || !filtered.Contains((Finding)Queue.SelectedItem))
            {
                Queue.SelectedIndex = 0;
            }
        }
    }

    private void UpdateEmptyPanel()
    {
        var target = Shell?.Target;
        var sweeps = target is null ? [] : App.Store.Sweeps(target.Id);

        if (target is null)
        {
            EmptyTitle.Text = "No target selected";
            EmptyBody.Text = "Select a target to see its backlog of findings.";
        }
        else if (sweeps.Count == 0)
        {
            EmptyTitle.Text = "No sweeps recorded";
            EmptyBody.Text = $"Nothing has been run against {target.Name} yet. Run a discovery or sweep to observe behaviour.";
        }
        else
        {
            EmptyTitle.Text = "No open findings";
            EmptyBody.Text = $"Every check on {target.Name} held. If something looks wrong, run a probe or explore a state to look deeper.";
        }
    }

    private void OnExportBugReport(object sender, RoutedEventArgs e)
    {
        var finding = Selected;
        var target = Shell?.Target;
        if (finding is null || target is null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# [DEFECT] {finding.Title} ({finding.Severity})");
        sb.AppendLine();
        sb.AppendLine($"- **ID:** `{finding.Id}`");
        sb.AppendLine($"- **Fingerprint:** `{(string.IsNullOrEmpty(finding.Fingerprint) ? "None" : finding.Fingerprint)}`");
        sb.AppendLine($"- **Lifecycle State:** {finding.Maturity}");
        sb.AppendLine($"- **Affected State Node:** `{finding.NodeId}`");
        sb.AppendLine($"- **Target:** {target.Name} (`{target.Id}`)");
        sb.AppendLine($"- **Environment:** {target.Environment} · Access: {target.Access}");
        sb.AppendLine($"- **First Observed:** {finding.FirstSeenAt:yyyy-MM-dd HH:mm:ss zzz}");
        if (!string.IsNullOrEmpty(finding.Owner))
        {
            sb.AppendLine($"- **Owner:** {finding.Owner}");
        }
        sb.AppendLine();
        sb.AppendLine("## Expected Behavior");
        sb.AppendLine("```");
        sb.AppendLine(string.IsNullOrWhiteSpace(finding.Expected) ? "[No explicit oracle contract recorded]" : finding.Expected);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Actual Observed Deviation");
        sb.AppendLine("```");
        sb.AppendLine(string.IsNullOrWhiteSpace(finding.Actual) ? "[No actual detail recorded]" : finding.Actual);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Reproduction & Trace");
        if (finding.SweepId is not null)
        {
            sb.AppendLine($"- Originating Sweep: `{finding.SweepId}`");
        }
        sb.AppendLine($"- Attempts / Successes: {finding.ReproAttempts} / {finding.ReproSuccesses}");
        sb.AppendLine($"- Reproducibility Rate: {(finding.Reproducibility.HasValue ? $"{finding.Reproducibility.Value * 100:F1}%" : "Untested")}");
        sb.AppendLine($"- 95% Wilson Confidence: [{finding.WilsonConfidence.Lower:F2} .. {finding.WilsonConfidence.Upper:F2}]");
        sb.AppendLine();
        sb.AppendLine("## Cryptographic Evidence Hashes");
        var evList = App.Store.EvidenceItems(findingId: finding.Id);
        if (evList.Count > 0)
        {
            foreach (var ev in evList)
            {
                sb.AppendLine($"- **Evidence ID:** `{ev.Id}` · **Kind:** `{ev.Kind}`");
                sb.AppendLine($"  - SHA-256: `{ev.Sha256}`");
            }
        }
        else
        {
            sb.AppendLine("- No evidence artifact directly bound to this finding.");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Generated by Capital-Iz Ghozt — Autonomous Application & Forensic Testing Engine*");

        try
        {
            Clipboard.SetText(sb.ToString());
            Shell?.Log("FLT.EXPORT.BUG_REPORT", $"Copied defect bug report for {finding.Id} to clipboard.", Severity.Info);
            if (ExportBugReportButton is not null)
            {
                ExportBugReportButton.Content = "Copied to Clipboard!";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    if (ExportBugReportButton is not null)
                    {
                        ExportBugReportButton.Content = "Export Bug Report";
                    }
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            Shell?.Log("FLT.EXPORT.FAIL", $"Failed to copy to clipboard: {ex.Message}", Severity.Medium);
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
        VerifyRepairButton.IsEnabled = finding is not null;
        AssignButton.IsEnabled = finding is not null;
        if (ExportBugReportButton is not null) ExportBugReportButton.IsEnabled = finding is not null;
        if (ReplayFaultButton is not null) ReplayFaultButton.IsEnabled = finding is not null;
        if (LocateMapButton is not null) LocateMapButton.IsEnabled = finding is not null;
        if (ViewEvidenceButton is not null) ViewEvidenceButton.IsEnabled = finding is not null;

        if (finding is null || Shell.Target is null)
        {
            NoSelectionBox.Visibility = Visibility.Visible;
            if (CrossNavCard is not null) CrossNavCard.Visibility = Visibility.Collapsed;
            MaturityCard.Visibility = Visibility.Collapsed;
            FingerprintCard.Visibility = Visibility.Collapsed;
            CausalCard.Visibility = Visibility.Collapsed;
            FixBriefCard.Visibility = Visibility.Collapsed;
            return;
        }

        NoSelectionBox.Visibility = Visibility.Collapsed;
        if (CrossNavCard is not null) CrossNavCard.Visibility = Visibility.Visible;

        // Render Maturity Lifecycle (Table 45)
        RenderMaturityCard(finding);

        // Render Fault Fingerprint & Duplicate Decision (Table 46 & 47)
        RenderFingerprintCard(finding, Shell.Target);

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
        FixBriefDiffText.Text = _currentFixBrief.UnifiedDiff;
        FixBriefPatchStatusText.Visibility = Visibility.Collapsed;
        ApplyPatchButton.IsEnabled = true;
        ApplyPatchButton.Content = "Apply Patch";

        FixBriefCard.Visibility = Visibility.Visible;

        Shell.Log("FXB.GENERATE", $"Generated bounded Fix Brief {_currentFixBrief.Id} for {finding.Id}.",
            Severity.Info, related: finding.Id.ToString(), detail: _currentFixBrief.RootMechanism);
    }

    private void OnCopyFixDiff(object sender, RoutedEventArgs e)
    {
        if (_currentFixBrief is null || string.IsNullOrWhiteSpace(_currentFixBrief.UnifiedDiff))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_currentFixBrief.UnifiedDiff);
            Shell.Log("FXB.COPY.DIFF", $"Copied unified fix diff for {_currentFixBrief.Id} to clipboard.", Severity.Info);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard access may fail in sandboxed/headless scenarios
        }
    }

    private void OnApplyPatch(object sender, RoutedEventArgs e)
    {
        if (_currentFixBrief is null || string.IsNullOrWhiteSpace(_currentFixBrief.UnifiedDiff) || Selected is not { } finding)
        {
            return;
        }

        ApplyPatchButton.IsEnabled = false;
        ApplyPatchButton.Content = "Applied!";

        var result = PatchApplicator.ApplyPatch(_currentFixBrief.UnifiedDiff);

        FixBriefPatchStatusText.Text = result.Message;
        FixBriefPatchStatusText.Visibility = Visibility.Visible;

        Shell.Log("AUDIT.PATCH.APPLIED",
            $"Applied self-healing patch for {finding.Id} ({_currentFixBrief.Id}): {result.Message}",
            result.Success ? Severity.Info : Severity.Medium,
            related: finding.Id.ToString());

        if (finding.Maturity < FindingMaturity.FixReady)
        {
            var updated = finding with { Maturity = FindingMaturity.FixReady };
            App.Store.Save(updated);
            RenderMaturityCard(updated);
        }

        VerifyRepairButton.IsEnabled = true;
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

    private async void OnVerifyRepair(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target) return;

        VerifyRepairButton.IsEnabled = false;
        VerifyRepairButton.Content = "Verifying…";

        try
        {
            var fixBrief = _currentFixBrief ?? new FixBriefEngine(App.Store).Generate(finding, target, _currentHypothesis);
            var verifier = new RepairVerifier(App.Store);
            var result = await verifier.VerifyRepairAsync(target, finding, fixBrief);

            Shell.Log(result.Passed ? "REPAIR.VERIFIED" : "REPAIR.REJECTED",
                result.Detail,
                result.Passed ? Severity.Info : Severity.High,
                related: finding.Id.ToString());

            Refresh();
            Shell.Refresh();
        }
        catch (Exception ex)
        {
            Shell.Log("REPAIR.VERIFY.ERROR", $"Repair verification failed to complete: {ex.Message}", Severity.High);
        }
        finally
        {
            VerifyRepairButton.IsEnabled = Selected is not null;
            VerifyRepairButton.Content = "Verify Repair";
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
        FindingMaturity.New => f.IsPhantom ? FindingMaturity.Phantom : FindingMaturity.Confirming,
        FindingMaturity.Phantom => f.ReproSuccesses > 0 ? FindingMaturity.Reproduced : null,
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

        var evidence = new TransitionEvidence
        {
            CausalHypothesisSupported = _currentHypothesis != null && _currentHypothesis.Confidence >= 0.70,
            CausalConfidence = _currentHypothesis?.Confidence ?? 0.0,
            FixBriefApproved = _currentFixBrief != null,
            FixBriefId = _currentFixBrief?.Id ?? "",
            RepairApplied = true,
            RepairVerified = finding.EvidenceCount > 0,
            SealedEvidenceCount = finding.EvidenceCount,
        };

        if (!FaultFingerprintEngine.CanTransition(finding, next, evidence, out var failureReason))
        {
            MessageBox.Show(failureReason, "Maturity Preconditions Not Met (Patent Table 45)", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Shell.Target is { } target)
        {
            var updated = FaultFingerprintEngine.AdvanceMaturity(finding, next, evidence, App.Store, target);
            Refresh();
            Shell.Refresh();
            RenderMaturityCard(updated);
        }
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

    private void RenderMaturityCard(Finding finding)
    {
        MaturityCard.Visibility = Visibility.Visible;
        MaturityBadgeText.Text = finding.Maturity.ToString().ToUpperInvariant();
        var index = (int)finding.Maturity + 1;
        MaturityStepCountText.Text = $"Stage {index} of 10";

        var next = NextMaturity(finding);
        var pre = next switch
        {
            FindingMaturity.Confirming => "Preconditions: Observation registered; ready for confirmation replays.",
            FindingMaturity.Phantom => "Preconditions: Intermittent occurrence rate (<50%); reproduction envelope incomplete.",
            FindingMaturity.Reproduced => $"Preconditions: {(finding.ReproSuccesses > 0 ? "Satisfied (reproduced successfully)" : "Awaiting successful reproduction")}.",
            FindingMaturity.CauseSupported => $"Preconditions: {(_currentHypothesis != null && _currentHypothesis.Confidence >= 0.70 ? "Satisfied (Causal confidence " + _currentHypothesis.Confidence.ToString("P0") + ")" : "Requires causal hypothesis with >= 70% confidence")}.",
            FindingMaturity.FixReady => $"Preconditions: {(_currentFixBrief != null ? "Satisfied (Fix Brief approved)" : "Requires approved Fix Brief contract")}.",
            FindingMaturity.ReTest => "Preconditions: Code repair applied; awaiting verification execution.",
            FindingMaturity.Verified => $"Preconditions: {(finding.EvidenceCount > 0 ? "Satisfied (Sealed evidence attached)" : "Requires sealed post-repair evidence")}.",
            _ => "Terminal state or awaiting operator action.",
        };
        MaturityPreconditionText.Text = pre;
    }

    private void RenderFingerprintCard(Finding finding, Target target)
    {
        FingerprintCard.Visibility = Visibility.Visible;

        var payload = new FaultFingerprintPayload
        {
            ExceptionSignature = finding.Actual,
            SemanticCategory = finding.SemanticCategory ?? SemanticCategory.Other,
            Expected = finding.Expected,
            Actual = finding.Actual,
            NodeId = finding.NodeId,
            Environment = target.Environment,
            OsFamily = "Windows",
            Architecture = "x64",
            ActionSequence = [$"action:{finding.NodeId}"],
        };

        var fp = string.IsNullOrWhiteSpace(finding.Fingerprint)
            ? FaultFingerprintEngine.ComputeCompositeFingerprint(payload)
            : finding.Fingerprint;

        FingerprintIdText.Text = fp;

        var existingFindings = App.Store.Findings(target.Id).Where(f => f.Id != finding.Id).ToList();
        var decision = FaultFingerprintEngine.ClassifyDuplicate(finding, payload, existingFindings, target);

        DuplicateBadgeText.Text = decision.Outcome switch
        {
            DuplicateOutcome.SameFault => "SAME FAULT",
            DuplicateOutcome.Variant => "VARIANT",
            DuplicateOutcome.Related => "RELATED",
            DuplicateOutcome.Independent => "INDEPENDENT",
            _ => "NEW",
        };

        DuplicateBadgeText.Foreground = decision.Outcome switch
        {
            DuplicateOutcome.SameFault => (Brush)TryFindResource("State.Fault"),
            DuplicateOutcome.Variant => (Brush)TryFindResource("Accent.Text"),
            DuplicateOutcome.Related => (Brush)TryFindResource("Authority.Clearance"),
            _ => (Brush)TryFindResource("State.Verified"),
        };

        DuplicateRationaleText.Text = decision.Rationale;

        FingerprintFeaturesList.Children.Clear();
        var features = new[]
        {
            ("F1 (Exception)", FaultFingerprintEngine.NormalizeExceptionSignature(payload.ExceptionSignature)),
            ("F2 (Semantic Diff)", $"Category: {payload.SemanticCategory}"),
            ("F3 (Lattice Node)", string.IsNullOrWhiteSpace(finding.NodeId) ? "<root>" : finding.NodeId),
            ("F4 (Action Sketch)", payload.ActionSequence.Count > 0 ? string.Join(", ", payload.ActionSequence) : "<implicit>"),
            ("F5 (State Delta)", string.IsNullOrWhiteSpace(finding.Actual) ? "<none>" : "actual_hash:" + Hashing.Sha256(finding.Actual)[..8]),
            ("F6 (Dependency)", "<local_process>"),
            ("F7 (Environment)", $"{target.Environment} / Windows x64"),
        };

        foreach (var (label, val) in features)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            sp.Children.Add(new TextBlock
            {
                Text = label + ": ",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Width = 110,
            });
            sp.Children.Add(new TextBlock
            {
                Text = val,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                MaxWidth = 230,
            });
            FingerprintFeaturesList.Children.Add(sp);
        }
    }

    private void OnCopyFingerprint(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(FingerprintIdText.Text))
        {
            Clipboard.SetText(FingerprintIdText.Text);
            Shell.Log("FLT.COPY", $"Copied fingerprint {FingerprintIdText.Text} to clipboard.", Severity.Info);
        }
    }

    private void OnExportGitHubIssue(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target) return;

        try
        {
            var evidence = App.Store.EvidenceItems(findingId: finding.Id);
            var markdown = IssueTrackerSyncEngine.FormatGitHubIssueMarkdown(finding, target, _currentHypothesis, evidence);
            try { Clipboard.SetText(markdown); } catch { }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz", "Ghozt", "Issues");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{finding.Id}-github.md");
            File.WriteAllText(path, markdown);

            Shell.Log("ISSUE.EXPORT.GITHUB", $"Exported GitHub Issue for {finding.Id} with Table 46 fingerprint token (copied to clipboard & saved).",
                Severity.Info, related: finding.Id.ToString());
        }
        catch (Exception ex)
        {
            Shell.Log("ISSUE.EXPORT.FAIL", $"Failed to export GitHub issue: {ex.Message}", Severity.Medium, related: finding.Id.ToString());
        }
    }

    private void OnExportJiraIssue(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target) return;

        try
        {
            var json = IssueTrackerSyncEngine.FormatJiraJson(finding, "GZT");
            try { Clipboard.SetText(json); } catch { }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz", "Ghozt", "Issues");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{finding.Id}-jira.json");
            File.WriteAllText(path, json);

            Shell.Log("ISSUE.EXPORT.JIRA", $"Exported Jira Cloud JSON for {finding.Id} with Table 46 fingerprint token (copied to clipboard & saved).",
                Severity.Info, related: finding.Id.ToString());
        }
        catch (Exception ex)
        {
            Shell.Log("ISSUE.EXPORT.FAIL", $"Failed to export Jira issue: {ex.Message}", Severity.Medium, related: finding.Id.ToString());
        }
    }

    private void OnWaiveFinding(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target) return;

        var evidence = new TransitionEvidence
        {
            WaivedBy = Environment.UserName,
            WaiverReason = "Operational risk accepted by operator waiver.",
            WaivedUntil = DateTimeOffset.Now.AddDays(30),
        };

        try
        {
            var updated = FaultFingerprintEngine.AdvanceMaturity(finding, FindingMaturity.Waived, evidence, App.Store, target);
            Refresh();
            Shell.Refresh();
            RenderMaturityCard(updated);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Waiver Rejected", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRejectFinding(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target) return;

        var evidence = new TransitionEvidence
        {
            RejectionReason = "Determined as test artifact / invalid oracle assertion.",
        };

        try
        {
            var updated = FaultFingerprintEngine.AdvanceMaturity(finding, FindingMaturity.Rejected, evidence, App.Store, target);
            Refresh();
            Shell.Refresh();
            RenderMaturityCard(updated);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rejection Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnReplayFault(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding) return;
        if (finding.SweepId is not null)
        {
            Shell.NavigateToReplay(finding.SweepId.Value, finding.Id);
        }
        else
        {
            var sweeps = App.Store.Sweeps(finding.TargetId);
            if (sweeps.Count > 0)
            {
                Shell.NavigateToReplay(sweeps[0].Id, finding.Id);
            }
        }
    }

    private void OnLocateOnStateMap(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding) return;
        var nodeId = !string.IsNullOrWhiteSpace(finding.NodeId) ? finding.NodeId : "system:root";
        Shell.NavigateToStateMap(nodeId);
    }

    private void OnViewEvidence(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding) return;
        var evidenceList = App.Store.EvidenceItems(findingId: finding.Id);
        if (evidenceList.Count > 0)
        {
            Shell.NavigateToEvidence(evidenceList[0].Id);
        }
        else
        {
            Shell.Workspace = WorkspaceKind.Evidence;
        }
    }
}

