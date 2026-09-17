using System.Globalization;
using System.Windows;
using Ghozt.App.Theming;
using Ghozt.Core;
using Ghozt.Theming;

namespace Ghozt.App.Shell;

/// <summary>How much of the black-box tray is showing.</summary>
public enum TrayState
{
    Collapsed,
    Peek,
    Half,
}

/// <summary>
/// The persistent operator frame: the truth that stays visible whichever workspace is open.
/// </summary>
/// <remarks>
/// The selected target and build never reset implicitly when switching workspaces. That is the whole
/// point of holding them here rather than in each workspace: a forensics tool whose target silently
/// changed between screens would let an operator draw a conclusion about the wrong system.
/// </remarks>
public sealed class ShellViewModel : ObservableBase
{
    private readonly GhoztStore _store;
    private WorkspaceKind _workspace = WorkspaceKind.Targets;
    private Target? _target;
    private TrayState _tray = TrayState.Peek;
    private string _streamFilter = "";
    private string _streamSeverityFilter = "All Severities";
    private bool _autoScrollEnabled = true;
    private object? _inspectorSubject;
    private bool _commandPaletteOpen;
    private bool _recording = true;

    public ShellViewModel(GhoztStore store)
    {
        _store = store;

        Targets = new ObservableList<Target>(store.Targets());
        Events = new ObservableList<LedgerEvent>();
        Findings = new ObservableList<Finding>();

        SelectWorkspace = new RelayCommand(p =>
        {
            if (p is WorkspaceKind kind)
            {
                Workspace = kind;
            }
        });

        SetTray = new RelayCommand(p =>
        {
            if (p is TrayState state)
            {
                Tray = state;
            }
        });

        ToggleTray = new RelayCommand(() =>
            Tray = Tray == TrayState.Collapsed ? TrayState.Peek : TrayState.Collapsed);

        OpenCommandPalette = new RelayCommand(() => CommandPaletteOpen = !CommandPaletteOpen);

        // Emergency control. Reachable during any execution, and it names what it does rather than
        // presenting one ambiguous "stop".
        StopAll = new RelayCommand(
            () => Log("RUN.STOP", "Operator issued a global stop.", Severity.High),
            () => IsAnyRunActive);

        PauseAll = new RelayCommand(
            () => Log("RUN.PAUSE", "Operator paused visual streaming; execution continues.", Severity.Medium),
            () => IsAnyRunActive);

        ToggleRecorder = new RelayCommand(() =>
        {
            Recording = !Recording;
            Log("EVD.RECORDER", Recording ? "Evidence recorder armed." : "Evidence recorder stopped.",
                Recording ? Severity.Info : Severity.Medium);
        });

        CopyVisibleEvents = new RelayCommand(ExecuteCopyVisibleEvents);
        ToggleAutoScroll = new RelayCommand(() => AutoScrollEnabled = !AutoScrollEnabled);
        ExportStreamJson = new RelayCommand(ExportVisibleEventsJson);
        ExportStreamNdjson = new RelayCommand(ExportVisibleEventsNdjson);

        Refresh();

        if (Targets.Count > 0)
        {
            _target = Targets[0];
        }
    }

    public ObservableList<Target> Targets { get; }

    public ObservableList<LedgerEvent> Events { get; }

    public ObservableList<Finding> Findings { get; }

    public ThemeManager Theme => ThemeManager.Current;

    public RelayCommand SelectWorkspace { get; }

    public RelayCommand SetTray { get; }

    public RelayCommand ToggleTray { get; }

    public RelayCommand OpenCommandPalette { get; }

    public RelayCommand StopAll { get; }

    public RelayCommand PauseAll { get; }

    public RelayCommand ToggleRecorder { get; }

    public RelayCommand CopyVisibleEvents { get; }

    public RelayCommand ToggleAutoScroll { get; }

    public RelayCommand ExportStreamJson { get; }

    public RelayCommand ExportStreamNdjson { get; }

    public bool AutoScrollEnabled
    {
        get => _autoScrollEnabled;
        set
        {
            if (Set(ref _autoScrollEnabled, value))
            {
                OnPropertyChanged(nameof(AutoScrollLabel));
            }
        }
    }

    public string AutoScrollLabel => _autoScrollEnabled ? "Auto-Scroll: ON" : "Auto-Scroll: OFF";

    public string StreamSeverityFilter
    {
        get => _streamSeverityFilter;
        set
        {
            if (Set(ref _streamSeverityFilter, value))
            {
                Refresh();
            }
        }
    }

