using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.Core;
using Ghozt.Theming;

namespace Ghozt.App.Shell;

/// <summary>One inspector tab, and whether it can answer for the current subject.</summary>
/// <remarks>
/// <see cref="Reason"/> is required when <see cref="Available"/> is false. A tab that is merely
/// greyed out tells an operator nothing; a tab that says "no reproduction has been attempted for
/// this finding" tells them what to do next.
/// </remarks>
public sealed class InspectorTab : ObservableBase
{
    private bool _selected;

    public required string Title { get; init; }

    public required bool Available { get; init; }

    public required string Reason { get; init; }

    public required Action<StackPanel> Render { get; init; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value) && value)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;
}

public partial class InspectorPanel : UserControl
{
    private ShellViewModel? _shell;
    private object? _pinned;

    public InspectorPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (_shell is not null)
        {
            _shell.PropertyChanged -= OnShellChanged;
        }

        _shell = DataContext as ShellViewModel;

        if (_shell is not null)
        {
            _shell.PropertyChanged += OnShellChanged;
        }

        Rebuild();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.InspectorSubject) or nameof(ShellViewModel.Target))
        {
            Rebuild();
        }
    }

    private void OnTogglePin(object sender, RoutedEventArgs e)
    {
        _pinned = _pinned is null ? Subject : null;
        PinButton.SetResourceReference(
            System.Windows.Controls.Control.ForegroundProperty,
            _pinned is null ? "Text.Secondary" : "Accent.Text");
        PinButton.ToolTip = _pinned is null
            ? "Pin this subject so selection no longer changes it"
            : "Unpin — the inspector will follow selection again";
        Rebuild();
    }

    private object? Subject => _pinned ?? _shell?.InspectorSubject ?? _shell?.Target;

    private void Rebuild()
    {
        Body.Children.Clear();

        var subject = Subject;
        if (subject is null)
        {
            EmptyState.Visibility = Visibility.Visible;
            Tabs.ItemsSource = null;
            SubjectTitle.Text = "";
            SubjectId.Text = "";
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        var tabs = subject switch
        {
            Finding finding => BuildFindingTabs(finding),
            LedgerEvent entry => BuildEventTabs(entry),
            Target target => BuildTargetTabs(target),
            Evidence evidence => BuildEvidenceTabs(evidence),
            LatticeNode node => BuildNodeTabs(node),
            _ => [],
        };

        (SubjectTitle.Text, SubjectId.Text) = subject switch
        {
            Finding f => (f.Title, $"{f.Id} · {f.Severity} · {f.Maturity}"),
            LedgerEvent l => (l.Message, $"{l.Id} · {l.Code}"),
            Target t => (t.Name, $"{t.Id} · {t.Kind}"),
            Evidence ev => (ev.Summary, $"{ev.Id} · {ev.Kind}"),
            LatticeNode n => (n.Label, $"{n.Id} · {n.Kind}"),
            _ => (subject.ToString() ?? "", ""),
        };

        foreach (var tab in tabs)
        {
            tab.SelectionChanged += (s, _) =>
            {
                Body.Children.Clear();
                ((InspectorTab)s!).Render(Body);
            };
        }

        Tabs.ItemsSource = tabs;

        var first = tabs.FirstOrDefault(t => t.Available);
        if (first is not null)
        {
            first.Selected = true;
        }
    }

    // ------------------------------------------------------------------ builders

    private IReadOnlyList<InspectorTab> BuildFindingTabs(Finding f)
    {
        var store = App.Store;
        var evidence = store.EvidenceItems(findingId: f.Id);

        return
        [
            new InspectorTab
            {
                Title = "Overview", Available = true, Reason = "",
                Render = panel =>
                {
                    Field(panel, "Identifier", f.Id.ToString(), mono: true);
                    Field(panel, "Severity", f.Severity.ToString(), token: SeverityToken(f.Severity));
                    Field(panel, "Maturity", f.Maturity.ToString());
                    Field(panel, "Owner", string.IsNullOrWhiteSpace(f.Owner) ? "unassigned" : f.Owner);
                    Field(panel, "First seen", f.FirstSeenAt.ToString("g", CultureInfo.CurrentCulture));
                    Field(panel, "Last seen", f.LastSeenAt.ToString("g", CultureInfo.CurrentCulture));

                    // Reproducibility is computed from attempts, and "not attempted" is shown as an
                    // absence rather than as zero: one is evidence, the other is its lack.
                    Field(panel, "Reproducibility",
                        f.Reproducibility is { } r
                            ? $"{r:P0}  ({f.ReproSuccesses} of {f.ReproAttempts} attempts)"
                            : "not attempted",
                        token: f.Reproducibility is null ? "Text.Muted" : null);

                    if (f.IsPhantom)
                    {
                        Callout(panel, "PHANTOM",
                            "This finding has been seen but does not reliably return. Treat the cause as unconfirmed until the reproduction envelope is known.",
                            "Ghozt.Counterfactual");
                    }

                    ProvenanceNote(panel, Ghozt.Theming.Provenance.Observed,
                        "Captured from the target during a sweep.");
                },
            },
            new InspectorTab
            {
                Title = "Actual", Available = !string.IsNullOrWhiteSpace(f.Actual),
                Reason = "No observed result was recorded for this finding.",
                Render = panel => Block(panel, "Observed result", f.Actual),
            },
            new InspectorTab
            {
                Title = "Expected", Available = !string.IsNullOrWhiteSpace(f.Expected),
                Reason = "No governing contract or oracle was recorded for this finding.",
                Render = panel => Block(panel, "Governing expectation", f.Expected),
            },
            new InspectorTab
            {
                Title = "Difference",
                Available = !string.IsNullOrWhiteSpace(f.Actual) && !string.IsNullOrWhiteSpace(f.Expected),
                Reason = "A difference needs both an expected and an actual value; one is missing.",
                Render = panel =>
                {
                    Block(panel, "Expected", f.Expected, "State.Verified");
                    Block(panel, "Actual", f.Actual, "State.Fault");
                },
            },
            new InspectorTab
            {
                Title = "Reproduction", Available = f.ReproAttempts > 0,
                Reason = "No reproduction has been attempted. Run one from the Faults workspace.",
                Render = panel =>
                {
                    Field(panel, "Attempts", f.ReproAttempts.ToString(CultureInfo.CurrentCulture));
                    Field(panel, "Successes", f.ReproSuccesses.ToString(CultureInfo.CurrentCulture));
                    Field(panel, "Fingerprint", f.Fingerprint, mono: true);
                },
            },
            new InspectorTab
            {
                Title = "Evidence", Available = evidence.Count > 0,
                Reason = "No artefact is bound to this finding, so it cannot yet be reported.",
                Render = panel =>
                {
                    foreach (var item in evidence)
                    {
                        Field(panel, item.Kind, item.Summary);
                        Field(panel, "  sha256", item.Sha256, mono: true);
                        Field(panel, "  sealed", item.Sealed ? $"yes, by {item.SealedBy}" : "not sealed",
                            token: item.Sealed ? "Authority.Clearance" : "Text.Muted");
                    }
                },
            },
        ];
    }

    private static IReadOnlyList<InspectorTab> BuildEventTabs(LedgerEvent e) =>
    [
        new InspectorTab
        {
            Title = "Event", Available = true, Reason = "",
            Render = panel =>
            {
                Field(panel, "Identifier", e.Id.ToString(), mono: true);
                Field(panel, "Time", e.At.ToString("O", CultureInfo.InvariantCulture), mono: true);
                Field(panel, "Code", e.Code, mono: true);
                Field(panel, "Severity", e.Severity.ToString(), token: SeverityToken(e.Severity));
                Field(panel, "Actor", e.Actor);
                if (!string.IsNullOrWhiteSpace(e.RelatedId))
                {
                    Field(panel, "Relates to", e.RelatedId, mono: true);
                }

                Block(panel, "Message", e.Message);
            },
        },
        new InspectorTab
        {
            Title = "Payload", Available = !string.IsNullOrWhiteSpace(e.Detail),
            Reason = "This event carries no structured payload.",
            Render = panel => Block(panel, "Structured payload", e.Detail, mono: true),
        },
    ];

    private static IReadOnlyList<InspectorTab> BuildTargetTabs(Target t) =>
    [
        new InspectorTab
        {
            Title = "Target", Available = true, Reason = "",
            Render = panel =>
            {
                Field(panel, "Identifier", t.Id.ToString(), mono: true);
                Field(panel, "Kind", t.Kind);
                Field(panel, "Environment", t.Environment.ToString(),
                    token: t.Environment == TargetEnvironment.Production ? "State.Critical" : null);
                Field(panel, "Access class", t.Access.ToString(),
                    token: t.Access == AccessClass.Destructive ? "State.Critical" : null);
                Field(panel, "Owner", string.IsNullOrWhiteSpace(t.Owner) ? "unassigned" : t.Owner);
                Field(panel, "Build", string.IsNullOrWhiteSpace(t.BuildLabel) ? "none recorded" : t.BuildLabel, mono: true);
                Field(panel, "Reachable", t.Reachable ? "yes" : "no",
                    token: t.Reachable ? "State.Verified" : "State.Warning");
                Field(panel, "Registered", t.RegisteredAt.ToString("g", CultureInfo.CurrentCulture));
            },
        },
        new InspectorTab
        {
            Title = "Authority", Available = !string.IsNullOrWhiteSpace(t.AuthorityNote),
            Reason = "No authority grant has been recorded. Ghozt will not act beyond observation without one.",
            Render = panel =>
            {
                Block(panel, "Authority grant", t.AuthorityNote);
                ProvenanceNote(panel, Ghozt.Theming.Provenance.Declared, "Entered by an operator, not inferred.");
            },
        },
        new InspectorTab
        {
            Title = "Connection", Available = !string.IsNullOrWhiteSpace(t.BaseAddress) || !string.IsNullOrWhiteSpace(t.LaunchCommand),
            Reason = "No launch command or base address is registered for this target.",
            Render = panel =>
            {
                if (!string.IsNullOrWhiteSpace(t.BaseAddress))
                {
                    Field(panel, "Base address", t.BaseAddress, mono: true);
                }

                if (!string.IsNullOrWhiteSpace(t.LaunchCommand))
                {
                    Field(panel, "Launch", t.LaunchCommand, mono: true);
                }

                if (!string.IsNullOrWhiteSpace(t.RepositoryPath))
                {
                    Field(panel, "Repository", t.RepositoryPath, mono: true);
                }
            },
        },
        new InspectorTab
        {
            Title = "Adapter", Available = true, Reason = "",
            Render = panel =>
            {
                var adapter = AdapterRegistry.Shared.Resolve(t);
                Field(panel, "Adapter", adapter.Manifest.Name);
                Field(panel, "Key", adapter.Manifest.Key, mono: true);
                Field(panel, "Version", adapter.Manifest.Version, mono: true);
                Field(panel, "Capabilities", adapter.Manifest.Capabilities.ToString());
                Field(panel, "Max access", adapter.Manifest.MaxAccessClass.ToString());
                ProvenanceNote(panel, Ghozt.Theming.Provenance.Observed, "Resolved dynamically from the examination adapter registry.");
            },
        },
    ];

    private static IReadOnlyList<InspectorTab> BuildEvidenceTabs(Evidence e) =>
    [
        new InspectorTab
        {
            Title = "Artefact", Available = true, Reason = "",
            Render = panel =>
            {
                Field(panel, "Identifier", e.Id.ToString(), mono: true);
                Field(panel, "Kind", e.Kind);
                Field(panel, "Captured", e.CapturedAt.ToString("O", CultureInfo.InvariantCulture), mono: true);
                Field(panel, "Adapter", e.SourceAdapter);
                Field(panel, "Size", $"{e.SizeBytes:N0} bytes");
            },
        },
        new InspectorTab
        {
            Title = "Chain of custody", Available = true, Reason = "",
            Render = panel =>
            {
                Field(panel, "sha256", e.Sha256, mono: true);
                Field(panel, "Seal", e.Sealed ? $"sealed by {e.SealedBy}" : "not sealed",
                    token: e.Sealed ? "Authority.Clearance" : "Text.Muted");
                Field(panel, "Redacted", e.Redacted ? "yes" : "no");

                if (!e.Sealed)
                {
                    Callout(panel, "UNSEALED",
                        "This artefact has not been verified by a reviewer. It may be inspected, but it cannot support a clearance decision.",
                        "State.Warning");
                }
            },
        },
        new InspectorTab
        {
            Title = "Payload", Available = !string.IsNullOrWhiteSpace(e.Payload),
            Reason = "This artefact has no inline payload; it is stored by reference.",
            Render = panel => Block(panel, "Raw payload", e.Payload, mono: true),
        },
    ];

    private static IReadOnlyList<InspectorTab> BuildNodeTabs(LatticeNode n) =>
    [
        new InspectorTab
        {
            Title = "Node", Available = true, Reason = "",
            Render = panel =>
            {
                Field(panel, "Identifier", n.Id, mono: true);
                Field(panel, "Kind", n.Kind.ToString());
                Field(panel, "State", n.State, token: StateToken(n.State));
                Field(panel, "Visits", n.VisitCount.ToString(CultureInfo.CurrentCulture));
                Field(panel, "Evidence", n.EvidenceCount.ToString(CultureInfo.CurrentCulture));
                if (n.LastSeenAt is { } seen)
                {
                    Field(panel, "Last seen", seen.ToString("g", CultureInfo.CurrentCulture));
                }

                if (System.Enum.TryParse<Ghozt.Theming.Provenance>(n.Provenance, true, out var p))
                {
                    ProvenanceNote(panel, p, ProvenanceText(p));
                }
            },
        },
        new InspectorTab
        {
            Title = "Detail", Available = !string.IsNullOrWhiteSpace(n.Detail),
            Reason = "No detail was captured for this node.",
            Render = panel => Block(panel, "Detail", n.Detail, mono: true),
        },
    ];

    // ------------------------------------------------------------------ rendering helpers

    private static string SeverityToken(Severity severity) => severity switch
    {
        Severity.Critical => "State.Critical",
        Severity.High => "State.Fault",
        Severity.Medium => "State.Warning",
        Severity.Low => "Ghozt.Telemetry",
        _ => "Text.Muted",
    };

    private static string StateToken(string state) =>
        System.Enum.TryParse<GhoztState>(state, true, out var parsed) ? parsed.TokenName() : "Text.Body";

    private static string ProvenanceText(Ghozt.Theming.Provenance p) => p switch
    {
        Ghozt.Theming.Provenance.Observed => "Captured from the target.",
        Ghozt.Theming.Provenance.Computed => "Derived arithmetically from observations.",
        Ghozt.Theming.Provenance.Inferred => "Produced by a model. Not an observation.",
        Ghozt.Theming.Provenance.Declared => "Entered by a person, who is recorded.",
        Ghozt.Theming.Provenance.Simulated => "Produced by varying a factor under control. Hypothetical.",
        _ => "",
    };

    /// <summary>
    /// Binds a property to a theme token by reference, the way DynamicResource does in markup.
    /// </summary>
    /// <remarks>
    /// Assigning the result of <c>TryFindResource</c> stores the brush that existed at that instant.
    /// ThemeManager publishes frozen brushes and replaces them on a palette change, so anything
    /// holding one keeps the old colour: switching to a light palette left this panel painting
    /// dark-mode inks on a white surface, and the values became almost unreadable. A resource
    /// reference re-resolves whenever the dictionary changes, which is what markup gets for free.
    /// </remarks>
    private static void Themed(FrameworkElement element, DependencyProperty property, string token) =>
        element.SetResourceReference(property, token);

    private static void Field(Panel panel, string label, string value, bool mono = false, string? token = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Foreground is set explicitly on both rather than left to a style.
        //
        // The label is signposting; the value is the answer, and must be the brighter of the two.
        // Leaving it to style lookup produced the opposite - and worse, one of the two keys did not
        // exist at all, so the value inherited its colour from nothing in particular and the whole
        // panel read with its emphasis inverted.
        var name = new TextBlock
        {
            Text = label,
            FontFamily = (FontFamily?)Application.Current.TryFindResource("Font.Ui"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Themed(name, TextBlock.ForegroundProperty, "Text.Muted");

        var content = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily?)Application.Current.TryFindResource(mono ? "Font.Mono" : "Font.Ui"),
            FontSize = mono ? 11 : 12,
            FontWeight = mono ? FontWeights.Normal : FontWeights.Medium,
        };
        Themed(content, TextBlock.ForegroundProperty, token ?? "Text.Primary");

        Grid.SetColumn(content, 1);
        grid.Children.Add(name);
        grid.Children.Add(content);
        panel.Children.Add(grid);
    }

    private static void Block(Panel panel, string label, string value, string? token = null, bool mono = false)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            Style = (Style?)Application.Current.TryFindResource("Type.MicroLabelStyle"),
        });

        var border = new Border
        {
            BorderThickness = new Thickness(token is null ? 1 : 0, 0, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(6),
        };

        border.Child = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style?)Application.Current.TryFindResource(mono ? "Type.MonoStyle" : "Text.Body"),
        };

        panel.Children.Add(border);
    }

    /// <summary>
    /// Labels how a claim came to be known.
    /// </summary>
    /// <remarks>
    /// The blueprint requires observed fact, computed metric, AI inference and human judgement to be
    /// distinguishable. A confidence percentage alone does not do that - it says how sure something
    /// is, not what kind of thing it is.
    /// </remarks>
    private static void ProvenanceNote(Panel panel, Ghozt.Theming.Provenance provenance, string note)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 6, 0, 0),
        };
        Themed(border, Border.BorderBrushProperty, provenance.TokenName());

        var stack = new StackPanel();

        var heading = new TextBlock
        {
            Text = provenance.Label().ToUpperInvariant(),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
        };
        Themed(heading, TextBlock.ForegroundProperty, provenance.TokenName());
        stack.Children.Add(heading);

        var caption = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        Themed(caption, TextBlock.ForegroundProperty, "Text.Secondary");
        stack.Children.Add(caption);

        border.Child = stack;
        panel.Children.Add(border);
    }

    private static void Callout(Panel panel, string title, string body, string token)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2, 10, 10, 10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 10, 0, 0),
        };
        Themed(border, Border.BorderBrushProperty, token);

        var stack = new StackPanel();

        var heading = new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeights.SemiBold };
        Themed(heading, TextBlock.ForegroundProperty, token);
        stack.Children.Add(heading);

        var text = new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
        };
        Themed(text, TextBlock.ForegroundProperty, "Text.Secondary");
        stack.Children.Add(text);

        border.Child = stack;
        panel.Children.Add(border);
    }
}
