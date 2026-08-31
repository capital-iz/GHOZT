using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class TargetDialog : Window
{
    public TargetDialog()
    {
        InitializeComponent();

        EnvironmentBox.ItemsSource = System.Enum.GetValues<TargetEnvironment>();
        EnvironmentBox.SelectedItem = TargetEnvironment.Local;

        AccessBox.ItemsSource = System.Enum.GetValues<AccessClass>();
        AccessBox.SelectedItem = AccessClass.ObserveOnly;

        KindBox.Text = "Desktop application";
        OwnerBox.Text = Environment.UserName;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            UpdatePolicyNote();
        };
    }

    /// <summary>The registered target, without an identifier. The caller allocates that.</summary>
    public Target? Result { get; private set; }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnEnvironmentChanged(object sender, SelectionChangedEventArgs e) => UpdatePolicyNote();

    private void OnAccessChanged(object sender, SelectionChangedEventArgs e) => UpdatePolicyNote();

    /// <summary>
    /// States what the chosen environment and access class will permit.
    /// </summary>
    /// <remarks>
    /// Shown at registration rather than at execution. A gated action should look gated before it is
    /// pressed; the same principle applies one level up, where the gate is being created.
    /// </remarks>
    private void UpdatePolicyNote()
    {
        if (!IsLoaded)
        {
            return;
        }

        var environment = (TargetEnvironment)(EnvironmentBox.SelectedItem ?? TargetEnvironment.Local);
        var access = (AccessClass)(AccessBox.SelectedItem ?? AccessClass.ObserveOnly);

        string token;
        string message;

        if (environment == TargetEnvironment.Production && access == AccessClass.Destructive)
        {
            token = "State.Critical";
            message = "Destructive tests against production are denied by policy. Ghozt will register "
                      + "the target but will refuse to execute destructive actions against it.";
        }
        else if (environment == TargetEnvironment.Production)
        {
            token = "State.Critical";
            message = "Production. A persistent banner will be shown, every action is recorded against "
                      + "your identity, and state-changing work needs an approver.";
        }
        else if (access == AccessClass.Destructive)
        {
            token = "State.Critical";
            message = "Destructive tests may delete or corrupt data in this environment. Each run "
                      + "requires typed confirmation and names the exact resources affected.";
        }
        else if (access == AccessClass.StateChanging)
        {
            token = "State.Warning";
            message = "State-changing tests may alter persistent data. Declare a reset or rollback "
                      + "hook so a run can be undone.";
        }
        else if (access == AccessClass.ObserveOnly)
        {
            token = "Ghozt.Live";
            message = "Observation only. Ghozt will read state and capture evidence, and will not "
                      + "issue any action that changes the target.";
        }
        else
        {
            token = "Ghozt.Telemetry";
            message = "Ghozt may drive the target through its ordinary surfaces. Nothing persistent "
                      + "is changed unless the access class is raised.";
        }

        PolicyText.Text = message;

        if (TryFindResource(token) is Brush brush)
        {
            PolicyNote.BorderBrush = brush;
            PolicyText.Foreground = brush;
        }
    }

    private void OnRegister(object sender, RoutedEventArgs e)
    {
        // Validation names the field and the correction, rather than reporting that something is
        // wrong somewhere.
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            Validation.Text = "A target needs a name — it is how findings and evidence will refer to it.";
            NameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(KindBox.Text))
        {
            Validation.Text = "Choose or type a kind, so the right adapters can be offered.";
            KindBox.Focus();
            return;
        }

        Result = new Target
        {
            // Allocated by the caller from the store's sequence; a placeholder here would be a second
            // source of identifiers.
            Id = GhoztId.Create(IdKind.Target, 1),
            Name = NameBox.Text.Trim(),
            Kind = KindBox.Text.Trim(),
            Environment = (TargetEnvironment)(EnvironmentBox.SelectedItem ?? TargetEnvironment.Local),
            Access = (AccessClass)(AccessBox.SelectedItem ?? AccessClass.ObserveOnly),
            Owner = OwnerBox.Text.Trim(),
            BaseAddress = AddressBox.Text.Trim(),
            BuildLabel = BuildBox.Text.Trim(),
            RepositoryPath = RepositoryBox.Text.Trim(),
            AuthorityNote = AuthorityBox.Text.Trim(),
            RegisteredAt = DateTimeOffset.Now,
            Reachable = false,
        };

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
