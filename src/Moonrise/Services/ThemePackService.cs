using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class ThemePackService
{
    public const int CurrentSchemaVersion = 1;
    public const long MaximumManifestBytes = 256 * 1024;
    public const long MaximumAssetBytes = 20 * 1024 * 1024;
    public const long MaximumImportBytes = 32 * 1024 * 1024;
    public const int MaximumImportFiles = 64;

    public static readonly IReadOnlySet<string> SemanticColorTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AppBackground", "SidebarBackground", "SurfacePrimary", "SurfaceSecondary", "SurfaceRaised",
        "SurfaceHover", "SurfaceSelected", "BorderPrimary", "BorderSubtle", "BorderStrong", "TextPrimary",
        "TextSecondary", "TextMuted", "TextDisabled", "AccentPrimary", "AccentSecondary", "AccentSoft",
        "Success", "Warning", "Danger", "Info", "FocusRing", "Selection", "ScrollbarTrack", "ScrollbarThumb"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public IReadOnlyList<ThemePack> LoadThemes(string directory, Action<string>? reportError = null)
    {
        Directory.CreateDirectory(directory);
        var themes = CreateBuiltIns().ToList();
        var ids = new HashSet<string>(themes.Select(theme => theme.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var themeDirectory in Directory.EnumerateDirectories(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(themeDirectory, "theme.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var theme = LoadCustomTheme(themeDirectory);
                if (!ids.Add(theme.Id))
                    throw new InvalidDataException($"Theme id '{theme.Id}' is already installed or reserved.");
                themes.Add(theme);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                reportError?.Invoke($"{Path.GetFileName(themeDirectory)}: {exception.Message}");
            }
        }
        return themes;
    }

    public ThemePack LoadCustomTheme(string themeDirectory)
    {
        var root = Path.GetFullPath(themeDirectory);
        var manifestPath = Path.Combine(root, "theme.json");
        var info = new FileInfo(manifestPath);
        if (!info.Exists) throw new InvalidDataException("theme.json is missing.");
        RejectReparsePoints(root, manifestPath);
        if (info.Length > MaximumManifestBytes) throw new InvalidDataException("theme.json is too large.");
        var theme = JsonSerializer.Deserialize<ThemePack>(File.ReadAllText(manifestPath), JsonOptions)
                    ?? throw new InvalidDataException("theme.json is empty.");
        theme.SourceDirectory = root;
        theme.IsBuiltIn = false;
        NormalizeAndValidate(theme);
        ValidateAssets(theme);
        return theme;
    }

    public string CreateTemplate(string themesDirectory)
    {
        Directory.CreateDirectory(themesDirectory);
        var directory = NextAvailableDirectory(themesDirectory, "my-theme");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        var theme = CreateBuiltIns().First(theme => theme.Id == "moonlight");
        theme.Id = Path.GetFileName(directory);
        theme.Name = "My Theme";
        theme.Author = Environment.UserName;
        theme.Description = "A safe Moonrise theme pack. Edit values and save while selected to hot reload.";
        theme.IsBuiltIn = false;
        theme.SourceDirectory = null;
        theme.Colors["AccentPrimary"] = "#C066FF";
        theme.Colors["AccentSecondary"] = "#6F7CFF";
        File.WriteAllText(Path.Combine(directory, "theme.json"), JsonSerializer.Serialize(theme, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "README.txt"), TemplateReadme, new UTF8Encoding(false));
        return directory;
    }

    public ThemePack ImportDirectory(string sourceDirectory, string themesDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory);
        var theme = LoadCustomTheme(source);
        Directory.CreateDirectory(themesDirectory);
        var destination = Path.Combine(Path.GetFullPath(themesDirectory), theme.Id);
        if (Directory.Exists(destination))
            throw new IOException($"Theme '{theme.Id}' is already installed.");

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length > MaximumImportFiles) throw new InvalidDataException("Theme contains too many files.");
        long total = 0;
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(source, file);
            if (!IsAllowedPackageFile(relative)) throw new InvalidDataException($"Unsupported theme file: {relative}");
            total += new FileInfo(file).Length;
            if (total > MaximumImportBytes) throw new InvalidDataException("Theme import exceeds the 32 MB limit.");
            EnsureInside(source, file);
            RejectReparsePoints(source, file);
        }

        try
        {
            Directory.CreateDirectory(destination);
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(source, file);
                var target = Path.GetFullPath(Path.Combine(destination, relative));
                EnsureInside(destination, target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
            return LoadCustomTheme(destination);
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            throw;
        }
    }

    public static IReadOnlyList<ThemePack> CreateBuiltIns() =>
    [
        BuiltIn("standard", "Moonrise Standard", "The original polished Moonrise look: near-black violet surfaces, soft cards and clean borderless controls.",
            Colors("#070811", "#0C0D17", "#10111C", "#151621", "#171824", "#201D2D", "#251C3B", "#242536", "#181925", "#65438E", "#F4F4FA", "#CFD0DD", "#85889C", "#686A7B", "#A86CFF", "#6B5CFF", "#251C3B", "#1FD184", "#F3B65E", "#FF6F87", "#63C9F5"),
            new ThemeGeometry { CardRadius = 16, ButtonRadius = 10, InputRadius = 10, PopupRadius = 11, BorderThickness = 1, CardPadding = 21, ControlHeight = 42 },
            new ThemeVariants { Sidebar = "glass", Navigation = "pill", Cards = "flat", Buttons = "gradient", Inputs = "soft", Toggle = "soft", Scrollbar = "rounded", Density = "comfortable" },
            new ThemeBackground { Mode = "gradient", GradientStart = "#23143D", GradientEnd = "#070811" }),
        BuiltIn("moonlight", "Moonlight", "Core Moonrise identity: deep navy, violet-blue energy and soft depth.",
            Colors("#070B15", "#0A1020", "#0F1828", "#142036", "#192843", "#1B2B47", "#21325A", "#2D4262", "#1C2B43", "#46658E", "#F7F9FD", "#B8C4D6", "#7F91AF", "#55647E", "#8F63F7", "#4F82FF", "#2C2657", "#43D5A5", "#F1C35B", "#F06475", "#4CC7E8"),
            new ThemeGeometry(), new ThemeVariants(), new ThemeBackground { Mode = "gradient", GradientStart = "#23143D", GradientEnd = "#070B15" }),
        BuiltIn("ember", "Ember", "Compact graphite surfaces with warm amber focus and crisp, flatter components.",
            Colors("#11100F", "#181410", "#1E1C19", "#27231E", "#302A23", "#352D25", "#403225", "#5C4937", "#352E26", "#806044", "#FFF7EC", "#E1CDB7", "#AD957C", "#786858", "#F3A13C", "#D9612C", "#51301B", "#5BD09C", "#F3BC62", "#F1766C", "#6DB4E3"),
            new ThemeGeometry { CardRadius = 7, ButtonRadius = 6, InputRadius = 6, PopupRadius = 7, CardPadding = 16, ControlHeight = 38, SpacingScale = .86 },
            new ThemeVariants { Sidebar = "floating", Navigation = "left-accent", Cards = "flat", Buttons = "solid", Inputs = "filled", Toggle = "compact", Scrollbar = "high-contrast", Density = "compact" },
            new ThemeBackground { Mode = "gradient", GradientStart = "#2B1B10", GradientEnd = "#11100F" }),
        BuiltIn("porcelain", "Porcelain", "A complete light theme with cool white surfaces, dark text and clean lavender detail.",
            Colors("#F6F7FA", "#ECEFF5", "#FFFFFF", "#F0F3F8", "#FFFFFF", "#E7EBF3", "#E1E6F0", "#C5CDDC", "#D8DEE9", "#98A5BA", "#1D2433", "#465168", "#68758D", "#9AA4B5", "#6259D6", "#3975C6", "#E5E3FA", "#197B59", "#9A6112", "#C13D50", "#23749D"),
            new ThemeGeometry { CardRadius = 12, ButtonRadius = 8, InputRadius = 8, PopupRadius = 10, CardPadding = 20, ControlHeight = 42 },
            new ThemeVariants { Sidebar = "flat", Navigation = "soft-block", Cards = "bordered", Buttons = "solid", Inputs = "filled", Toggle = "minimal", Scrollbar = "minimal", Density = "comfortable" },
            new ThemeBackground { Mode = "gradient", GradientStart = "#FCFCFD", GradientEnd = "#F1F3F8" })
    ];

    public void NormalizeAndValidate(ThemePack theme)
    {
        if (theme.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported schemaVersion {theme.SchemaVersion}; Moonrise supports version {CurrentSchemaVersion}.");
        if (!ThemeIdPattern.IsMatch(theme.Id ?? string.Empty) || string.IsNullOrWhiteSpace(theme.Name) || theme.Name.Length > 80)
            throw new InvalidDataException("Theme id or name is invalid.");
        if (!VersionPattern.IsMatch(theme.Version ?? string.Empty))
            throw new InvalidDataException("Theme version must be a dotted numeric version such as 1.0.0.");
        if (!string.Equals(theme.Base, "moonlight", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("V1 custom themes must use base 'moonlight'.");

        theme.Colors = new Dictionary<string, string>(theme.Colors ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var pair in theme.Colors)
        {
            if (!SemanticColorTokens.Contains(pair.Key)) continue; // forward-compatible unknown token
            if (!ColorPattern.IsMatch(pair.Value ?? string.Empty))
                throw new InvalidDataException($"Color '{pair.Key}' must use #RRGGBB or #AARRGGBB.");
        }

        theme.Typography ??= new ThemeTypography();
        theme.Geometry ??= new ThemeGeometry();
        theme.Effects ??= new ThemeEffects();
        theme.Variants ??= new ThemeVariants();
        theme.Background ??= new ThemeBackground();
        theme.Assets = new Dictionary<string, string>(theme.Assets ?? [], StringComparer.OrdinalIgnoreCase);
        theme.Typography.BaseSizeScale = ClampFinite(theme.Typography.BaseSizeScale, .85, 1.25, 1);
        theme.Typography.LetterSpacing = ClampFinite(theme.Typography.LetterSpacing, 0, 1.5, 0);
        ValidateChoice(theme.Typography.HeadingWeight, "headingWeight", "Normal", "Medium", "SemiBold", "Bold");
        ValidateChoice(theme.Typography.BodyWeight, "bodyWeight", "Normal", "Medium", "SemiBold");
        theme.Geometry.CardRadius = ClampFinite(theme.Geometry.CardRadius, 0, 28, 14);
        theme.Geometry.ButtonRadius = ClampFinite(theme.Geometry.ButtonRadius, 0, 24, 10);
        theme.Geometry.InputRadius = ClampFinite(theme.Geometry.InputRadius, 0, 24, 10);
        theme.Geometry.PopupRadius = ClampFinite(theme.Geometry.PopupRadius, 0, 24, 12);
        theme.Geometry.BorderThickness = ClampFinite(theme.Geometry.BorderThickness, 0, 3, 1);
        theme.Geometry.SpacingScale = ClampFinite(theme.Geometry.SpacingScale, .75, 1.35, 1);
        theme.Geometry.CardPadding = ClampFinite(theme.Geometry.CardPadding, 10, 32, 20);
        theme.Geometry.ControlHeight = ClampFinite(theme.Geometry.ControlHeight, 34, 52, 42);
        theme.Effects.ShadowOpacity = ClampFinite(theme.Effects.ShadowOpacity, 0, .6, .34);
        theme.Effects.SurfaceOpacity = ClampFinite(theme.Effects.SurfaceOpacity, .72, 1, 1);

        ValidateChoice(theme.Variants.Sidebar, "sidebar", "flat", "floating", "glass");
        ValidateChoice(theme.Variants.Navigation, "navigation", "pill", "left-accent", "soft-block");
        ValidateChoice(theme.Variants.Cards, "cards", "flat", "bordered", "glass", "elevated");
        ValidateChoice(theme.Variants.Buttons, "buttons", "solid", "gradient", "soft", "outline");
        ValidateChoice(theme.Variants.Inputs, "inputs", "filled", "outlined", "soft");
        ValidateChoice(theme.Variants.Toggle, "toggle", "compact", "soft", "minimal");
        ValidateChoice(theme.Variants.Scrollbar, "scrollbar", "minimal", "rounded", "high-contrast");
        ValidateChoice(theme.Variants.Density, "density", "compact", "comfortable");
        ValidateChoice(theme.Background.Mode, "background.mode", "solid", "gradient", "image");
        ValidateChoice(theme.Background.Stretch, "background.stretch", "None", "Fill", "Uniform", "UniformToFill");
        ValidateChoice(theme.Background.Alignment, "background.alignment", "Top", "Center", "Bottom", "Left", "Right");
        theme.Background.Opacity = ClampFinite(theme.Background.Opacity, .1, 1, 1);
        foreach (var color in new[] { theme.Background.Color, theme.Background.GradientStart, theme.Background.GradientEnd, theme.Background.OverlayColor }.Where(value => !string.IsNullOrWhiteSpace(value)))
            if (!ColorPattern.IsMatch(color!)) throw new InvalidDataException("Background colors must use #RRGGBB or #AARRGGBB.");
    }

    public string? ResolveAsset(ThemePack theme, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (theme.SourceDirectory is null) throw new InvalidDataException("Built-in themes cannot reference user assets.");
        if (Path.IsPathRooted(reference) || reference.Contains(':')) throw new InvalidDataException("Theme assets must use relative local paths.");
        var root = Path.GetFullPath(theme.SourceDirectory);
        var path = Path.GetFullPath(Path.Combine(root, reference.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInside(root, path);
        if (!AllowedAssetExtensions.Contains(Path.GetExtension(path))) throw new InvalidDataException("Theme asset format is not supported.");
        if (!File.Exists(path)) throw new InvalidDataException($"Theme asset is missing: {reference}");
        if (new FileInfo(path).Length > MaximumAssetBytes) throw new InvalidDataException("Theme asset exceeds the 20 MB limit.");
        RejectReparsePoints(root, path);
        return path;
    }

    private void ValidateAssets(ThemePack theme)
    {
        foreach (var pair in theme.Assets)
        {
            if (!AllowedAssetRoles.Contains(pair.Key)) continue;
            ResolveAsset(theme, pair.Value);
        }
        if (string.Equals(theme.Background.Mode, "image", StringComparison.OrdinalIgnoreCase))
            ResolveAsset(theme, theme.Background.Asset ?? throw new InvalidDataException("Image background requires an asset."));
    }

    private static ThemePack BuiltIn(string id, string name, string description, Dictionary<string, string> colors,
        ThemeGeometry geometry, ThemeVariants variants, ThemeBackground background) => new()
    {
        Id = id, Name = name, Author = "Moonrise", Description = description, IsBuiltIn = true,
        Colors = colors, Geometry = geometry, Variants = variants, Background = background
    };

    private static Dictionary<string, string> Colors(string app, string sidebar, string primary, string secondary, string raised,
        string hover, string selected, string border, string subtle, string strong, string text, string textSecondary,
        string muted, string disabled, string accent, string accent2, string accentSoft, string success, string warning,
        string danger, string info) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppBackground"] = app, ["SidebarBackground"] = sidebar, ["SurfacePrimary"] = primary,
        ["SurfaceSecondary"] = secondary, ["SurfaceRaised"] = raised, ["SurfaceHover"] = hover,
        ["SurfaceSelected"] = selected, ["BorderPrimary"] = border, ["BorderSubtle"] = subtle,
        ["BorderStrong"] = strong, ["TextPrimary"] = text, ["TextSecondary"] = textSecondary,
        ["TextMuted"] = muted, ["TextDisabled"] = disabled, ["AccentPrimary"] = accent,
        ["AccentSecondary"] = accent2, ["AccentSoft"] = accentSoft, ["Success"] = success,
        ["Warning"] = warning, ["Danger"] = danger, ["Info"] = info, ["FocusRing"] = accent,
        ["Selection"] = selected, ["ScrollbarTrack"] = subtle, ["ScrollbarThumb"] = muted
    };

    private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static void ValidateChoice(string value, string field, params string[] allowed)
    {
        if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported {field} value '{value}'. Allowed: {string.Join(", ", allowed)}.");
    }

    private static string NextAvailableDirectory(string parent, string baseName)
    {
        for (var suffix = 1; suffix < 10_000; suffix++)
        {
            var name = suffix == 1 ? baseName : $"{baseName}-{suffix}";
            var candidate = Path.Combine(parent, name);
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not choose a unique theme template directory.");
    }

    private static bool IsAllowedPackageFile(string relative)
    {
        if (string.Equals(relative, "theme.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relative, "README.txt", StringComparison.OrdinalIgnoreCase)) return true;
        if (!relative.StartsWith("assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !relative.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) return false;
        return AllowedAssetExtensions.Contains(Path.GetExtension(relative));
    }

    internal static void EnsureInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Theme path escapes its theme directory.");
    }

    private static void RejectReparsePoints(string root, string path)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(path)!);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        while (current is not null && current.FullName.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Theme assets cannot pass through links or junctions.");
            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase)) break;
            current = current.Parent;
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked theme assets are not allowed.");
    }

    private static readonly HashSet<string> AllowedAssetExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
    private static readonly HashSet<string> AllowedAssetRoles = new(StringComparer.OrdinalIgnoreCase) { "appBackground", "sidebarBackground", "heroBackground", "surfaceTexture" };
    private const string TemplateReadme = """
Moonrise Theme Pack V1

Edit theme.json while this theme is selected; Moonrise hot reloads valid saves.
Only declarative colors, geometry, typography, variants, and local PNG/JPG assets are accepted.
Theme packs cannot contain XAML, DLLs, scripts, remote URLs, or executable code.
See THEMING.md in the Moonrise distribution/repository for the complete schema.
""";

    private static readonly Regex ThemeIdPattern = new("^[a-z0-9][a-z0-9-]{1,40}$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("^[0-9]+(?:\\.[0-9]+){0,3}(?:[-+][a-zA-Z0-9.-]+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex ColorPattern = new("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.CultureInvariant);
}
