using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Ghozt.Core;
using Ghozt.Theming;
using Microsoft.Win32;

namespace Ghozt.App.Theming;

/// <summary>
/// Publishes the active token table into the application's resources, so every surface re-themes at
/// once and no view needs to know a theme exists.
/// </summary>
/// <remarks>
/// <para>
/// Every token is published under three keys: the raw <see cref="Color"/> (for gradients and
/// animations), a <see cref="SolidColorBrush"/> under the plain token name, and the same brush
/// again. Views bind by <c>DynamicResource</c>, which is what makes a palette change take effect
/// live rather than at next launch.
/// </para>
/// <para>
/// The brushes are frozen. A frozen brush can be shared across threads and drawn without a lock,
/// and a resource dictionary of unfrozen brushes is a per-render allocation the State Lattice cannot
/// afford on the machine this product is sized for.
/// </para>
/// </remarks>
public sealed class ThemeManager : INotifyPropertyChanged
{
    private const string PaletteSetting = "Theme.Palette";
    private const string ModeSetting = "Theme.Mode";
    private const string OpacitySetting = "Theme.Opacity";
    private const string EffectsSetting = "Theme.Effects";
    private const string DensitySetting = "Theme.Density";
    private const string MotionSetting = "Theme.Motion";

    private readonly GhoztStore? _store;
    private readonly ResourceDictionary _dictionary = new();

    private string _paletteKey = GhoztCatalog.DefaultKey;
    private ThemeMode _mode = ThemeMode.Dark;
    private int _opacity = 82;
    private ThemeEffects _effects = ThemeEffects.Standard;
    private Density _density = Density.Standard;
    private MotionLevel _motion = MotionLevel.Full;

    public ThemeManager(GhoztStore? store = null)
    {
        _store = store;
        Load();
        Tokens = Build();
        Publish();
    }

    public static ThemeManager Current { get; private set; } = null!;

    public static void Initialise(ThemeManager manager) => Current = manager;

    /// <summary>The dictionary to merge into <c>Application.Resources</c>.</summary>
    public ResourceDictionary Dictionary => _dictionary;

    public GhoztTokens Tokens { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the table has been rebuilt and republished.</summary>
    public event EventHandler? ThemeChanged;

    public string PaletteKey
    {
        get => _paletteKey;
        set => Set(ref _paletteKey, GhoztCatalog.Exists(value) ? value : GhoztCatalog.DefaultKey, PaletteSetting, value);
    }

    public ThemeMode Mode
    {
        get => _mode;
        set => Set(ref _mode, value, ModeSetting, value.ToString());
    }

    public int SurfaceOpacity
    {
        get => _opacity;
        set => Set(ref _opacity, Math.Clamp(value, 35, 100), OpacitySetting, Math.Clamp(value, 35, 100).ToString());
    }

    public ThemeEffects Effects
    {
        get => _effects;
        set => Set(ref _effects, value.Normalised(), EffectsSetting, value.Normalised().Encode());
    }

    public Density Density
    {
        get => _density;
        set => Set(ref _density, value, DensitySetting, value.ToString());
    }

    public MotionLevel Motion
    {
        get => _motion;
        set => Set(ref _motion, value, MotionSetting, value.ToString());
    }

    public GhoztPalette Palette => GhoztCatalog.Find(_paletteKey);

    /// <summary>Whether the resolved mode is dark, after System has been asked of the OS.</summary>
    public bool IsDark => _mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => SystemPrefersDark(),
    };

    /// <summary>
    /// Whether Windows is set to a dark app theme.
    /// </summary>
    /// <remarks>
    /// Read from the registry rather than from a WinRT API so the app has no package dependency, and
    /// defaulting to dark when the value is missing: Ghozt's own default identity is dark, and an
    /// unreadable preference should land on the product's own default rather than on white.
    /// </remarks>
    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Applies a palette together with the finish it was designed to be seen in.</summary>
    public void ApplyPaletteWithItsDefaults(string key)
    {
        var palette = GhoztCatalog.Find(key);
        _paletteKey = palette.Key;
        _opacity = palette.DefaultOpacity;
        _effects = palette.DefaultEffects.Normalised();

        Store(PaletteSetting, _paletteKey);
        Store(OpacitySetting, _opacity.ToString());
        Store(EffectsSetting, _effects.Encode());

        Rebuild();
        OnPropertyChanged(nameof(PaletteKey));
        OnPropertyChanged(nameof(SurfaceOpacity));
        OnPropertyChanged(nameof(Effects));
        OnPropertyChanged(nameof(Palette));
    }

    private void Set<T>(ref T field, T value, string settingKey, string settingValue, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Store(settingKey, settingValue);
        Rebuild();
        OnPropertyChanged(name);
    }

    private void Store(string key, string value)
    {
        try
        {
            _store?.SetSetting(key, value);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            // A preference that cannot be written is a nuisance, not a reason to refuse the change.
            // The look still applies for this session.
            System.Diagnostics.Debug.WriteLine($"Ghozt theme: could not persist {key}: {ex.Message}");
        }
    }

