using System.Text.Json;
using Moonrise.Models;

namespace Moonrise.Infrastructure;

// Fail closed before any shared library mutation. Already shipped binaries cannot
// be retroactively guarded; do not run them against a newer storage format.
internal static class StorageFormatGuard
{
    public static void Check(string root)
    {
        CheckSettings(Path.Combine(root, "settings", "moonrise-settings.json"));
        CheckVersion(Path.Combine(root, "packages", "metadata", "index.json"), "FormatVersion", 1);
    }

    public static void CheckSettings(string path) =>
        CheckVersion(path, "SettingsSchemaVersion", MoonriseSettings.CurrentSettingsSchemaVersion);

    private static void CheckVersion(string path, string property, int supported)
    {
        if (!File.Exists(path)) return;
        JsonDocument document;
        try { document = JsonDocument.Parse(File.ReadAllText(path)); }
        catch (JsonException)
        {
            // Corruption is handled by the owner's existing recovery path. This
            // guard rejects recognizable unsupported versions, not broken JSON.
            return;
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;
            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(field.Name, property, StringComparison.OrdinalIgnoreCase)) continue;
                if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out var version) ||
                    version < (property == "SettingsSchemaVersion" ? 0 : 1) || version > supported)
                    throw new InvalidDataException("Unsupported Moonrise storage version; use a compatible build. " + path);
            }
        }
    }
}
