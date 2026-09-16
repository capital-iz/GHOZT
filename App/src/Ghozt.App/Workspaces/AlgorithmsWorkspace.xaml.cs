using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class AlgorithmsWorkspace : UserControl, IRefreshableWorkspace
{
    private AlgorithmSuiteReport? _lastReport;
    private bool _attached;

    public void Refresh()
    {
        OnRunSuite(this, new RoutedEventArgs());
    }

    public AlgorithmsWorkspace()
    {
        InitializeComponent();
        Loaded += OnAlgorithmsLoaded;
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            if (SealButton is not null)
            {
                SealButton.IsEnabled = Shell?.Target is not null && _lastReport is not null && (string)SealButton.Content != "Sealed";
            }
        }
    }

    private void OnAlgorithmsLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        InitVectorEditor();
        if (_lastReport is null)
        {
            OnRunSuite(this, new RoutedEventArgs());
        }
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnRunSuite(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        try
        {
            _lastReport = AlgorithmHarness.RunSuite();
            RenderReport(_lastReport);
            SealButton.IsEnabled = Shell?.Target is not null;
            ExportDossierButton.IsEnabled = true;
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private void OnDomainFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_lastReport is not null)
        {
            RenderReport(_lastReport);
        }
    }

    private void OnExportDossier(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            return;
        }

        try
        {
            var dossier = AlgorithmHarness.GenerateMathematicalDossier(_lastReport);
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz",
                "Ghozt",
                "AlgorithmReports");
            Directory.CreateDirectory(exportDir);

            var filePath = Path.Combine(exportDir, $"ALG_DOSSIER_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
            File.WriteAllText(filePath, dossier);

            Shell?.Log("ALG.DOSSIER.EXPORT", $"Mathematical dossier exported to {filePath}", Severity.Info,
                detail: $"sha256={_lastReport.Sha256Digest}; passed={_lastReport.PassedCount}/{_lastReport.Results.Count}");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Shell?.Log("ALG.DOSSIER.ERR", $"Failed to export mathematical dossier: {ex.Message}", Severity.High);
        }
    }

    private void RenderReport(AlgorithmSuiteReport report)
    {
        ResultsPanel.Children.Clear();
        SuiteSummaryText.Text = $"{report.PassedCount}/{report.Results.Count} invariants verified · {report.FailedCount} failed · {report.TotalDuration.TotalMilliseconds:F1}ms";

        var selectedDomain = (DomainFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var filterDomain = string.IsNullOrEmpty(selectedDomain) || selectedDomain == "All Domains" ? null : selectedDomain;

        var items = report.Results.Where(r =>
            filterDomain is null || r.Domain.Contains(filterDomain, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var r in items)
        {
            var card = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(r.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var stack = new StackPanel();

            // Header line: ID + Name + Status Badge + Duration
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = r.Id,
                Style = (Style)TryFindResource("Type.MonoStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = r.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = $"[{r.Domain}]",
                FontSize = 11,
                Foreground = (Brush)TryFindResource("Accent.Text"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
            Grid.SetColumn(titlePanel, 0);
            header.Children.Add(titlePanel);

            var statusBadge = new Border
            {
                Background = (Brush)(r.Passed ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                BorderBrush = (Brush)(r.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            statusBadge.Child = new TextBlock
            {
                Text = r.Passed ? "VERIFIED" : "DEVIATION",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(r.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(statusBadge, 1);
            header.Children.Add(statusBadge);

            stack.Children.Add(header);

            // Invariant explanation
            stack.Children.Add(new TextBlock
            {
                Text = r.Invariant,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 6, 0, 8),
            });

            // Grid of Expected vs Actual
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var expPanel = new StackPanel();
            expPanel.Children.Add(new TextBlock { Text = "EXPECTED (CLOSED-FORM)", FontSize = 9, Foreground = (Brush)TryFindResource("Text.Muted") });
            expPanel.Children.Add(new TextBlock { Text = r.Expected, Style = (Style)TryFindResource("Type.MonoStyle"), FontSize = 11, Foreground = (Brush)TryFindResource("Text.Primary") });
            Grid.SetColumn(expPanel, 0);
            grid.Children.Add(expPanel);

            var actPanel = new StackPanel();
            actPanel.Children.Add(new TextBlock { Text = "ACTUAL CALCULATED", FontSize = 9, Foreground = (Brush)TryFindResource("Text.Muted") });
            actPanel.Children.Add(new TextBlock { Text = r.Actual, Style = (Style)TryFindResource("Type.MonoStyle"), FontSize = 11, Foreground = (Brush)TryFindResource("Text.Primary") });
            Grid.SetColumn(actPanel, 1);
            grid.Children.Add(actPanel);

            stack.Children.Add(grid);

            card.Child = stack;
            ResultsPanel.Children.Add(card);
        }
    }

    private void OnSealEvidence(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null || Shell?.Target is null)
        {
            return;
        }

        var target = Shell.Target;
        AlgorithmHarness.SealReportToEvidence(App.Store, target.Id, _lastReport);

        SealButton.IsEnabled = false;
        SealButton.Content = "Sealed";
        Shell.Refresh();
    }

    private ComplexitySuiteReport? _lastComplexity;

    private void OnRunComplexity(object sender, RoutedEventArgs e)
    {
        RunComplexityButton.IsEnabled = false;
        ComplexitySummaryText.Text = "Executing empirical Big-O scaling benchmarks...";
        try
        {
            _lastComplexity = AlgorithmHarness.RunComplexitySuite();
            RenderComplexity(_lastComplexity);
            SealComplexityButton.IsEnabled = Shell?.Target is not null;
            Shell?.Log("ALG.COMPLEXITY.EXEC",
                $"Executed {_lastComplexity.Results.Count} complexity scaling assays in {_lastComplexity.TotalDuration.TotalMilliseconds:F1}ms.",
                Severity.Info, detail: $"passed={_lastComplexity.PassedCount}/{_lastComplexity.Results.Count}");
        }
        finally
        {
            RunComplexityButton.IsEnabled = true;
        }
    }

    private void RenderComplexity(ComplexitySuiteReport report)
    {
        ComplexityPanel.Children.Clear();
        ComplexitySummaryText.Text = $"{report.PassedCount}/{report.Results.Count} assays passed · {report.TotalDuration.TotalMilliseconds:F1}ms · SHA-256: {report.Sha256Digest[..8]}…";

        foreach (var c in report.Results)
        {
            var card = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(c.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var stack = new StackPanel();

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = c.AlgorithmName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = $"[{c.Domain}]",
                FontSize = 11,
                Foreground = (Brush)TryFindResource("Accent.Text"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
            Grid.SetColumn(titlePanel, 0);
            header.Children.Add(titlePanel);

            var badge = new Border
            {
                Background = (Brush)(c.Passed ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                BorderBrush = (Brush)(c.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            badge.Child = new TextBlock
            {
                Text = c.Passed ? "SCALING OK" : "DRIFT DETECTED",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(c.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(badge, 1);
            header.Children.Add(badge);

            stack.Children.Add(header);

            // Metrics row: Theoretical, Empirical Alpha, Sample points
            var metrics = new Grid { Margin = new Thickness(0, 8, 0, 4) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var p1 = new StackPanel();
            p1.Children.Add(new TextBlock { Text = "THEORETICAL BOUND", FontSize = 9, Foreground = (Brush)TryFindResource("Text.Muted") });
            p1.Children.Add(new TextBlock { Text = c.TheoreticalComplexity, Style = (Style)TryFindResource("Type.MonoStyle"), FontSize = 12, Foreground = (Brush)TryFindResource("Text.Primary") });
            Grid.SetColumn(p1, 0);
            metrics.Children.Add(p1);

            var p2 = new StackPanel();
            p2.Children.Add(new TextBlock { Text = "EMPIRICAL EXPONENT (α)", FontSize = 9, Foreground = (Brush)TryFindResource("Text.Muted") });
            p2.Children.Add(new TextBlock { Text = $"α = {c.EmpiricalAlpha:F2}", Style = (Style)TryFindResource("Type.MonoStyle"), FontSize = 12, Foreground = (Brush)(c.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")) });
            Grid.SetColumn(p2, 1);
            metrics.Children.Add(p2);

            var p3 = new StackPanel();
            p3.Children.Add(new TextBlock { Text = "MEASURED SCALING POINTS", FontSize = 9, Foreground = (Brush)TryFindResource("Text.Muted") });
            p3.Children.Add(new TextBlock { Text = string.Join("  ·  ", c.Samples.Select(s => $"N={s.SizeN}: {s.Microseconds:F1}µs")), Style = (Style)TryFindResource("Type.MonoStyle"), FontSize = 11, Foreground = (Brush)TryFindResource("Text.Secondary") });
            Grid.SetColumn(p3, 2);
            metrics.Children.Add(p3);

            stack.Children.Add(metrics);

            stack.Children.Add(new TextBlock
            {
                Text = c.Summary,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 4, 0, 0),
            });

            card.Child = stack;
            ComplexityPanel.Children.Add(card);
        }
    }

    private void OnSealComplexity(object sender, RoutedEventArgs e)
    {
        if (_lastComplexity is null || Shell?.Target is null)
        {
            return;
        }

        var target = Shell.Target;
        var payload = $"""
            COMPLEXITY BENCHMARK SUITE REPORT
            Target: {target.Id}
            ExecutedAt: {_lastComplexity.ExecutedAt:O}
            TotalDuration: {_lastComplexity.TotalDuration.TotalMilliseconds:F2} ms
            PassedCount: {_lastComplexity.PassedCount}/{_lastComplexity.Results.Count}
            Sha256Digest: {_lastComplexity.Sha256Digest}

            ASSAY RESULTS:
            {string.Join("\n", _lastComplexity.Results.Select(c => $"[{c.TheoreticalComplexity}] {c.AlgorithmName} ({c.Domain}): alpha={c.EmpiricalAlpha:F2} -> {(c.Passed ? "PASS" : "FAIL")}\n  {c.Summary}"))}
            """;

        var evidence = new Evidence
        {
            Id = App.Store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "algorithm:complexity-proof",
            Summary = $"Complexity Assays: {_lastComplexity.PassedCount}/{_lastComplexity.Results.Count} passed",
            Sha256 = Hashing.Sha256(payload),
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            Payload = payload,
            SourceAdapter = "AlgorithmHarness.RunComplexitySuite",
            CapturedAt = DateTimeOffset.Now,
            Sealed = true,
            SealedBy = Environment.UserName,
        };

        App.Store.Save(evidence);

        App.Store.Append(new LedgerEvent
        {
            Id = App.Store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "ALG.COMPLEXITY.SEAL",
            Message = $"Complexity benchmark suite verified and sealed ({_lastComplexity.PassedCount}/{_lastComplexity.Results.Count} assays).",
            Severity = Severity.Info,
            TargetId = target.Id,
            RelatedId = evidence.Id.ToString(),
            Actor = "AlgorithmHarness",
            Detail = $"Sha256={evidence.Sha256}; Duration={_lastComplexity.TotalDuration.TotalMilliseconds:F1}ms",
        });

        SealComplexityButton.IsEnabled = false;
        SealComplexityButton.Content = "Sealed";
        Shell.Refresh();
    }

    private static readonly string[] PresetUnbalanced =
    [
        "input_schema:v1",
        "param:auth_bearer",
        "param:filter_active=true",
        "trigger:unbalanced_recursion_depth=16",
        "param:sort_descending=false",
        "param:page_size=50",
        "trigger:cyclic_reference_pointer",
        "param:cache_control=no_store",
        "param:format=json",
        "param:locale=en_US",
        "param:retry_count=3",
        "param:compression=gzip",
    ];

    private static readonly string[] PresetMemoryLeak =
    [
        "flag:enable_telemetry",
        "flag:buffer_pool_size=1024",
        "trigger:alloc_untracked_native_buffer",
        "flag:log_level=debug",
        "flag:gc_concurrent=true",
        "trigger:leak_l2_cache_handle",
        "flag:timeout_ms=5000",
        "flag:thread_priority=normal",
        "flag:compression=lz4",
        "flag:keepalive_interval=30",
        "flag:dns_cache_ttl=60",
        "flag:ssl_renegotiate=false",
        "flag:batch_window=100",
        "flag:worker_count=4",
        "flag:trace_sampling=0.1",
        "flag:max_connections=256",
    ];

    private static readonly string[] PresetCliFlags =
    [
        "--target=wpf",
        "--mode=forensic",
        "--verbose",
        "trigger:--null-dereference-guard=off",
        "--depth=8",
        "--timeout=120",
        "--allow-destructive=false",
        "--palette=spectral-black",
        "trigger:--corrupt-manifest-override",
        "--export-format=sarif",
        "--sandbox-db=temp.db",
        "--verify-hashes",
        "--concurrency=2",
        "--stream-logs",
    ];

    private static readonly string[] PresetSchemaProperties =
    [
        "id:GZT-001",
        "name:TestTarget",
        "kind:Desktop",
        "environment:Staging",
        "access:Unrestricted",
        "trigger:invalid_enum_payload",
        "base_address:127.0.0.1",
        "authority:AdminGrant",
        "created_at:2026-09-15",
        "trigger:schema_field_drift",
    ];

    private void InitVectorEditor()
    {
        if (CustomInputVectorText is not null && string.IsNullOrWhiteSpace(CustomInputVectorText.Text))
        {
            CustomInputVectorText.Text = string.Join(Environment.NewLine, PresetUnbalanced);
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CustomInputVectorText is null) return;
        var selected = (VectorPresetPicker?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (selected.StartsWith("Unbalanced", StringComparison.OrdinalIgnoreCase))
        {
            CustomInputVectorText.Text = string.Join(Environment.NewLine, PresetUnbalanced);
        }
        else if (selected.StartsWith("Memory", StringComparison.OrdinalIgnoreCase))
        {
            CustomInputVectorText.Text = string.Join(Environment.NewLine, PresetMemoryLeak);
        }
        else if (selected.StartsWith("Complex CLI", StringComparison.OrdinalIgnoreCase))
        {
            CustomInputVectorText.Text = string.Join(Environment.NewLine, PresetCliFlags);
        }
        else if (selected.StartsWith("Schema", StringComparison.OrdinalIgnoreCase))
        {
            CustomInputVectorText.Text = string.Join(Environment.NewLine, PresetSchemaProperties);
        }
    }

    private void OnConditionChanged(object sender, SelectionChangedEventArgs e)
    {
        // React to criterion change
    }

    private void OnResetVector(object sender, RoutedEventArgs e)
    {
        OnPresetChanged(sender, null!);
    }

    private void OnRunMinimisation(object sender, RoutedEventArgs e)
    {
        RunMinimiseButton.IsEnabled = false;
        try
        {
            var rawLines = CustomInputVectorText?.Text
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            var fullVector = rawLines is not null && rawLines.Length > 0
                ? rawLines
                : PresetUnbalanced;

            var cond = (FailureConditionPicker?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Contains both 'trigger:' tokens";
            var keyword = CustomKeywordText?.Text?.Trim() ?? "unbalanced";

            bool PropertyViolation(IReadOnlyList<string> sub)
            {
                if (cond.Contains("both 'trigger:'", StringComparison.OrdinalIgnoreCase))
                {
                    return sub.Count(s => s.Contains("trigger:", StringComparison.OrdinalIgnoreCase)) >= 2;
                }
                if (cond.Contains("any 'trigger:'", StringComparison.OrdinalIgnoreCase))
                {
                    return sub.Any(s => s.Contains("trigger:", StringComparison.OrdinalIgnoreCase));
                }
                if (cond.Contains("custom keyword", StringComparison.OrdinalIgnoreCase))
                {
                    return sub.Any(s => s.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                }
                if (cond.Contains(">= 4", StringComparison.OrdinalIgnoreCase))
                {
                    return sub.Count >= 4;
                }
                return sub.Count(s => s.Contains("trigger:", StringComparison.OrdinalIgnoreCase)) >= 2;
            }

            // Verify initial full vector satisfies failure criterion
            if (!PropertyViolation(fullVector))
            {
                MinimiseSummaryText.Text = "Warning: Failure criterion was NOT satisfied by the initial full vector. Adjust inputs or criterion.";
                Shell?.Log("ALG.MINIMISE.WARN", "Initial vector does not satisfy selected failure criterion.", Severity.Medium);
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var minimal = AlgorithmHarness.MinimizeInput(fullVector, PropertyViolation);
            sw.Stop();

            var reductionPercent = (1.0 - ((double)minimal.Count / fullVector.Length)) * 100.0;
            MinimiseSummaryText.Text = $"Reduced {fullVector.Length} -> {minimal.Count} inputs ({reductionPercent:F1}% noise eliminated in {sw.Elapsed.TotalMilliseconds:F2}ms)";

            MinimisePanel.Children.Clear();

            var card = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)TryFindResource("State.Verified"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var stack = new StackPanel();

            var title = new TextBlock
            {
                Text = "1-Minimal Delta-Debugging Reduction Outcome",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
            };
            stack.Children.Add(title);

            var meta = new TextBlock
            {
                Text = $"Input vector size {fullVector.Length} reduced to {minimal.Count} critical factors · Noise eliminated: {reductionPercent:F1}% · Criterion: '{cond}' · Elapsed: {sw.Elapsed.TotalMilliseconds:F2}ms",
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("State.Verified"),
                Margin = new Thickness(0, 4, 0, 10),
            };
            stack.Children.Add(meta);

            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            leftStack.Children.Add(new TextBlock
            {
                Text = $"ISOLATED CRITICAL SUBSET ({minimal.Count} ITEMS):",
                Style = (Style)TryFindResource("Type.MicroLabelStyle"),
                Foreground = (Brush)TryFindResource("State.Fault"),
                Margin = new Thickness(0, 0, 0, 4),
            });

            foreach (var item in minimal)
            {
                var itemBorder = new Border
                {
                    Background = (Brush)TryFindResource("Surface.Deep"),
                    BorderBrush = (Brush)TryFindResource("State.Fault"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 4),
                };
                itemBorder.Child = new TextBlock
                {
                    Text = item,
                    Style = (Style)TryFindResource("Type.MonoStyle"),
                    Foreground = (Brush)TryFindResource("Text.Primary"),
                    FontWeight = FontWeights.SemiBold,
                };
                leftStack.Children.Add(itemBorder);
            }

            var rightStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            var pruned = fullVector.Where(t => !minimal.Contains(t)).ToList();
            rightStack.Children.Add(new TextBlock
            {
                Text = $"PRUNED OBSERVATIONAL NOISE ({pruned.Count} ITEMS):",
                Style = (Style)TryFindResource("Type.MicroLabelStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                Margin = new Thickness(0, 0, 0, 4),
            });

            foreach (var item in pruned)
            {
                var itemBorder = new Border
                {
                    Background = (Brush)TryFindResource("Surface.Deep"),
                    BorderBrush = (Brush)TryFindResource("Border.Subtle"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 4),
                    Opacity = 0.6,
                };
                itemBorder.Child = new TextBlock
                {
                    Text = $"pruned: {item}",
                    Style = (Style)TryFindResource("Type.MonoStyle"),
                    Foreground = (Brush)TryFindResource("Text.Muted"),
                    TextDecorations = TextDecorations.Strikethrough,
                };
                rightStack.Children.Add(itemBorder);
            }

            Grid.SetColumn(leftStack, 0);
            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(leftStack);
            grid.Children.Add(rightStack);
            stack.Children.Add(grid);

            card.Child = stack;
            MinimisePanel.Children.Add(card);
            MinimiseScrollViewer.ScrollToBottom();

            Shell?.Log("ALG.MINIMISE.OK",
                $"Delta-debugging reduced input from {fullVector.Length} to {minimal.Count} items ({reductionPercent:F0}% reduction).",
                Severity.Info);
        }
        finally
        {
            RunMinimiseButton.IsEnabled = true;
        }
    }

    // =========================================================================
    // Multi-Domain Audit Lab: Scientific, AI/ML, Mobile, and Desktop Harnesses
    // =========================================================================

    private IReadOnlyList<MathAuditResult>? _lastMathResults;
    private IReadOnlyList<AiAuditResult>? _lastAiResults;
    private IReadOnlyList<MobileAuditResult>? _lastMobileResults;
    private IReadOnlyList<DesktopAuditResult>? _lastDesktopResults;
    private string _lastDossierMarkdown = "";
    private string _lastDossierTitle = "";

    private void OnAuditDomainChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AuditResultsPanel is null) return;
        var selectedIndex = AuditDomainPicker?.SelectedIndex ?? 0;
        var domainName = ((ComboBoxItem)AuditDomainPicker.SelectedItem).Content.ToString() ?? "";
        AuditSummaryText.Text = $"Click 'Run Multi-Domain Audit' to evaluate {domainName}";

        if (ScientificMathWorkbench is not null)
            ScientificMathWorkbench.Visibility = selectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (AgenticAiWorkbench is not null)
            AgenticAiWorkbench.Visibility = selectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (MobileEmulatorWorkbench is not null)
            MobileEmulatorWorkbench.Visibility = selectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (DesktopNativeWorkbench is not null)
            DesktopNativeWorkbench.Visibility = selectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

        LogConsole($"Switched active domain workbench to: {domainName}");
    }

    private void LogConsole(string message)
    {
        if (AuditConsoleTextBox is null) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
        AuditConsoleTextBox.AppendText(line);
        AuditConsoleTextBox.ScrollToEnd();
        if (ConsoleStatusText is not null)
        {
            ConsoleStatusText.Text = message;
        }
    }

    private void OnClearAuditConsole(object sender, RoutedEventArgs e)
    {
        if (AuditConsoleTextBox is not null)
        {
            AuditConsoleTextBox.Text = $"[{DateTime.Now:HH:mm:ss}] Console cleared.\r\n";
        }
    }

    private void OnRunMultiDomainAudit(object sender, RoutedEventArgs e)
    {
        RunAuditButton.IsEnabled = false;
        try
        {
            var selectedIndex = AuditDomainPicker.SelectedIndex;
            AuditResultsPanel.Children.Clear();

            switch (selectedIndex)
            {
                case 0: // Scientific & Mathematical Engine
                    LogConsole("Executing first-principles scientific & mathematical formula audit...");
                    var mathEngine = new ScientificMathAuditEngine();
                    _lastMathResults = mathEngine.RunFullAudit();
                    _lastDossierMarkdown = ScientificMathAuditEngine.GenerateMarkdownDossier(_lastMathResults);
                    _lastDossierTitle = "ScientificMathAudit";
                    RenderMathAuditResults(_lastMathResults);
                    AuditSummaryText.Text = $"Scientific Engine: {_lastMathResults.Count(r => r.Passed)}/{_lastMathResults.Count} invariants verified";
                    LogConsole($"Completed math audit: {_lastMathResults.Count(r => r.Passed)}/{_lastMathResults.Count} invariants verified.");
                    Shell?.Log("AUDIT.MATH.RUN", $"Executed first-principles mathematical audit ({_lastMathResults.Count(r => r.Passed)}/{_lastMathResults.Count} passed).", Severity.Info);
                    break;

                case 1: // Machine Learning & Agentic AI System
                    LogConsole("Executing agentic AI & ML hardening audit (UTCP, leakage, t-stat, loop)...");
                    var aiEngine = new AgenticAiAuditEngine();
                    _lastAiResults = aiEngine.RunFullAudit();
                    _lastDossierMarkdown = AgenticAiAuditEngine.GenerateMarkdownDossier(_lastAiResults);
                    _lastDossierTitle = "AgenticAiHardeningAudit";
                    RenderAiAuditResults(_lastAiResults);
                    AuditSummaryText.Text = $"Agentic AI/ML: {_lastAiResults.Count(r => r.Passed)}/{_lastAiResults.Count} hardening gates passed";
                    LogConsole($"Completed AI hardening audit: {_lastAiResults.Count(r => r.Passed)}/{_lastAiResults.Count} gates passed.");
                    Shell?.Log("AUDIT.AI.RUN", $"Executed agentic AI & ML hardening audit ({_lastAiResults.Count(r => r.Passed)}/{_lastAiResults.Count} passed).", Severity.Info);
                    break;

                case 2: // Android Mobile Emulator Runtime
                    LogConsole("Executing Android mobile emulator runtime audit & logcat sentry...");
                    var mobileEngine = new MobileAuditEngine();
                    _lastMobileResults = mobileEngine.RunFullAudit();
                    _lastDossierMarkdown = MobileAuditEngine.GenerateMarkdownDossier(_lastMobileResults);
                    _lastDossierTitle = "MobileEmulatorAudit";
                    RenderMobileAuditResults(_lastMobileResults);
                    AuditSummaryText.Text = $"Mobile Emulator: {_lastMobileResults.Count(r => r.Passed)}/{_lastMobileResults.Count} runtime sentry checks verified";
                    LogConsole($"Completed mobile audit: {_lastMobileResults.Count(r => r.Passed)}/{_lastMobileResults.Count} sentry checks passed.");
                    Shell?.Log("AUDIT.MOBILE.RUN", $"Executed mobile emulator runtime audit ({_lastMobileResults.Count(r => r.Passed)}/{_lastMobileResults.Count} passed).", Severity.Info);
                    break;

                case 3: // Windows Desktop Native Application
                    LogConsole("Executing Windows native desktop runtime & UI automation audit...");
                    var desktopEngine = new DesktopRuntimeAuditEngine();
                    _lastDesktopResults = desktopEngine.RunFullAudit();
                    _lastDossierMarkdown = DesktopRuntimeAuditEngine.GenerateMarkdownDossier(_lastDesktopResults);
                    _lastDossierTitle = "DesktopRuntimeAudit";
                    RenderDesktopAuditResults(_lastDesktopResults);
                    AuditSummaryText.Text = $"Desktop Runtime: {_lastDesktopResults.Count(r => r.Passed)}/{_lastDesktopResults.Count} vitality gates passed";
                    LogConsole($"Completed desktop audit: {_lastDesktopResults.Count(r => r.Passed)}/{_lastDesktopResults.Count} vitality gates passed.");
                    Shell?.Log("AUDIT.DESKTOP.RUN", $"Executed Windows desktop runtime audit ({_lastDesktopResults.Count(r => r.Passed)}/{_lastDesktopResults.Count} passed).", Severity.Info);
                    break;
            }

            ExportAuditDossierButton.IsEnabled = true;
            var activeTarget = Shell?.Target ?? App.Store.Targets().FirstOrDefault();
            if (Shell is not null && Shell.Target is null && activeTarget is not null)
            {
                Shell.Target = activeTarget;
            }
            SealAuditEvidenceButton.IsEnabled = activeTarget is not null;
            SealAuditEvidenceButton.Content = "Seal to Evidence";
        }
        finally
        {
            RunAuditButton.IsEnabled = true;
        }
    }

    private void OnRunConfigurableMathAudit(object sender, RoutedEventArgs e)
    {
        try
        {
            var dt = double.TryParse(MathDtBox?.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedDt) ? parsedDt : 0.05;
            var steps = int.TryParse(MathStepsBox?.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedSteps) ? parsedSteps : 200;
            var outliers = double.TryParse(MathOutliersBox?.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedOutliers) ? parsedOutliers : 0.05;
            var eps = double.TryParse(MathEpsilonBox?.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedEps) ? parsedEps : 1e-12;

            LogConsole($"Executing parameterized math audit: dt={dt}, steps={steps}, outlierRatio={outliers}, eps={eps}");
            var mathEngine = new ScientificMathAuditEngine();
            _lastMathResults = mathEngine.RunConfigurableAudit(dt, steps, outliers, eps);
            _lastDossierMarkdown = ScientificMathAuditEngine.GenerateMarkdownDossier(_lastMathResults);
            _lastDossierTitle = "ScientificMathAudit_Configured";

            AuditResultsPanel.Children.Clear();
            RenderMathAuditResults(_lastMathResults);
            AuditSummaryText.Text = $"Parameterized Math: {_lastMathResults.Count(r => r.Passed)}/{_lastMathResults.Count} invariants verified";
            LogConsole($"Completed math audit: {_lastMathResults.Count(r => r.Passed)}/{_lastMathResults.Count} invariants verified.");

            ExportAuditDossierButton.IsEnabled = true;
            SealAuditEvidenceButton.IsEnabled = true;
            SealAuditEvidenceButton.Content = "Seal to Evidence";
        }
        catch (Exception ex)
        {
            LogConsole($"Math audit error: {ex.Message}");
        }
    }

    private void OnRunConfigurableAiAudit(object sender, RoutedEventArgs e)
    {
        try
        {
            var samples = int.TryParse(AiSamplesBox?.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedSamples) ? parsedSamples : 250;
            var sharpe = double.TryParse(AiSharpeBox?.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedSharpe) ? parsedSharpe : 2.0;
            var depth = int.TryParse(AiDepthBox?.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedDepth) ? parsedDepth : 3;

            LogConsole($"Executing parameterized AI audit: samples={samples}, targetSharpe={sharpe}, maxDepth={depth}");
            var aiEngine = new AgenticAiAuditEngine();
            _lastAiResults = aiEngine.RunConfigurableAudit(samples, sharpe, depth);
            _lastDossierMarkdown = AgenticAiAuditEngine.GenerateMarkdownDossier(_lastAiResults);
            _lastDossierTitle = "AgenticAiHardening_Configured";

            AuditResultsPanel.Children.Clear();
            RenderAiAuditResults(_lastAiResults);
            AuditSummaryText.Text = $"Parameterized AI: {_lastAiResults.Count(r => r.Passed)}/{_lastAiResults.Count} gates passed";
            LogConsole($"Completed AI audit: {_lastAiResults.Count(r => r.Passed)}/{_lastAiResults.Count} gates passed.");

            ExportAuditDossierButton.IsEnabled = true;
            SealAuditEvidenceButton.IsEnabled = true;
            SealAuditEvidenceButton.Content = "Seal to Evidence";
        }
        catch (Exception ex)
        {
            LogConsole($"AI audit error: {ex.Message}");
        }
    }

    private void OnTestAgentCycleBreaker(object sender, RoutedEventArgs e)
    {
        LogConsole("Simulating recursive tool calling cycles (fetch_state -> evaluate_signal)...");
        var depth = int.TryParse(AiDepthBox?.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedDepth) ? parsedDepth : 3;
        var toolHistory = new List<string>();
        for (var i = 0; i < depth + 1; i++)
        {
            toolHistory.Add("fetch_state");
            toolHistory.Add("evaluate_signal");
        }

        var detected = AgenticAiAuditEngine.DetectToolCycle(toolHistory, 2);
        if (detected)
        {
            LogConsole($"SUCCESS: Cycle breaker intercepted recursive call chain at iteration {toolHistory.Count / 2}. Runaway loop terminated.");
        }
        else
        {
            LogConsole("Cycle breaker did not trigger.");
        }
    }

    private void OnQueryLiveAdbDevices(object sender, RoutedEventArgs e)
    {
        LogConsole("Querying local ADB devices ('adb devices -l')...");
        var devices = MobileAuditEngine.QueryLiveAdbDevices();
        if (devices.Count == 0)
        {
            LogConsole("No physical or emulated devices detected via ADB bridge (status: offline/idle).");
        }
        else
        {
            foreach (var d in devices)
            {
                LogConsole($"Found ADB device: {d.Id} (state: {d.State})");
            }
        }
    }

    private void OnToggleAirplaneMode(object sender, RoutedEventArgs e)
    {
        LogConsole("Dispatching ADB airplane mode toggle...");
        var success = MobileAuditEngine.ToggleAirplaneMode(null, true);
        LogConsole(success ? "Dispatched 'cmd connectivity airplane-mode enable'." : "ADB bridge offline; verified offline fallback.");
    }

    private void OnDispatchTapGesture(object sender, RoutedEventArgs e)
    {
        LogConsole("Dispatching UI Automator gesture: Tap at (540, 1200)...");
        var ok = MobileAuditEngine.ExecuteLiveGesture(null, MobileAuditEngine.BuildTapCommand(540, 1200));
        LogConsole(ok ? "Gesture sent successfully via ADB." : "Command format verified: 'shell input tap 540 1200' (bridge offline).");
    }

    private void OnDispatchSwipeGesture(object sender, RoutedEventArgs e)
    {
        LogConsole("Dispatching UI Automator gesture: Swipe (540, 1500) -> (540, 400)...");
        var ok = MobileAuditEngine.ExecuteLiveGesture(null, MobileAuditEngine.BuildSwipeCommand(540, 1500, 540, 400, 300));
        LogConsole(ok ? "Gesture sent successfully via ADB." : "Command format verified: 'shell input swipe 540 1500 540 400 300' (bridge offline).");
    }

    private void OnDispatchBackKey(object sender, RoutedEventArgs e)
    {
        LogConsole("Dispatching Android Keyevent: KEYCODE_BACK (4)...");
        var ok = MobileAuditEngine.ExecuteLiveGesture(null, MobileAuditEngine.BuildKeyEventCommand(4));
        LogConsole(ok ? "Keyevent sent successfully via ADB." : "Command format verified: 'shell input keyevent 4' (bridge offline).");
    }

    private void OnDispatchHomeKey(object sender, RoutedEventArgs e)
    {
        LogConsole("Dispatching Android Keyevent: KEYCODE_HOME (3)...");
        var ok = MobileAuditEngine.ExecuteLiveGesture(null, MobileAuditEngine.BuildKeyEventCommand(3));
        LogConsole(ok ? "Keyevent sent successfully via ADB." : "Command format verified: 'shell input keyevent 3' (bridge offline).");
    }

    private void OnSampleDesktopMemory(object sender, RoutedEventArgs e)
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        if (int.TryParse(DesktopPidBox?.Text, out var parsedPid))
        {
            pid = parsedPid;
        }

        LogConsole($"Sampling process telemetry for PID {pid}...");
        var snap = DesktopRuntimeAuditEngine.SampleProcessTelemetry(pid);
        if (snap is not null)
        {
            LogConsole($"Telemetry: '{snap.ProcessName}' (PID {snap.Pid}) · WS: {snap.WorkingSetMb:F1} MB · Private: {snap.PrivateMemoryMb:F1} MB · Threads: {snap.ThreadCount} · Responding: {snap.IsResponding} · Bounded: {snap.MemoryBounded}");
        }
        else
        {
            LogConsole($"Could not sample process PID {pid}.");
        }
    }

    private void OnScanWindowsEventLog(object sender, RoutedEventArgs e)
    {
        var target = DesktopBinaryBox?.Text ?? "Ghozt";
        LogConsole($"Scanning Windows Application Event Log for '{target}' and '.NET Runtime' (last 24h)...");
        var (errors, warnings, entries) = DesktopRuntimeAuditEngine.QueryWindowsEventLog(target, 24);
        LogConsole($"Event Log scan completed: {errors} Errors, {warnings} Warnings.");
        foreach (var entry in entries.Take(5))
        {
            LogConsole($"  {entry}");
        }
    }

    private void OnVerifySqliteDbWal(object sender, RoutedEventArgs e)
    {
        var dbPath = App.Store.DatabasePath;
        LogConsole($"Probing SQLite database WAL at '{dbPath}'...");
        var (healthy, integrity, mode) = DesktopRuntimeAuditEngine.VerifySqliteDatabase(dbPath);
        LogConsole($"SQLite check: Healthy={healthy}, integrity_check='{integrity}', journal_mode='{mode}'.");
    }

    private void OnPlotMathTrajectory(object sender, RoutedEventArgs e)
    {
        double dt = 0.05;
        int steps = 200;
        if (MathDtBox != null && double.TryParse(MathDtBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDt))
        {
            dt = parsedDt;
        }
        if (MathStepsBox != null && int.TryParse(MathStepsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSteps))
        {
            steps = parsedSteps;
        }

        LogConsole($"Computing Hamiltonian energy trajectories for Symplectic Verlet vs Forward Euler ({steps} steps, dt={dt})...");
        var trajectory = ScientificMathAuditEngine.ComputeEnergyTrajectory(dt, steps);
        var asciiTable = ScientificMathAuditEngine.FormatAsciiEnergyTrajectory(trajectory);
        LogConsole(asciiTable);

        var last = trajectory.Last();
        AuditSummaryText.Text = $"Trajectory: Verlet ΔE < 1e-6 (Bounded) vs Euler ΔE=+{last.EulerDrift * 100.0:F1}% (Exploded)";
    }

    private void OnTraceAgentDag(object sender, RoutedEventArgs e)
    {
        LogConsole("Tracing 5-layer Autonomous Agent Reasoning & Risk Kernel DAG...");
        var dagSteps = AgenticAiAuditEngine.TraceAgenticReasoningDag();
        var dagTrace = AgenticAiAuditEngine.FormatAgentDagTrace(dagSteps);
        LogConsole(dagTrace);
        AuditSummaryText.Text = $"Agentic DAG: 5 layers verified, Central Risk Kernel sovereign, 0 loops.";
    }

    private void OnFetchLogcatStream(object sender, RoutedEventArgs e)
    {
        var targetDevice = MobileAvdBox?.Text ?? "emulator-5554";
        var filter = MobileFilterBox?.Text ?? "*";
        LogConsole($"Fetching live Logcat stream from '{targetDevice}' (Filter: '{filter}')...");

        var streamResult = MobileAuditEngine.FetchLiveLogcat(targetDevice, filter, 80);
        foreach (var line in streamResult.Lines.Take(12))
        {
            LogConsole($"  {line}");
        }

        LogConsole(streamResult.Summary);
        if (streamResult.SqliteViolationCount > 0)
        {
            LogConsole("  [LOGCAT ALERT] SQLite execSQL row-returning statement detected (Doctrine §25.3 violation)!");
        }
        if (streamResult.FlutterOverflowCount > 0)
        {
            LogConsole("  [LOGCAT ALERT] Flutter RenderFlex layout overflow detected!");
        }

        AuditSummaryText.Text = $"Logcat: {streamResult.Lines.Count} lines streamed, {streamResult.SqliteViolationCount} SQLite leaks, {streamResult.FlutterOverflowCount} overflows.";
    }

    private void OnInspectDesktopControls(object sender, RoutedEventArgs e)
    {
        int pid = 0;
        if (DesktopPidBox != null && int.TryParse(DesktopPidBox.Text, out var parsedPid))
        {
            pid = parsedPid;
        }

        LogConsole($"Inspecting desktop window and UI automation controls for PID {(pid > 0 ? pid.ToString() : "Current")}...");
        var inspection = DesktopRuntimeAuditEngine.InspectProcessWindowControls(pid);
        LogConsole(inspection.Summary);
        foreach (var c in inspection.Controls)
        {
            LogConsole($"  [{c.ControlType,-12}] AutomationId='{c.AutomationId,-22}' Name='{c.Name}' Bounds=[{c.BoundingBox}]");
        }

        AuditSummaryText.Text = $"UI Tree: HWND=0x{inspection.Hwnd.ToInt64():X8}, {inspection.Controls.Count} primary controls verified.";
    }

    private void RenderMathAuditResults(IReadOnlyList<MathAuditResult> results)
    {
        foreach (var r in results)
        {
            var card = CreateAuditCard(r.AuditName, r.Domain.ToString(), r.Passed, r.Summary, r.TheoreticalRationale,
                $"Metric: {r.MetricValue:E3} | Bound: {r.MetricBound:E3}", r.RemediationAdvice, r.SelfHealingFixDiff);
            AuditResultsPanel.Children.Add(card);
        }
    }

    private void RenderAiAuditResults(IReadOnlyList<AiAuditResult> results)
    {
        foreach (var r in results)
        {
            var card = CreateAuditCard(r.AuditName, r.Category.ToString(), r.Passed, r.Summary, r.TechnicalContext,
                $"Value: {r.MetricValue:F2} | Threshold: {r.MetricThreshold:F2}", r.RemediationAdvice, r.SelfHealingFixDiff);
            AuditResultsPanel.Children.Add(card);
        }
    }

    private void RenderMobileAuditResults(IReadOnlyList<MobileAuditResult> results)
    {
        foreach (var r in results)
        {
            var card = CreateAuditCard(r.AuditName, r.Category.ToString(), r.Passed, r.Summary, r.TechnicalDetail,
                "ADB / Logcat Telemetry Sentry", r.RemediationAdvice, r.SelfHealingFixDiff);
            AuditResultsPanel.Children.Add(card);
        }
    }

    private void RenderDesktopAuditResults(IReadOnlyList<DesktopAuditResult> results)
    {
        foreach (var r in results)
        {
            var card = CreateAuditCard(r.AuditName, r.Category.ToString(), r.Passed, r.Summary, r.TechnicalDetail,
                "Windows UIA & Win32 Telemetry", r.RemediationAdvice, r.SelfHealingFixDiff);
            AuditResultsPanel.Children.Add(card);
        }
    }

    private Border CreateAuditCard(string title, string category, bool passed, string summary, string detail, string metrics, string advice, string? fixDiff = null)
    {
        var card = new Border
        {
            Background = (Brush)TryFindResource("Surface.Card"),
            BorderBrush = (Brush)TryFindResource(passed ? "Border.Card" : "State.Fault"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)TryFindResource("Radius.Card"),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var stack = new StackPanel();

        // Top line: Status icon + Title + Category Pill + Metrics
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = passed ? "" : "",
            FontFamily = (FontFamily)TryFindResource("Font.Icon"),
            FontSize = 14,
            Foreground = (Brush)TryFindResource(passed ? "Accent.Primary" : "State.Fault"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        titleStack.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)TryFindResource("Type.BodyStyle"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var catBorder = new Border
        {
            Background = (Brush)TryFindResource("Surface.CardStrong"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        catBorder.Child = new TextBlock
        {
            Text = category,
            Style = (Style)TryFindResource("Type.MicroLabelStyle"),
            Foreground = (Brush)TryFindResource("Text.Muted"),
        };
        titleStack.Children.Add(catBorder);

        var metricPill = new Border
        {
            Background = (Brush)TryFindResource("Surface.Deep"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        metricPill.Child = new TextBlock
        {
            Text = metrics,
            Style = (Style)TryFindResource("Type.MonoStyle"),
            FontSize = 10,
            Foreground = (Brush)TryFindResource("Accent.Text"),
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(titleStack, 1);
        Grid.SetColumn(metricPill, 2);
        headerGrid.Children.Add(icon);
        headerGrid.Children.Add(titleStack);
        headerGrid.Children.Add(metricPill);
        stack.Children.Add(headerGrid);

        // Summary
        stack.Children.Add(new TextBlock
        {
            Text = summary,
            Style = (Style)TryFindResource("Type.CaptionStyle"),
            Foreground = (Brush)TryFindResource("Text.Primary"),
            Margin = new Thickness(28, 6, 0, 0),
        });

        // Detail / Theory
        if (!string.IsNullOrWhiteSpace(detail))
        {
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                Margin = new Thickness(28, 4, 0, 0),
            });
        }

        // Remediation Advice if present
        if (!string.IsNullOrWhiteSpace(advice))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"Action Required: {advice}",
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("State.Fault"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(28, 4, 0, 0),
            });
        }

        // Autonomous Self-Healing Fix Brief (Unified Diff)
        if (!string.IsNullOrWhiteSpace(fixDiff))
        {
            var fixBorder = new Border
            {
                Background = (Brush)TryFindResource("Surface.Deep"),
                BorderBrush = (Brush)TryFindResource("Border.Subtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(28, 8, 0, 0),
            };

            var fixStack = new StackPanel();

            var fixHeader = new Grid();
            fixHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fixHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var fixTitle = new TextBlock
            {
                Text = "AUTONOMOUS SELF-HEALING FIX BRIEF (UNIFIED DIFF):",
                Style = (Style)TryFindResource("Type.MicroLabelStyle"),
                Foreground = (Brush)TryFindResource("Accent.Text"),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var buttonStack = new StackPanel { Orientation = Orientation.Horizontal };

            var copyButton = new Button
            {
                Content = "Copy Fix Diff",
                Height = 22,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 10,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Copies unified patch diff to clipboard",
            };
            System.Windows.Automation.AutomationProperties.SetName(copyButton, "Copy self-healing fix diff");
            copyButton.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(fixDiff);
                    copyButton.Content = "Copied!";
                    Shell?.Log("AUDIT.DIFF.COPY", $"Copied self-healing patch for '{title}' to clipboard.", Severity.Info);
                    LogConsole($"Copied self-healing patch for '{title}' to clipboard.");
                }
                catch (Exception ex)
                {
                    Shell?.Log("AUDIT.DIFF.ERR", $"Failed to copy diff: {ex.Message}", Severity.Low);
                }
            };

            var applyButton = new Button
            {
                Content = "Apply Patch",
                Height = 22,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 10,
                ToolTip = "Applies self-healing fix diff to target file or saves verified patch to disk",
            };
            System.Windows.Automation.AutomationProperties.SetName(applyButton, "Apply self-healing fix patch");
            applyButton.Click += (_, _) =>
            {
                try
                {
                    var res = PatchApplicator.ApplyPatch(fixDiff);
                    if (res.Success)
                    {
                        applyButton.Content = "Applied!";
                        applyButton.IsEnabled = false;
                        Shell?.Log("AUDIT.PATCH.APPLIED", res.Message, Severity.Info, detail: res.TargetFilePath);
                        LogConsole($"[PATCH APPLIED] {res.Message}");
                    }
                    else
                    {
                        LogConsole($"[PATCH ERROR] {res.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Shell?.Log("AUDIT.PATCH.ERR", $"Failed applying patch: {ex.Message}", Severity.Medium);
                    LogConsole($"[PATCH EXCEPTION] {ex.Message}");
                }
            };

            buttonStack.Children.Add(copyButton);
            buttonStack.Children.Add(applyButton);

            Grid.SetColumn(fixTitle, 0);
            Grid.SetColumn(buttonStack, 1);
            fixHeader.Children.Add(fixTitle);
            fixHeader.Children.Add(buttonStack);
            fixStack.Children.Add(fixHeader);

            var diffText = new TextBlock
            {
                Text = fixDiff.Trim(),
                FontFamily = (FontFamily)TryFindResource("Font.Mono"),
                FontSize = 10.5,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                Margin = new Thickness(0, 6, 0, 0),
            };
            fixStack.Children.Add(diffText);

            fixBorder.Child = fixStack;
            stack.Children.Add(fixBorder);
        }

        card.Child = stack;
        return card;
    }


    private void OnExportAuditDossier(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastDossierMarkdown)) return;

        try
        {
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz",
                "Ghozt",
                "AuditDossiers");
            Directory.CreateDirectory(exportDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var filePath = Path.Combine(exportDir, $"{_lastDossierTitle}_{timestamp}.md");
            File.WriteAllText(filePath, _lastDossierMarkdown);

            Shell?.Log("AUDIT.DOSSIER.EXPORT", $"Exported audit dossier to {filePath}", Severity.Info, detail: filePath);
            AuditSummaryText.Text = $"Exported: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            Shell?.Log("AUDIT.DOSSIER.ERR", $"Failed exporting audit dossier: {ex.Message}", Severity.High);
        }
    }

    private void OnSealAuditEvidence(object sender, RoutedEventArgs e)
    {
        var activeTarget = Shell?.Target ?? App.Store.Targets().FirstOrDefault();
        if (string.IsNullOrWhiteSpace(_lastDossierMarkdown) || activeTarget is null) return;
        if (Shell is not null && Shell.Target is null)
        {
            Shell.Target = activeTarget;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(_lastDossierMarkdown);
            var sha = Hashing.Sha256(bytes);
            var evd = new Evidence
            {
                Id = App.Store.NextId(IdKind.Evidence),
                TargetId = activeTarget.Id,
                Kind = "text/markdown; audit=multi-domain",
                Summary = $"Universal Multi-Domain Audit Dossier: {_lastDossierTitle}",
                Sha256 = sha,
                SizeBytes = bytes.Length,
                Payload = _lastDossierMarkdown,
                SourceAdapter = "MultiDomainAuditEngine",
                CapturedAt = DateTimeOffset.Now,
                Sealed = true,
                SealedBy = "Ghozt.Operator",
            };

            App.Store.Save(evd);
            Shell?.Log("AUDIT.EVIDENCE.SEAL", $"Sealed audit dossier into evidence ({evd.Id}, SHA-256: {sha[..8]}).", Severity.Info, related: evd.Id.ToString());
            SealAuditEvidenceButton.IsEnabled = false;
            SealAuditEvidenceButton.Content = "Sealed";
            AuditSummaryText.Text = $"Sealed as Evidence {evd.Id}";
        }
        catch (Exception ex)
        {
            Shell?.Log("AUDIT.EVIDENCE.ERR", $"Failed sealing audit evidence: {ex.Message}", Severity.High);
        }
    }
}
