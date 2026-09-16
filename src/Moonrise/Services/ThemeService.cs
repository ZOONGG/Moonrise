using System.Windows;

namespace Moonrise.Services;

public sealed class ThemeService
{
    public static readonly IReadOnlyDictionary<string, string> BuiltInThemeSources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["moonlight"] = "Themes/Palettes/Moonlight.xaml",
            ["obsidian"] = "Themes/Palettes/Obsidian.xaml",
            ["aurora"] = "Themes/Palettes/Aurora.xaml"
        };

    public string CurrentThemeId { get; private set; } = "moonlight";

    public bool Apply(ResourceDictionary applicationResources, string? themeId)
    {
        var normalized = BuiltInThemeSources.ContainsKey(themeId ?? string.Empty)
            ? themeId!
            : "moonlight";
        var source = BuiltInThemeSources[normalized];
        var dictionaries = applicationResources.MergedDictionaries;
        var index = dictionaries
            .Select((dictionary, position) => (dictionary, position))
            .FirstOrDefault(item => item.dictionary.Source?.OriginalString.Contains("Themes/Palettes/", StringComparison.OrdinalIgnoreCase) == true)
            .position;
        var replacement = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (dictionaries.Count > 0 && dictionaries[index].Source?.OriginalString.Contains("Themes/Palettes/", StringComparison.OrdinalIgnoreCase) == true)
            dictionaries[index] = replacement;
        else
            dictionaries.Insert(Math.Min(1, dictionaries.Count), replacement);
        CurrentThemeId = normalized;
        return string.Equals(normalized, themeId, StringComparison.OrdinalIgnoreCase);
    }
}
