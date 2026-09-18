using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class ThemeService : IDisposable
{
    private readonly ThemePackService _packs;
    private readonly object _reloadGate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private ResourceDictionary? _resources;
    private ThemePack? _currentTheme;
    private Action<string>? _reportDiagnostic;
    private Action<ThemePack>? _reloaded;
    private bool _disposed;

    public ThemeService(ThemePackService? packs = null) => _packs = packs ?? new ThemePackService();

    public string CurrentThemeId { get; private set; } = "standard";
    public bool HasActiveWatcher => _watcher is not null;

    public void Apply(ResourceDictionary applicationResources, ThemePack theme,
        Action<string>? reportDiagnostic = null, Action<ThemePack>? reloaded = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopWatcher();
        ApplyDictionary(applicationResources, theme);
        _resources = applicationResources;
        _currentTheme = theme;
        _reportDiagnostic = reportDiagnostic;
        _reloaded = reloaded;
        CurrentThemeId = theme.Id;
        if (!theme.IsBuiltIn && theme.SourceDirectory is not null) StartWatcher(theme.SourceDirectory);
    }

    private void ApplyDictionary(ResourceDictionary resources, ThemePack theme)
    {
        var fallback = ThemePackService.CreateBuiltIns().First(item => item.Id == "moonlight");
        string C(string name) => theme.Colors.TryGetValue(name, out var value) ? value : fallback.Colors[name];
        var isLight = RelativeLuminance(ColorValue(C("AppBackground"))) > .55;
        var dictionary = new ResourceDictionary { ["MoonriseThemeRuntime"] = true, ["ThemeId"] = theme.Id };

        dictionary["BackgroundPrimaryColor"] = ColorValue(C("AppBackground"));
        dictionary["BackgroundSecondaryColor"] = ColorValue(C("SidebarBackground"));
        dictionary["SurfacePrimaryColor"] = ColorValue(C("SurfacePrimary"));
        dictionary["SurfaceRaisedColor"] = ColorValue(C("SurfaceSecondary"));
        dictionary["SurfaceFloatingColor"] = ColorValue(C("SurfaceRaised"));
        dictionary["SurfaceHoverColor"] = ColorValue(C("SurfaceHover"));
        dictionary["SurfaceSelectedColor"] = ColorValue(C("SurfaceSelected"));
        dictionary["SurfaceDisabledColor"] = ColorValue(C("SurfaceSecondary"));
        dictionary["BorderPrimaryColor"] = ColorValue(C("BorderPrimary"));
        dictionary["BorderSubtleColor"] = ColorValue(C("BorderSubtle"));
        dictionary["BorderStrongColor"] = ColorValue(C("BorderStrong"));
        dictionary["FocusColor"] = ColorValue(C("FocusRing"));
        dictionary["TextPrimaryColor"] = ColorValue(C("TextPrimary"));
        dictionary["TextSecondaryColor"] = ColorValue(C("TextSecondary"));
        dictionary["TextMutedColor"] = ColorValue(C("TextMuted"));
        dictionary["TextDisabledColor"] = ColorValue(C("TextDisabled"));
        dictionary["AccentPurpleColor"] = ColorValue(C("AccentPrimary"));
        dictionary["AccentVioletColor"] = ColorValue(C("AccentSecondary"));
        dictionary["AccentBlueColor"] = ColorValue(C("AccentSecondary"));
        dictionary["AccentCyanColor"] = ColorValue(C("Info"));
        dictionary["SuccessColor"] = ColorValue(C("Success"));
        dictionary["WarningColor"] = ColorValue(C("Warning"));
        dictionary["DangerColor"] = ColorValue(C("Danger"));

        AddBrush(dictionary, "AppBackground", C("AppBackground"));
        AddBrush(dictionary, "BackgroundPrimary", C("AppBackground"));
        AddBrush(dictionary, "BackgroundSecondary", C("SidebarBackground"));
        AddBrush(dictionary, "SidebarBackground", C("SidebarBackground"));
        AddBrush(dictionary, "SurfacePrimary", C("SurfacePrimary"), theme.Effects.SurfaceOpacity);
        AddBrush(dictionary, "SurfaceSecondary", C("SurfaceSecondary"), theme.Effects.SurfaceOpacity);
        AddBrush(dictionary, "SurfaceRaised", C("SurfaceRaised"), theme.Effects.SurfaceOpacity);
        AddBrush(dictionary, "SurfaceFloating", C("SurfaceRaised"));
        AddBrush(dictionary, "SurfaceElevated", C("SurfaceRaised"));
        AddBrush(dictionary, "SurfaceHover", C("SurfaceHover"));
        AddBrush(dictionary, "SurfaceSelected", C("SurfaceSelected"));
        AddBrush(dictionary, "SurfaceDisabled", C("SurfaceSecondary"), .7);
        AddBrush(dictionary, "BorderPrimary", C("BorderPrimary"));
        AddBrush(dictionary, "BorderSubtle", C("BorderSubtle"));
        AddBrush(dictionary, "BorderStrong", C("BorderStrong"));
        AddBrush(dictionary, "BorderActive", C("FocusRing"));
        AddBrush(dictionary, "FocusRing", C("FocusRing"));
        AddBrush(dictionary, "TextPrimary", C("TextPrimary"));
        AddBrush(dictionary, "TextSecondary", C("TextSecondary"));
        AddBrush(dictionary, "TextMuted", C("TextMuted"));
        AddBrush(dictionary, "TextDisabled", C("TextDisabled"));
        AddBrush(dictionary, "AccentPurple", C("AccentPrimary"));
        AddBrush(dictionary, "AccentViolet", C("AccentSecondary"));
        AddBrush(dictionary, "AccentBlue", C("AccentSecondary"));
        AddBrush(dictionary, "AccentCyan", C("Info"));
        AddBrush(dictionary, "AccentSoft", C("AccentSoft"));
        AddBrush(dictionary, "Success", C("Success"));
        AddBrush(dictionary, "Warning", C("Warning"));
        AddBrush(dictionary, "Danger", C("Danger"));
        AddBrush(dictionary, "Info", C("Info"));
        AddBrush(dictionary, "Selection", C("Selection"));
        AddBrush(dictionary, "ScrollTrack", C("ScrollbarTrack"));
        AddBrush(dictionary, "ScrollThumb", C("ScrollbarThumb"));
        AddBrush(dictionary, "ScrollThumbHover", C("BorderStrong"));
        AddBrush(dictionary, "ScrollThumbPressed", C("TextMuted"));
        AddBrush(dictionary, "Panel", C("SurfacePrimary"));
        AddBrush(dictionary, "Panel2", C("SurfaceSecondary"));
        AddBrush(dictionary, "Border", C("BorderSubtle"));
        AddBrush(dictionary, "Muted", C("TextMuted"));
        AddBrush(dictionary, "Accent", C("AccentPrimary"));
        AddBrush(dictionary, "ChromeSurface", C("SidebarBackground"), theme.Variants.Sidebar == "glass" ? .78 : 1);
        AddBrush(dictionary, "HeaderStatusSurface", C("SurfaceSecondary"), .85);
        AddBrush(dictionary, "DrawerSurface", C("SurfaceRaised"), .98);
        AddBrush(dictionary, "OverlaySurface", C("AppBackground"), .86);
        AddBrush(dictionary, "DropOverlaySurface", C("AppBackground"), .92);

        Brush primary = theme.Variants.Buttons.ToLowerInvariant() switch
        {
            "gradient" => Gradient(C("AccentPrimary"), C("AccentSecondary")),
            "soft" => Brush(C("AccentSoft")),
            "outline" => Brush(C("SurfacePrimary")),
            _ => Brush(C("AccentPrimary"))
        };
        dictionary["PrimaryGradient"] = primary;
        dictionary["PrimaryGradientHover"] = theme.Variants.Buttons.ToLowerInvariant() switch
        {
            "gradient" => Gradient(C("AccentSecondary"), C("AccentPrimary")),
            "soft" => Brush(C("SurfaceSelected")),
            "outline" => Brush(C("AccentSoft")),
            _ => Brush(C("AccentSecondary"))
        };
        dictionary["PrimaryGradientPressed"] = Brush(C("AccentSecondary"));
        dictionary["PrimaryButtonBorder"] = Brush(theme.Variants.Buttons.Equals("outline", StringComparison.OrdinalIgnoreCase) ? C("AccentPrimary") : "#00000000");
        dictionary["PrimaryButtonBorderThickness"] = new Thickness(theme.Variants.Buttons.Equals("outline", StringComparison.OrdinalIgnoreCase) ? theme.Geometry.BorderThickness : 0);
        dictionary["PrimaryButtonForeground"] = Brush(ContrastOn(C("AccentPrimary")));
        dictionary["ButtonBackground"] = Brush(C("SurfaceRaised"));
        dictionary["ButtonHoverBackground"] = Brush(C("SurfaceHover"));
        dictionary["ButtonBorder"] = Brush(C("BorderPrimary"));
        dictionary["ButtonBorderThickness"] = new Thickness(isLight || theme.Variants.Buttons.Equals("outline", StringComparison.OrdinalIgnoreCase) ? theme.Geometry.BorderThickness : 0);
        dictionary["ActiveNavigationGradient"] = theme.Variants.Navigation.Equals("left-accent", StringComparison.OrdinalIgnoreCase)
            ? Brush(C("SurfaceHover")) : Gradient(C("AccentSoft"), C("SurfaceSelected"));
        dictionary["NavigationAccentOpacity"] = theme.Variants.Navigation.Equals("soft-block", StringComparison.OrdinalIgnoreCase) ? 0d : 1d;
        dictionary["SidebarGradient"] = theme.Variants.Sidebar.Equals("flat", StringComparison.OrdinalIgnoreCase)
            ? Brush(C("SidebarBackground")) : Gradient(C("SidebarBackground"), C("SurfaceSecondary"));
        dictionary["InputBackground"] = Brush(theme.Variants.Inputs.Equals("filled", StringComparison.OrdinalIgnoreCase) ? C("SurfaceSecondary") : C("SurfacePrimary"));
        dictionary["InputBorderThickness"] = new Thickness(theme.Variants.Inputs.Equals("outlined", StringComparison.OrdinalIgnoreCase) ? theme.Geometry.BorderThickness : 0);
        dictionary["CardBackground"] = Brush(theme.Variants.Cards.Equals("glass", StringComparison.OrdinalIgnoreCase) ? C("SurfaceRaised") : C("SurfacePrimary"), theme.Variants.Cards.Equals("glass", StringComparison.OrdinalIgnoreCase) ? .76 : theme.Effects.SurfaceOpacity);
        dictionary["CardBorderThickness"] = new Thickness(theme.Variants.Cards.Equals("flat", StringComparison.OrdinalIgnoreCase) ? 0 : theme.Geometry.BorderThickness);
        dictionary["ToggleOffBrush"] = Brush(C("SurfaceSelected"));
        dictionary["ToggleOnBrush"] = Brush(C("AccentPrimary"));
        dictionary["ToggleThumbOffBrush"] = Brush(isLight ? C("TextMuted") : C("TextSecondary"));
        dictionary["ToggleThumbBrush"] = Brush(ContrastOn(C("AccentPrimary")));
        dictionary["ToggleBorderBrush"] = Brush(isLight ? C("BorderStrong") : C("BorderPrimary"));
        dictionary["ToggleBorderThickness"] = new Thickness(theme.Variants.Toggle.Equals("compact", StringComparison.OrdinalIgnoreCase) ? 0 : theme.Geometry.BorderThickness);
        dictionary["ModIconBackground"] = Brush(C("AccentSoft"));
        dictionary["ModIconBorder"] = Brush(C("AccentPrimary"), isLight ? .34 : .45);
        dictionary["ModIconForeground"] = Brush(C("AccentPrimary"));
        dictionary["AgentIconBackground"] = Brush(isLight ? C("SurfaceSelected") : C("SurfaceSecondary"));
        dictionary["AgentIconBorder"] = Brush(C("Info"), isLight ? .38 : .5);
        dictionary["AgentIconForeground"] = Brush(C("Info"));
        dictionary["UnknownIconBackground"] = Brush(C("SurfaceSecondary"));
        dictionary["UnknownIconBorder"] = Brush(C("BorderStrong"));
        dictionary["UnknownIconForeground"] = Brush(C("TextMuted"));
        dictionary["ClientIconBackground"] = Brush(isLight ? C("SurfaceSelected") : C("SurfaceSecondary"));
        dictionary["ClientIconBorder"] = Brush(C("BorderSubtle"));
        dictionary["LunarIconForeground"] = Brush(C("TextPrimary"));
        dictionary["DangerSurface"] = Brush(C("Danger"), isLight ? .10 : .16);
        dictionary["DangerBorder"] = Brush(C("Danger"), .42);
        dictionary["DangerForeground"] = Brush(C("Danger"));

        var densityScale = theme.Variants.Density.Equals("compact", StringComparison.OrdinalIgnoreCase) ? .86d : theme.Geometry.SpacingScale;
        dictionary["ControlHeight"] = Math.Max(34, theme.Geometry.ControlHeight * densityScale);
        dictionary["CompactControlHeight"] = Math.Max(32, (theme.Geometry.ControlHeight - 6) * densityScale);
        dictionary["CardPadding"] = new Thickness(Math.Max(8, theme.Geometry.CardPadding * densityScale));
        dictionary["CardRadius"] = Radius(theme.Geometry.CardRadius);
        dictionary["SurfaceRadius"] = Radius(Math.Min(28, theme.Geometry.CardRadius + 2));
        dictionary["ButtonRadius"] = Radius(theme.Geometry.ButtonRadius);
        dictionary["ControlRadius"] = Radius(theme.Geometry.InputRadius);
        dictionary["PopupRadius"] = Radius(theme.Geometry.PopupRadius);
        dictionary["BadgeRadius"] = Radius(Math.Min(theme.Geometry.ButtonRadius, 9));
        dictionary["BaseFontSize"] = 14 * theme.Typography.BaseSizeScale;
        dictionary["BodyFontSize"] = 14 * theme.Typography.BaseSizeScale;
        dictionary["CaptionFontSize"] = 13 * theme.Typography.BaseSizeScale;
        dictionary["HeadingFontSize"] = 32 * theme.Typography.BaseSizeScale;
        dictionary["HeadingFontWeight"] = FontWeightFrom(theme.Typography.HeadingWeight);
        dictionary["BodyFontWeight"] = FontWeightFrom(theme.Typography.BodyWeight);
        dictionary["ScrollBarSize"] = theme.Variants.Scrollbar.Equals("high-contrast", StringComparison.OrdinalIgnoreCase) ? 13d : theme.Variants.Scrollbar.Equals("minimal", StringComparison.OrdinalIgnoreCase) ? 7d : 10d;
        dictionary["ScrollBarRadius"] = Radius(theme.Variants.Scrollbar.Equals("rounded", StringComparison.OrdinalIgnoreCase) ? 5 : 2);
        dictionary["ThemeDensityScale"] = densityScale;

        dictionary["FloatingShadow"] = Shadow(theme.Variants.Cards.Equals("flat", StringComparison.OrdinalIgnoreCase) ? 0 : theme.Effects.ShadowOpacity, C("AppBackground"));
        dictionary["PrimaryButtonShadow"] = Shadow(theme.Variants.Buttons.Equals("gradient", StringComparison.OrdinalIgnoreCase) ? Math.Min(.35, theme.Effects.ShadowOpacity) : 0, C("AccentPrimary"), 10, 2);
        dictionary["AppBackgroundTreatment"] = CreateBackground(theme, C);
        dictionary["AppBackgroundOverlay"] = Brush(theme.Background.OverlayColor ?? C("AppBackground"), theme.Background.OverlayColor is null ? 0 : .35);

        var dictionaries = resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(item => item.Contains("MoonriseThemeRuntime"));
        if (existing is null) dictionaries.Add(dictionary);
        else dictionaries[dictionaries.IndexOf(existing)] = dictionary;
    }

    private Brush CreateBackground(ThemePack theme, Func<string, string> color)
    {
        if (theme.Background.Mode.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            var path = _packs.ResolveAsset(theme, theme.Background.Asset)!;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 2560;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            var brush = new ImageBrush(bitmap)
            {
                Stretch = Enum.Parse<Stretch>(theme.Background.Stretch, ignoreCase: true),
                AlignmentX = theme.Background.Alignment.Equals("Left", StringComparison.OrdinalIgnoreCase) ? AlignmentX.Left :
                    theme.Background.Alignment.Equals("Right", StringComparison.OrdinalIgnoreCase) ? AlignmentX.Right : AlignmentX.Center,
                AlignmentY = theme.Background.Alignment.Equals("Top", StringComparison.OrdinalIgnoreCase) ? AlignmentY.Top :
                    theme.Background.Alignment.Equals("Bottom", StringComparison.OrdinalIgnoreCase) ? AlignmentY.Bottom : AlignmentY.Center,
                Opacity = theme.Background.Opacity
            };
            brush.Freeze();
            return brush;
        }
        if (theme.Background.Mode.Equals("solid", StringComparison.OrdinalIgnoreCase))
            return Brush(theme.Background.Color ?? color("AppBackground"));
        return Gradient(theme.Background.GradientStart ?? color("SidebarBackground"), theme.Background.GradientEnd ?? color("AppBackground"), vertical: true);
    }

    private void StartWatcher(string themeDirectory)
    {
        _reloadTimer = new Timer(_ => ReloadFromWatcher(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(themeDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            Filter = "*.*",
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnThemeFileChanged;
        _watcher.Created += OnThemeFileChanged;
        _watcher.Deleted += OnThemeFileChanged;
        _watcher.Renamed += OnThemeFileChanged;
    }

    private void OnThemeFileChanged(object sender, FileSystemEventArgs e)
    {
        var extension = Path.GetExtension(e.FullPath);
        if (!string.Equals(Path.GetFileName(e.FullPath), "theme.json", StringComparison.OrdinalIgnoreCase) &&
            extension is not ".png" and not ".jpg" and not ".jpeg") return;
        lock (_reloadGate) _reloadTimer?.Change(400, Timeout.Infinite);
    }

    private void ReloadFromWatcher()
    {
        var current = _currentTheme;
        var resources = _resources;
        if (current?.SourceDirectory is null || resources is null) return;
        try
        {
            var parsed = _packs.LoadCustomTheme(current.SourceDirectory);
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed || _currentTheme?.SourceDirectory != current.SourceDirectory) return;
                ApplyDictionary(resources, parsed);
                _currentTheme = parsed;
                CurrentThemeId = parsed.Id;
                _reloaded?.Invoke(parsed);
            });
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => _reportDiagnostic?.Invoke($"Theme hot reload kept the previous appearance: {exception.Message}"));
        }
    }

    private void StopWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnThemeFileChanged;
            _watcher.Created -= OnThemeFileChanged;
            _watcher.Deleted -= OnThemeFileChanged;
            _watcher.Renamed -= OnThemeFileChanged;
            _watcher.Dispose();
            _watcher = null;
        }
        _reloadTimer?.Dispose();
        _reloadTimer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatcher();
    }

    private static void AddBrush(ResourceDictionary dictionary, string key, string color, double opacity = 1) => dictionary[key] = Brush(color, opacity);
    private static SolidColorBrush Brush(string value, double opacity = 1)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }
    private static LinearGradientBrush Gradient(string start, string end, bool vertical = false)
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(start), (Color)ColorConverter.ConvertFromString(end), vertical ? 90 : 0);
        brush.Freeze();
        return brush;
    }
    private static CornerRadius Radius(double value) => new(value);
    private static DropShadowEffect Shadow(double opacity, string color, double blur = 18, double depth = 5) => new()
    {
        BlurRadius = blur, ShadowDepth = depth, Opacity = opacity,
        Color = (Color)ColorConverter.ConvertFromString(color), RenderingBias = RenderingBias.Performance
    };
    private static FontWeight FontWeightFrom(string value) => value.ToLowerInvariant() switch
    {
        "bold" => FontWeights.Bold, "semibold" => FontWeights.SemiBold, "medium" => FontWeights.Medium, _ => FontWeights.Normal
    };
    private static Color ColorValue(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static string ContrastOn(string value) => RelativeLuminance(ColorValue(value)) > .42 ? "#17130E" : "#FFFFFF";
    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }
}