    public Severity? ResolveMinSeverity() => _streamSeverityFilter switch
    {
        "Critical Only" => Severity.Critical,
        "High & Critical" => Severity.High,
        "Warnings & Faults" => Severity.Low,
        "Info Only" => Severity.Info,
        _ => null,
    };

    public event Action? RefreshRequested;

    public event Action? FocusSearchRequested;

    public void RequestRefresh()
    {
        Refresh();
        RefreshRequested?.Invoke();
        Log("SYS.REFRESH", $"Operator refreshed {CurrentWorkspace.Title} workspace.", Severity.Info);
    }

    public void RequestFocusSearch()
    {
        FocusSearchRequested?.Invoke();
    }

    private void ExecuteCopyVisibleEvents()
    {
        if (Events.Count == 0) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Black-Box Event Stream Export — {TargetName} ({Events.Count} events at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine("Timestamp\tID\tCode\tSeverity\tMessage\tActor\tDetail");
            foreach (var evt in Events)
            {
                sb.AppendLine($"{evt.At:HH:mm:ss.fff}\t{evt.Id}\t{evt.Code}\t{evt.Severity}\t{evt.Message}\t{evt.Actor}\t{evt.Detail}");
            }
            Clipboard.SetText(sb.ToString());
            Log("EVT.COPY", $"Copied {Events.Count} visible events to clipboard.", Severity.Info);
        }
        catch
        {
            // Safe clipboard access handling on native desktop
        }
    }

