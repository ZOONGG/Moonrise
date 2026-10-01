using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class LauncherProfileService
{
    private readonly string _profilesDatabasePath;
    private readonly string _launcherSettingsPath;

    public LauncherProfileService(string? profilesDatabasePath = null, string? launcherSettingsPath = null)
    {
        _profilesDatabasePath = profilesDatabasePath ??
            Path.Combine(UserHome(), ".lunarclient", "db", "profiles.db");
        _launcherSettingsPath = launcherSettingsPath ??
            Path.Combine(UserHome(), ".lunarclient", "settings", "launcher.json");
    }

    public string ProfilesDatabasePath => _profilesDatabasePath;
    public string LauncherSettingsPath => _launcherSettingsPath;

    public IReadOnlyList<LauncherProfile> GetProfiles()
    {
        if (!File.Exists(ProfilesDatabasePath))
            return [];
        var profiles = new List<LauncherProfile>();
        using var connection = new SqliteConnection(
            $"Data Source={ProfilesDatabasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "PRAGMA table_info(profiles)";
            using var fields = schema.ExecuteReader();
            while (fields.Read()) columns.Add(fields.GetString(1));
        }
        var required = new[] { "id", "name", "type", "major_game_version", "game_version" };
        if (required.Any(column => !columns.Contains(column))) return [];
        string Optional(string column) => columns.Contains(column) ? column : "NULL";
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, name, type, major_game_version, game_version, {Optional("loaders")}, {Optional("loader_version")}, {Optional("lunar_module")} FROM profiles WHERE type = 'lunar' ORDER BY major_game_version, game_version, id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            profiles.Add(new LauncherProfile(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            {
                Loaders = ParseLoaders(reader.IsDBNull(5) ? null : reader.GetString(5)),
                LoaderVersion = reader.IsDBNull(6) ? null : reader.GetString(6),
                LunarModule = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        return profiles;
    }

    internal static IReadOnlyList<string> ParseLoaders(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        try
        {
            using var json = JsonDocument.Parse(value);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return [];
            return json.RootElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }
        catch (JsonException) { return []; }
    }

    public LauncherProfileSelection BeginExactProfileSelection(string client, string version, string? profileId = null)
    {
        var profile = GetProfiles().FirstOrDefault(item =>
            string.Equals(item.Client, client, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.GameVersion, version, StringComparison.OrdinalIgnoreCase) &&
            (profileId is null || string.Equals(item.Id, profileId, StringComparison.Ordinal)));
        if (profile is null)
            throw new InvalidOperationException($"The official launcher has no {client} {version} profile yet. Open Lunar once, create that profile, then return to Moonrise.");

        var root = ReadLauncherSettings();
        var settings = root["settings"] as JsonObject ?? new JsonObject();
        root["settings"] = settings;
        var hadOriginalValue = settings.TryGetPropertyValue("gameProfile", out var originalValue);
        var originalClone = originalValue?.DeepClone();
        settings["gameProfile"] = profile.Id;
        WriteAtomic(LauncherSettingsPath, root);

        return new LauncherProfileSelection(
            profile,
            () => RestoreProfileSelection(hadOriginalValue, originalClone));
    }

    private JsonObject ReadLauncherSettings()
    {
        if (!File.Exists(LauncherSettingsPath))
            return new JsonObject();
        try
        {
            return JsonNode.Parse(File.ReadAllText(LauncherSettingsPath))?.AsObject()
                ?? throw new InvalidDataException("Lunar launcher.json must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Lunar launcher.json is invalid; Moonrise did not modify it.", exception);
        }
    }

    private void RestoreProfileSelection(bool hadOriginalValue, JsonNode? originalValue)
    {
        var root = ReadLauncherSettings();
        var settings = root["settings"] as JsonObject ?? new JsonObject();
        root["settings"] = settings;
        if (hadOriginalValue)
            settings["gameProfile"] = originalValue?.DeepClone();
        else
            settings.Remove("gameProfile");
        WriteAtomic(LauncherSettingsPath, root);
    }

    private static void WriteAtomic(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".moonrise-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string UserHome() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

public sealed class LauncherProfileSelection : IDisposable
{
    private Action? _restore;

    internal LauncherProfileSelection(LauncherProfile profile, Action restore)
    {
        Profile = profile;
        _restore = restore;
    }

    public LauncherProfile Profile { get; }

    public void Restore()
    {
        Interlocked.Exchange(ref _restore, null)?.Invoke();
    }

    public void Dispose() => Restore();
}
