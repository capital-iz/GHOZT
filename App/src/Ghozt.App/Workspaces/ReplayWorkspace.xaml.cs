using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public sealed record ReplayStepModel
{
    public required int StepIndex { get; init; }
    public required string ActionDescription { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public Evidence? ScreenshotEvidence { get; init; }
    public Evidence? TreeEvidence { get; init; }
    public UiElementSnapshot? UiTree { get; init; }
    public IReadOnlyList<OracleResult> Oracles { get; init; } = [];
    public string StateId { get; init; } = "";
    public string TargetName { get; init; } = "";
    public string Kind { get; init; } = "";
    public bool Success { get; init; } = true;
    public string Error { get; init; } = "";
    public int DurationMs { get; init; }
}

public partial class ReplayWorkspace : UserControl
{
    private readonly DispatcherTimer _playbackTimer;
    private readonly List<ReplayStepModel> _steps = [];
    private int _currentStepIndex;
    private bool _isPlaying;
    private bool _attached;

    public ReplayWorkspace()
    {
        InitializeComponent();

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200),
        };
        _playbackTimer.Tick += OnPlaybackTick;

        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => StopPlayback();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }

        RefreshSweeps();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            RefreshSweeps();
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => RefreshSweeps();

    private void RefreshSweeps()
    {
        StopPlayback();
        var target = Shell?.Target;
        var sweeps = target is null ? [] : App.Store.Sweeps(target.Id);

        SweepPicker.ItemsSource = sweeps;
        if (sweeps.Count > 0)
        {
            SweepPicker.SelectedIndex = 0;
        }
        else
        {
            SweepPicker.SelectedItem = null;
            LoadSweep(null);
        }
    }

    private void OnSweepSelected(object sender, SelectionChangedEventArgs e)
    {
        var sweep = SweepPicker.SelectedItem as Sweep;
        LoadSweep(sweep);
    }

    private void LoadSweep(Sweep? sweep)
    {
        StopPlayback();
        _steps.Clear();

        if (sweep is null || Shell?.Target is null)
        {
            EmptyNotice.Visibility = Visibility.Visible;
            ReplayContent.Visibility = Visibility.Collapsed;
            OutcomeText.Text = "NO SWEEP SELECTED";
            StepSlider.Maximum = 0;
            StepCounterText.Text = "0 / 0";
            return;
        }

        OutcomeText.Text = $"{sweep.State.ToString().ToUpperInvariant()} · {sweep.Mode.ToUpperInvariant()}";

        // Query black-box evidence artefacts for this sweep
        var allEvidence = App.Store.EvidenceItems(sweep.TargetId)
            .Where(e => e.SweepId == sweep.Id)
            .ToList();

        var screenshots = allEvidence
            .Where(e => e.Kind == "ui:screenshot/png")
            .OrderBy(e => e.CapturedAt)
            .ToList();

        var treeSnapshots = allEvidence
            .Where(e => e.Kind == "ui:snapshot/json")
            .ToDictionary(e => e.CapturedAt.ToUnixTimeMilliseconds(), e => e);

        var events = App.Store.RecentEvents(limit: 1000, targetId: sweep.TargetId)
            .Where(ev => ev.SweepId == sweep.Id)
            .OrderBy(ev => ev.At)
            .ToList();

        // Build timeline steps
        for (var i = 0; i < screenshots.Count; i++)
        {
            var shot = screenshots[i];
            
            // Find closest tree snapshot by timestamp
            Evidence? matchedTree = null;
            var shotMs = shot.CapturedAt.ToUnixTimeMilliseconds();
            var closestDiff = long.MaxValue;
            foreach (var kvp in treeSnapshots)
            {
                var diff = Math.Abs(kvp.Key - shotMs);
                if (diff < closestDiff && diff < 2000)
                {
                    closestDiff = diff;
                    matchedTree = kvp.Value;
                }
            }

            UiElementSnapshot? parsedTree = null;
            if (matchedTree is not null && !string.IsNullOrWhiteSpace(matchedTree.Payload))
            {
                try
                {
                    parsedTree = JsonSerializer.Deserialize<UiElementSnapshot>(matchedTree.Payload);
                }
                catch (Exception)
                {
                    // Tree JSON deserialize fallback
                }
            }

            // Find matching event
            var stepEvent = events.FirstOrDefault(ev => ev.RelatedId == shot.Id.ToString())
                            ?? (i < events.Count ? events[i] : null);

            var desc = shot.Summary;
            if (desc.StartsWith("Step ", StringComparison.OrdinalIgnoreCase) && desc.Contains(':'))
            {
                desc = desc[(desc.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
            }

            _steps.Add(new ReplayStepModel
            {
                StepIndex = i,
                ActionDescription = desc,
                Timestamp = shot.CapturedAt,
                ScreenshotEvidence = shot,
                TreeEvidence = matchedTree,
                UiTree = parsedTree,
                StateId = $"state:{i:D3}",
                Success = stepEvent?.Severity != Severity.Critical && stepEvent?.Severity != Severity.High,
                Error = stepEvent?.Detail ?? "",
            });
        }

        if (_steps.Count == 0)
        {
            EmptyNotice.Visibility = Visibility.Visible;
            ReplayContent.Visibility = Visibility.Collapsed;
            StepSlider.Maximum = 0;
            StepCounterText.Text = "0 / 0";
            return;
        }

        EmptyNotice.Visibility = Visibility.Collapsed;
        ReplayContent.Visibility = Visibility.Visible;

        StepSlider.Minimum = 0;
        StepSlider.Maximum = _steps.Count - 1;
        _currentStepIndex = 0;
        ShowStep(0);
    }

    private void ShowStep(int index)
    {
        if (index < 0 || index >= _steps.Count) return;

        _currentStepIndex = index;
        StepSlider.Value = index;
        StepCounterText.Text = $"{index + 1} / {_steps.Count}";

        var step = _steps[index];

        // 1. Screenshot Display
        StepActionHeader.Text = $"Step {index}: {step.ActionDescription}";
        StepTimestampText.Text = step.Timestamp.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        SealShaText.Text = step.ScreenshotEvidence?.Sha256.Length >= 16 
            ? step.ScreenshotEvidence.Sha256[..16] + "…" 
            : (step.ScreenshotEvidence?.Sha256 ?? "UNSEALED");
        StateIdText.Text = step.StateId;
        TransitionText.Text = step.ActionDescription;

        if (step.ScreenshotEvidence is not null && !string.IsNullOrWhiteSpace(step.ScreenshotEvidence.Payload))
        {
            try
            {
                var bytes = Convert.FromBase64String(step.ScreenshotEvidence.Payload);
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                ScreenshotImage.Source = bmp;
                ScreenshotImage.Visibility = Visibility.Visible;
                NoScreenshotNotice.Visibility = Visibility.Collapsed;
            }
            catch (Exception)
            {
                ScreenshotImage.Visibility = Visibility.Collapsed;
                NoScreenshotNotice.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ScreenshotImage.Visibility = Visibility.Collapsed;
            NoScreenshotNotice.Visibility = Visibility.Visible;
        }

        // 2. Action & Execution Details
        ActionDescText.Text = step.ActionDescription;
        ActionTargetText.Text = $"Target element: {(step.UiTree?.Name ?? "App Window")}";
        ActionKindText.Text = $"Timestamp: {step.Timestamp:O}";

        ActionResultStatus.Text = step.Success ? "Verified / Clean Execution" : "Deviation Detected";
        ActionResultStatus.SetResourceReference(TextBlock.ForegroundProperty, step.Success ? "State.Verified" : "State.Critical");
        ActionResultDuration.Text = $"Artifact Size: {step.ScreenshotEvidence?.SizeBytes ?? 0:N0} bytes";
        ActionResultError.Text = step.Error;
        ActionResultError.Visibility = string.IsNullOrWhiteSpace(step.Error) ? Visibility.Collapsed : Visibility.Visible;

        EvidenceScreenshotSha.Text = step.ScreenshotEvidence?.Sha256 ?? "None";
        EvidenceTreeSha.Text = step.TreeEvidence?.Sha256 ?? "None";

        // 3. Oracles
        OracleList.ItemsSource = step.Oracles;
        AllOraclesPassedText.Visibility = step.Oracles.Count == 0 || step.Oracles.All(o => o.Passed) 
            ? Visibility.Visible 
            : Visibility.Collapsed;

        // 4. UI Tree Hierarchy
        if (step.UiTree is not null)
        {
            UiElementTree.ItemsSource = new[] { step.UiTree };
            UiElementTree.Visibility = Visibility.Visible;
            NoTreeText.Visibility = Visibility.Collapsed;
        }
        else
        {
            UiElementTree.ItemsSource = null;
            UiElementTree.Visibility = Visibility.Collapsed;
            NoTreeText.Visibility = Visibility.Visible;
        }

        FirstBtn.IsEnabled = index > 0;
        PrevBtn.IsEnabled = index > 0;
        NextBtn.IsEnabled = index < _steps.Count - 1;
        LastBtn.IsEnabled = index < _steps.Count - 1;
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var targetIndex = (int)Math.Round(e.NewValue);
        if (targetIndex != _currentStepIndex && targetIndex >= 0 && targetIndex < _steps.Count)
        {
            ShowStep(targetIndex);
        }
    }

    private void OnFirstStep(object sender, RoutedEventArgs e) => ShowStep(0);

    private void OnPrevStep(object sender, RoutedEventArgs e)
    {
        if (_currentStepIndex > 0) ShowStep(_currentStepIndex - 1);
    }

    private void OnNextStep(object sender, RoutedEventArgs e)
    {
        if (_currentStepIndex < _steps.Count - 1) ShowStep(_currentStepIndex + 1);
    }

    private void OnLastStep(object sender, RoutedEventArgs e)
    {
        if (_steps.Count > 0) ShowStep(_steps.Count - 1);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            StopPlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void StartPlayback()
    {
        if (_steps.Count == 0) return;
        if (_currentStepIndex >= _steps.Count - 1)
        {
            ShowStep(0);
        }
        _isPlaying = true;
        PlayPauseBtn.Content = "Pause";
        _playbackTimer.Start();
    }

    private void StopPlayback()
    {
        _isPlaying = false;
        PlayPauseBtn.Content = "Play";
        _playbackTimer.Stop();
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (_currentStepIndex < _steps.Count - 1)
        {
            ShowStep(_currentStepIndex + 1);
        }
        else
        {
            StopPlayback();
        }
    }
}