    private void Load()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            _paletteKey = _store.GetSetting(PaletteSetting) is { } p && GhoztCatalog.Exists(p)
                ? p
                : GhoztCatalog.DefaultKey;

            _mode = System.Enum.TryParse<ThemeMode>(_store.GetSetting(ModeSetting), out var m) ? m : ThemeMode.Dark;
            _opacity = int.TryParse(_store.GetSetting(OpacitySetting), out var o) ? Math.Clamp(o, 35, 100) : GhoztCatalog.Find(_paletteKey).DefaultOpacity;
            _effects = _store.GetSetting(EffectsSetting) is { } e ? ThemeEffects.Decode(e) : GhoztCatalog.Find(_paletteKey).DefaultEffects;
            _density = System.Enum.TryParse<Density>(_store.GetSetting(DensitySetting), out var d) ? d : Density.Standard;
            _motion = System.Enum.TryParse<MotionLevel>(_store.GetSetting(MotionSetting), out var mo) ? mo : MotionLevel.Full;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Ghozt theme: could not read preferences: {ex.Message}");
        }

        // The operating system's own reduced-motion setting overrides a stored preference for Full.
        // A person who has asked Windows to stop animating should not have to ask again here.
        if (!SystemParameters.ClientAreaAnimation && _motion == MotionLevel.Full)
        {
            _motion = MotionLevel.Reduced;
        }
    }

    private GhoztTokens Build() => new(_paletteKey, IsDark, _opacity, _effects, _density, _motion);

    public void Rebuild()
    {
        var next = Build();
        if (next.Equals(Tokens))
        {
            return;
        }

        Tokens = next;
        Publish();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Tokens));
        OnPropertyChanged(nameof(IsDark));
    }

    private void Publish()
    {
        foreach (var (name, rgba) in Tokens.Raw)
        {
            var colour = Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);

            _dictionary[name + ".Color"] = colour;

            if (_dictionary[name] is SolidColorBrush existing && !existing.IsFrozen)
            {
                existing.Color = colour;
            }
            else
            {
                var brush = new SolidColorBrush(colour);
                brush.Freeze();
                _dictionary[name] = brush;
            }
        }

        PublishMetrics();
    }

    /// <summary>
    /// The non-colour half of the theme: the spacing that density moves, and the durations that the
    /// motion setting collapses.
    /// </summary>
    /// <remarks>
    /// Published as resources for the same reason the colours are: a view that hard-codes 16 pixels
    /// or 140 milliseconds cannot respond to a density or motion preference, and the preference then
    /// exists in the settings screen without existing in the product.
    /// </remarks>
    private void PublishMetrics()
    {
        foreach (var step in new[] { 2, 4, 8, 12, 16, 24, 32, 48 })
        {
            _dictionary[$"Space.{step}"] = Tokens.Space(step);
        }

        _dictionary["Space.Card"] = new Thickness(Tokens.Space(16));
        _dictionary["Space.CardTight"] = new Thickness(Tokens.Space(12));
        _dictionary["Space.Panel"] = new Thickness(Tokens.Space(16), Tokens.Space(12), Tokens.Space(16), Tokens.Space(12));
        _dictionary["Space.Row"] = new Thickness(Tokens.Space(12), Tokens.Space(8), Tokens.Space(12), Tokens.Space(8));

        // Row height carries density most visibly. A compact operator sees more of the working set;
        // a comfortable one sees the same information with more air. Neither loses a column.
        _dictionary["Metric.RowHeight"] = _density switch
        {
            Density.Compact => 28.0,
            Density.Comfortable => 42.0,
            _ => 34.0,
        };

        _dictionary["Metric.ControlHeight"] = _density switch
        {
            Density.Compact => 28.0,
            Density.Comfortable => 38.0,
            _ => 32.0,
        };

        // Motion durations. Off collapses every one to zero; the end state is identical, because
        // motion is never the only channel carrying a state change.
        double Scale(double milliseconds) => _motion switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => Math.Min(milliseconds, 90) * 0.5,
            _ => milliseconds,
        };

        _dictionary["Motion.Selection"] = TimeSpan.FromMilliseconds(Scale(120));
        _dictionary["Motion.Panel"] = TimeSpan.FromMilliseconds(Scale(180));
        _dictionary["Motion.Materialise"] = TimeSpan.FromMilliseconds(Scale(240));
        _dictionary["Motion.Pulse"] = TimeSpan.FromMilliseconds(Scale(360));
        _dictionary["Motion.Trail"] = TimeSpan.FromMilliseconds(Scale(900));
        _dictionary["Motion.Enabled"] = _motion != MotionLevel.Off;

        // The glass blur, as a radius the effect can bind. Zero means the caller drops the effect
        // entirely rather than applying a zero-radius blur, which is a full-surface composite that
        // changes nothing.
        _dictionary["Finish.BlurRadius"] = Tokens.GlassBlurSigma;
        _dictionary["Finish.GlassEnabled"] = Tokens.Effects.Glass;
        _dictionary["Finish.GlowEnabled"] = Tokens.GlowEnabled;
        _dictionary["Finish.GradientEnabled"] = Tokens.Effects.Gradient;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
