using System.Text.Json;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class ModpackProfileService
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ModpackProfile Create(
        string name,
        string client,
        string gameVersion,
        IEnumerable<PackageInfo> packages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);

        return new ModpackProfile
        {
            Name = name.Trim(),
            Client = client.Trim(),
            GameVersion = gameVersion.Trim(),
            Packages = packages.Select(package => new ProfilePackageReference(
                package.Kind,
                package.Identifier,
                package.Version,
                package.Sha256,
                package.FileName,
                package.IsEnabled)).ToList()
        };
    }

    public IReadOnlyList<ModpackProfile> LoadAll(string directory)
    {
        Directory.CreateDirectory(directory);
        var profiles = new List<ModpackProfile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.moonrise.json")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try { profiles.Add(Load(path)); }
            catch (InvalidDataException) { }
            catch (JsonException) { }
        }
        return profiles;
    }

    public ModpackProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<ModpackProfile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The loadout profile is empty.");
        Validate(profile);
        return profile;
    }

    public string Save(ModpackProfile profile, string directory)
    {
        Validate(profile);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{profile.Id}.moonrise.json");
        SaveToPath(profile, path);
        return path;
    }

    public void Export(ModpackProfile profile, string destinationPath)
    {
        Validate(profile);
        if (!string.Equals(Path.GetExtension(destinationPath), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Loadout exports must use the .json extension.");
        SaveToPath(profile, destinationPath);
    }

    public IReadOnlyList<PackageInfo> Apply(
        ModpackProfile profile,
        IEnumerable<PackageInfo> installedPackages)
    {
        Validate(profile);
        var installed = installedPackages.ToArray();
        foreach (var package in installed) package.IsEnabled = false;

        var enabled = new List<PackageInfo>();
        foreach (var reference in profile.Packages.Where(item => item.Enabled))
        {
            var match = installed.FirstOrDefault(package =>
                package.Kind == reference.Kind &&
                string.Equals(package.Sha256, reference.Sha256, StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;
            match.IsEnabled = true;
            enabled.Add(match);
        }
        return enabled;
    }

    private static void SaveToPath(ModpackProfile profile, string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profile, JsonOptions));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(ModpackProfile profile)
    {
        if (profile.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported loadout schema version: {profile.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("The loadout id is invalid.");
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Client) ||
            string.IsNullOrWhiteSpace(profile.GameVersion))
            throw new InvalidDataException("The loadout name, client, and game version are required.");
        if (profile.Packages.Any(item => string.IsNullOrWhiteSpace(item.Identifier) ||
                                         string.IsNullOrWhiteSpace(item.Sha256) ||
                                         item.Sha256.Length != 64 ||
                                         item.Sha256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("A loadout package reference is invalid.");
    }
}
