using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class RegressionsWorkspace : UserControl
{
    private bool _attached;

    public RegressionsWorkspace()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }

        Refresh();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var target = Shell?.Target;
        if (target is null)
        {
            GuardsPanel.Children.Clear();
            GuardsPanel.Children.Add(EmptyState);
            GuardSummaryText.Text = "No target selected";
            return;
        }

        var engine = new RegressionEngine(App.Store);
        var guards = engine.GenerateGuards(target.Id);

        RenderGuards(guards);
    }

    private void OnGenerateGuards(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void RenderGuards(IReadOnlyList<RegressionGuard> guards)
    {
        GuardsPanel.Children.Clear();

        if (guards.Count == 0)
        {
            GuardsPanel.Children.Add(new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)TryFindResource("Border.Card"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24),
                Child = new TextBlock
                {
                    Text = "No regression guards generated yet. Confirmed findings in the Faults workspace automatically populate guards.",
                    Style = (Style)TryFindResource("Type.CaptionStyle"),
                    TextAlignment = TextAlignment.Center,
                }
            });
            GuardSummaryText.Text = "0 active regression guards";
            return;
        }

        var passing = guards.Count(g => g.Status == GuardStatus.Passing);
        var failing = guards.Count(g => g.Status == GuardStatus.Failing);
        GuardSummaryText.Text = $"{guards.Count} total guards · {passing} passing · {failing} failing";

        foreach (var guard in guards)
        {
            var isPassing = guard.Status == GuardStatus.Passing;
            var card = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var stack = new StackPanel();

            // Header: Guard ID + Title + Status Badge
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = guard.Id,
                Style = (Style)TryFindResource("Type.MonoStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = guard.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(titlePanel, 0);
            header.Children.Add(titlePanel);

            var statusBadge = new Border
            {
                Background = (Brush)(isPassing ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                BorderBrush = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            statusBadge.Child = new TextBlock
            {
                Text = isPassing ? "PASSING" : "FAILING",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(isPassing ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(statusBadge, 1);
            header.Children.Add(statusBadge);

            stack.Children.Add(header);

            // Invariant & Expected
            stack.Children.Add(new TextBlock
            {
                Text = $"Expected: {guard.ExpectedBehavior}",
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 6, 0, 4),
            });

            stack.Children.Add(new TextBlock
            {
                Text = guard.Details,
                FontSize = 11,
                Foreground = (Brush)TryFindResource("Text.Muted"),
            });

            card.Child = stack;
            GuardsPanel.Children.Add(card);
        }
    }
}
