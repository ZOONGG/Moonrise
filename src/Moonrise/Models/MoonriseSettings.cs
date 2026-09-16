namespace Moonrise.Models;

public sealed class MoonriseSettings
{
    public const int CurrentSettingsSchemaVersion = 3;

    public int SettingsSchemaVersion { get; set; }
    public string Language { get; set; } = "ru";
    public string Theme { get; set; } = "moonlight";
    public string Client { get; set; } = "lunar";
    public string MinecraftVersion { get; set; } = "1.8.9";
    public string LauncherExecutablePath { get; set; } = string.Empty;
    public bool CloseAfterLaunch { get; set; }
    public bool SafeLaunch { get; set; }
    public bool BackgroundLunarLaunch { get; set; } = true;
    public bool RevealLunarWhenActionRequired { get; set; } = true;
    public int LaunchTimeoutSeconds { get; set; } = 180;
    public bool CheckForUpdates { get; set; } = true;
    public bool IncludePrereleaseUpdates { get; set; }
    public bool UpdateCatalogAutomatically { get; set; } = true;
    public bool DeveloperMode { get; set; }
    public string CatalogUrl { get; set; } = "https://zoongg.github.io/Moonrise-Catalog/catalog/index.json";
    public string CustomTestCatalogUrl { get; set; } = string.Empty;
    public string ActiveLoadoutId { get; set; } = string.Empty;
    public List<string> DisabledMods { get; set; } = [];
    public List<string> DisabledAgents { get; set; } = [];
}
