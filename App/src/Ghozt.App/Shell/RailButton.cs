using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;

namespace Ghozt.App.Shell;

/// <summary>
/// One workspace in the operations rail.
/// </summary>
/// <remarks>
/// The active item is marked three ways: an accent rail down the leading edge, an accent-tinted
/// fill, and accent ink. Three channels rather than one because the rail is the operator's primary
/// orientation control and it must survive high contrast, a colour-blind operator and a glance.
/// </remarks>
public sealed class RailButton : Control
{
    static RailButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RailButton),
            new FrameworkPropertyMetadata(typeof(RailButton)));
    }

    public static readonly DependencyProperty DefinitionProperty =
        DependencyProperty.Register(nameof(Definition), typeof(WorkspaceDefinition), typeof(RailButton),
            new PropertyMetadata(null, OnDefinitionChanged));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(RailButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(RailButton),
            new PropertyMetadata(false));

    public WorkspaceDefinition? Definition
    {
        get => (WorkspaceDefinition?)GetValue(DefinitionProperty);
        set => SetValue(DefinitionProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public RailButton()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        MouseLeftButtonUp += (_, _) => Activate();
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                Activate();
                e.Handled = true;
            }
        };

        Loaded += (_, _) => Bind();
    }

    private static void OnDefinitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RailButton button)
        {
            button.Bind();
        }
    }

    private void Bind()
    {
        if (Definition is null)
        {
            return;
        }

        // The accessible name is the title plus its purpose, so a screen-reader user gets what a
        // sighted operator gets from the tooltip - not merely the glyph's name.
        AutomationProperties.SetName(this, $"{Definition.Title}. {Definition.Purpose}");
        ToolTip = $"{Definition.Title}  ({Definition.Shortcut})\n{Definition.Purpose}";

        var shell = FindShell();
        if (shell is null)
        {
            return;
        }

        IsActive = shell.Workspace == Definition.Kind;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShellViewModel.Workspace))
            {
                IsActive = shell.Workspace == Definition.Kind;
            }
        };

        SetBinding(IsExpandedProperty, new Binding(nameof(MainWindow.RailExpanded))
        {
            Source = Window.GetWindow(this),
            Mode = BindingMode.OneWay,
        });
    }

    private ShellViewModel? FindShell()
    {
        if (Application.Current?.MainWindow?.DataContext is ShellViewModel appShell)
        {
            return appShell;
        }

        var element = (FrameworkElement?)this;
        while (element is not null)
        {
            if (element.DataContext is ShellViewModel shell)
            {
                return shell;
            }

            element = element.Parent as FrameworkElement ?? element.TemplatedParent as FrameworkElement;
        }

        return Window.GetWindow(this)?.DataContext as ShellViewModel;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RailButtonAutomationPeer(this);

    internal void Activate()
    {
        if (Definition is not null && FindShell() is { } shell)
        {
            shell.Workspace = Definition.Kind;
        }
    }

    private sealed class RailButtonAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        public RailButtonAutomationPeer(RailButton owner) : base(owner) { }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(RailButton);

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        public void Invoke()
        {
            try
            {
                if (Owner is RailButton btn)
                {
                    if (btn.Dispatcher.CheckAccess())
                    {
                        btn.Activate();
                    }
                    else
                    {
                        btn.Dispatcher.Invoke(btn.Activate);
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ghozt-invoke-ex.txt"),
                        $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\nInner: {ex.InnerException}");
                }
                catch { }
                throw;
            }
        }
    }
}
