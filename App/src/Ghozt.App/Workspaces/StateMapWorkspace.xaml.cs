using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Ghozt.App.Shell;
using Ghozt.Core;
using Ghozt.Theming;

namespace Ghozt.App.Workspaces;

public partial class StateMapWorkspace : UserControl
{
    public StateMapWorkspace()
    {
        InitializeComponent();

        // Both hooks, deliberately. DataContextChanged fires before the first render, so the
        // workspace never paints an empty state over data it already has; Loaded covers the case
        // where the context was set before this control existed.
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();

        // The lattice is drawn in code, so its colours are resolved rather than bound. Rebuilding on
        // a palette change is what keeps a node's state colour honest after the operator switches
        // theme - otherwise a "verified" node keeps the previous palette's green.
        Theming.ThemeManager.Current.ThemeChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                Refresh();
            }
        };
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

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            Refresh();
        }
    }

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var graph = ViewGraph.IsChecked == true;
        GraphScroll.Visibility = graph ? Visibility.Visible : Visibility.Collapsed;
        Tree.Visibility = graph ? Visibility.Collapsed : Visibility.Visible;
    }

    private Brush B(string token) => (Brush?)TryFindResource(token) ?? Brushes.Gray;

    private void Refresh()
    {
        Lattice.Children.Clear();
        Tree.Items.Clear();

        var target = Shell?.Target;
        var nodes = target is null ? [] : App.Store.Nodes(target.Id);
        var edges = target is null ? [] : App.Store.Edges(target.Id);

        EmptyPanel.Visibility = nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (nodes.Count == 0)
        {
            if (target is null)
            {
                EmptyTitle.Text = "No target selected";
                EmptyBody.Text = "Select a target to see its state lattice.";
            }
            else
            {
                EmptyTitle.Text = "Nothing has been discovered yet";
                EmptyBody.Text = $"The lattice for {target.Name} is empty because no sweep has explored it. "
                                 + "Ghozt draws only what it has actually observed — it will not invent "
                                 + "structure to make the map look complete.";
            }

            Coverage.Text = "—";
            return;
        }

        // Coverage is counted, not estimated. Untested is its own category: it is an absence of
        // observation, and equating it with a pass is how coverage debt hides.
        var verified = nodes.Count(n => n.State == "Verified");
        Coverage.Text = $"{verified}/{nodes.Count} verified · {nodes.Count(n => n.State == "Deviation")} deviation · "
                        + $"{nodes.Count(n => n.State is "Untested")} untested";

        LayOut(nodes, edges);
        BuildTree(nodes, edges);
    }

    /// <summary>
    /// Places the nodes on the canvas.
    /// </summary>
    /// <remarks>
    /// A deterministic radial layout around the system root rather than a force simulation. Two
    /// reasons: the same graph must land in the same place every time so an exported snapshot is
    /// comparable with a later one, and a physics loop is exactly the kind of continuous animation
    /// this product's performance baseline cannot afford.
    /// </remarks>
    private void LayOut(IReadOnlyList<LatticeNode> nodes, IReadOnlyList<LatticeEdge> edges)
    {
        var centre = new Point(Lattice.Width / 2, Lattice.Height / 2);
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);

        var root = nodes.FirstOrDefault(n => n.Kind == NodeKind.System);
        var children = nodes.Where(n => n != root).ToList();

        if (root is not null)
        {
            positions[root.Id] = centre;
        }

        var radius = Math.Min(centre.X, centre.Y) * 0.62;
        for (var i = 0; i < children.Count; i++)
        {
            var angle = (2 * Math.PI * i / Math.Max(children.Count, 1)) - (Math.PI / 2);
            positions[children[i].Id] = new Point(
                centre.X + (Math.Cos(angle) * radius),
                centre.Y + (Math.Sin(angle) * radius));
        }

        // Edges first, so nodes draw over them.
        foreach (var edge in edges)
        {
            if (!positions.TryGetValue(edge.FromId, out var from) ||
                !positions.TryGetValue(edge.ToId, out var to))
            {
                continue;
            }

            var line = new Line
            {
                X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
                StrokeThickness = 1,
                Stroke = edge.Relationship == "counterfactual" ? B("Ghozt.Counterfactual") : B("Border.Default"),
                Opacity = 0.7,
            };

            // A counterfactual branch is dotted and violet, so a hypothetical path can never be
            // mistaken for an observed one.
            if (edge.Relationship == "counterfactual")
            {
                line.StrokeDashArray = [3, 3];
            }

            Lattice.Children.Add(line);
        }

        foreach (var node in nodes)
        {
            if (!positions.TryGetValue(node.Id, out var position))
            {
                continue;
            }

            var element = BuildNode(node);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(element, position.X - (element.DesiredSize.Width / 2));
            Canvas.SetTop(element, position.Y - (element.DesiredSize.Height / 2));
            Lattice.Children.Add(element);
        }
    }

    /// <summary>
    /// One node, shaped by kind and coloured by observation state.
    /// </summary>
    /// <remarks>
    /// Shape carries kind and colour carries state, so the two are readable independently. An
    /// untested node is drawn with an interrupted border and a muted label: it must be visibly
    /// missing coverage without being mistaken for a failure.
    /// </remarks>
    private FrameworkElement BuildNode(LatticeNode node)
    {
        var state = System.Enum.TryParse<GhoztState>(node.State, true, out var parsed) ? parsed : GhoztState.Untested;
        var untested = state == GhoztState.Untested;
        var brush = B(state.TokenName());

        var border = new Border
        {
            Background = untested ? Brushes.Transparent : B("Surface.CardSolid"),
            BorderBrush = untested ? B("Text.Muted") : brush,
            BorderThickness = new Thickness(untested ? 1 : 2),
            Padding = new Thickness(11, 7, 11, 7),
            Cursor = Cursors.Hand,
            Opacity = untested ? 0.55 : 1.0,
            ToolTip = $"{node.Label}\n{node.Kind} · {state.Label()}\n{node.Detail}",
            Tag = node,

            // Shape by kind: a system is a rounded region, an action a pill, an endpoint a tighter
            // plate, data a small square.
            CornerRadius = node.Kind switch
            {
                NodeKind.System => new CornerRadius(14),
                NodeKind.Action => new CornerRadius(999),
                NodeKind.Endpoint => new CornerRadius(4),
                NodeKind.DataState => new CornerRadius(2),
                NodeKind.Evidence => new CornerRadius(2, 10, 2, 10),
                _ => new CornerRadius(8),
            },
        };

        var stack = new StackPanel();

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = state.Glyph(),
            FontFamily = (FontFamily?)TryFindResource("Font.Icon"),
            FontSize = 10,
            Foreground = untested ? B("Text.Muted") : brush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = node.Label,
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = untested ? B("Text.Muted") : B("Text.Primary"),
        });
        stack.Children.Add(header);

        // The label is always present alongside the colour, so status never depends on hue alone.
        stack.Children.Add(new TextBlock
        {
            Text = state.Label(),
            FontSize = 9,
            Margin = new Thickness(16, 1, 0, 0),
            Foreground = untested ? B("Text.Muted") : brush,
        });

        border.Child = stack;
        border.MouseLeftButtonUp += (_, _) => Shell.InspectorSubject = node;

        return border;
    }

    private void BuildTree(IReadOnlyList<LatticeNode> nodes, IReadOnlyList<LatticeEdge> edges)
    {
        var root = nodes.FirstOrDefault(n => n.Kind == NodeKind.System);
        if (root is null)
        {
            return;
        }

        TreeViewItem Item(LatticeNode node)
        {
            var state = System.Enum.TryParse<GhoztState>(node.State, true, out var parsed) ? parsed : GhoztState.Untested;

            var item = new TreeViewItem
            {
                Header = $"{node.Label} — {state.Label()}",
                Tag = node,
                IsExpanded = true,
                Foreground = B(state.TokenName()),
                FontSize = 13,
            };

            foreach (var edge in edges.Where(e => e.FromId == node.Id))
            {
                var child = nodes.FirstOrDefault(n => n.Id == edge.ToId);
                if (child is not null)
                {
                    item.Items.Add(Item(child));
                }
            }

            return item;
        }

        Tree.Items.Add(Item(root));
    }

    private void OnTreeSelect(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: LatticeNode node })
        {
            Shell.InspectorSubject = node;
        }
    }
}
