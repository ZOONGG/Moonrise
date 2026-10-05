using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Moonrise.Compatibility;

/// <summary>Read-only adapter for the observed Lunar Genesis 9.10.1 / MC 1.8.9 profile contract.</summary>
public static class ControlledGenesisDiscovery
{
    public const string ContractId = "lunar-genesis-9.10.1-mc-1.8.9";
    public const string GenesisMain = "com.moonsworth.lunar.genesis.Genesis";
    private const string GenesisSha256 = "a86d3446fdce9012bcb4715ca7ea2045db58913dd2e89d3b94e3cf226b2fac80";

    public static ControlledGenesisRuntimeSnapshot Discover(string lunarRoot, string gameDirectory,
        string runtimeDirectory, string sessionDirectory, string profileId, string gameVersion,
        IReadOnlyList<string> profileLoaders) => Discover(lunarRoot, gameDirectory, runtimeDirectory,
            sessionDirectory, profileId, gameVersion, profileLoaders, GenesisSha256);

    internal static ControlledGenesisRuntimeSnapshot Discover(string lunarRoot, string gameDirectory,
        string runtimeDirectory, string sessionDirectory, string profileId, string gameVersion,
        IReadOnlyList<string> profileLoaders, string expectedGenesisSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lunarRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(profileLoaders);
        var root = Path.GetFullPath(lunarRoot);
        var game = Path.GetFullPath(gameDirectory);
        _ = Path.GetFullPath(runtimeDirectory);
        var session = Path.GetFullPath(sessionDirectory);
        if (!string.Equals(gameVersion, "1.8.9", StringComparison.Ordinal) ||
            profileLoaders.Count != 1 || !string.Equals(profileLoaders[0], "ichor", StringComparison.Ordinal))
            throw new NotSupportedException("This profile does not match the observed Lunar Minecraft 1.8.9 Ichor contract.");

        var offline = Path.Combine(root, "offline", "multiver");
        var genesis = Capture(Path.Combine(offline, "genesis-0.1.0-SNAPSHOT-all.jar"), "lunar.genesis");
        if (!string.Equals(genesis.Sha256, expectedGenesisSha256, StringComparison.Ordinal))
            throw new NotSupportedException("The installed Genesis snapshot is outside the verified 9.10.1 contract.");
        VerifyGenesis(genesis.CanonicalPath);
        var profile = Path.Combine(game, "versions", "1.8.9", "1.8.9.jar");
        var assetIndex = Path.Combine(game, "assets", "indexes", "1.8.json");
        var sharedAssets = Path.Combine(root, "shared", "assets", "indexes", "1.8.json");
        if (!File.Exists(assetIndex) && File.Exists(sharedAssets)) assetIndex = sharedAssets;

        // versions.conf in the pinned Genesis JAR identifies the default lunar module as
        // [lunar, :external:optifine], with a separate private noOF module.
        var ichor = ImmutableArray.Create(
            Capture(Path.Combine(offline, "lunar.jar"), "lunar.ichor-module:lunar"),
            Capture(Path.Combine(offline, "legacy-0.1.0-SNAPSHOT-all-nomappings.jar"), "lunar.ichor-module:legacy"),
            Capture(Path.Combine(offline, "common-0.1.0-SNAPSHOT-all-nomappings.jar"), "lunar.ichor-module:common"),
            Capture(Path.Combine(offline, "optifine-0.1.0-SNAPSHOT-all.jar"), "lunar.ichor-module:optifine"),
            Capture(Path.Combine(offline, "lunar-platform-mappings-v1_8.jar"), "lunar.ichor-mapping:v1_8"));
        // Ichor discovers module providers through ServiceLoader on the bootstrap
        // classloader. ichorClassPath describes the later baker inputs; it does
        // not add these JARs to the bootstrap classloader.
        // Lunar language bytecode uses ClassLoader.getResourceAsStream; this
        // resource-only JAR must also be visible to the bootstrap parent loader.
        var language = Capture(Path.Combine(offline, "lunar-lang.jar"), "lunar.profile-resource:language");
        var classPath = ImmutableArray.Create(genesis).AddRange(ichor).Add(language);
        var minecraftJar = Capture(profile, "minecraft.version:1.8.9");
        var external = ImmutableArray.Create(
            Capture(Path.Combine(offline, "OptiFine_v1_8.jar"), "lunar.profile-external:optifine"),
            minecraftJar);
        var nativesDirectory = Path.Combine(offline, "natives");
        if (!Directory.Exists(nativesDirectory) || !File.Exists(Path.Combine(nativesDirectory, "lwjgl64.dll")))
            throw new NotSupportedException("The provisioned Lunar Windows x64 native directory is incomplete; open Lunar Client to complete provisioning.");
        // Native discovery never traverses mixed data roots or snapshots arbitrary
        // JSON/account-looking files. Web resources are frozen separately.
        var natives = Directory.EnumerateFiles(nativesDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => { ControlledGenesisResources.EnsureBounded(root, path); return Capture(path, "lunar.native:" + Path.GetRelativePath(nativesDirectory, path).Replace('\\', '/')); })
            .ToImmutableArray();
        if (natives.IsEmpty) throw new NotSupportedException("The Lunar native directory is empty.");
        if (!File.Exists(assetIndex))
            throw new NotSupportedException("The Lunar 1.8 asset index is missing; open Lunar Client to complete provisioning.");

        var evidence = ImmutableArray.Create(
            new Evidence("profiles.db.read-only", "exact profile id and game version supplied by selected lunar profile"),
            new Evidence("versions.conf:1.8.9.modules", "default lunar module declares lunar and :external:optifine"),
            new Evidence("versions.conf:1.8.9.modules", "lunar-noOF is private and not the default module"),
            new Evidence("genesis.manifest", "Main-Class com.moonsworth.lunar.genesis.Genesis; Implementation-Version 9.10.1"),
            new Evidence("genesis.bytecode", "version, classpathDir, workingDirectory, ichorClassPath and ichorExternalFiles options are consumed"),
            new Evidence("genesis.bytecode", "the pinned Genesis contract creates safe in-menu accessToken=0 and standard game arguments"));
        var assetArtifact = Capture(assetIndex, "minecraft.asset-index:1.8");
        var fingerprints = classPath.Concat(ichor).Concat(external).Append(minecraftJar).Concat(natives).Append(assetArtifact)
            .OrderBy(a => a.CanonicalPath, StringComparer.Ordinal).ToImmutableArray();
        return new(root, offline, PlanSerialization.SnapshotId(fingerprints), "9.10.1", classPath, ichor,
            external, natives, game, session, "1.8", GenesisMain, "optifine", evidence);
    }

