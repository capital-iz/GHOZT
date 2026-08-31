using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ghozt.App.Shell;

/// <summary>
/// One reading on the restricted status strip: a micro-label above a value.
/// </summary>
/// <remarks>
/// Built as a control rather than repeated markup because the strip carries eight of these and they
/// must be identical. The label is never dropped, even where the value looks self-explanatory: a
/// strip whose labels appear only on the ambiguous readings tells the operator nothing about the
/// rest.
/// </remarks>
public sealed class StatusPlate : Control
{
    static StatusPlate()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusPlate),
            new FrameworkPropertyMetadata(typeof(StatusPlate)));
    }

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusPlate),
            new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatusPlate),
            new PropertyMetadata(""));

    /// <summary>The token the value is painted with. Defaults to ordinary body text.</summary>
    public static readonly DependencyProperty TokenProperty =
        DependencyProperty.Register(nameof(Token), typeof(string), typeof(StatusPlate),
            new PropertyMetadata("Text.Body", OnTokenChanged));

    /// <summary>Monospaced, for identifiers and build labels where alignment carries meaning.</summary>
    public static readonly DependencyProperty MonoProperty =
        DependencyProperty.Register(nameof(Mono), typeof(bool), typeof(StatusPlate),
            new PropertyMetadata(false));

    /// <summary>Boxed and upper-cased, for the readings that gate what may be done.</summary>
    public static readonly DependencyProperty StrongProperty =
        DependencyProperty.Register(nameof(Strong), typeof(bool), typeof(StatusPlate),
            new PropertyMetadata(false));

    internal static readonly DependencyPropertyKey ValueBrushKey =
        DependencyProperty.RegisterReadOnly(nameof(ValueBrush), typeof(Brush), typeof(StatusPlate),
            new PropertyMetadata(Brushes.Gray));

    public static readonly DependencyProperty ValueBrushProperty = ValueBrushKey.DependencyProperty;

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Token
    {
        get => (string)GetValue(TokenProperty);
        set => SetValue(TokenProperty, value);
    }

    public bool Mono
    {
        get => (bool)GetValue(MonoProperty);
        set => SetValue(MonoProperty, value);
    }

    public bool Strong
    {
        get => (bool)GetValue(StrongProperty);
        set => SetValue(StrongProperty, value);
    }

    public Brush ValueBrush => (Brush)GetValue(ValueBrushProperty);

    private static void OnTokenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusPlate plate)
        {
            plate.ResolveBrush();
        }
    }

    public StatusPlate()
    {
        Loaded += (_, _) => ResolveBrush();

        // Re-resolve on a palette change: the plate holds a brush rather than a DynamicResource
        // reference because the token itself is data, and a data-chosen token cannot be expressed as
        // a static resource key in markup.
        if (Theming.ThemeManager.Current is { } manager)
        {
            manager.ThemeChanged += (_, _) => ResolveBrush();
        }
    }

    private void ResolveBrush()
    {
        var brush = TryFindResource(Token) as Brush
                    ?? Application.Current?.TryFindResource(Token) as Brush
                    ?? Brushes.Gray;

        SetValue(ValueBrushKey, brush);
    }
}
