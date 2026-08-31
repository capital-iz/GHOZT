using System.Windows;
using System.Windows.Controls;

namespace Ghozt.App.Workspaces;

public partial class DestructiveGate : Window
{
    private readonly string _typeToConfirm;

    public DestructiveGate(string title, string effect, string typeToConfirm, string confirmLabel = "Remove permanently")
    {
        InitializeComponent();

        _typeToConfirm = typeToConfirm;

        TitleText.Text = title;
        EffectText.Text = effect;
        PromptLabel.Text = $"TYPE “{typeToConfirm}” TO CONFIRM";
        ConfirmButton.Content = confirmLabel;

        Loaded += (_, _) => Confirmation.Focus();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    private void OnTyped(object sender, TextChangedEventArgs e)
    {
        var matches = string.Equals(Confirmation.Text.Trim(), _typeToConfirm, StringComparison.Ordinal);
        ConfirmButton.IsEnabled = matches;

        // The mismatch note appears only once something has been typed. Showing it against an empty
        // box would read as an error before the operator has done anything.
        Mismatch.Text = Confirmation.Text.Length > 0 && !matches
            ? "That does not match. The name must be typed exactly, including case."
            : "";
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
