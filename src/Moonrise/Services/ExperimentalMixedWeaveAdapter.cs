using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text;
using Moonrise.Models;

namespace Moonrise.Services;

// Explicit opt-in. Unknown legacy archives remain fail-closed.
public static class ExperimentalMixedWeaveAdapter
{
    public const string Flag = "--dev-experimental-raven-bwh";
    public const string Id = "moonrise-mixed-raven-abi-0.1.0";
    public const string RavenHash = "c6edd2979121df4f7c2585abee8da0b84e3b7b5496b0b1a4a684edad179727a1";
    public const string BwhHash = "6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57";
    public const string AdapterHash = "b1e5408c014486731e24b56efcd7b32f2219249a17accaca9f897270c2eb70e4";
    public const string AbiHash = "f73cda328c5538be38316207c184b3d6734e0a63feba0f958e9ce57304c473bc";

    // Runtime adaptation is structural; this allowlist bounds the legacy ABI to
    // archives whose lifecycle was actually verified on Lunar. Current packages
    // and package agents retain the normal preflight and are never rewritten.
    public static void ValidateSelection(IReadOnlyList<PackageInfo> mods, IReadOnlyList<PackageInfo> agents)
    {
        foreach (var package in mods.Concat(agents))
            CurrentWeaveSyntheticAdapter.ValidateHash(package.FullPath, package.Sha256);
        var inspection = new WeaveModApiInspector().Inspect(mods);
        if (!inspection.HasLegacy) throw new InvalidDataException("No legacy package needs the experimental adapter.");
        foreach (var item in inspection.Mods)
        {
            CurrentWeaveSyntheticAdapter.ValidateHash(item.Package.FullPath, item.Package.Sha256);
            if (item.Generation is WeaveApiGeneration.Legacy02 or WeaveApiGeneration.Mixed &&
                !item.Package.Sha256.Equals(RavenHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{item.Package.OriginalFileName}: this legacy ABI has not been verified with the experimental Weave 1.3.4 adapter.");
        }
        foreach (var agent in agents) CurrentWeaveSyntheticAdapter.ValidateHash(agent.FullPath, agent.Sha256);
    }

    private static void WriteSelection(string agentPath, IReadOnlyList<PackageInfo> mods)
    {
        using var agentJar = ZipFile.Open(agentPath, ZipArchiveMode.Update);
        using var writer = new StreamWriter(agentJar.CreateEntry("moonrise-mixed/selection.properties").Open(), new UTF8Encoding(false));
        var legacy = new WeaveModApiInspector().Inspect(mods).Mods.Where(m =>
            m.Generation is WeaveApiGeneration.Legacy02 or WeaveApiGeneration.Mixed).ToArray();
        writer.WriteLine($"count={legacy.Length}");
        for (var i = 0; i < legacy.Length; i++)
        {
            var mod = legacy[i].Package;
            using var jar = ZipFile.OpenRead(mod.FullPath);
            using var input = jar.GetEntry("weave.mod.json")!.Open();
            using var bytes = new MemoryStream(); input.CopyTo(bytes);
            var original = bytes.ToArray();
            var metadata = JsonNode.Parse(original)!.AsObject();
            var about = metadata["about"] as JsonObject;
            var name = about?["name"]?.GetValue<string>() ?? mod.DisplayName;
            // Preserve declarations; translate only the legacy schema contract.
            metadata["name"] = name;
            metadata["modId"] = "moonrise-legacy-" + mod.Sha256.ToLowerInvariant()[..16];
            metadata["namespace"] = "mcp-named";
            metadata["compiledFor"] = "1.8.9";
            metadata["entryPoints"] = metadata["entrypoints"]?.DeepClone() ?? new JsonArray();
            metadata.Remove("entrypoints"); metadata.Remove("about");
            foreach (var key in new[] { "mixinConfigs", "hooks", "tweakers", "accessWideners", "dependencies" })
                metadata[key] ??= new JsonArray();
            writer.WriteLine($"{i}.hash={mod.Sha256.ToLowerInvariant()}");
            writer.WriteLine($"{i}.original={Convert.ToBase64String(original)}");
            writer.WriteLine($"{i}.current={Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata.ToJsonString()))}");
        }
    }

    public static LaunchPlan Prepare(LaunchPlan plan, string enabledModsDirectory, string adapterPath, IReadOnlyList<PackageInfo>? mods = null)
    {
        if (plan.WeaveMode != WeaveRuntimeMode.Current)
            throw new InvalidDataException("Mixed experiment requires one current Weave runtime.");
        var weave = plan.Agents.Single(a => a.IsWeaveLoader);
        CurrentWeaveSyntheticAdapter.ValidateHash(weave.Path, "013b7b9a4ca01a473a1ffc0b031c843c70fe61616e9048fb326e03794c8457b7");
        CurrentWeaveSyntheticAdapter.ValidateHash(adapterPath, AdapterHash);
        var abiPath = Path.Combine(Path.GetDirectoryName(adapterPath)!, Id + ".jar");
        CurrentWeaveSyntheticAdapter.ValidateHash(abiPath, AbiHash);
        var copy = Path.Combine(enabledModsDirectory, "000-" + Id + ".jar");
        File.Copy(abiPath, copy, overwrite: false);
        CurrentWeaveSyntheticAdapter.ValidateHash(copy, AbiHash);
        var agentCopy = Path.Combine(Path.GetDirectoryName(enabledModsDirectory)!, "moonrise-mixed-agent-0.1.0.jar");
        File.Copy(adapterPath, agentCopy, overwrite: false);
        CurrentWeaveSyntheticAdapter.ValidateHash(agentCopy, AdapterHash);
        if (mods is not null)
        {
            ValidateSelection(mods, []);
            WriteSelection(agentCopy, mods);
        }
        var agents = plan.Agents.ToList();
        agents.Insert(0, new LaunchAgent(agentCopy, weave.Path, Id, LaunchAgentRole.CompatibilityAdapter));
        return plan with { Agents = agents };
    }
}
