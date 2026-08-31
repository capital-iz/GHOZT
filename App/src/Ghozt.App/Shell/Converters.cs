using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Ghozt.App.Theming;
using Ghozt.Core;
using Ghozt.Theming;

namespace Ghozt.App.Shell;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>
/// Resolves a token name to its live brush.
/// </summary>
/// <remarks>
/// Needed wherever the token is chosen by data rather than by markup - a severity badge, an
/// environment band, a lattice node. The alternative is a trigger per state per control, which is
/// how a status colour ends up defined in nine places and wrong in one of them.
/// </remarks>
public sealed class TokenBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var token = value as string ?? parameter as string;
        if (string.IsNullOrWhiteSpace(token))
        {
            return Brushes.Transparent;
        }

        return Application.Current?.TryFindResource(token) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a severity to its token, so a badge never picks its own colour.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var token = value switch
        {
            Severity.Critical => "State.Critical",
            Severity.High => "State.Fault",
            Severity.Medium => "State.Warning",
            Severity.Low => "Ghozt.Telemetry",
            _ => "Text.Muted",
        };

        return Application.Current?.TryFindResource(token) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Reproducibility as a percentage, or an explicit absence.
/// </summary>
/// <remarks>
/// "Not attempted" and "never reproduces" are different claims and must not look alike on a triage
/// queue: one is an absence of evidence, the other is evidence.
/// </remarks>
public sealed class ReproducibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d
            ? string.Format(culture, "{0:P0}", d)
            : "not attempted";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a timestamp with millisecond precision, as the black-box stream requires.</summary>
public sealed class StreamTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset at ? at.ToString("HH:mm:ss.fff", culture) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when a string has content. Drives empty states without a code-behind flag.</summary>
public sealed class HasTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible when a count is zero, for empty-state overlays.
/// </summary>
/// <remarks>
/// Returns <see cref="Visibility"/> rather than a bool. Bound to a Visibility property, a bool is
/// coerced to Visible whatever its value, so an empty-state panel sits on top of a populated list -
/// which is what it did, over fifteen live events.
/// </remarks>
public sealed class IsEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
