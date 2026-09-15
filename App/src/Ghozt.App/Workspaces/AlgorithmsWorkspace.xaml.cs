using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class AlgorithmsWorkspace : UserControl
{
    private AlgorithmSuiteReport? _lastReport;

    public AlgorithmsWorkspace()
    {
        InitializeComponent();
        Loaded += (_, _) => InitVectorEditor();
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
                MinimiseSummaryText.Text = "Failure criterion was NOT satisfied by the initial full vector. Adjust inputs or criterion.";
                MessageBox.Show("The initial input vector does not satisfy the selected failure criterion. At least one failing subset must exist for delta debugging.", "Delta Debugging Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            var minTitle = new TextBlock
            {
                Text = "ISOLATED CRITICAL SUBSET (1-MINIMAL CAUSE):",
                Style = (Style)TryFindResource("Type.MicroLabelStyle"),
                Margin = new Thickness(0, 6, 0, 4),
            };
            stack.Children.Add(minTitle);

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
                };
                stack.Children.Add(itemBorder);
            }

            card.Child = stack;
            MinimisePanel.Children.Add(card);

            Shell?.Log("ALG.MINIMISE.OK",
                $"Delta-debugging reduced input from {fullVector.Length} to {minimal.Count} items ({reductionPercent:F0}% reduction).",
                Severity.Info);
        }
        finally
        {
            RunMinimiseButton.IsEnabled = true;
        }
    }
}
