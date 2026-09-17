using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public sealed class EvidenceItemViewModel
{
    public required Evidence Artefact { get; init; }
    public GhoztId Id => Artefact.Id;
    public GhoztId TargetId => Artefact.TargetId;
    public string Kind => Artefact.Kind;
    public string Summary => Artefact.Summary;
    public string Sha256 => Artefact.Sha256;
    public long SizeBytes => Artefact.SizeBytes;
    public string Payload => Artefact.Payload;
    public string SourceAdapter => Artefact.SourceAdapter;
    public GhoztId? FindingId => Artefact.FindingId;
    public GhoztId? SweepId => Artefact.SweepId;
    public DateTimeOffset CapturedAt => Artefact.CapturedAt;
    public bool Sealed => Artefact.Sealed;
    public string SealedBy => Artefact.SealedBy;
    public bool Redacted => Artefact.Redacted;

    public bool IsTampered { get; set; }
    public bool IsVerified { get; set; }
    public bool IsVerifiedOnly => IsVerified && !Sealed && !IsTampered;
}

public partial class EvidenceWorkspace : UserControl, IRefreshableWorkspace
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
    private readonly HashSet<GhoztId> _verified = [];
    private readonly HashSet<GhoztId> _tampered = [];

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }

        Refresh();
        CheckRequestedEvidence();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private Evidence? Selected => (Items.SelectedItem as EvidenceItemViewModel)?.Artefact ?? Items.SelectedItem as Evidence;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            Refresh();
        }
        else if (e.PropertyName is nameof(ShellViewModel.RequestedEvidenceId))
        {
            CheckRequestedEvidence();
        }
    }

    private void CheckRequestedEvidence()
    {
        if (Shell?.RequestedEvidenceId is not { } requestedId)
        {
            return;
        }

        var target = Shell.Target;
        if (target is null)
        {
            return;
        }

        var all = App.Store.EvidenceItems(target.Id);
        var match = all.FirstOrDefault(e => e.Id == requestedId);
        if (match is not null)
        {
            var matchVm = (Items.ItemsSource as IEnumerable<EvidenceItemViewModel>)?.FirstOrDefault(vm => vm.Id == requestedId);
            if (matchVm is not null)
            {
                Items.SelectedItem = matchVm;
                Items.ScrollIntoView(matchVm);
            }
            OpenPreviewDrawer(match);
        }

        Shell.RequestedEvidenceId = null;
    }

    private void OnStatusFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized)
        {
            Refresh();
        }
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            Refresh();
        }
    }

    private void OnKindFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized)
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        if (!IsInitialized || Items is null)
        {
            return;
        }

        var target = Shell?.Target;
        var all = target is null ? [] : App.Store.EvidenceItems(target.Id);

        var selectedKindItem = (KindFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var filterKind = string.IsNullOrEmpty(selectedKindItem) || selectedKindItem == "All Kinds" ? null : selectedKindItem;

        var selectedStatusItem = (StatusFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Statuses";

        var query = Search?.Text.Trim() ?? "";

        var viewModels = all.Select(ev => new EvidenceItemViewModel
        {
            Artefact = ev,
            IsTampered = _tampered.Contains(ev.Id),
            IsVerified = _verified.Contains(ev.Id),
        }).ToList();

        var shown = viewModels.Where(vm =>
        {
            var a = vm.Artefact;
            if (filterKind is not null && !string.Equals(a.Kind, filterKind, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (selectedStatusItem == "Tampered Only" && !vm.IsTampered) return false;
            if (selectedStatusItem == "Verified" && !vm.IsVerified) return false;
            if (selectedStatusItem == "Sealed" && !a.Sealed) return false;
            if (selectedStatusItem == "Redacted" && !a.Redacted) return false;

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
            if (Shell?.RequestedEvidenceId is { } reqEvId)
            {
                var match = shown.FirstOrDefault(e => e.Id == reqEvId);
                if (match is not null)
                {
                    Items.SelectedItem = match;
                    Items.ScrollIntoView(match);
                }
            }
            else if (Items.SelectedItem is not EvidenceItemViewModel ev || !shown.Contains(ev))
            {
                Items.SelectedIndex = 0;
            }
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

        if (artefact is not null)
        {
            OpenPreviewDrawer(artefact);
        }
        else
        {
            EvidencePreviewDrawer.Visibility = Visibility.Collapsed;
        }

        VerifyButton.IsEnabled = artefact is not null;
        ExportArtefactButton.IsEnabled = artefact is not null && !string.IsNullOrWhiteSpace(artefact.Payload);
        RedactButton.IsEnabled = artefact is not null && !artefact.Redacted;
        if (RedactAllButton is not null)
        {
            RedactAllButton.IsEnabled = Shell?.Target is not null;
        }

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

    private void OpenPreviewDrawer(Evidence artefact)
    {
        EvidencePreviewDrawer.Visibility = Visibility.Visible;
        PreviewSummary.Text = artefact.Summary;
        PreviewId.Text = artefact.Id.ToString();
        PreviewKindText.Text = artefact.Kind.ToUpperInvariant();

        PreviewSealBadge.Visibility = artefact.Sealed ? Visibility.Visible : Visibility.Collapsed;
        PreviewSealText.Text = artefact.Sealed ? $"SEALED ({artefact.SealedBy})" : "";

        if (PreviewTamperBadge != null)
        {
            PreviewTamperBadge.Visibility = _tampered.Contains(artefact.Id) ? Visibility.Visible : Visibility.Collapsed;
        }

        PreviewSourceAdapter.Text = string.IsNullOrWhiteSpace(artefact.SourceAdapter) ? "Native Observer" : artefact.SourceAdapter;
        PreviewSizeBytes.Text = $"{artefact.SizeBytes:N0} bytes";
        PreviewCapturedAt.Text = artefact.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        PreviewSha256.Text = artefact.Sha256;

        // Visual screenshot rendering
        if (artefact.Kind.Contains("image", StringComparison.OrdinalIgnoreCase) ||
            artefact.Kind.Contains("screenshot", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bytes = Convert.FromBase64String(artefact.Payload);
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                PreviewImage.Source = bmp;
                PreviewImageContainer.Visibility = Visibility.Visible;
            }
            catch
            {
                PreviewImage.Source = null;
                PreviewImageContainer.Visibility = Visibility.Collapsed;
            }

            PreviewPayloadText.Text = $"[Binary Screenshot Artefact — {artefact.SizeBytes:N0} bytes]\nSHA-256: {artefact.Sha256}";
        }
        else
        {
            PreviewImage.Source = null;
            PreviewImageContainer.Visibility = Visibility.Collapsed;

            if (artefact.Kind.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                artefact.Payload.TrimStart().StartsWith('{') ||
                artefact.Payload.TrimStart().StartsWith('['))
            {
                try
                {
                    using var doc = JsonDocument.Parse(artefact.Payload);
                    PreviewPayloadText.Text = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    PreviewPayloadText.Text = artefact.Payload;
                }
            }
            else
            {
                PreviewPayloadText.Text = artefact.Payload;
            }
        }

        // Cross-workspace link to finding
        var target = Shell?.Target;
        Finding? linkedFault = null;
        if (artefact.FindingId is { } fId && target is not null)
        {
            linkedFault = App.Store.Findings(target.Id).FirstOrDefault(f => f.Id == fId);
        }
        else if (artefact.SweepId is { } sId && target is not null)
        {
            linkedFault = App.Store.Findings(target.Id).FirstOrDefault(f => f.SweepId == sId);
        }

        if (linkedFault is not null || artefact.FindingId is not null)
        {
            BtnViewLinkedFault.Visibility = Visibility.Visible;
            BtnViewLinkedFault.Tag = linkedFault?.Id ?? artefact.FindingId;
        }
        else
        {
            BtnViewLinkedFault.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClosePreview(object sender, RoutedEventArgs e)
    {
        EvidencePreviewDrawer.Visibility = Visibility.Collapsed;
        Items.SelectedItem = null;
    }

    private void OnCopyPreviewPayload(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } artefact)
        {
            return;
        }

        try
        {
            Clipboard.SetText(artefact.Payload);
            Shell?.Log("EVD.COPY", $"Artefact {artefact.Id} payload copied to clipboard.", Severity.Info);
            if (CopyPayloadButton is not null)
            {
                CopyPayloadButton.Content = "Copied!";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    if (CopyPayloadButton is not null)
                    {
                        CopyPayloadButton.Content = "Copy Payload";
                    }
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            Shell?.Log("EVD.COPY.ERR", $"Failed to copy artefact payload: {ex.Message}", Severity.Medium);
        }
    }

    private void OnViewLinkedFault(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GhoztId findingId })
        {
            Shell?.NavigateToFaults(findingId);
        }
        else if (Selected?.FindingId is { } fId)
        {
            Shell?.NavigateToFaults(fId);
        }
        else
        {
            Shell?.NavigateToFaults();
        }
    }

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
                _tampered.Remove(artefact.Id);
                matched++;
            }
            else
            {
                _verified.Remove(artefact.Id);
                _tampered.Add(artefact.Id);
                failed++;
            }
        }

        Shell.Log(
            failed == 0 ? "EVD.VERIFY.ALL" : "EVD.VERIFY.FAIL",
            $"{matched}/{all.Count} evidence hashes verified intact. {(failed > 0 ? $"{failed} MISMATCHES DETECTED!" : "")}",
            failed == 0 ? Severity.Info : Severity.Critical,
            related: target.Id.ToString());

        if (VerifyAllButton is not null)
        {
            VerifyAllButton.Content = failed == 0 ? "Hashes Verified!" : $"{failed} Mismatches!";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                if (VerifyAllButton is not null)
                {
                    VerifyAllButton.Content = "Verify all";
                }
            };
            timer.Start();
        }

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

            if (ExportArtefactButton is not null)
            {
                ExportArtefactButton.Content = "Exported!";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    if (ExportArtefactButton is not null)
                    {
                        ExportArtefactButton.Content = "Export payload";
                    }
                };
                timer.Start();
            }

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
            _tampered.Remove(artefact.Id);
        }
        else
        {
            _verified.Remove(artefact.Id);
            _tampered.Add(artefact.Id);
        }

        Shell.Log(
            matches ? "EVD.VERIFY.OK" : "EVD.VERIFY.FAIL",
            matches
                ? $"{artefact.Id} hash verified; content is unchanged since capture."
                : $"{artefact.Id} HASH MISMATCH — the content has changed since capture.",
            matches ? Severity.Info : Severity.Critical,
            detail: $"recorded={artefact.Sha256}\nrecomputed={recomputed}",
            related: artefact.Id.ToString());

        if (VerifyButton is not null)
        {
            VerifyButton.Content = matches ? "Hash Verified!" : "Hash Mismatch!";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                if (VerifyButton is not null)
                {
                    VerifyButton.Content = "Verify hash";
                }
            };
            timer.Start();
        }

        // Re-evaluate the seal gate now that the verification result has changed.
        UpdateActionGates();
        Refresh();
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
            effect: $"This masks credentials, secrets, tokens, and PII in {artefact.Id} while preserving "
                    + "telemetry integrity and audit lineage. A new cryptographic hash will seal the sanitized payload.",
            typeToConfirm: artefact.Id.ToString(),
            confirmLabel: "Redact")
        {
            Owner = Window.GetWindow(this),
        };

        if (gate.ShowDialog() != true)
        {
            return;
        }

        RedactionEngine.RedactEvidence(artefact, App.Store, Environment.UserName);

        Refresh();
        Shell.Refresh();
    }

    private void OnRedactAll(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            return;
        }

        var count = RedactionEngine.RedactTargetEvidence(App.Store, target.Id, Environment.UserName);

        Shell.Log(
            "EVD.REDACT.ALL",
            count > 0
                ? $"{count} evidence artefacts containing sensitive secrets were redacted."
                : "No unredacted sensitive secrets were detected across evidence artefacts.",
            Severity.Info,
            related: target.Id.ToString());

        Refresh();
        Shell.Refresh();
    }

    private AdaptiveCaptureEngine? _adaptiveCapture;

    private AdaptiveCaptureEngine GetOrCreateAdaptiveCapture()
    {
        return _adaptiveCapture ??= new AdaptiveCaptureEngine(App.Store);
    }

    private void OnCaptureModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CaptureModeFilter?.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<CaptureMode>(item.Content?.ToString(), true, out var mode))
        {
            var engine = GetOrCreateAdaptiveCapture();
            engine.CurrentMode = mode;
            Shell?.Log("CAP.MODE.CHANGED", $"Active capture mode transitioned to {mode}.", Severity.Info);
        }
    }

    private void OnFreezeRingBuffer(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            return;
        }

        var engine = GetOrCreateAdaptiveCapture();
        var evidence = engine.SealForensicPackage(target, "Operator manual freeze from Evidence workspace");

        Shell?.Log(
            "EVD.FREEZE.RINGBUFFER",
            $"Captured and sealed pre-failure forensic ring buffer artefact {evidence.Id} ({evidence.SizeBytes} bytes).",
            Severity.Info,
            related: evidence.Id.ToString());

        Refresh();
        Shell?.Refresh();
    }
}
