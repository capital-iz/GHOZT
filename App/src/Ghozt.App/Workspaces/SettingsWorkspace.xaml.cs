using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.App.Theming;
using Ghozt.Theming;

namespace Ghozt.App.Workspaces;

public partial class SettingsWorkspace : UserControl, IRefreshableWorkspace
{
    private bool _loading = true;

    public void Refresh()
    {
        LoadFromTheme();
        UpdateDigest();
    }

    public SettingsWorkspace()
    {
        InitializeComponent();
        BuildPaletteCards();
        Loaded += (_, _) => LoadFromTheme();
        ThemeManager.Current.ThemeChanged += (_, _) => UpdateDigest();
    }

    private ThemeManager Theme => ThemeManager.Current;

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    /// <summary>
    /// Builds a chip for each palette, painted in that palette's own colours.
    /// </summary>
    /// <remarks>
    /// This is the one place in the product where colours appear that are not from the active theme,
    /// and it is legitimate because here the colour <em>is</em> the content. Each chip carries its
    /// own legible ink, computed the same way the theme engine computes one, so a chip can never
    /// show a label the operator cannot read.
    /// </remarks>
    private void BuildPaletteCards()
    {
        var cards = new List<Border>();

        foreach (var palette in GhoztCatalog.All)
        {
            var captured = palette;

            // The chip previews the palette in the mode currently in force, so the swatches show
            // what choosing it would actually produce rather than a fixed dark preview.
            var preview = new GhoztTokens(palette.Key, Theme.IsDark, palette.DefaultOpacity, palette.DefaultEffects);

            Brush Brush(string token)
            {
                var c = preview[token];
                var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
                brush.Freeze();
                return brush;
            }

            var card = new Border
            {
                Width = 268,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Background = Brush("Surface.CardSolid"),
                BorderBrush = Brush("Border.Card"),
                Cursor = Cursors.Hand,
                Tag = captured.Key,
            };

            var stack = new StackPanel();

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = palette.Label,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("Text.Primary"),
            };
            header.Children.Add(name);

            var check = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily?)Application.Current.TryFindResource("Font.Icon"),
                FontSize = 12,
                Foreground = Brush("Accent.Text"),
                Visibility = palette.Key == Theme.PaletteKey ? Visibility.Visible : Visibility.Collapsed,
            };
            Grid.SetColumn(check, 1);
            header.Children.Add(check);
            stack.Children.Add(header);

            stack.Children.Add(new TextBlock
            {
                Text = palette.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10.5,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = Brush("Text.Muted"),
            });

            // Swatch row: the four identity colours plus the semantic states, so the operator can see
            // before choosing that fault and verified stay distinguishable in this palette.
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var token in new[] { "Accent.Fill", "Ghozt.Live", "Ghozt.Telemetry", "Ghozt.Counterfactual", "State.Verified", "State.Warning", "State.Fault", "Authority.Clearance" })
            {
                swatches.Children.Add(new Border
                {
                    Width = 22,
                    Height = 14,
                    Margin = new Thickness(0, 0, 3, 0),
                    CornerRadius = new CornerRadius(3),
                    Background = Brush(token),
                });
            }

            stack.Children.Add(swatches);

            stack.Children.Add(new TextBlock
            {
                Text = palette.SuitedTo,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = Brush("Text.Secondary"),
            });

            card.Child = stack;
            card.MouseLeftButtonUp += (_, _) => Apply(captured.Key);
            cards.Add(card);
        }

        Palettes.ItemsSource = cards;
    }

    private void Apply(string key)
    {
        Theme.ApplyPaletteWithItsDefaults(key);
        _loading = true;
        LoadFromTheme();
        BuildPaletteCards();
        Shell?.Log("UI.THEME", $"Palette changed to {GhoztCatalog.Find(key).Label}.",
            Core.Severity.Info, detail: Theme.Tokens.Digest());
    }

    private void LoadFromTheme()
    {
        _loading = true;

        ModeDark.IsChecked = Theme.Mode == ThemeMode.Dark;
        ModeLight.IsChecked = Theme.Mode == ThemeMode.Light;
        ModeSystem.IsChecked = Theme.Mode == ThemeMode.System;

        OpacitySlider.Value = Theme.SurfaceOpacity;
        OpacityValue.Text = $"{Theme.SurfaceOpacity}%";

        GradientToggle.IsChecked = Theme.Effects.Gradient;
        GlassToggle.IsChecked = Theme.Effects.Glass;
        GlowToggle.IsChecked = Theme.Effects.Glow;

        DirectionSlider.Value = Theme.Effects.Direction;
        IntensitySlider.Value = Theme.Effects.Intensity;
        WashSlider.Value = Theme.Effects.GradientOpacity;
        BlurSlider.Value = Theme.Effects.Blur;

        DensityCompact.IsChecked = Theme.Density == Density.Compact;
        DensityStandard.IsChecked = Theme.Density == Density.Standard;
        DensityComfortable.IsChecked = Theme.Density == Density.Comfortable;

        MotionFull.IsChecked = Theme.Motion == MotionLevel.Full;
        MotionReduced.IsChecked = Theme.Motion == MotionLevel.Reduced;
        MotionOff.IsChecked = Theme.Motion == MotionLevel.Off;

        UpdateFinishValues();
        UpdateDigest();

        RefreshStorageInfo();
        LoadSafetyAndBudgetSettings();

        _loading = false;
    }

    /// <summary>
    /// Writes the numeric readout beside each finish slider.
    /// </summary>
    /// <remarks>
    /// Guarded on <see cref="FrameworkElement.IsInitialized"/> rather than on any one field.
    ///
    /// Slider handlers fire during XAML parsing: setting Maximum coerces Value, which raises
    /// ValueChanged while the tree is still being built. A null check on the first label was not
    /// enough - the labels declared after it were still null, and Settings threw before it drew
    /// anything. IsInitialized is false for the whole parse and true once it is finished, which is
    /// the actual question being asked.
    /// </remarks>
    private void UpdateFinishValues()
    {
        if (!IsInitialized)
        {
            return;
        }

        DirectionValue.Text = $"{(int)DirectionSlider.Value}°";
        IntensityValue.Text = $"{(int)IntensitySlider.Value}%";
        WashValue.Text = $"{(int)WashSlider.Value}%";
        BlurValue.Text = ((int)BlurSlider.Value).ToString(CultureInfo.CurrentCulture);
    }

    private void UpdateDigest() => DigestText.Text = Theme.Tokens.Digest();

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        Theme.Mode = System.Enum.Parse<ThemeMode>(tag);
        BuildPaletteCards();
        UpdateDigest();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        OpacityValue.Text = $"{(int)OpacitySlider.Value}%";

        if (!_loading)
        {
            Theme.SurfaceOpacity = (int)OpacitySlider.Value;
            UpdateDigest();
        }
    }

    private void OnFinishChanged(object sender, RoutedEventArgs e)
    {
        UpdateFinishValues();

        if (_loading || !IsInitialized)
        {
            return;
        }

        Theme.Effects = new ThemeEffects(
            Gradient: GradientToggle.IsChecked == true,
            Direction: (int)DirectionSlider.Value,
            Intensity: (int)IntensitySlider.Value,
            GradientOpacity: (int)WashSlider.Value,
            Glass: GlassToggle.IsChecked == true,
            Blur: (int)BlurSlider.Value,
            Glow: GlowToggle.IsChecked == true);

        UpdateDigest();
    }

    private void OnFinishChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        OnFinishChanged(sender, (RoutedEventArgs)e);

    private void OnDensityChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        Theme.Density = System.Enum.Parse<Density>(tag);
        UpdateDigest();
    }

    private void OnMotionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        Theme.Motion = System.Enum.Parse<MotionLevel>(tag);
        UpdateDigest();
    }

    // ================================================================== STORAGE & DATABASE MAINTENANCE

    private void RefreshStorageInfo()
    {
        try
        {
            var stats = App.Store.GetDatabaseStatistics();
            DbPathText.Text = stats.DatabasePath;

            var mainMb = stats.MainSizeBytes / (1024.0 * 1024.0);
            var walKb = stats.WalSizeBytes / 1024.0;
            DbSizeText.Text = $"{mainMb:F2} MB (WAL: {walKb:F0} KB)";

            DbCountsText.Text = $"{stats.TargetCount} Targets · {stats.SweepCount} Sweeps · "
                + $"{stats.NodeCount} Nodes · {stats.EdgeCount} Edges · "
                + $"{stats.FindingCount} Findings · {stats.EvidenceCount} Evidence · {stats.EventCount} Events";
        }
        catch (Exception ex)
        {
            DbPathText.Text = App.Store.DatabasePath;
            DbSizeText.Text = "Error reading storage";
            DbCountsText.Text = ex.Message;
        }
    }

    private void OnVerifyIntegrity(object sender, RoutedEventArgs e)
    {
        VerifyIntegrityButton.IsEnabled = false;
        try
        {
            var result = App.Store.VerifyIntegrity();
            var ok = string.Equals(result.Trim(), "ok", StringComparison.OrdinalIgnoreCase);
            MaintenanceStatusText.Text = ok
                ? "Database integrity verified: All b-trees, page headers, and foreign key constraints passed."
                : $"INTEGRITY ISSUES DETECTED: {result}";

            Shell?.Log(
                ok ? "DB.INTEGRITY.OK" : "DB.INTEGRITY.FAIL",
                $"Database integrity check: {result}",
                ok ? Core.Severity.Info : Core.Severity.Critical);
        }
        catch (Exception ex)
        {
            MaintenanceStatusText.Text = $"Integrity check failed: {ex.Message}";
        }
        finally
        {
            VerifyIntegrityButton.IsEnabled = true;
        }
    }

    private void OnVacuumOptimize(object sender, RoutedEventArgs e)
    {
        VacuumButton.IsEnabled = false;
        try
        {
            App.Store.VacuumAndOptimize();
            RefreshStorageInfo();
            MaintenanceStatusText.Text = "Database vacuumed and WAL checkpointed successfully.";
            Shell?.Log("DB.VACUUM.OK", "SQLite database vacuum and WAL truncation complete.", Core.Severity.Info);
        }
        catch (Exception ex)
        {
            MaintenanceStatusText.Text = $"Vacuum failed: {ex.Message}";
        }
        finally
        {
            VacuumButton.IsEnabled = true;
        }
    }

    private void OnBackupDatabase(object sender, RoutedEventArgs e)
    {
        BackupButton.IsEnabled = false;
        try
        {
            var path = App.Store.BackupDatabase();
            MaintenanceStatusText.Text = $"Database backup created: {Path.GetFileName(path)}";
            Shell?.Log("DB.BACKUP.OK", $"Database snapshot saved to {path}", Core.Severity.Info, detail: path);
        }
        catch (Exception ex)
        {
            MaintenanceStatusText.Text = $"Backup failed: {ex.Message}";
        }
        finally
        {
            BackupButton.IsEnabled = true;
        }
    }

    // ================================================================== EXECUTION & SAFETY BOUNDARIES

    private void LoadSafetyAndBudgetSettings()
    {
        var isolation = App.Store.GetSetting("execution.isolation") ?? "IsolatedProcess";
        IsolationProcess.IsChecked = isolation == "IsolatedProcess";
        IsolationAttached.IsChecked = isolation == "AttachedMonitor";
        IsolationPassive.IsChecked = isolation == "PassiveObserver";

        var destructive = App.Store.GetSetting("execution.destructive_interlock");
        DestructiveInterlockToggle.IsChecked = destructive != "false"; // Default true for safety

        if (int.TryParse(App.Store.GetSetting("sweep.max_states"), CultureInfo.InvariantCulture, out var maxStates))
        {
            MaxStatesSlider.Value = Math.Clamp(maxStates, 10, 500);
        }
        else
        {
            MaxStatesSlider.Value = 100;
        }
        MaxStatesValue.Text = ((int)MaxStatesSlider.Value).ToString(CultureInfo.CurrentCulture);

        if (int.TryParse(App.Store.GetSetting("sweep.max_depth"), CultureInfo.InvariantCulture, out var maxDepth))
        {
            MaxDepthSlider.Value = Math.Clamp(maxDepth, 1, 50);
        }
        else
        {
            MaxDepthSlider.Value = 10;
        }
        MaxDepthValue.Text = ((int)MaxDepthSlider.Value).ToString(CultureInfo.CurrentCulture);
    }

    private void OnIsolationChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        App.Store.SetSetting("execution.isolation", tag);
        Shell?.Log("EXEC.ISOLATION", $"Execution isolation set to {tag}.", Core.Severity.Info);
    }

    private void OnDestructiveGateChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var isEnabled = DestructiveInterlockToggle.IsChecked == true;
        App.Store.SetSetting("execution.destructive_interlock", isEnabled ? "true" : "false");
        Shell?.Log("EXEC.SAFETY", $"Destructive action gate interlock {(isEnabled ? "ENABLED" : "DISABLED")}.", Core.Severity.Medium);
    }

    private void OnBudgetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (MaxStatesValue is not null && MaxStatesSlider is not null)
        {
            MaxStatesValue.Text = ((int)MaxStatesSlider.Value).ToString(CultureInfo.CurrentCulture);
        }

        if (MaxDepthValue is not null && MaxDepthSlider is not null)
        {
            MaxDepthValue.Text = ((int)MaxDepthSlider.Value).ToString(CultureInfo.CurrentCulture);
        }

        if (!_loading && MaxStatesSlider is not null && MaxDepthSlider is not null)
        {
            App.Store.SetSetting("sweep.max_states", ((int)MaxStatesSlider.Value).ToString(CultureInfo.InvariantCulture));
            App.Store.SetSetting("sweep.max_depth", ((int)MaxDepthSlider.Value).ToString(CultureInfo.InvariantCulture));
        }
    }
}

