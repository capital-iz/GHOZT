using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;

namespace Ghozt.App.Workspaces;

/// <summary>
/// A workspace whose interface is specified but not yet implemented.
/// </summary>
/// <remarks>
/// It says so plainly and lists what it will contain, rather than showing an empty dashboard or,
/// worse, mock data. The blueprint is explicit: no mock, placeholder or simulated success may be
/// represented as live product behaviour, and no decorative node, metric or terminal line may
/// pretend to be real system data. A screen that admits it is not built yet is honest; a screen
/// full of invented findings is a lie the operator may act on.
/// </remarks>
public sealed class PlannedWorkspace : UserControl
{
    public PlannedWorkspace(WorkspaceDefinition definition)
    {
        var scroll = new ScrollViewer { Padding = new Thickness(20, 18, 20, 18) };
        var stack = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };

        var badge = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2, 10, 10, 10),
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "State.WarningSoft");
        badge.SetResourceReference(Border.BorderBrushProperty, "State.Warning");

        var badgeText = new TextBlock
        {
            Text = "SPECIFIED · NOT YET IMPLEMENTED",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
        };
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "State.Warning");
        badge.Child = badgeText;
        stack.Children.Add(badge);

        stack.Children.Add(new TextBlock
        {
            Text = definition.Purpose,
            Margin = new Thickness(0, 16, 0, 0),
            Style = (Style?)Application.Current.TryFindResource("Type.BodyStyle"),
        });

        stack.Children.Add(new TextBlock
        {
            Text = "This workspace is defined in the blueprint and its shell region, navigation, "
                   + "shortcut and inspector behaviour already work. Its canvas is not built, and "
                   + "Ghozt will not show invented findings, metrics or events in place of one.",
            Margin = new Thickness(0, 8, 0, 0),
            Style = (Style?)Application.Current.TryFindResource("Type.CaptionStyle"),
        });

        stack.Children.Add(new TextBlock
        {
            Text = "WHAT IT WILL CONTAIN",
            Margin = new Thickness(0, 22, 0, 8),
            Style = (Style?)Application.Current.TryFindResource("Type.MicroLabelStyle"),
        });

        foreach (var item in Contents(definition.Kind))
        {
            var row = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 6),
            };
            row.SetResourceReference(Border.BackgroundProperty, "Surface.Card");
            row.SetResourceReference(Border.BorderBrushProperty, "Border.Card");

            var rowText = new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            rowText.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            row.Child = rowText;
            stack.Children.Add(row);
        }

        scroll.Content = stack;
        Content = scroll;
    }

    private static IReadOnlyList<string> Contents(WorkspaceKind kind) => kind switch
    {
        WorkspaceKind.Replay =>
        [
            "Synchronised panes for frames, UI tree, actions, network, logs, state, resources and agent decisions.",
            "Timeline scrubbing with event snapping and causal markers.",
            "Compare a successful run against a failing one, with the divergence point marked.",
            "A reproduction minimiser that removes steps while retaining the failure.",
            "A counterfactual editor that changes one factor and labels the branch as simulated.",
        ],
        WorkspaceKind.Algorithms =>
        [
            "Harness definition for a function, service, model, pipeline or data transformation.",
            "Input generation: fixtures, real samples, boundary values, property inputs and parameter grids.",
            "Expected-output definition by exact oracle, tolerance, invariant, metamorphic relation or reference model.",
            "Determinism controls: seeds, versions, hardware and dependency capture.",
            "Run comparison with distribution visualisation and evidence-backed recommendations.",
        ],
        WorkspaceKind.Regressions =>
        [
            "Suites derived from feature contracts and confirmed faults.",
            "Ownership, freshness, flake rate and affected builds.",
            "Mutation resilience, so a guard that cannot fail is identified as one.",
            "Required gates, linked to the clearance policy that depends on them.",
        ],
        WorkspaceKind.Reports =>
        [
            "Dossier builder with governed sections and an answer-first narrative.",
            "Finding blocks that carry narrative, impact, reproduction, cause status and evidence.",
            "Provenance on every section, with a review warning when a source finding changes.",
            "Export to Word-compatible DOCX, PDF, HTML and structured JSON.",
            "An export validator that refuses missing evidence, broken references and unsupported claims.",
        ],
        WorkspaceKind.Clearance =>
        [
            "A gate matrix by product, feature, risk, platform, environment and release class.",
            "Each gate showing its requirement, result, evidence, owner and policy source.",
            "Outcomes: Cleared, Conditionally Cleared, Denied, Blocked or Expired.",
            "A waiver workflow with reason, compensating control, approver and expiry.",
            "A release certificate carrying build identity, evidence digest and residual risk.",
        ],
        _ => ["This workspace is specified in the blueprint."],
    };
}
