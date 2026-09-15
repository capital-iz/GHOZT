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
    private double _zoom = 1.0;
    private bool _isPanning;
    private Point _lastPanPoint;
    private LatticeNode? _selectedNode;
    private readonly List<(LatticeEdge Edge, Line Line)> _renderedEdges = [];
    private readonly List<(LatticeNode Node, FrameworkElement Element)> _renderedNodes = [];
    private IReadOnlyList<LatticeEdge> _currentEdges = [];

    // Interactive node dragging state
    private bool _isDraggingNode;
    private FrameworkElement? _draggedElement;
    private LatticeNode? _draggedNode;
    private Point _dragStartPoint;
    private double _nodeStartLeft;
    private double _nodeStartTop;

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }

        Refresh();

        if (!string.IsNullOrWhiteSpace(Shell?.RequestedNodeId))
        {
            FocusNode(Shell.RequestedNodeId);
        }
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            Refresh();
        }
        else if (e.PropertyName is nameof(ShellViewModel.RequestedNodeId))
        {
            if (!string.IsNullOrWhiteSpace(Shell?.RequestedNodeId))
            {
                FocusNode(Shell.RequestedNodeId);
            }
        }
    }

    public void FocusNode(string nodeId)
    {
        var match = _renderedNodes.FirstOrDefault(rn => rn.Node.Id.Equals(nodeId, StringComparison.OrdinalIgnoreCase));
        if (match.Node is not null && match.Element is not null)
        {
            var left = Canvas.GetLeft(match.Element);
            var top = Canvas.GetTop(match.Element);
            if (GraphScroll.ActualWidth > 50 && GraphScroll.ActualHeight > 50)
            {
                LatticeTranslate.X = (GraphScroll.ActualWidth / 2) - (left * _zoom);
                LatticeTranslate.Y = (GraphScroll.ActualHeight / 2) - (top * _zoom);
            }

            HighlightNode(match.Node);
            OpenNodeInspector(match.Node);
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

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Refresh();
        }
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom + 0.15);
    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom - 0.15);
    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        SetZoom(1.0);
        LatticeTranslate.X = 0;
        LatticeTranslate.Y = 0;
    }

    private void OnZoomFit(object sender, RoutedEventArgs e)
    {
        var actualW = GraphScroll.ActualWidth;
        var actualH = GraphScroll.ActualHeight;
        if (actualW > 50 && actualH > 50)
        {
            var fitX = actualW / (Lattice.Width + 40);
            var fitY = actualH / (Lattice.Height + 40);
            var fitZoom = Math.Min(fitX, fitY);
            SetZoom(Math.Max(0.25, Math.Min(3.0, fitZoom)));
            LatticeTranslate.X = Math.Max(0, (actualW - (Lattice.Width * _zoom)) / 2);
            LatticeTranslate.Y = Math.Max(0, (actualH - (Lattice.Height * _zoom)) / 2);
        }
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.25, 4.0);
        LatticeScale.ScaleX = _zoom;
        LatticeScale.ScaleY = _zoom;
        ZoomLevelText.Text = $"{(int)Math.Round(_zoom * 100)}%";
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var delta = e.Delta > 0 ? 0.12 : -0.12;
        SetZoom(_zoom + delta);
        e.Handled = true;
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Left)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(GraphScroll);
            CanvasContainer.CaptureMouse();

            if (e.OriginalSource is Canvas || e.OriginalSource is Grid)
            {
                HighlightNode(null);
            }
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var current = e.GetPosition(GraphScroll);
            var deltaX = current.X - _lastPanPoint.X;
            var deltaY = current.Y - _lastPanPoint.Y;
            LatticeTranslate.X += deltaX;
            LatticeTranslate.Y += deltaY;
            _lastPanPoint = current;
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            CanvasContainer.ReleaseMouseCapture();
        }
    }

    private Brush B(string token) => (Brush?)TryFindResource(token) ?? Brushes.Gray;

    private void Refresh()
    {
        Lattice.Children.Clear();
        Tree.Items.Clear();
        _renderedEdges.Clear();
        _renderedNodes.Clear();

        var target = Shell?.Target;
        var allNodes = target is null ? [] : App.Store.Nodes(target.Id);
        var allEdges = target is null ? [] : App.Store.Edges(target.Id);

        EmptyPanel.Visibility = allNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (allNodes.Count == 0)
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

        // Apply State and Kind Filters
        var selectedState = (StateFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        var selectedKind = (KindFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";

        var filteredNodes = allNodes.Where(n =>
        {
            if (n.Kind == NodeKind.System) return true; // Always retain system anchor

            var matchesState = selectedState is "All" || n.State.Equals(selectedState, StringComparison.OrdinalIgnoreCase);
            var matchesKind = selectedKind is "All" || n.Kind.ToString().Equals(selectedKind, StringComparison.OrdinalIgnoreCase);

            return matchesState && matchesKind;
        }).ToList();

        var activeNodeIds = filteredNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var filteredEdges = allEdges.Where(e => activeNodeIds.Contains(e.FromId) && activeNodeIds.Contains(e.ToId)).ToList();
        _currentEdges = filteredEdges;

        // Coverage is counted, not estimated. Untested is its own category: it is an absence of
        // observation, and equating it with a pass is how coverage debt hides.
        var verified = allNodes.Count(n => n.State == "Verified");
        Coverage.Text = $"{verified}/{allNodes.Count} verified · {allNodes.Count(n => n.State == "Deviation")} deviation · "
                        + $"{allNodes.Count(n => n.State is "Untested")} untested";

        LayOut(filteredNodes, filteredEdges);
        BuildTree(filteredNodes, filteredEdges);
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
        var w = double.IsNaN(Lattice.Width) || Lattice.Width <= 0 ? 1300.0 : Lattice.Width;
        var h = double.IsNaN(Lattice.Height) || Lattice.Height <= 0 ? 750.0 : Lattice.Height;
        var centre = new Point(w / 2, h / 2);
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);

        var root = nodes.FirstOrDefault(n => n.Kind == NodeKind.System);
        var children = nodes.Where(n => n != root).ToList();

        if (root is not null)
        {
            positions[root.Id] = (root.X > 0 && root.Y > 0) ? new Point(root.X, root.Y) : centre;
        }

        var radius = Math.Max(40.0, Math.Min(centre.X, centre.Y) * 0.62);
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].X > 0 && children[i].Y > 0)
            {
                positions[children[i].Id] = new Point(children[i].X, children[i].Y);
            }
            else
            {
                var angle = (2 * Math.PI * i / Math.Max(children.Count, 1)) - (Math.PI / 2);
                positions[children[i].Id] = new Point(
                    centre.X + (Math.Cos(angle) * radius),
                    centre.Y + (Math.Sin(angle) * radius));
            }
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
            _renderedEdges.Add((edge, line));
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
            _renderedNodes.Add((node, element));
        }
    }

    private void HighlightNode(LatticeNode? selected)
    {
        _selectedNode = selected;

        if (selected is null)
        {
            foreach (var (edge, line) in _renderedEdges)
            {
                line.StrokeThickness = 1;
                line.Opacity = 0.7;
                line.Stroke = edge.Relationship == "counterfactual" ? B("Ghozt.Counterfactual") : B("Border.Default");
            }

            foreach (var (node, element) in _renderedNodes)
            {
                element.Opacity = node.State == "Untested" ? 0.55 : 1.0;
                if (element is Border b)
                {
                    var state = System.Enum.TryParse<GhoztState>(node.State, true, out var parsed) ? parsed : GhoztState.Untested;
                    b.BorderBrush = state == GhoztState.Untested ? B("Text.Muted") : B(state.TokenName());
                    b.BorderThickness = new Thickness(state == GhoztState.Untested ? 1 : 2);
                }
            }
            return;
        }

        // Highlight connected edges (Traversal Trail)
        var connectedNodeIds = new HashSet<string>(StringComparer.Ordinal) { selected.Id };

        foreach (var (edge, line) in _renderedEdges)
        {
            var isIncident = edge.FromId == selected.Id || edge.ToId == selected.Id;
            if (isIncident)
            {
                connectedNodeIds.Add(edge.FromId);
                connectedNodeIds.Add(edge.ToId);
                line.StrokeThickness = 2.5;
                line.Opacity = 1.0;
                line.Stroke = edge.Relationship == "counterfactual" ? B("Ghozt.Counterfactual") : B("Ghozt.Live");
            }
            else
            {
                line.StrokeThickness = 1;
                line.Opacity = 0.2;
                line.Stroke = B("Border.Subtle");
            }
        }

        // Highlight selected node and immediate neighbours
        foreach (var (node, element) in _renderedNodes)
        {
            if (node.Id == selected.Id)
            {
                element.Opacity = 1.0;
                if (element is Border b)
                {
                    b.BorderBrush = B("Authority.Fill");
                    b.BorderThickness = new Thickness(3);
                }
            }
            else if (connectedNodeIds.Contains(node.Id))
            {
                element.Opacity = 1.0;
                if (element is Border b)
                {
                    b.BorderThickness = new Thickness(2);
                }
            }
            else
            {
                element.Opacity = 0.3;
            }
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

            // Shape by kind: a system is a rounded region, an action a rounded rectangle, an endpoint a tighter
            // plate, data a small square.
            CornerRadius = node.Kind switch
            {
                NodeKind.System => new CornerRadius(14),
                NodeKind.Action => new CornerRadius(8),
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

        border.MouseLeftButtonDown += (s, e) =>
        {
            _isDraggingNode = true;
            _draggedElement = border;
            _draggedNode = node;
            _dragStartPoint = e.GetPosition(Lattice);
            _nodeStartLeft = Canvas.GetLeft(border);
            _nodeStartTop = Canvas.GetTop(border);
            border.CaptureMouse();
            e.Handled = true;
        };

        border.MouseMove += (s, e) =>
        {
            if (_isDraggingNode && _draggedElement == border && _draggedNode == node)
            {
                var current = e.GetPosition(Lattice);
                var deltaX = current.X - _dragStartPoint.X;
                var deltaY = current.Y - _dragStartPoint.Y;
                var newLeft = _nodeStartLeft + deltaX;
                var newTop = _nodeStartTop + deltaY;

                Canvas.SetLeft(border, newLeft);
                Canvas.SetTop(border, newTop);

                var centreX = newLeft + (border.ActualWidth > 0 ? border.ActualWidth / 2 : border.DesiredSize.Width / 2);
                var centreY = newTop + (border.ActualHeight > 0 ? border.ActualHeight / 2 : border.DesiredSize.Height / 2);

                foreach (var (edge, line) in _renderedEdges)
                {
                    if (edge.FromId == node.Id)
                    {
                        line.X1 = centreX;
                        line.Y1 = centreY;
                    }
                    if (edge.ToId == node.Id)
                    {
                        line.X2 = centreX;
                        line.Y2 = centreY;
                    }
                }
                e.Handled = true;
            }
        };

        border.MouseLeftButtonUp += (s, e) =>
        {
            if (_isDraggingNode && _draggedNode == node)
            {
                border.ReleaseMouseCapture();
                _isDraggingNode = false;

                var finalLeft = Canvas.GetLeft(border);
                var finalTop = Canvas.GetTop(border);
                var centreX = finalLeft + (border.ActualWidth > 0 ? border.ActualWidth / 2 : border.DesiredSize.Width / 2);
                var centreY = finalTop + (border.ActualHeight > 0 ? border.ActualHeight / 2 : border.DesiredSize.Height / 2);

                var updated = node with { X = centreX, Y = centreY };
                App.Store.Save(updated);

                Shell?.Log("LATTICE.NODE.MOVE", $"Repositioned '{node.Label}' to ({centreX:F0}, {centreY:F0}).",
                    Severity.Info, detail: $"id={node.Id}");
            }

            Shell.InspectorSubject = node;
            HighlightNode(node);
            OpenNodeInspector(node);
            e.Handled = true;
        };

        return border;
    }

    private void OnViewNodeFaults(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is not null)
        {
            Shell.NavigateToFaults(null, _selectedNode.Id);
        }
    }

    private sealed record TransitionItem(string Direction, string TargetNode, string Relationship);

    private void OpenNodeInspector(LatticeNode node)
    {
        _selectedNode = node;
        InspectorNodeLabel.Text = node.Label;
        InspectorNodeId.Text = node.Id;

        var state = System.Enum.TryParse<GhoztState>(node.State, true, out var parsed) ? parsed : GhoztState.Untested;
        InspectorStateText.Text = state.Label().ToUpperInvariant();
        InspectorStateBadge.Background = B(state.TokenName());

        InspectorKindText.Text = node.Kind.ToString().ToUpperInvariant();
        InspectorProvenanceText.Text = node.Provenance;
        InspectorVisitCount.Text = node.VisitCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
        InspectorEvidenceCount.Text = node.EvidenceCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
        InspectorLastSeen.Text = node.LastSeenAt?.ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture) ?? "never";
        InspectorDetailText.Text = string.IsNullOrWhiteSpace(node.Detail) ? "No contract detail recorded." : node.Detail;

        var transitions = new List<TransitionItem>();
        foreach (var edge in _currentEdges)
        {
            if (edge.FromId == node.Id)
            {
                transitions.Add(new TransitionItem("OUTBOUND →", edge.ToId, edge.Relationship));
            }
            else if (edge.ToId == node.Id)
            {
                transitions.Add(new TransitionItem("INBOUND ←", edge.FromId, edge.Relationship));
            }
        }
        InspectorTransitionsList.ItemsSource = transitions;
        NodeInspectorDrawer.Visibility = Visibility.Visible;
    }

    private void OnCloseNodeInspector(object sender, RoutedEventArgs e)
    {
        NodeInspectorDrawer.Visibility = Visibility.Collapsed;
        HighlightNode(null);
    }

    private async void OnRunDiscoverySweep(object sender, RoutedEventArgs e)
    {
        if (Shell?.Target is not { } target)
        {
            return;
        }

        BtnEmptyDiscovery.IsEnabled = false;
        EmptyTitle.Text = "Running discovery sweep…";
        EmptyBody.Text = "Executing live reachability, identity, and structure probes to populate the State Lattice.";

        var sweep = new Sweep
        {
            Id = App.Store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = $"Discovery Sweep of {target.Name}",
            Mode = "Smoke",
            State = RunState.Running,
            MaxStates = 50,
            MaxSeconds = 30,
            MaxDepth = 3,
            CreatedAt = DateTimeOffset.Now,
            StartedAt = DateTimeOffset.Now,
        };
        App.Store.Save(sweep);

        App.Store.Save(new LatticeNode
        {
            Id = "system:root",
            TargetId = target.Id,
            Label = target.Name,
            Kind = NodeKind.System,
            State = "Observed",
            Provenance = "Declared",
            Detail = $"{target.Kind} · {target.Environment} · {target.Access}",
            LastSeenAt = DateTimeOffset.Now,
        });

        var engine = new SweepEngine(App.Store);
        try
        {
            await engine.RunAsync(sweep, target, null, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Shell?.Log("SWP.DISCOVER.FAIL", $"Discovery sweep failed: {ex.Message}", Severity.Medium);
        }
        finally
        {
            BtnEmptyDiscovery.IsEnabled = true;
            Shell?.ReloadTargets();
            Shell?.Refresh();
            Refresh();
        }
    }

    private void BuildTree(IReadOnlyList<LatticeNode> nodes, IReadOnlyList<LatticeEdge> edges)
    {
        var root = nodes.FirstOrDefault(n => n.Kind == NodeKind.System);
        if (root is null)
        {
            return;
        }

        // Project onto an acyclic spanning tree to prevent infinite recursion and StackOverflowException
        // when directed state cycles exist in the exploration lattice.
        var tree = StateSpaceMath.ProjectAcyclicSpanningTree(nodes, edges, root.Id);

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

            if (tree.TryGetValue(node.Id, out var children))
            {
                foreach (var child in children)
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
            HighlightNode(node);
            OpenNodeInspector(node);
        }
    }

    private void OnExploreClick(object sender, RoutedEventArgs e)
    {
        var target = Shell.Target;
        if (target is null) return;

        var startNodeId = _selectedNode?.Id ?? $"node-{target.Id}-root";
        var engine = new ExplorationEngine(App.Store);
        var result = engine.Step(target, startNodeId, ExplorationStrategy.Frontier);

        Refresh();

        if (result.Executed)
        {
            var updatedNodes = App.Store.Nodes(target.Id);
            var resultNode = updatedNodes.FirstOrDefault(n => n.Id == result.ResultStateId);
            if (resultNode != null)
            {
                HighlightNode(resultNode);
                Shell.InspectorSubject = resultNode;
            }
        }
    }

    private void OnTriangulateClick(object sender, RoutedEventArgs e)
    {
        var target = Shell.Target;
        if (target is null) return;

        var engine = new TruthTriangulationEngine(App.Store);
        var report = engine.Triangulate(target);

        if (report.DriftCount > 0)
        {
            DriftBadgeBorder.Visibility = Visibility.Visible;
            DriftBadgeText.Text = $"{report.DriftCount} DRIFT{(report.DriftCount > 1 ? "S" : "")}";
        }
        else
        {
            DriftBadgeBorder.Visibility = Visibility.Collapsed;
        }

        Refresh();
    }
}
