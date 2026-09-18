using System.Text.Json.Serialization;

namespace Moonrise.Models;

public sealed class LanguagePack
{
    public int SchemaVersion { get; init; } = 1;
    public required string Code { get; init; }
    public required string Name { get; init; }
    public Dictionary<string, string> Translations { get; init; } = new(StringComparer.Ordinal);
    public override string ToString() => Name;
}

public sealed class ThemePack
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = "Moonrise";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public string Base { get; set; } = "moonlight";
    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ThemeTypography Typography { get; set; } = new();
    public ThemeGeometry Geometry { get; set; } = new();
    public ThemeEffects Effects { get; set; } = new();
    public ThemeVariants Variants { get; set; } = new();
    public ThemeBackground Background { get; set; } = new();
    public Dictionary<string, string> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore] public bool IsBuiltIn { get; set; }
    [JsonIgnore] public string? SourceDirectory { get; set; }
    [JsonIgnore] public string TypeLabel => IsBuiltIn ? "Built-in" : "Custom";
    [JsonIgnore] public string PreviewBackground => Color("AppBackground", "#070B15");
    [JsonIgnore] public string PreviewSidebar => Color("SidebarBackground", "#090F1C");
    [JsonIgnore] public string PreviewSurface => Color("SurfacePrimary", "#0E1727");
    [JsonIgnore] public string PreviewAccent => Color("AccentPrimary", "#8B5CF6");
    [JsonIgnore] public string PreviewText => Color("TextPrimary", "#F5F7FC");

    public string Color(string name, string fallback) =>
        Colors.TryGetValue(name, out var value) ? value : fallback;

    public override string ToString() => Name;
}

public sealed class ThemeTypography
{
    public double BaseSizeScale { get; set; } = 1;
    public string HeadingWeight { get; set; } = "SemiBold";
    public string BodyWeight { get; set; } = "Normal";
    public double LetterSpacing { get; set; }
}

public sealed class ThemeGeometry
{
    public double CardRadius { get; set; } = 14;
    public double ButtonRadius { get; set; } = 10;
    public double InputRadius { get; set; } = 10;
    public double PopupRadius { get; set; } = 12;
    public double BorderThickness { get; set; } = 1;
    public double SpacingScale { get; set; } = 1;
    public double CardPadding { get; set; } = 20;
    public double ControlHeight { get; set; } = 42;
}

public sealed class ThemeEffects
{
    public double ShadowOpacity { get; set; } = 0.34;
    public double SurfaceOpacity { get; set; } = 1;
}

public sealed class ThemeVariants
{
    public string Sidebar { get; set; } = "flat";
    public string Navigation { get; set; } = "pill";
    public string Cards { get; set; } = "elevated";
    public string Buttons { get; set; } = "gradient";
    public string Inputs { get; set; } = "filled";
    public string Toggle { get; set; } = "soft";
    public string Scrollbar { get; set; } = "rounded";
    public string Density { get; set; } = "comfortable";
}

public sealed class ThemeBackground
{
    public string Mode { get; set; } = "gradient";
    public string? Color { get; set; }
    public string? GradientStart { get; set; }
    public string? GradientEnd { get; set; }
    public string? Asset { get; set; }
    public string Stretch { get; set; } = "UniformToFill";
    public string Alignment { get; set; } = "Center";
    public double Opacity { get; set; } = 1;
    public string? OverlayColor { get; set; }
}