    public void ExportVisibleEventsJson()
    {
        if (Events.Count == 0) return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Events, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Clipboard.SetText(json);
            Log("STREAM.EXPORT", $"Exported {Events.Count} visible events to clipboard as JSON array.", Severity.Info);
        }
        catch (Exception ex)
        {
            Log("STREAM.EXPORT.FAULT", $"Failed to export JSON stream: {ex.Message}", Severity.High);
        }
    }

    public void ExportVisibleEventsNdjson()
    {
        if (Events.Count == 0) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var evt in Events)
            {
                sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(evt));
            }
            Clipboard.SetText(sb.ToString());
            Log("STREAM.EXPORT", $"Exported {Events.Count} visible events to clipboard as NDJSON.", Severity.Info);
        }
        catch (Exception ex)
        {
            Log("STREAM.EXPORT.FAULT", $"Failed to export NDJSON stream: {ex.Message}", Severity.High);
        }
    }

    public IReadOnlyList<WorkspaceDefinition> Workspaces => WorkspaceDefinition.All;

    public WorkspaceKind Workspace
    {
        get => _workspace;
        set
        {
            if (Set(ref _workspace, value))
            {
                OnPropertyChanged(nameof(CurrentWorkspace));
                Refresh();
            }
        }
    }

    public WorkspaceDefinition CurrentWorkspace => WorkspaceDefinition.For(_workspace);

    /// <summary>The target every workspace is scoped to. Never changed implicitly.</summary>
    public Target? Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, value))
            {
                OnPropertyChanged(nameof(HasTarget));
                OnPropertyChanged(nameof(TargetName));
                OnPropertyChanged(nameof(BuildLabel));
                OnPropertyChanged(nameof(EnvironmentLabel));
                OnPropertyChanged(nameof(AccessLabel));
                OnPropertyChanged(nameof(EnvironmentToken));
                OnPropertyChanged(nameof(AccessToken));
                OnPropertyChanged(nameof(IsProduction));
                OnPropertyChanged(nameof(ConnectionLabel));
                OnPropertyChanged(nameof(ConnectionToken));
                Refresh();

                if (_target is not null)
                {
                    Log("TGT.ACTIVE", $"Active target switched to {_target.Name} ({_target.Id}).", Severity.Info, related: _target.Id.ToString());
                }
                else
                {
                    Log("TGT.ACTIVE", "Active target cleared (no target selected).", Severity.Medium);
                }
            }
        }
    }

    public bool HasTarget => _target is not null;

    // ---- the restricted status strip: the current truth about what Ghozt is examining ----

    public string TargetName => _target?.Name ?? "No target";

    public string TargetId => _target?.Id.ToString() ?? "—";

    public string BuildLabel => string.IsNullOrWhiteSpace(_target?.BuildLabel) ? "no build recorded" : _target!.BuildLabel;

    public string EnvironmentLabel => _target?.Environment.ToString().ToUpperInvariant() ?? "—";

    /// <summary>
    /// The colour band for the environment.
    /// </summary>
    /// <remarks>
    /// Production is critical rather than merely warning, and it is the one environment that also
    /// gets a persistent banner. An operator who cannot tell at a glance that they are pointed at
    /// production is one command away from a real incident.
    /// </remarks>
    public string EnvironmentToken => _target?.Environment switch
    {
        TargetEnvironment.Production => "State.Critical",
        TargetEnvironment.Staging => "State.Warning",
        TargetEnvironment.Development => "Ghozt.Telemetry",
        TargetEnvironment.Local => "Ghozt.Live",
        _ => "Text.Muted",
    };

    public bool IsProduction => _target?.Environment == TargetEnvironment.Production;

    public string AccessLabel => _target?.Access switch
    {
        AccessClass.ObserveOnly => "READ-ONLY",
        AccessClass.Interact => "INTERACT",
        AccessClass.StateChanging => "STATE-CHANGING",
        AccessClass.Destructive => "DESTRUCTIVE",
        _ => "—",
    };

    public string AccessToken => _target?.Access switch
    {
        AccessClass.ObserveOnly => "Ghozt.Live",
        AccessClass.Interact => "Ghozt.Telemetry",
        AccessClass.StateChanging => "State.Warning",
        AccessClass.Destructive => "State.Critical",
        _ => "Text.Muted",
    };

    public string ConnectionLabel => _target is null ? "—" : _target.Reachable ? "CONNECTED" : "UNREACHABLE";

    public string ConnectionToken => _target is null
        ? "Text.Muted"
        : _target.Reachable ? "State.Verified" : "State.Warning";

    public bool Recording
    {
        get => _recording;
        set
        {
            if (Set(ref _recording, value))
            {
                OnPropertyChanged(nameof(RecorderLabel));
                OnPropertyChanged(nameof(RecorderToken));
            }
        }
    }

    public string RecorderLabel => _recording ? "RECORDING" : "RECORDER OFF";

    public string RecorderToken => _recording ? "State.Fault" : "Text.Muted";

    /// <summary>Whether anything is currently executing. Gates the emergency controls.</summary>
    public bool IsAnyRunActive => _store.Sweeps().Any(s =>
        s.State is RunState.Running or RunState.PreFlight or RunState.Stopping or RunState.Paused);

    public string RunModeLabel
    {
        get
        {
            var active = _store.Sweeps().FirstOrDefault(s => s.State is RunState.Running or RunState.Paused);
            return active is null ? "IDLE" : active.Mode.ToUpperInvariant();
        }
    }

    public string RunModeToken => IsAnyRunActive ? "Ghozt.Live" : "Text.Muted";

    // ---- black-box tray ----

    public TrayState Tray
    {
        get => _tray;
        set
        {
            if (Set(ref _tray, value))
            {
                OnPropertyChanged(nameof(TrayHeight));
                OnPropertyChanged(nameof(TrayExpanded));
            }
        }
    }

    public GridLength TrayHeight => _tray switch
    {
        TrayState.Collapsed => new GridLength(0),
        TrayState.Peek => new GridLength(160),
        _ => new GridLength(1, GridUnitType.Star),
    };

    public bool TrayExpanded => _tray != TrayState.Collapsed;

    public string StreamFilter
    {
        get => _streamFilter;
        set
        {
            if (Set(ref _streamFilter, value))
            {
                Refresh();
            }
        }
    }

    // ---- forensics inspector ----

    /// <summary>Whatever the inspector is currently describing. Follows selection.</summary>
    public object? InspectorSubject
    {
        get => _inspectorSubject;
        set
        {
            if (Set(ref _inspectorSubject, value))
            {
                OnPropertyChanged(nameof(HasInspectorSubject));
            }
        }
    }

    public bool HasInspectorSubject => _inspectorSubject is not null;

    public bool CommandPaletteOpen
    {
        get => _commandPaletteOpen;
        set => Set(ref _commandPaletteOpen, value);
    }

    // ---- cross-workspace forensic navigation handoffs ----

    private GhoztId? _requestedSweepId;
    public GhoztId? RequestedSweepId
    {
        get => _requestedSweepId;
        set => Set(ref _requestedSweepId, value);
    }

    private GhoztId? _requestedFindingId;
    public GhoztId? RequestedFindingId
    {
        get => _requestedFindingId;
        set => Set(ref _requestedFindingId, value);
    }

    private string? _requestedNodeId;
    public string? RequestedNodeId
    {
        get => _requestedNodeId;
        set => Set(ref _requestedNodeId, value);
    }

    private GhoztId? _requestedEvidenceId;
    public GhoztId? RequestedEvidenceId
    {
        get => _requestedEvidenceId;
        set => Set(ref _requestedEvidenceId, value);
    }

    public void NavigateToReplay(GhoztId sweepId, GhoztId? findingId = null)
    {
        RequestedSweepId = sweepId;
        RequestedFindingId = findingId;
        Workspace = WorkspaceKind.Replay;
        Log("NAV.REPLAY", $"Navigating to Replay for sweep {sweepId}" + (findingId != null ? $" and finding {findingId}" : ""), Severity.Info);
    }

    public void NavigateToStateMap(string? nodeId = null)
    {
        RequestedNodeId = nodeId;
        Workspace = WorkspaceKind.StateMap;
        Log("NAV.STATEMAP", !string.IsNullOrWhiteSpace(nodeId)
            ? $"Navigating to State Map for node {nodeId}"
            : "Navigating to State Map", Severity.Info);
    }

    public void NavigateToFaults(GhoztId? findingId = null, string? nodeId = null)
    {
        RequestedFindingId = findingId;
        RequestedNodeId = nodeId;
        Workspace = WorkspaceKind.Faults;
        Log("NAV.FAULTS", $"Navigating to Faults" + (findingId != null ? $" for finding {findingId}" : ""), Severity.Info);
    }

    public void NavigateToEvidence(GhoztId evidenceId)
    {
        RequestedEvidenceId = evidenceId;
        Workspace = WorkspaceKind.Evidence;
        Log("NAV.EVIDENCE", $"Navigating to Evidence for artefact {evidenceId}", Severity.Info);
    }

    public void NavigateToSweep(string? mode = null)
    {
        Workspace = WorkspaceKind.Sweep;
        Log("NAV.SWEEP", "Navigating to Sweep workspace", Severity.Info);
    }

    // ---- counts, for the workspace header's scope line ----

    public int EventCount => _store.CountEvents();

    public string ScopeLine
    {
        get
        {
            if (_target is null)
            {
                return "No target selected — register one to begin.";
            }

            var findings = _store.Findings(_target.Id).Count;
            var evidence = _store.EvidenceItems(_target.Id).Count;
            var sweeps = _store.Sweeps(_target.Id).Count;

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} · {1} sweep{2} · {3} finding{4} · {5} evidence artefact{6}",
                _target.Name,
                sweeps, sweeps == 1 ? "" : "s",
                findings, findings == 1 ? "" : "s",
                evidence, evidence == 1 ? "" : "s");
        }
    }

    /// <summary>Writes one line into the ledger and surfaces it in the tray immediately.</summary>
    public LedgerEvent Log(string code, string message, Severity severity = Severity.Info, string detail = "", string related = "")
    {
        var entry = new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = code,
            Message = message,
            Severity = severity,
            TargetId = _target?.Id,
            RelatedId = related,
            Actor = "operator",
            Detail = detail,
        };

        _store.Append(entry);
        Events.Insert(0, entry);

        // The tray is bounded in memory as well as on screen. The ledger keeps everything; the view
        // keeps what a person can actually scroll.
        while (Events.Count > 600)
        {
            Events.RemoveAt(Events.Count - 1);
        }

        OnPropertyChanged(nameof(EventCount));
        return entry;
    }

    public void ReloadTargets()
    {
        var selected = _target?.Id;
        Targets.Reset(_store.Targets());
        _target = selected is null ? Targets.FirstOrDefault() : Targets.FirstOrDefault(t => t.Id == selected.Value);
        OnPropertyChanged(nameof(Targets));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(HasTarget));
        OnPropertyChanged(nameof(TargetName));
        Refresh();
    }

    /// <summary>Re-reads the derived views from the store.</summary>
    public void Refresh()
    {
        Events.Reset(_store.RecentEvents(
            400,
            _target?.Id,
            string.IsNullOrWhiteSpace(_streamFilter) ? null : _streamFilter.ToUpperInvariant(),
            ResolveMinSeverity()));

        Findings.Reset(_target is null ? [] : _store.Findings(_target.Id));

        OnPropertyChanged(nameof(Findings));
        OnPropertyChanged(nameof(ScopeLine));
        OnPropertyChanged(nameof(EventCount));
        OnPropertyChanged(nameof(IsAnyRunActive));
        OnPropertyChanged(nameof(RunModeLabel));
        OnPropertyChanged(nameof(RunModeToken));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
