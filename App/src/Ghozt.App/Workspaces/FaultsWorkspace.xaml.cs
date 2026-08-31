using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class FaultsWorkspace : UserControl
{
    public FaultsWorkspace()
    {
        InitializeComponent();

        // Both hooks, deliberately. DataContextChanged fires before the first render, so the
        // workspace never paints an empty state over data it already has; Loaded covers the case
        // where the context was set before this control existed.
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
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

    private Finding? Selected => Queue.SelectedItem as Finding;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target) or nameof(ShellViewModel.Findings))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var target = Shell?.Target;
        var findings = target is null ? [] : App.Store.Findings(target.Id);

        Queue.ItemsSource = findings;
        EmptyPanel.Visibility = findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (findings.Count > 0)
        {
            return;
        }

        // "No faults found in tested scope" and "nothing has been tested" are different facts. An
        // operator who reads the second as the first concludes the build is clean.
        var sweeps = target is null ? [] : App.Store.Sweeps(target.Id);

        if (target is null)
        {
            EmptyTitle.Text = "No target selected";
            EmptyBody.Text = "Select a target in Targets to see the findings recorded against it.";
        }
        else if (sweeps.Count == 0)
        {
            EmptyTitle.Text = "Nothing has been tested";
            EmptyBody.Text = $"No sweep has run against {target.Name}. An empty fault queue here means "
                             + "absence of testing, not absence of faults. Run a sweep to establish coverage.";
        }
        else
        {
            var partial = sweeps.Count(s => s.State is RunState.Partial or RunState.Cancelled);
            EmptyTitle.Text = "No faults in tested scope";
            EmptyBody.Text = partial > 0
                ? $"{sweeps.Count} sweep(s) have run and found no deviations, but {partial} ended early. "
                  + "The scope those runs did not reach remains untested rather than proven."
                : $"{sweeps.Count} sweep(s) have run against {target.Name} with no deviations. "
                  + "Coverage is bounded by the checks in those sweep modes.";
        }
    }

    private void OnSelect(object sender, SelectionChangedEventArgs e)
    {
        var finding = Selected;

        Shell.InspectorSubject = finding;

        ReproButton.IsEnabled = finding is not null;
        AdvanceButton.IsEnabled = finding is not null && NextMaturity(finding) is not null;
        AssignButton.IsEnabled = finding is not null;
    }

    /// <summary>
    /// The maturity a finding may advance to next.
    /// </summary>
    /// <remarks>
    /// Returns null where advancing is not yet earned, which is what disables the control. A finding
    /// cannot reach Reproduced without a successful reproduction, and cannot reach Verified without
    /// evidence: those are the two places the lifecycle is load-bearing rather than decorative.
    /// </remarks>
    private static FindingMaturity? NextMaturity(Finding f) => f.Maturity switch
    {
        FindingMaturity.New => FindingMaturity.Confirming,
        FindingMaturity.Confirming => f.ReproSuccesses > 0 ? FindingMaturity.Reproduced : null,
        FindingMaturity.Reproduced => FindingMaturity.CauseSupported,
        FindingMaturity.CauseSupported => FindingMaturity.FixReady,
        FindingMaturity.FixReady => FindingMaturity.ReTest,
        FindingMaturity.ReTest => f.EvidenceCount > 0 ? FindingMaturity.Verified : null,
        _ => null,
    };

    /// <summary>
    /// Re-runs the check behind a finding and records whether it recurred.
    /// </summary>
    /// <remarks>
    /// A real re-check, not a simulated one. The outcome updates the attempt and success counters,
    /// from which reproducibility is computed - so a Phantom is identified by measurement rather
    /// than by an operator's impression.
    /// </remarks>
    private async void OnReproduce(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || Shell.Target is not { } target)
        {
            return;
        }

        ReproButton.IsEnabled = false;
        Shell.Log("RPR.START", $"Attempting reproduction of {finding.Id}.", Severity.Info,
            related: finding.Id.ToString());

        var sweep = new Sweep
        {
            Id = App.Store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = $"Reproduction of {finding.Id}",
            Mode = "Smoke",
            MaxStates = 20,
            MaxSeconds = 30,
            MaxDepth = 2,
        };

        App.Store.Save(sweep);

        var before = App.Store.Findings(target.Id).FirstOrDefault(f => f.Id == finding.Id);
        var engine = new SweepEngine(App.Store);

        await engine.RunAsync(sweep, target, null, CancellationToken.None).ConfigureAwait(true);

        // Whether it recurred is read from the record the engine wrote, not assumed from the fact
        // that a run happened.
        var after = App.Store.Findings(target.Id).FirstOrDefault(f => f.Id == finding.Id);
        var recurred = after is not null && before is not null && after.ReproSuccesses > before.ReproSuccesses;

        if (!recurred && after is not null)
        {
            // A failed attempt still counts. Without it, reproducibility would only ever rise, and a
            // Phantom would look like a solid fault.
            App.Store.Save(after with { ReproAttempts = after.ReproAttempts + 1 });
        }

        Shell.Log(recurred ? "RPR.RECURRED" : "RPR.ABSENT",
            recurred
                ? $"{finding.Id} reproduced under the same conditions."
                : $"{finding.Id} did not recur. It is now less reliably reproducible, not resolved.",
            recurred ? Severity.Medium : Severity.Info,
            related: finding.Id.ToString());

        Refresh();
        Shell.Refresh();
        ReproButton.IsEnabled = true;
    }

    private void OnAdvance(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding || NextMaturity(finding) is not { } next)
        {
            return;
        }

        App.Store.Save(finding with { Maturity = next, LastSeenAt = DateTimeOffset.Now });

        Shell.Log("FLT.MATURITY", $"{finding.Id} advanced from {finding.Maturity} to {next}.",
            Severity.Info, related: finding.Id.ToString(),
            detail: $"Advanced by {Environment.UserName}.");

        Refresh();
        Shell.Refresh();
    }

    private void OnAssign(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } finding)
        {
            return;
        }

        var owner = Environment.UserName;
        App.Store.Save(finding with { Owner = owner });

        Shell.Log("FLT.OWNER", $"{finding.Id} assigned to {owner}.", Severity.Info,
            related: finding.Id.ToString());

        Refresh();
    }
}
