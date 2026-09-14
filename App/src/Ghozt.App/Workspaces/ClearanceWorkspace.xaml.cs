using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class ClearanceWorkspace : UserControl
{
    private ClearanceDecision? _currentDecision;

    public ClearanceWorkspace()
    {
        InitializeComponent();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnEvaluate(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            ClearanceSummaryText.Text = "No target selected";
            return;
        }

        var engine = new ClearanceEngine(App.Store);
        _currentDecision = engine.EvaluateTarget(target, "operator");

        RenderDecision(_currentDecision);
        SignButton.IsEnabled = true;
        ExportCertButton.IsEnabled = true;
    }

    private void RenderDecision(ClearanceDecision decision)
    {
        ClearanceContainer.Children.Clear();
        var isCleared = decision.Outcome == ClearanceOutcome.Cleared;
        ClearanceSummaryText.Text = $"Verdict: {decision.Outcome} · {decision.Gates.Count(g => g.Passed)}/{decision.Gates.Count} gates passed";

        // Verdict Card
        var card = new Border
        {
            Background = (Brush)TryFindResource("Surface.Card"),
            BorderBrush = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("Border.Card")),
            BorderThickness = new Thickness(isCleared ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var stack = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Release Clearance Verdict",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)TryFindResource("Text.Primary"),
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"Target: {decision.TargetId} · Build: {decision.BuildLabel} · Evaluated: {decision.DecidedAt:yyyy-MM-dd HH:mm}",
            Style = (Style)TryFindResource("Type.CaptionStyle"),
            Foreground = (Brush)TryFindResource("Text.Muted"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var badge = new Border
        {
            Background = (Brush)(isCleared ? TryFindResource("Authority.Soft") : TryFindResource("State.FaultSoft")),
            BorderBrush = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 6, 14, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock
        {
            Text = decision.Outcome.ToString().ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
        };
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);

        stack.Children.Add(header);

        card.Child = stack;
        ClearanceContainer.Children.Add(card);

        // Gates Table
        foreach (var gate in decision.Gates)
        {
            var gateCard = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(gate.Passed ? TryFindResource("State.Verified") : (gate.Mandatory ? TryFindResource("State.Fault") : TryFindResource("State.Warning"))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var gateStack = new StackPanel();

            var gHeader = new Grid();
            gHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var gTitle = new StackPanel { Orientation = Orientation.Horizontal };
            gTitle.Children.Add(new TextBlock
            {
                Text = gate.Id,
                Style = (Style)TryFindResource("Type.MonoStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            gTitle.Children.Add(new TextBlock
            {
                Text = gate.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (gate.Mandatory)
            {
                gTitle.Children.Add(new TextBlock
                {
                    Text = "MANDATORY",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)TryFindResource("Authority.Clearance"),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            Grid.SetColumn(gTitle, 0);
            gHeader.Children.Add(gTitle);

            var gBadge = new Border
            {
                Background = (Brush)(gate.Passed ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                BorderBrush = (Brush)(gate.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            gBadge.Child = new TextBlock
            {
                Text = gate.Result.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(gate.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(gBadge, 1);
            gHeader.Children.Add(gBadge);

            gateStack.Children.Add(gHeader);

            gateStack.Children.Add(new TextBlock
            {
                Text = gate.Requirement,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 6, 0, 4),
            });

            gateStack.Children.Add(new TextBlock
            {
                Text = $"Note: {gate.EvidenceNote} · Source: {gate.PolicySource}",
                FontSize = 11,
                Foreground = (Brush)TryFindResource("Text.Muted"),
            });

            gateCard.Child = gateStack;
            ClearanceContainer.Children.Add(gateCard);
        }
    }

    private void OnSignDecision(object sender, RoutedEventArgs e)
    {
        if (_currentDecision is null || Shell?.Target is null)
        {
            return;
        }

        var target = Shell.Target;
        var payload = $"clearance=Decision\ntarget={target.Id}\noutcome={_currentDecision.Outcome}\n"
                      + $"at={_currentDecision.DecidedAt:O}\nby={_currentDecision.DecidedBy}\n"
                      + $"digest={_currentDecision.EvidenceDigest}\n"
                      + string.Join('\n', _currentDecision.Gates.Select(g => $"{g.Id}: {g.Result}"));

        App.Store.Save(new Evidence
        {
            Id = App.Store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "clearance:certificate",
            Summary = $"Release clearance decision: {_currentDecision.Outcome} ({_currentDecision.Gates.Count(g => g.Passed)}/{_currentDecision.Gates.Count} gates passed)",
            Sha256 = Hashing.Sha256(payload),
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            Payload = payload,
            SourceAdapter = "ClearanceEngine",
            Sealed = true,
            SealedBy = "release-approver@capital-iz.com",
            CapturedAt = DateTimeOffset.Now,
        });

        App.Store.Append(new LedgerEvent
        {
            Id = App.Store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "CLR.SIGN",
            Message = $"Clearance decision signed: {_currentDecision.Outcome}",
            Severity = _currentDecision.Outcome == ClearanceOutcome.Cleared ? Severity.Info : Severity.Medium,
            TargetId = target.Id,
            Actor = "ClearanceEngine",
            Detail = payload,
        });

        SignButton.IsEnabled = false;
        SignButton.Content = "Signed & Sealed";
        ExportCertButton.IsEnabled = true;
    }

    private void OnExportCertificate(object sender, RoutedEventArgs e)
    {
        if (_currentDecision is null || Shell?.Target is null) return;

        var target = Shell.Target;
        try
        {
            var dir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz", "Ghozt", "Certificates");
            System.IO.Directory.CreateDirectory(dir);

            var certPath = System.IO.Path.Combine(dir, $"CERT-{target.Id}-{_currentDecision.BuildLabel}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(_currentDecision, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(certPath, json);

            ClearanceSummaryText.Text = $"Certificate exported to {certPath}";
            Shell.Log("CLR.CERT.EXPORT", $"Exported release clearance certificate to {certPath}.", Severity.Info,
                related: target.Id.ToString(), detail: $"Verdict: {_currentDecision.Outcome}");
        }
        catch (System.Exception ex)
        {
            ClearanceSummaryText.Text = $"Export error: {ex.Message}";
            Shell.Log("CLR.CERT.FAIL", $"Failed to export certificate: {ex.Message}", Severity.Medium);
        }
    }
}
