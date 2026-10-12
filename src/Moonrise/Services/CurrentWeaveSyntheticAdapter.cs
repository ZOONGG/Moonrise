using System.IO.Compression;
using Moonrise.Models;

namespace Moonrise.Services;

// OfficialLunar current Weave only; adapter lifetime follows the existing launch session.
public static class CurrentWeaveSyntheticAdapter
{
    public const string Id = "moonrise-current-weave-synthetic-1.0.0";
    public static LaunchPlan Prepare(LaunchPlan plan, string sessionDirectory)
    {
        if (plan.WeaveMode != WeaveRuntimeMode.Current) return plan;
        var weave = plan.Agents.Single(a => a.IsWeaveLoader);
        var genesis = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lunarclient", "offline", "multiver", "genesis-0.1.0-SNAPSHOT-all.jar");
        ValidateHash(weave.Path, "013b7b9a4ca01a473a1ffc0b031c843c70fe61616e9048fb326e03794c8457b7");
        ValidateGenesisHash(genesis);
        var adapter = Path.Combine(sessionDirectory, Id + ".jar");
        WriteJar(adapter);
        var agents = plan.Agents.ToList();
        agents.Insert(0, new LaunchAgent(adapter, weave.Path, Id, LaunchAgentRole.CompatibilityAdapter));
        return plan with { Agents = agents };
    }
    internal static void ValidateHash(string path, string hash)
    {
        if (!File.Exists(path) || !LocalPackageLibrary.ComputeSha256(path).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Current Weave synthetic bridge requires the verified Weave 1.3.4 / Genesis runtime: {Path.GetFileName(path)}.");
    }
    internal static void ValidateGenesisHash(string path)
    {
        // Both exact builds passed SyntheticIntegrationTest against their real game loader.
        var hash = File.Exists(path) ? LocalPackageLibrary.ComputeSha256(path) : "";
        if (!hash.Equals("e3f34c4ad92f1c3bae83c17bbacb45952c156df359cb39d6c34d086b05e83c44", StringComparison.OrdinalIgnoreCase)
            && !hash.Equals("40797ad1d1449ffff7c6e7342b79a3edc8d08517505dc2d57ce9d97e0669389c", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Current Weave synthetic bridge requires a verified Genesis runtime: {Path.GetFileName(path)}.");
    }
    internal static void WriteJar(string path)
    {
        using var file = new FileStream(path, FileMode.CreateNew);
        using var jar = new ZipArchive(file, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(jar.CreateEntry("META-INF/MANIFEST.MF").Open()))
            writer.Write("Manifest-Version: 1.0\r\nPremain-Class: dev.moonrise.current.CurrentSyntheticAgent\r\n\r\n");
        foreach (var item in CurrentSyntheticAdapterClasses.Bytes)
        {
            using var output = jar.CreateEntry(item.Key).Open(); output.Write(Convert.FromBase64String(item.Value));
        }
    }
}
