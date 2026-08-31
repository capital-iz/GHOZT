using System.Windows;

// Tells WPF that this assembly carries its own Themes/Generic.xaml.
//
// Without it, a control that overrides DefaultStyleKey - StatusPlate and RailButton both do - gets
// no ControlTemplate at all and renders as nothing. The failure is silent: the control is present in
// the visual tree, occupies no space, and throws no error. That is exactly how the restricted status
// strip and the whole operations rail came to be invisible while every test still passed.
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]
