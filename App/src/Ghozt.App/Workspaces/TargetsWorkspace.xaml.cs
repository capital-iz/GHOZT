using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class TargetsWorkspace : UserControl
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
                    if (e.PropertyName is nameof(ShellViewModel.Targets))
                    {
                        ApplyFilter();
                    }
                };
            }
            ApplyFilter();
        };
        Loaded += (_, _) => ApplyFilter();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void OnKindFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

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

    private void OnSelectTarget(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Target target })
        {
            Shell.Target = target;
            Shell.InspectorSubject = target;
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
    /// Checks whether the selected target is actually reachable, and records the result.
    /// </summary>
    private async void OnProbe(object sender, RoutedEventArgs e)
    {
        if (Shell.Target is not { } target)
        {
            return;
        }

        Shell.Log("TGT.PROBE", $"Probing {target.Name}…", Severity.Info, related: target.Id.ToString());

        var reachable = false;
        var detail = "";

        try
        {
            if (Uri.TryCreate(target.BaseAddress, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                using var response = await Probe.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(true);
                reachable = response.IsSuccessStatusCode;
                detail = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {uri}";
            }
            else if (target.BaseAddress.StartsWith("process:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(target.BaseAddress["process:".Length..], out var pid))
            {
                try
                {
                    var proc = System.Diagnostics.Process.GetProcessById(pid);
                    reachable = !proc.HasExited;
                    detail = reachable
                        ? $"Process active: {proc.ProcessName} (PID {pid}, responding={proc.Responding})"
                        : $"Process has exited: PID {pid}";
                }
                catch (Exception pex)
                {
                    reachable = false;
                    detail = $"Process unreachable: {pex.Message}";
                }
            }
            else if (!string.IsNullOrWhiteSpace(target.BaseAddress))
            {
                if (Directory.Exists(target.BaseAddress))
                {
                    reachable = true;
                    var dInfo = new DirectoryInfo(target.BaseAddress);
                    detail = $"Directory exists: {target.BaseAddress} (created {dInfo.CreationTimeUtc:O})";
                }
                else if (File.Exists(target.BaseAddress))
                {
                    reachable = true;
                    var fInfo = new FileInfo(target.BaseAddress);
                    detail = $"File exists: {target.BaseAddress} ({fInfo.Length:N0} bytes)";
                }
                else
                {
                    reachable = false;
                    detail = $"Path not found: {target.BaseAddress}";
                }
            }
            else
            {
                detail = "No base address is registered, so reachability cannot be established.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            // The failure is the result, and it is recorded as one. Swallowing it and reporting
            // "unreachable" with no reason would hide which of several causes applied.
            detail = $"{ex.GetType().Name}: {ex.Message}";
        }

        var updated = target with { Reachable = reachable };
        App.Store.Save(updated);
        Shell.ReloadTargets();

        Shell.Log(
            reachable ? "TGT.PROBE.OK" : "TGT.PROBE.FAIL",
            reachable ? $"{target.Name} is reachable." : $"{target.Name} could not be reached.",
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
}
