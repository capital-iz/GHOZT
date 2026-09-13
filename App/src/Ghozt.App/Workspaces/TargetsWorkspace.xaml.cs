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

    public TargetsWorkspace() => InitializeComponent();

    private ShellViewModel Shell => (ShellViewModel)DataContext;

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
    /// </summary>
    /// <remarks>
    /// A real target rather than fabricated demo data. The blueprint forbids any node, metric or
    /// stream line that pretends to be real system data, so the thing the shell examines out of the
    /// box is a file that genuinely exists on this machine: its own database. Every reading shown
    /// against it - size, reachability, schema - is measured, not invented.
    /// </remarks>
    private void OnSeed(object sender, RoutedEventArgs e)
    {
        var path = App.Store.DatabasePath;

        var target = new Target
        {
            Id = App.Store.NextId(IdKind.Target),
            Name = "Ghozt evidence store",
            Kind = "SQLite database",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            Owner = System.Environment.UserName,
            RepositoryPath = Path.GetDirectoryName(path) ?? "",
            BaseAddress = path,
            BuildLabel = typeof(TargetsWorkspace).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            AuthorityNote = "Self-examination. Ghozt owns this file; observation only, no state change.",
            Reachable = File.Exists(path),
            Notes = "Registered by the seed action so the shell has a real subject rather than fabricated data.",
        };

        App.Store.Save(target);
        Shell.ReloadTargets();
        Shell.Target = App.Store.Targets().FirstOrDefault(t => t.Id == target.Id);

        Shell.Log("TGT.REGISTER", "Registered Ghozt's own evidence store as a self-examination target.",
            Severity.Info, detail: $"path={path}; exists={File.Exists(path)}", related: target.Id.ToString());
    }

    /// <summary>
    /// Checks whether the selected target is actually reachable, and records the result.
    /// </summary>
    /// <remarks>
    /// A real check, not a simulated success. A file target is probed by asking the filesystem; an
    /// HTTP target by issuing a request. Either way the outcome - including a failure - is written
    /// to the ledger and stored on the target, because a reachability claim with no evidence behind
    /// it is exactly what this product exists to eliminate.
    /// </remarks>
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
            else if (!string.IsNullOrWhiteSpace(target.BaseAddress))
            {
                var exists = File.Exists(target.BaseAddress) || Directory.Exists(target.BaseAddress);
                reachable = exists;
                detail = exists
                    ? $"Path exists: {target.BaseAddress} ({new FileInfo(target.BaseAddress).Length:N0} bytes)"
                    : $"Path not found: {target.BaseAddress}";
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
