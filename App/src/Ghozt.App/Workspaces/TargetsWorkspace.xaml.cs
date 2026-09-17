using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class TargetsWorkspace : UserControl, IRefreshableWorkspace
{
    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(4) };

    public TargetsWorkspace()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
            {
                shell.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(ShellViewModel.Targets) or nameof(ShellViewModel.Target))
                    {
                        ApplyFilter();
                    }
                };
                shell.Targets.CollectionChanged += (_, _) => ApplyFilter();
            }
            ApplyFilter();
        };
        Loaded += (_, _) => ApplyFilter();
    }

    public void Refresh()
    {
        Shell?.ReloadTargets();
        ApplyFilter();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void OnKindFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnClearFilter(object sender, RoutedEventArgs e)
    {
        if (SearchBox is not null) SearchBox.Text = "";
        if (KindFilter is not null) KindFilter.SelectedIndex = 0;
    }

    private void ApplyFilter()
    {
        if (Shell is null || TargetsList is null) return;

        var query = SearchBox?.Text?.Trim() ?? "";
        var kind = (KindFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Kinds";

        var filtered = Shell.Targets.Where(t =>
        {
            var matchQuery = string.IsNullOrWhiteSpace(query)
                             || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || t.BaseAddress.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || t.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

            var matchKind = kind is "All Kinds" || t.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase);

            return matchQuery && matchKind;
        }).ToList();

        TargetsList.ItemsSource = filtered;

        if (Shell.Targets.Count == 0)
        {
            if (EmptyState is not null) EmptyState.Visibility = Visibility.Visible;
            if (FilterEmptyState is not null) FilterEmptyState.Visibility = Visibility.Collapsed;
        }
        else if (filtered.Count == 0)
        {
            if (EmptyState is not null) EmptyState.Visibility = Visibility.Collapsed;
            if (FilterEmptyState is not null)
            {
                FilterEmptyState.Visibility = Visibility.Visible;
                if (FilterEmptyDetail is not null)
                {
                    FilterEmptyDetail.Text = !string.IsNullOrWhiteSpace(query)
                        ? $"No registered targets match query \"{query}\"."
                        : $"No registered targets match kind \"{kind}\".";
                }
            }
        }
        else
        {
            if (EmptyState is not null) EmptyState.Visibility = Visibility.Collapsed;
            if (FilterEmptyState is not null) FilterEmptyState.Visibility = Visibility.Collapsed;
        }

        UpdateOperationsConsole();
    }

    private void OnLaunchTarget(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Target target }) return;

        Shell.Target = target;

        var exe = AgenticSweepRunner.ResolveExecutablePath(target);
        if (target.BaseAddress.StartsWith("process:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(target.BaseAddress["process:".Length..], out var pid))
        {
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(pid);
                Shell.Log("TGT.PROCESS", $"Target {target.Name} is running (PID {pid}, {proc.ProcessName}).", Severity.Info, related: target.Id.ToString());
            }
            catch (Exception ex)
            {
                Shell.Log("TGT.PROCESS", $"Target {target.Name} process PID {pid} is not active: {ex.Message}", Severity.Medium, related: target.Id.ToString());
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
        {
            try
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                    UseShellExecute = true,
                });
                Shell.Log("TGT.LAUNCH", $"Launched process for target '{target.Name}': {exe} (PID: {p?.Id}).", Severity.Info, related: target.Id.ToString());
                var updated = target with { Reachable = true };
                App.Store.Save(updated);
                Shell.ReloadTargets();
            }
            catch (Exception ex)
            {
                Shell.Log("TGT.LAUNCH.FAIL", $"Failed to launch '{target.Name}': {ex.Message}", Severity.High, related: target.Id.ToString());
            }
        }
        else
        {
            Shell.Log("TGT.LAUNCH.SKIP", $"Cannot launch target '{target.Name}': No executable binary found at '{target.BaseAddress}'.", Severity.Medium, related: target.Id.ToString());
        }
    }

    private void OnExploreTargetLattice(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Target target })
        {
            Shell.Target = target;
            Shell.Workspace = WorkspaceKind.StateMap;
        }
    }

    private void OnSweepTarget(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Target target })
        {
            Shell.Target = target;
            Shell.Workspace = WorkspaceKind.Sweep;
        }
    }

    private void OnClearanceTarget(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Target target })
        {
            Shell.Target = target;
            Shell.Workspace = WorkspaceKind.Clearance;
        }
    }

    private void OnSelectTarget(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Target target })
        {
            Shell.Target = target;
            Shell.InspectorSubject = target;
            UpdateOperationsConsole();
        }
    }

    private void OnRegister(object sender, RoutedEventArgs e)
    {
        var dialog = new TargetDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var draft = dialog.Result;
        var target = draft with { Id = App.Store.NextId(IdKind.Target) };

        App.Store.Save(target);
        Shell.ReloadTargets();
        Shell.Target = App.Store.Targets().FirstOrDefault(t => t.Id == target.Id);

        Shell.Log("TGT.REGISTER", $"Registered target {target.Name}.", Severity.Info,
            detail: $"id={target.Id}; kind={target.Kind}; environment={target.Environment}; access={target.Access}",
            related: target.Id.ToString());
    }

    /// <summary>
    /// Registers Ghozt's own evidence store as a target.
    private void OnAttachProcess(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerDialog { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true && picker.SelectedProcess is { } proc)
        {
            var build = "1.0.0";
            var repo = "";
            if (!string.IsNullOrWhiteSpace(proc.ExecutablePath) && File.Exists(proc.ExecutablePath))
            {
                try
                {
                    var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(proc.ExecutablePath);
                    build = vi.FileVersion ?? vi.ProductVersion ?? "1.0.0";
                    var dir = Path.GetDirectoryName(proc.ExecutablePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(Path.Combine(dir, ".git")))
                    {
                        repo = dir;
                    }
                }
                catch { }
            }

            var target = new Target
            {
                Id = App.Store.NextId(IdKind.Target),
                Name = proc.MainWindowTitle,
                Kind = "Desktop application",
                Environment = TargetEnvironment.Local,
                Access = AccessClass.ObserveOnly,
                Owner = System.Environment.UserName,
                RepositoryPath = repo,
                BaseAddress = !string.IsNullOrWhiteSpace(proc.ExecutablePath) ? proc.ExecutablePath : $"process:{proc.ProcessId}",
                LaunchCommand = proc.ExecutablePath,
                BuildLabel = build,
                AuthorityNote = $"Local desktop process audit. Attached to {proc.ProcessName} (PID {proc.ProcessId}).",
                Reachable = true,
                Notes = $"Attached via Process Picker from running process '{proc.ProcessName}' with window handle {proc.MainWindowHandle:X}.",
            };

            App.Store.Save(target);
            Shell.ReloadTargets();
            Shell.Target = App.Store.Targets().FirstOrDefault(t => t.Id == target.Id);

            Shell.Log("TGT.ATTACH", $"Attached running process {proc.ProcessName} (PID {proc.ProcessId}) as a live target.",
                Severity.Info, detail: $"title={proc.MainWindowTitle}; exe={proc.ExecutablePath}", related: target.Id.ToString());
        }
    }

    /// <summary>
    /// Registers Ghozt's host subsystem and evidence store as an operational target.
    /// </summary>
    private void OnRegisterLocalHost(object sender, RoutedEventArgs e)
    {
        var path = App.Store.DatabasePath;

        var existing = App.Store.Targets().FirstOrDefault(t => t.Name == "Ghozt Host Subsystem" && t.BaseAddress == path);
        if (existing is not null)
        {
            Shell.Target = existing;
            Shell.InspectorSubject = existing;
            Shell.Log("TGT.SELECT", "Ghozt Host Subsystem already registered. Active target selected.", Severity.Info, related: existing.Id.ToString());
            ApplyFilter();
            return;
        }

        var target = new Target
        {
            Id = App.Store.NextId(IdKind.Target),
            Name = "Ghozt Host Subsystem",
            Kind = "SQLite database",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            Owner = System.Environment.UserName,
            RepositoryPath = Path.GetDirectoryName(path) ?? "",
            BaseAddress = path,
            BuildLabel = typeof(TargetsWorkspace).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            AuthorityNote = "Host system examination. Ghozt owns this database; observation only, no state change.",
            Reachable = File.Exists(path),
            Notes = "Operational local host target mapped to Ghozt's SQLite database store.",
        };

        App.Store.Save(target);
        Shell.ReloadTargets();
        Shell.Target = App.Store.Targets().FirstOrDefault(t => t.Id == target.Id);

        Shell.Log("TGT.REGISTER", "Registered Ghozt Host Subsystem as an operational target.",
            Severity.Info, detail: $"path={path}; exists={File.Exists(path)}", related: target.Id.ToString());
    }

    /// <summary>
    /// Prunes ephemeral test canary targets from the evidence store.
    /// </summary>
    private void OnPruneCanaries(object sender, RoutedEventArgs e)
    {
        var canaries = App.Store.Targets()
            .Where(t => t.Name.Contains("Clearance Canary", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.StartsWith("Test Canary", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (canaries.Count == 0)
        {
            Shell.Log("TGT.PRUNE.NONE", "No ephemeral or canary targets found to prune.", Severity.Info);
            return;
        }

        foreach (var canary in canaries)
        {
            App.Store.DeleteTarget(canary.Id);
        }

        Shell.Log("TGT.PRUNE", $"Pruned {canaries.Count} ephemeral canary targets from evidence store.", Severity.Info,
            detail: string.Join(", ", canaries.Select(c => $"{c.Name} ({c.Id})")));

        Shell.ReloadTargets();
        if (Shell.Target is not null && canaries.Any(c => c.Id == Shell.Target.Id))
        {
            Shell.Target = App.Store.Targets().FirstOrDefault();
        }
        ApplyFilter();
    }

    /// <summary>
    /// Checks whether the selected target is actually reachable, and records the result.
    /// </summary>
    private async void OnProbe(object sender, RoutedEventArgs e)
    {
        if (Shell.Target is not { } target)
        {
            return;
        }

        Shell.Log("TGT.PROBE", $"Probing {target.Name}…", Severity.Info, related: target.Id.ToString());

        var adapter = AdapterRegistry.Shared.Resolve(target);
        Shell.Log("TGT.ADAPTER.RESOLVE",
            $"Resolved examination adapter '{adapter.Manifest.Name}' ({adapter.Manifest.Key} v{adapter.Manifest.Version}).",
            Severity.Info, related: target.Id.ToString());

        var reachable = false;
        var detail = "";

        try
        {
            var preflight = await adapter.PreflightCheckAsync(target).ConfigureAwait(true);
            reachable = preflight.Success;
            detail = $"{preflight.Code}: {preflight.Message}";

            if (preflight.Diagnostics.Count > 0)
            {
                var diagSummary = string.Join("; ", preflight.Diagnostics.Select(d => $"{d.Key}={d.Value}"));
                Shell.Log("TGT.ADAPTER.PREFLIGHT", $"Adapter preflight: {diagSummary}",
                    reachable ? Severity.Info : Severity.Medium, related: target.Id.ToString());
            }

            if (reachable)
            {
                var discovery = await adapter.DiscoverAsync(target).ConfigureAwait(true);
                if (discovery.Success && discovery.DiscoveredNodes.Count > 0)
                {
                    foreach (var node in discovery.DiscoveredNodes)
                    {
                        App.Store.Save(node);
                    }
                    Shell.Log("TGT.ADAPTER.DISCOVER",
                        $"Adapter discovered {discovery.DiscoveredNodes.Count} lattice nodes on '{target.Name}'.",
                        Severity.Info, related: target.Id.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            detail = $"{ex.GetType().Name}: {ex.Message}";
        }

        var updated = target with { Reachable = reachable };
        App.Store.Save(updated);
        Shell.ReloadTargets();

        Shell.Log(
            reachable ? "TGT.PROBE.OK" : "TGT.PROBE.FAIL",
            reachable ? $"{target.Name} is reachable via {adapter.Manifest.Key}." : $"{target.Name} could not be reached via {adapter.Manifest.Key}.",
            reachable ? Severity.Info : Severity.Medium,
            detail: detail,
            related: target.Id.ToString());

        // The probe is evidence: hashed, stored and bound to the target, so a later claim about
        // reachability can be traced to the moment it was measured.
        var payload = $"{DateTimeOffset.Now:O}\ntarget={target.Id}\nreachable={reachable}\n{detail}";
        App.Store.Save(new Evidence
        {
            Id = App.Store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "capability-probe",
            Summary = reachable ? "Target reachable" : "Target unreachable",
            Sha256 = Hashing.Sha256(payload),
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            Payload = payload,
            SourceAdapter = "TargetsWorkspace.Probe",
            CapturedAt = DateTimeOffset.Now,
        });

        Shell.Refresh();
    }

    private void OnDiscoverManifest(object sender, RoutedEventArgs e)
    {
        if (Shell.Target is not { } target)
        {
            return;
        }

        Shell.Log("MAN.DISCOVER", $"Scanning target {target.Name} for behavioural contracts...", Severity.Info, related: target.Id.ToString());

        var engine = new ManifestEngine(App.Store);
        var manifest = engine.Discover(target);

        Shell.Log("MAN.DISCOVER.OK", $"Extracted {manifest.Features.Count} feature contracts into State Lattice.", Severity.Info,
            related: target.Id.ToString(), detail: $"Manifest ID: {manifest.Id}");

        Shell.Refresh();
    }

    private void OnToolbarAuthorityGrant(object sender, RoutedEventArgs e)
    {
        if (Shell.Target is { } target)
        {
            OpenAuthorityGrantDialog(target);
        }
    }

    private void OnTargetCardAuthorityGrant(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Target target })
        {
            OpenAuthorityGrantDialog(target);
        }
    }

    private void OpenAuthorityGrantDialog(Target target)
    {
        var dialog = new AuthorityGrantDialog(target, App.Store) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            Shell.ReloadTargets();
            Shell.Target = App.Store.Targets().FirstOrDefault(t => t.Id == target.Id);
            Shell.Refresh();
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Shell.Target is not { } target)
        {
            return;
        }

        // A destructive gate that names the exact effect, and requires the target's name typed back.
        // A yes/no prompt on an irreversible action is not a gate; it is a speed bump.
        var confirm = new DestructiveGate(
            title: "Remove target",
            effect: $"This permanently removes {target.Name} ({target.Id}) and every sweep, finding, "
                    + "evidence artefact and event recorded against it. It cannot be undone.",
            typeToConfirm: target.Name)
        {
            Owner = Window.GetWindow(this),
        };

        if (confirm.ShowDialog() != true)
        {
            Shell.Log("TGT.REMOVE.CANCEL", $"Removal of {target.Name} was cancelled at the gate.", Severity.Info);
            return;
        }

        App.Store.DeleteTarget(target.Id);
        Shell.Log("TGT.REMOVE", $"Removed target {target.Name} and all records bound to it.", Severity.High,
            detail: $"id={target.Id}", related: target.Id.ToString());

        Shell.ReloadTargets();
    }

    private void UpdateOperationsConsole()
    {
        if (OpDetailContainer is null) return;

        var target = Shell?.Target ?? Shell?.Targets.FirstOrDefault();
        if (target is null)
        {
            OpDetailContainer.Visibility = Visibility.Collapsed;
            return;
        }

        OpDetailContainer.Visibility = Visibility.Visible;

        if (OpTargetName is not null) OpTargetName.Text = target.Name;
        if (OpTargetId is not null) OpTargetId.Text = target.Id.ToString();
        if (OpBaseAddress is not null) OpBaseAddress.Text = string.IsNullOrWhiteSpace(target.BaseAddress) ? "None specified" : target.BaseAddress;
        if (OpEnvironment is not null) OpEnvironment.Text = target.Environment.ToString();
        if (OpAccess is not null) OpAccess.Text = target.Access.ToString();
        if (OpAuthorityNote is not null)
        {
            OpAuthorityNote.Text = string.IsNullOrWhiteSpace(target.AuthorityNote)
                ? "No authority grant recorded — Ghozt will observe only."
                : target.AuthorityNote;
        }

        if (OpReachableText is not null)
        {
            OpReachableText.Text = target.Reachable ? "Reachable" : "Unreachable";
            OpReachableText.SetResourceReference(TextBlock.ForegroundProperty, target.Reachable ? "State.Verified" : "State.Fault");
        }

        try
        {
            var nodes = App.Store.Nodes(target.Id);
            var edges = App.Store.Edges(target.Id);
            var findings = App.Store.Findings(target.Id);
            var evidence = App.Store.EvidenceItems(target.Id);

            if (OpNodesCount is not null) OpNodesCount.Text = nodes.Count.ToString();
            if (OpEdgesCount is not null) OpEdgesCount.Text = edges.Count.ToString();
            if (OpDeviationsCount is not null) OpDeviationsCount.Text = findings.Count.ToString();
            if (OpEvidenceCount is not null) OpEvidenceCount.Text = evidence.Count.ToString();

            if (OpClearanceVerdictText is not null && OpClearanceVerdictBadge is not null)
            {
                var criticalOrHigh = findings.Count(f => f.Severity is Severity.Critical or Severity.High);
                if (criticalOrHigh > 0)
                {
                    OpClearanceVerdictText.Text = $"CLEARANCE DENIED ({criticalOrHigh} CRITICAL/HIGH FAULTS)";
                    OpClearanceVerdictBadge.SetResourceReference(Border.BackgroundProperty, "State.CriticalSoft");
                    OpClearanceVerdictText.SetResourceReference(TextBlock.ForegroundProperty, "State.Critical");
                }
                else if (findings.Count > 0)
                {
                    OpClearanceVerdictText.Text = $"CONDITIONAL CLEARANCE ({findings.Count} DEVIATIONS)";
                    OpClearanceVerdictBadge.SetResourceReference(Border.BackgroundProperty, "State.WarningSoft");
                    OpClearanceVerdictText.SetResourceReference(TextBlock.ForegroundProperty, "State.Warning");
                }
                else if (nodes.Count > 0)
                {
                    OpClearanceVerdictText.Text = "CLEARANCE ELIGIBLE (0 OPEN DEVIATIONS)";
                    OpClearanceVerdictBadge.SetResourceReference(Border.BackgroundProperty, "State.VerifiedSoft");
                    OpClearanceVerdictText.SetResourceReference(TextBlock.ForegroundProperty, "State.Verified");
                }
                else
                {
                    OpClearanceVerdictText.Text = "EXPLORATION REQUIRED (0 LATTICE NODES)";
                    OpClearanceVerdictBadge.SetResourceReference(Border.BackgroundProperty, "Surface.Inset");
                    OpClearanceVerdictText.SetResourceReference(TextBlock.ForegroundProperty, "Text.Muted");
                }
            }
        }
        catch
        {
            // Store query guard during uninitialized state
        }
    }

    private void OnOpRunSweep(object sender, RoutedEventArgs e)
    {
        if (Shell is null) return;
        if (Shell.Target is null && Shell.Targets.Count > 0)
            Shell.Target = Shell.Targets[0];
        if (Shell.Target is not null)
            Shell.Workspace = WorkspaceKind.Sweep;
    }

    private void OnOpExploreLattice(object sender, RoutedEventArgs e)
    {
        if (Shell is null) return;
        if (Shell.Target is null && Shell.Targets.Count > 0)
            Shell.Target = Shell.Targets[0];
        if (Shell.Target is not null)
            Shell.Workspace = WorkspaceKind.StateMap;
    }

    private void OnOpTriageFaults(object sender, RoutedEventArgs e)
    {
        if (Shell is null) return;
        if (Shell.Target is null && Shell.Targets.Count > 0)
            Shell.Target = Shell.Targets[0];
        if (Shell.Target is not null)
            Shell.Workspace = WorkspaceKind.Faults;
    }

    private void OnOpViewEvidence(object sender, RoutedEventArgs e)
    {
        if (Shell is null) return;
        if (Shell.Target is null && Shell.Targets.Count > 0)
            Shell.Target = Shell.Targets[0];
        if (Shell.Target is not null)
            Shell.Workspace = WorkspaceKind.Evidence;
    }

    private void OnOpClearanceDossier(object sender, RoutedEventArgs e)
    {
        if (Shell is null) return;
        if (Shell.Target is null && Shell.Targets.Count > 0)
            Shell.Target = Shell.Targets[0];
        if (Shell.Target is not null)
            Shell.Workspace = WorkspaceKind.Clearance;
    }
}
