using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed partial class ClientPluginService
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public IReadOnlyList<RegisteredClientPlugin> LoadAll(string directory)
    {
        Directory.CreateDirectory(directory);
        var plugins = new List<RegisteredClientPlugin>();
        foreach (var manifestPath in Directory.EnumerateFiles(directory, "*.moonrise-plugin.json")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            plugins.Add(Load(manifestPath, directory));
        if (plugins.GroupBy(plugin => plugin.Manifest.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Client plugin identifiers must be unique.");
        return plugins;
    }

    public RegisteredClientPlugin Load(string manifestPath, string pluginRoot)
    {
        var manifest = JsonSerializer.Deserialize<ClientPluginManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("The client plugin manifest is empty.");
        if (manifest.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported client plugin schema version: {manifest.SchemaVersion}.");
        if (!PluginIdPattern().IsMatch(manifest.Id) || string.Equals(manifest.Id, "lunar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The client plugin id is invalid or reserved.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Executable))
            throw new InvalidDataException("The client plugin name and executable are required.");

        var root = Path.GetFullPath(pluginRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var executable = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, manifest.Executable));
        if (!executable.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The client plugin executable must stay inside the plugins directory.");
        if (!string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The client plugin executable must be a Windows .exe file.");
        if (!File.Exists(executable)) throw new FileNotFoundException("The client plugin executable was not found.", executable);
        return new RegisteredClientPlugin(manifest, executable);
    }

    public string WriteLaunchRequest(
        RegisteredClientPlugin plugin,
        string gameVersion,
        IEnumerable<PackageInfo> mods,
        IEnumerable<PackageInfo> agents,
        bool safeMode,
        string requestDirectory)
    {
        if (!plugin.Manifest.SupportedGameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"The client plugin does not support Minecraft {gameVersion}.");
        Directory.CreateDirectory(requestDirectory);
        var request = new ClientPluginLaunchRequest
        {
            ClientId = plugin.Manifest.Id,
            GameVersion = gameVersion,
            WorkingDirectory = Path.GetDirectoryName(plugin.ExecutablePath)!,
            EnabledModPaths = safeMode ? [] : mods.Select(package => package.FullPath).ToArray(),
            EnabledAgentPaths = safeMode ? [] : agents.Select(package => package.FullPath).ToArray(),
            SafeMode = safeMode
        };
        var path = Path.Combine(requestDirectory, $"request-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(request, JsonOptions));
        return path;
    }

    public Process Launch(RegisteredClientPlugin plugin, string requestPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = plugin.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(plugin.ExecutablePath),
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--moonrise-request");
        startInfo.ArgumentList.Add(Path.GetFullPath(requestPath));
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The client plugin did not start a process.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdPattern();
}
