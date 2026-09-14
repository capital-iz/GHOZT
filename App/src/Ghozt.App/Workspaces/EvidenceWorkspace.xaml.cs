using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class EvidenceWorkspace : UserControl
{
    public EvidenceWorkspace()
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

    private Evidence? Selected => Items.SelectedItem as Evidence;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            Refresh();
        }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Refresh();

    private void OnKindFilterChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var target = Shell?.Target;
        var all = target is null ? [] : App.Store.EvidenceItems(target.Id);

        var selectedKindItem = (KindFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var filterKind = string.IsNullOrEmpty(selectedKindItem) || selectedKindItem == "All Kinds" ? null : selectedKindItem;

        var query = Search?.Text.Trim() ?? "";
        var shown = all.Where(a =>
        {
            if (filterKind is not null && !string.Equals(a.Kind, filterKind, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return a.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
                || a.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)
                || a.Sha256.Contains(query, StringComparison.OrdinalIgnoreCase)
                || a.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        Items.ItemsSource = shown;
        EmptyPanel.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Completeness is a measured ratio, not a badge. A clearance gate depends on it, so it has
        // to be a number an operator can check.
        var sealedCount = all.Count(a => a.Sealed);
        Completeness.Text = all.Count == 0
            ? "—"
            : $"{sealedCount}/{all.Count} sealed ({(double)sealedCount / all.Count:P0})";

        if (shown.Count > 0)
        {
            return;
        }

        if (target is null)
        {
            EmptyTitle.Text = "No target selected";
            EmptyBody.Text = "Select a target to see the artefacts captured against it.";
        }
        else if (!string.IsNullOrWhiteSpace(query))
        {
            EmptyTitle.Text = "No artefact matches";
            EmptyBody.Text = $"Nothing in {all.Count} artefact(s) matches “{query}”. Clear the search to see them all.";
        }
        else
        {
            EmptyTitle.Text = "No evidence captured";
            EmptyBody.Text = $"Nothing has been captured against {target.Name}. Evidence is produced by "
                             + "sweeps and probes — every check records an artefact, including the ones that pass.";
        }
    }

    private void OnSelect(object sender, SelectionChangedEventArgs e) => UpdateActionGates();

    private void UpdateActionGates()
    {
        var artefact = Selected;
        Shell.InspectorSubject = artefact;

        VerifyButton.IsEnabled = artefact is not null;
        ExportArtefactButton.IsEnabled = artefact is not null && !string.IsNullOrWhiteSpace(artefact.Payload);
        RedactButton.IsEnabled = artefact is not null && !artefact.Redacted;

        // Sealing is gated on the hash having been verified in this session, not merely on an
        // artefact being selected. A seal is an assertion that the content is unchanged; making it
        // without checking would be exactly the unverified completion this product exists to stop.
        SealButton.IsEnabled = artefact is not null && !artefact.Sealed && _verified.Contains(artefact.Id);
        SealButton.ToolTip = artefact is null
            ? "Select an artefact"
            : artefact.Sealed
                ? $"Already sealed by {artefact.SealedBy}"
                : _verified.Contains(artefact.Id)
                    ? "Verifies the hash and records you as the reviewer"
                    : "Verify the hash first — a seal asserts the content is unchanged.";
    }

    private readonly HashSet<GhoztId> _verified = [];

    private void OnVerifyAll(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            return;
        }

        var all = App.Store.EvidenceItems(target.Id);
        int matched = 0;
        int failed = 0;

        foreach (var artefact in all)
        {
            var recomputed = Hashing.Sha256(artefact.Payload);
            if (string.Equals(recomputed, artefact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _verified.Add(artefact.Id);
                matched++;
            }
            else
            {
                _verified.Remove(artefact.Id);
                failed++;
            }
        }

        Shell.Log(
            failed == 0 ? "EVD.VERIFY.ALL" : "EVD.VERIFY.FAIL",
            $"{matched}/{all.Count} evidence hashes verified intact. {(failed > 0 ? $"{failed} MISMATCHES DETECTED!" : "")}",
            failed == 0 ? Severity.Info : Severity.Critical,
            related: target.Id.ToString());

        UpdateActionGates();
        Refresh();
        Shell.Refresh();
    }

    private void OnExportArtefact(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } artefact)
        {
            return;
        }

        try
        {
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz",
                "Ghozt",
                "EvidenceExport");
            Directory.CreateDirectory(exportDir);

            var ext = artefact.Kind.Contains("json", StringComparison.OrdinalIgnoreCase) ? "json"
                : artefact.Kind.Contains("tree", StringComparison.OrdinalIgnoreCase) ? "txt"
                : "txt";
            var fileName = $"{artefact.Id}_{artefact.Kind.Replace('/', '_')}.{ext}";
            var fullPath = Path.Combine(exportDir, fileName);

            File.WriteAllText(fullPath, artefact.Payload);

            Shell.Log("EVD.EXPORT", $"Artefact {artefact.Id} exported to {fullPath}.", Severity.Info,
                detail: $"sha256={artefact.Sha256}; size={artefact.SizeBytes} bytes",
                related: artefact.Id.ToString());

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Shell.Log("EVD.EXPORT.ERR", $"Failed to export artefact {artefact.Id}: {ex.Message}", Severity.High,
                related: artefact.Id.ToString());
        }
    }


    /// <summary>
    /// Recomputes the artefact's hash and compares it with the recorded value.
    /// </summary>
    /// <remarks>
    /// A real recomputation over the stored payload. This is what makes a seal mean something: it is
    /// the difference between "this artefact was captured" and "this artefact has not changed since
    /// it was captured".
    /// </remarks>
    private void OnVerify(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } artefact)
        {
            return;
        }

        var recomputed = Hashing.Sha256(artefact.Payload);
        var matches = string.Equals(recomputed, artefact.Sha256, StringComparison.OrdinalIgnoreCase);

        if (matches)
        {
            _verified.Add(artefact.Id);
        }
        else
        {
            _verified.Remove(artefact.Id);
        }

        Shell.Log(
            matches ? "EVD.VERIFY.OK" : "EVD.VERIFY.FAIL",
            matches
                ? $"{artefact.Id} hash verified; content is unchanged since capture."
                : $"{artefact.Id} HASH MISMATCH — the content has changed since capture.",
            matches ? Severity.Info : Severity.Critical,
            detail: $"recorded={artefact.Sha256}\nrecomputed={recomputed}",
            related: artefact.Id.ToString());

        // Re-evaluate the seal gate now that the verification result has changed.
        UpdateActionGates();
        Shell.Refresh();
    }

    private void OnSeal(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } artefact || !_verified.Contains(artefact.Id))
        {
            return;
        }

        var reviewer = Environment.UserName;
        App.Store.Save(artefact with { Sealed = true, SealedBy = reviewer });

        Shell.Log("EVD.SEAL", $"{artefact.Id} sealed by {reviewer}.", Severity.Info,
            detail: $"sha256={artefact.Sha256}; reviewer={reviewer}; at={DateTimeOffset.Now:O}",
            related: artefact.Id.ToString());

        Refresh();
        Shell.Refresh();
    }

    private void OnRedact(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } artefact)
        {
            return;
        }

        var gate = new DestructiveGate(
            title: "Redact artefact",
            effect: $"This masks the payload of {artefact.Id} in every view and export. The recorded "
                    + "hash still refers to the original content, so the artefact remains verifiable "
                    + "but its payload will no longer be readable here.",
            typeToConfirm: artefact.Id.ToString(),
            confirmLabel: "Redact")
        {
            Owner = Window.GetWindow(this),
        };

        if (gate.ShowDialog() != true)
        {
            return;
        }

        App.Store.Save(artefact with
        {
            Redacted = true,
            Summary = artefact.Summary,
            Payload = "[REDACTED]",
        });

        Shell.Log("EVD.REDACT", $"{artefact.Id} redacted by {Environment.UserName}.", Severity.Medium,
            detail: $"Original hash retained: {artefact.Sha256}", related: artefact.Id.ToString());

        Refresh();
        Shell.Refresh();
    }
}