    private static ArtifactSnapshot Capture(string path, string provenance)
    {
        if (!File.Exists(path)) throw new NotSupportedException("Required Lunar runtime artifact is missing: " + provenance + ". Open Lunar Client to complete provisioning.");
        return ArtifactSnapshots.Capture(path, provenance, false);
    }

    private static void VerifyGenesis(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var manifest = archive.GetEntry("META-INF/MANIFEST.MF") ?? throw new NotSupportedException("Genesis manifest is missing.");
        using var reader = new StreamReader(manifest.Open());
        var text = reader.ReadToEnd();
        if (!Regex.IsMatch(text, @"(?m)^Main-Class:\s*com\.moonsworth\.lunar\.genesis\.Genesis\s*$") ||
            !Regex.IsMatch(text, @"(?m)^Implementation-Version:\s*9\.10\.1\s*$"))
            throw new NotSupportedException("Genesis manifest no longer matches the verified 9.10.1 entry point.");
        var version = archive.GetEntry("versions.conf");
        if (version is null) throw new NotSupportedException("Genesis version module metadata is missing.");
        using var configReader = new StreamReader(version.Open());
        var config = configReader.ReadToEnd();
        var block = Regex.Match(config, @"(?ms)^v1_8:\s*\$\{legacy\}\s*\{(?<body>.*?)^\}");
        if (!block.Success || !block.Groups["body"].Value.Contains("exact-version: \"1.8.9\"", StringComparison.Ordinal) ||
            !block.Groups["body"].Value.Contains("name: \"lunar\"", StringComparison.Ordinal) ||
            !block.Groups["body"].Value.Contains("modules: [\"lunar\", \":external:optifine\"]", StringComparison.Ordinal))
            throw new NotSupportedException("Genesis module metadata no longer proves the Lunar OptiFine 1.8.9 contract.");
    }

}
