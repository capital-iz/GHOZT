using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        ThemeManager.Current.ThemeChanged += (_, _) => ApplyFinish();

        SizeChanged += (_, _) => ApplyWidthState();
        Loaded += OnLoaded;

        BindShortcuts();
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
        WorkspaceKind.Evidence => new EvidenceWorkspace { DataContext = _shell },
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

        Bind(Key.K, ModifierKeys.Control, () => _shell.CommandPaletteOpen = true);
        Bind(Key.P, ModifierKeys.Control, () => _shell.CommandPaletteOpen = true);
        Bind(Key.OemComma, ModifierKeys.Control, () => _shell.Workspace = WorkspaceKind.Settings);
        Bind(Key.L, ModifierKeys.Control, () => StreamFilter.Focus());

        var keys = new[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };
        for (var i = 0; i < keys.Length && i < WorkspaceDefinition.All.Count; i++)
        {
            var kind = WorkspaceDefinition.All[i].Kind;
            Bind(keys[i], ModifierKeys.Alt, () => _shell.Workspace = kind);
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

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _shell.CommandPaletteOpen)
        {
            _shell.CommandPaletteOpen = false;
            e.Handled = true;
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
