using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed partial class AppearancePackService
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<LanguagePack> LoadLanguages(string directory, Action<string>? reportError = null)
    {
        Directory.CreateDirectory(directory);
        var packs = new List<LanguagePack>
        {
            new() { Code = "ru", Name = "Русский" },
            new() { Code = "en", Name = "English" }
        };
        var assembly = typeof(AppearancePackService).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith("Moonrise.LanguagePacks.", StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var pack = JsonSerializer.Deserialize<LanguagePack>(stream, JsonOptions)!;
            ValidateLanguage(pack);
            packs.Add(pack);
        }
        foreach (var path in Directory.EnumerateFiles(directory, "*.moonrise-language.json")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var pack = JsonSerializer.Deserialize<LanguagePack>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("The language pack is empty.");
                ValidateLanguage(pack);
                packs.RemoveAll(existing => string.Equals(existing.Code, pack.Code, StringComparison.OrdinalIgnoreCase));
                packs.Add(pack);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                reportError?.Invoke($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }
        return packs;
    }

    public void WriteLanguageExamples(string directory)
    {
        Directory.CreateDirectory(directory);
        var assembly = typeof(AppearancePackService).Assembly;
        foreach (var name in new[] { "en.template.json", "ru.template.json", "README.md" })
        {
            var destination = Path.Combine(directory, name);
            if (File.Exists(destination)) continue;
            using var source = assembly.GetManifestResourceStream("Moonrise.LanguageTemplates." + name)!;
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            source.CopyTo(output);
        }
    }

    public IReadOnlyList<ThemePack> LoadThemes(string directory, Action<string>? reportError = null) =>
        new ThemePackService().LoadThemes(directory, reportError);

    public string Translate(LanguagePack language, string english) =>
        language.Translations.TryGetValue(english, out var translation) && !string.IsNullOrWhiteSpace(translation)
            ? translation
            : english;

    private static void ValidateLanguage(LanguagePack pack)
    {
        if (pack.SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(pack.Code) ||
            !LanguageCodePattern().IsMatch(pack.Code) || string.IsNullOrWhiteSpace(pack.Name) || pack.Translations is null)
            throw new InvalidDataException("The language pack metadata is invalid.");
        if (pack.Translations.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
            throw new InvalidDataException("The language pack contains an empty translation.");
        foreach (var pair in pack.Translations.Where(pair => pair.Key.Contains('{')))
        {
            try
            {
                if (CompositeFormat.Parse(pair.Key).MinimumArgumentCount != CompositeFormat.Parse(pair.Value).MinimumArgumentCount)
                    throw new InvalidDataException($"Translation placeholders do not match: {pair.Key}");
            }
            catch (FormatException)
            {
                throw new InvalidDataException($"Invalid translation placeholders: {pair.Key}");
            }
        }
    }

    [GeneratedRegex("^[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCodePattern();
}
