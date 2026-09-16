using System.Text.Json;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class AppSettingsService(string path)
{
    private readonly string _path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MoonriseSettings Load()
    {
        try
        {
            var settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<MoonriseSettings>(File.ReadAllText(_path)) ?? new MoonriseSettings()
                : new MoonriseSettings();
            if (settings.SettingsSchemaVersion < MoonriseSettings.CurrentSettingsSchemaVersion)
            {
                if (settings.SettingsSchemaVersion < 2)
                {
                    // Version 2 makes quiet Lunar startup the default for existing
                    // installations. Once migrated, an explicit user choice is kept.
                    settings.BackgroundLunarLaunch = true;
                }
                if (settings.SettingsSchemaVersion < 3)
                {
                    // Version 3 restores the 02.08 Moonlight presentation once for
                    // existing installations. The theme picker remains available and
                    // later explicit choices are preserved.
                    settings.Theme = "moonlight";
                }
                settings.SettingsSchemaVersion = MoonriseSettings.CurrentSettingsSchemaVersion;
                TrySaveMigration(settings);
            }
            return settings;
        }
        catch
        {
            return new MoonriseSettings();
        }
    }

    public void Save(MoonriseSettings settings)
    {
        settings.SettingsSchemaVersion = MoonriseSettings.CurrentSettingsSchemaVersion;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _path, true);
    }

    private void TrySaveMigration(MoonriseSettings settings)
    {
        try
        {
            Save(settings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The in-memory migration still applies to this launch. A later
            // successful settings save will persist the schema version.
        }
    }
}
