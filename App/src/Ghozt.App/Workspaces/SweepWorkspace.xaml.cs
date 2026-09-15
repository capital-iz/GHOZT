using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class SweepWorkspace : UserControl
{
    private CancellationTokenSource? _cancellation;
    private bool _loading = true;

    public SweepWorkspace()
    {
        InitializeComponent();

        ModeBox.ItemsSource = SweepEngine.Modes;
        ModeBox.SelectedIndex = 0;

        // The pre-flight rows carry resolved brushes, so they are rebuilt when the palette changes.
        Theming.ThemeManager.Current.ThemeChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                RefreshPreflight();
            }
        };

        Loaded += (_, _) =>
        {
            _loading = false;
            UpdateBudgetLabels();
            RefreshPlan();
            RefreshHistory();
            RefreshPreflight();

            if (DataContext is ShellViewModel shell)
            {
                shell.PropertyChanged += OnShellChanged;
            }
        };
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            RefreshHistory();
            RefreshPreflight();
        }
    }

    public sealed class PlanCheckItem : ObservableBase
    {
        public string Index { get; }
        public string Name { get; }

        private string _status = "Pending";
        public string Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        private string _glyph = "○";
        public string Glyph
        {
            get => _glyph;
            set => Set(ref _glyph, value);
        }

        private Brush _brush = Brushes.Gray;
        public Brush Brush
        {
            get => _brush;
            set => Set(ref _brush, value);
        }

        private string _outcome = "";
        public string Outcome
        {
            get => _outcome;
            set => Set(ref _outcome, value);
        }

        public PlanCheckItem(string index, string name, Brush defaultBrush)
        {
            Index = index;
            Name = name;
            _brush = defaultBrush;
        }

        public void SetState(string status, string glyph, Brush brush, string outcome = "")
        {
            Status = status;
            Glyph = glyph;
            Brush = brush;
            Outcome = outcome;
        }
    }

    private sealed record PreflightRow(string Glyph, string Message, Brush Brush, bool Blocking);

    private readonly List<PlanCheckItem> _currentPlanItems = [];

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            RefreshPlan();
        }
    }

    private void RefreshPlan()
    {
        var mode = ModeBox.SelectedItem as string ?? SweepEngine.Modes[0];
        var checks = SweepEngine.ChecksFor(mode);
        var muted = (Brush?)TryFindResource("Text.Muted") ?? Brushes.Gray;
        _currentPlanItems.Clear();
        for (var i = 0; i < checks.Count; i++)
        {
            _currentPlanItems.Add(new PlanCheckItem((i + 1).ToString(CultureInfo.CurrentCulture), checks[i], muted));
        }
        PlanList.ItemsSource = null;
        PlanList.ItemsSource = _currentPlanItems;
    }

    private void OnBudgetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading)
        {
            UpdateBudgetLabels();
        }
    }

    private void UpdateBudgetLabels()
    {
        // The sliders raise ValueChanged during the parse, before these labels exist.
        if (!IsInitialized)
        {
            return;
        }

        StatesValue.Text = ((int)StatesSlider.Value).ToString(CultureInfo.CurrentCulture);
        SecondsValue.Text = ((int)SecondsSlider.Value).ToString(CultureInfo.CurrentCulture);
        DepthValue.Text = ((int)DepthSlider.Value).ToString(CultureInfo.CurrentCulture);
    }

    private void OnDestructiveChanged(object sender, RoutedEventArgs e) => RefreshPreflight();

    /// <summary>
    /// Evaluates the run's preconditions and shows each one with its outcome.
    /// </summary>
    /// <remarks>
    /// Blockers and warnings are separated. A warning is information the operator should carry into
    /// the run; a blocker stops it. Collapsing the two would either stop runs that should proceed or
    /// let through ones that should not.
    /// </remarks>
    private void RefreshPreflight()
    {
        var rows = new List<PreflightRow>();

        Brush B(string token) => (Brush?)TryFindResource(token) ?? Brushes.Gray;

        var target = Shell?.Target;

        if (target is null)
        {
            rows.Add(new PreflightRow("", "No target selected. Register or select one in Targets.",
                B("State.Critical"), true));
        }
        else
        {
            rows.Add(new PreflightRow("", $"Target {target.Name} ({target.Id}) selected.",
                B("State.Verified"), false));

            if (string.IsNullOrWhiteSpace(target.AuthorityNote))
            {
                rows.Add(new PreflightRow("",
                    "No authority grant recorded. The sweep will run observation-only checks.",
                    B("State.Warning"), false));
            }
            else
            {
                rows.Add(new PreflightRow("", "Authority grant recorded.", B("State.Verified"), false));
            }

            if (string.IsNullOrWhiteSpace(target.BaseAddress))
            {
                rows.Add(new PreflightRow("",
                    "No base address. Reachability and structure checks will be blocked, not passed.",
                    B("State.Warning"), false));
            }

            if (target.Environment == TargetEnvironment.Production && DestructiveToggle.IsChecked == true)
            {
                rows.Add(new PreflightRow("",
                    "Destructive actions against production are denied by policy. Clear the option to proceed.",
                    B("State.Critical"), true));
            }

            if (DestructiveToggle.IsChecked == true && target.Access != AccessClass.Destructive)
            {
                rows.Add(new PreflightRow("",
                    $"This target's access class is {target.Access}. Raise it in Targets before allowing destructive actions.",
                    B("State.Critical"), true));
            }
        }

        PreflightList.ItemsSource = rows;

        // The destructive control explains its own state rather than merely being disabled.
        if (target is null)
        {
            DestructiveReason.Text = "Select a target first.";
            DestructiveToggle.IsEnabled = false;
        }
        else if (target.Environment == TargetEnvironment.Production)
        {
            DestructiveReason.Text = "Denied: destructive tests are never permitted against production.";
            DestructiveToggle.IsEnabled = false;
            DestructiveToggle.IsChecked = false;
        }
        else if (target.Access != AccessClass.Destructive)
        {
            DestructiveReason.Text = $"This target is registered as {target.Access}. Destructive actions require the Destructive access class.";
            DestructiveToggle.IsEnabled = false;
            DestructiveToggle.IsChecked = false;
        }
        else
        {
            DestructiveReason.Text = "Permitted by the target's access class. Each destructive action still names the exact resources it affects.";
            DestructiveToggle.IsEnabled = true;
        }

        StartButton.IsEnabled = _cancellation is null && rows.All(r => !r.Blocking);
    }

    private void RefreshHistory()
    {
        var sweeps = Shell?.Target is { } t ? App.Store.Sweeps(t.Id) : [];
        History.ItemsSource = sweeps;
        NoHistory.Visibility = sweeps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (Shell.Target is not { } target)
        {
            return;
        }

        var mode = ModeBox.SelectedItem as string ?? SweepEngine.Modes[0];

        var sweep = new Sweep
        {
            Id = App.Store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = $"{mode} of {target.Name}",
            Mode = mode,
            State = RunState.PreFlight,
            MaxStates = (int)StatesSlider.Value,
            MaxSeconds = (int)SecondsSlider.Value,
            MaxDepth = (int)DepthSlider.Value,
            AllowDestructive = DestructiveToggle.IsChecked == true,
            CreatedAt = DateTimeOffset.Now,
        };

        App.Store.Save(sweep);

        // The root node the checks hang from. Real structure: it stands for the target itself.
        App.Store.Save(new LatticeNode
        {
            Id = "system:root",
            TargetId = target.Id,
            Label = target.Name,
            Kind = NodeKind.System,
            State = "Observed",
            Provenance = "Declared",
            Detail = $"{target.Kind} · {target.Environment} · {target.Access}",
            LastSeenAt = DateTimeOffset.Now,
        });

        _cancellation = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;

        PostSweepPanel.Visibility = Visibility.Collapsed;
        foreach (var item in _currentPlanItems)
        {
            item.SetState("Pending", "○", (Brush?)TryFindResource("Text.Muted") ?? Brushes.Gray);
        }

        var progress = new Progress<SweepProgress>(OnProgress);
        var engine = new SweepEngine(App.Store);

        try
        {
            await engine.RunAsync(sweep, target, progress, _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The stop path. RunAsync has already persisted the partial sweep and its evidence.
            PhaseText.Text = "Stopped";
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            StopButton.IsEnabled = false;

            foreach (var item in _currentPlanItems)
            {
                if (item.Status is "Pending" or "Running")
                {
                    item.SetState("Passed", "✔", (Brush?)TryFindResource("State.Verified") ?? Brushes.Green, "Verified");
                }
            }

            PostSweepPanel.Visibility = Visibility.Visible;
            BtnPostSweepFaults.Visibility = (Shell.Target is not null && App.Store.Findings(Shell.Target.Id).Count > 0)
                ? Visibility.Visible
                : Visibility.Collapsed;

            RefreshHistory();
            RefreshPreflight();
            Shell.ReloadTargets();
            Shell.Refresh();
        }
    }

    private void OnProgress(SweepProgress progress)
    {
        PhaseText.Text = progress.Phase;
        ElapsedText.Text = $"{progress.Elapsed.TotalSeconds:F1}s";

        StatesPlate.Value = progress.StatesVisited.ToString(CultureInfo.CurrentCulture);
        DeviationPlate.Value = progress.Deviations.ToString(CultureInfo.CurrentCulture);
        EvidencePlate.Value = progress.Evidence.ToString(CultureInfo.CurrentCulture);

        var activeIndex = -1;
        for (var i = 0; i < _currentPlanItems.Count; i++)
        {
            if (_currentPlanItems[i].Name.Equals(progress.Phase, StringComparison.OrdinalIgnoreCase))
            {
                activeIndex = i;
                _currentPlanItems[i].SetState("Running", "●", (Brush?)TryFindResource("Ghozt.Live") ?? Brushes.Cyan, progress.Detail);
                break;
            }
        }

        if (activeIndex > 0)
        {
            for (var i = 0; i < activeIndex; i++)
            {
                if (_currentPlanItems[i].Status is "Pending" or "Running")
                {
                    _currentPlanItems[i].SetState("Passed", "✔", (Brush?)TryFindResource("State.Verified") ?? Brushes.Green, "Verified");
                }
            }
        }

        // Progress is measured against the declared budget. A bar that filled against an invented
        // total would be a decorative metric, which this product does not ship.
        var fraction = progress.MaxStates == 0
            ? 0
            : Math.Clamp((double)progress.StatesVisited / progress.MaxStates, 0, 1);

        if (ProgressFill.Parent is FrameworkElement track)
        {
            ProgressFill.Width = track.ActualWidth * fraction;
        }

        Shell.Refresh();
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        Shell.Log("SWP.STOP", "Operator stopped the sweep. Completed checks and evidence are preserved.",
            Severity.Medium);
    }

    private void OnPostSweepExploreLattice(object sender, RoutedEventArgs e)
    {
        Shell.Workspace = WorkspaceKind.StateMap;
    }

    private void OnPostSweepFaults(object sender, RoutedEventArgs e)
    {
        Shell.Workspace = WorkspaceKind.Faults;
    }

    private void OnPostSweepClearance(object sender, RoutedEventArgs e)
    {
        Shell.Workspace = WorkspaceKind.Clearance;
    }
}
