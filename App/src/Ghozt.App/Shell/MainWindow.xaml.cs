using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ghozt.App.Theming;
using Ghozt.App.Workspaces;
using Ghozt.Core;

namespace Ghozt.App.Shell;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly Dictionary<WorkspaceKind, ContentControl> _cache = [];

    public static readonly DependencyProperty RailExpandedProperty =
        DependencyProperty.Register(nameof(RailExpanded), typeof(bool), typeof(MainWindow),
            new PropertyMetadata(false));

    /// <summary>Whether the operations rail shows labels beside its glyphs.</summary>
    public bool RailExpanded
    {
        get => (bool)GetValue(RailExpandedProperty);
        set => SetValue(RailExpandedProperty, value);
    }

    public MainWindow()
    {
        // The converters the window's own bindings need. Registered before InitializeComponent so
        // the markup can resolve them.
        Resources["BoolToVisibility"] = new BoolToVisibilityConverter();
        Resources["InverseBoolToVisibility"] = new InverseBoolToVisibilityConverter();
        Resources["TokenBrush"] = new TokenBrushConverter();
        Resources["SeverityBrush"] = new SeverityBrushConverter();
        Resources["StreamTime"] = new StreamTimeConverter();
        Resources["Reproducibility"] = new ReproducibilityConverter();
        Resources["IsEmpty"] = new IsEmptyConverter();
        Resources["HasText"] = new HasTextConverter();

        InitializeComponent();

        _shell = new ShellViewModel(App.Store);
        DataContext = _shell;

        _shell.PropertyChanged += OnShellChanged;
        _shell.RefreshRequested += OnRefreshRequested;
        _shell.FocusSearchRequested += () => Dispatcher.Invoke(FocusWorkspaceSearch);
        ThemeManager.Current.ThemeChanged += (_, _) => ApplyFinish();

        SizeChanged += (_, _) => ApplyWidthState();
        Loaded += OnLoaded;

        BindShortcuts();
    }

    private void OnRefreshRequested()
    {
        if (Canvas.Content is ContentControl cc && cc.Content is Workspaces.IRefreshableWorkspace refreshable)
        {
            refreshable.Refresh();
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ShowWorkspace(_shell.Workspace);
        ApplyFinish();
        ApplyWidthState();
        UpdateFooter();

        _shell.Log("APP.START", "Ghozt shell opened.", Severity.Info,
            detail: $"database={App.Store.DatabasePath}; theme={ThemeManager.Current.Tokens.Digest()}");
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Workspace))
        {
            ShowWorkspace(_shell.Workspace);
        }
    }

    /// <summary>
    /// Swaps the canvas to a workspace, keeping each one alive once created.
    /// </summary>
    /// <remarks>
    /// Cached rather than rebuilt so a workspace keeps its zoom, filters and selection when the
    /// operator navigates away and back. The blueprint requires exactly that, and rebuilding would
    /// also discard the scroll position in the middle of a long triage queue.
    /// </remarks>
    private void ShowWorkspace(WorkspaceKind kind)
    {
        if (!_cache.TryGetValue(kind, out var view))
        {
            view = new ContentControl
            {
                Content = CreateWorkspace(kind),
                Focusable = false,
            };
            _cache[kind] = view;
        }

        Canvas.Content = view;
    }

    /// <summary>Switches workspace for a capture run, without going through the view model.</summary>
    internal void ShowWorkspaceForCapture(WorkspaceKind kind) => _shell.Workspace = kind;

    private UIElement CreateWorkspace(WorkspaceKind kind) => kind switch
    {
        WorkspaceKind.Targets => new TargetsWorkspace { DataContext = _shell },
        WorkspaceKind.Sweep => new SweepWorkspace { DataContext = _shell },
        WorkspaceKind.StateMap => new StateMapWorkspace { DataContext = _shell },
        WorkspaceKind.Faults => new FaultsWorkspace { DataContext = _shell },
        WorkspaceKind.Replay => new ReplayWorkspace { DataContext = _shell },
        WorkspaceKind.Evidence => new EvidenceWorkspace { DataContext = _shell },
        WorkspaceKind.Algorithms => new AlgorithmsWorkspace { DataContext = _shell },
        WorkspaceKind.Regressions => new RegressionsWorkspace { DataContext = _shell },
        WorkspaceKind.Reports => new ReportsWorkspace { DataContext = _shell },
        WorkspaceKind.Clearance => new ClearanceWorkspace { DataContext = _shell },
        WorkspaceKind.Settings => new SettingsWorkspace { DataContext = _shell },
        _ => new PlannedWorkspace(WorkspaceDefinition.For(kind)) { DataContext = _shell },
    };

    // ---------------------------------------------------------------- finish

    /// <summary>
    /// Applies the parts of the finish that are geometry rather than colour.
    /// </summary>
    /// <remarks>
    /// The wash is collapsed rather than made transparent when it is off. A transparent gradient
    /// still costs a composited layer every frame, and this product has to stay responsive on a
    /// GTX 960.
    /// </remarks>
    private void ApplyFinish()
    {
        var tokens = ThemeManager.Current.Tokens;
        AppWash.Visibility = tokens.Effects.Gradient ? Visibility.Visible : Visibility.Collapsed;
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        FooterPath.Text = $"Evidence store: {App.Store.DatabasePath}";
        FooterTheme.Text = $"theme {ThemeManager.Current.Tokens.Digest()}";
    }

    // ---------------------------------------------------------------- responsive

    /// <summary>
    /// The width states the blueprint specifies.
    /// </summary>
    /// <remarks>
    /// Responsive here is not shrinking cards: it is prioritising target truth, task and evidence.
    /// The status strip never collapses, because it is the one region whose absence would let an
    /// operator act against the wrong system.
    /// </remarks>
    private void ApplyWidthState()
    {
        var width = ActualWidth;

        if (width >= 1800)
        {
            RailExpanded = true;
            SetInspector(visible: true, width: 380);
        }
        else if (width >= 1400)
        {
            RailExpanded = false;
            SetInspector(visible: true, width: 340);
        }
        else if (width >= 1100)
        {
            RailExpanded = false;
            SetInspector(visible: true, width: 300);
        }
        else
        {
            // Below 1100 the inspector becomes an overlay rather than a column, so the canvas keeps
            // a usable width. It is not removed: the detail it carries is the product.
            RailExpanded = false;
            SetInspector(visible: false, width: 0);
        }
    }

    private void SetInspector(bool visible, double width)
    {
        Inspector.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Inspector.Width = width;
        InspectorColumn.Width = visible ? GridLength.Auto : new GridLength(0);
    }

    private void OnToggleRail(object sender, RoutedEventArgs e) => RailExpanded = !RailExpanded;

    // ---------------------------------------------------------------- keyboard

    /// <summary>
    /// The shortcuts the blueprint names.
    /// </summary>
    /// <remarks>
    /// No destructive action is bound to a single unmodified key. Alt+1..0 select workspaces, which
    /// is navigation; the emergency stop is deliberately not on a bare key, because a stop issued by
    /// a stray keypress in the middle of a run destroys the run's continuity.
    /// </remarks>
    private void BindShortcuts()
    {
        void Bind(Key key, ModifierKeys modifiers, Action action) =>
            InputBindings.Add(new KeyBinding(new RelayCommand(action), key, modifiers));

        Bind(Key.K, ModifierKeys.Control, () => _shell.CommandPaletteOpen = !_shell.CommandPaletteOpen);
        Bind(Key.P, ModifierKeys.Control, () => _shell.CommandPaletteOpen = !_shell.CommandPaletteOpen);
        Bind(Key.F1, ModifierKeys.None, () => _shell.CommandPaletteOpen = !_shell.CommandPaletteOpen);
        Bind(Key.F, ModifierKeys.Control, FocusWorkspaceSearch);
        Bind(Key.F3, ModifierKeys.None, FocusWorkspaceSearch);
        Bind(Key.F5, ModifierKeys.None, () => _shell.RequestRefresh());
        Bind(Key.OemComma, ModifierKeys.Control, () => _shell.Workspace = WorkspaceKind.Settings);
        Bind(Key.L, ModifierKeys.Control, () =>
        {
            if (_shell.Tray == TrayState.Collapsed)
            {
                _shell.Tray = TrayState.Peek;
            }
            StreamFilter.Focus();
        });
        Bind(Key.F4, ModifierKeys.None, () =>
        {
            if (_shell.Tray == TrayState.Collapsed)
            {
                _shell.Tray = TrayState.Peek;
                StreamFilter.Focus();
            }
            else
            {
                _shell.Tray = TrayState.Collapsed;
            }
        });

        var keys = new[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };
        for (var i = 0; i < keys.Length && i < WorkspaceDefinition.All.Count; i++)
        {
            var kind = WorkspaceDefinition.All[i].Kind;
            Bind(keys[i], ModifierKeys.Alt, () => _shell.Workspace = kind);
        }

        var numPadKeys = new[] { Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4, Key.NumPad5, Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9, Key.NumPad0 };
        for (var i = 0; i < numPadKeys.Length && i < WorkspaceDefinition.All.Count; i++)
        {
            var kind = WorkspaceDefinition.All[i].Kind;
            Bind(numPadKeys[i], ModifierKeys.Alt, () => _shell.Workspace = kind);
        }

        // F6 cycles the shell regions, which is what makes the whole frame keyboard-navigable rather
        // than only the focused panel.
        Bind(Key.F6, ModifierKeys.None, CycleRegion);
    }

    private int _region;

    private void CycleRegion()
    {
        var regions = new UIElement?[] { Rail, Canvas, Inspector, StreamFilter };
        _region = (_region + 1) % regions.Length;
        regions[_region]?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void FocusWorkspaceSearch()
    {
        DependencyObject root = Canvas;
        if (Canvas.Content is ContentControl cc && cc.Content is DependencyObject dep)
        {
            root = dep;
        }

        var box = FindDescendant<TextBox>(root, tb =>
            tb.IsVisible && (tb.Name.Contains("Search", StringComparison.OrdinalIgnoreCase)
                             || tb.ToolTip?.ToString()?.Contains("Search", StringComparison.OrdinalIgnoreCase) == true
                             || System.Windows.Automation.AutomationProperties.GetName(tb).Contains("Search", StringComparison.OrdinalIgnoreCase)));

        if (box is null)
        {
            box = FindDescendant<TextBox>(Canvas, tb =>
                tb.IsVisible && (tb.Name.Contains("Search", StringComparison.OrdinalIgnoreCase)
                                 || tb.ToolTip?.ToString()?.Contains("Search", StringComparison.OrdinalIgnoreCase) == true
                                 || System.Windows.Automation.AutomationProperties.GetName(tb).Contains("Search", StringComparison.OrdinalIgnoreCase)));
        }

        if (box is not null)
        {
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        }
        else
        {
            _shell.CommandPaletteOpen = true;
        }
    }

    private static T? FindDescendant<T>(DependencyObject? parent, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        if (parent is null) return null;

        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T typed && (predicate is null || predicate(typed)))
            {
                return typed;
            }
            if (child is DependencyObject depChild)
            {
                var found = FindDescendant(depChild, predicate);
                if (found is not null) return found;
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && (predicate is null || predicate(typed)))
            {
                return typed;
            }
            var found = FindDescendant(child, predicate);
            if (found is not null) return found;
        }

        return null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_shell.CommandPaletteOpen)
            {
                _shell.CommandPaletteOpen = false;
                e.Handled = true;
                return;
            }

            if (_shell.Tray != TrayState.Collapsed)
            {
                _shell.Tray = TrayState.Collapsed;
                e.Handled = true;
                return;
            }
        }
        else if ((e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None) ||
                 ((e.Key == Key.K || e.Key == Key.P) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
        {
            _shell.CommandPaletteOpen = !_shell.CommandPaletteOpen;
            e.Handled = true;
            return;
        }
        else if ((e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                 (e.Key == Key.F3 && Keyboard.Modifiers == ModifierKeys.None))
        {
            FocusWorkspaceSearch();
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_shell.Tray == TrayState.Collapsed)
            {
                _shell.Tray = TrayState.Peek;
                StreamFilter.Focus();
            }
            else
            {
                _shell.Tray = TrayState.Collapsed;
            }
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _shell.RequestRefresh();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    // ---------------------------------------------------------------- window chrome

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OnMaximise(sender, e);
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimise(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximise(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
