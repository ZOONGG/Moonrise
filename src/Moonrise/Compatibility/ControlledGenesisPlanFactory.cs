using System.Collections.Immutable;

namespace Moonrise.Compatibility;

/// <summary>Builds a frozen clean-baseline plan from the Stage 2A discovery adapter.</summary>
public static class ControlledGenesisPlanFactory
{
    public static LaunchPlan Create(string lunarRoot, string gameDirectory, string lunarJreDirectory,
        string sessionDirectory, string profileId, string gameVersion, IReadOnlyList<string> profileLoaders,
        JavaRuntimeMode javaMode = JavaRuntimeMode.Auto, string? customJava = null,
        string userJvmArgumentsText = "", int minimumMemoryMb = 1024, int maximumMemoryMb = 4096,
        IEnumerable<EnvironmentOption>? inheritedEnvironment = null)
    {
        var runtime = ControlledGenesisDiscovery.Discover(lunarRoot, gameDirectory, lunarJreDirectory,
            sessionDirectory, profileId, gameVersion, profileLoaders);
        var javaCandidates = DiscoverJreCandidates(lunarJreDirectory);
        var resources = ControlledGenesisResources.Discover(ControlledGenesisDiscovery.ContractId, lunarRoot, sessionDirectory);
        var runtimeView = Path.Combine(runtime.WorkingDirectory, "runtime");
        var runtimeFiles = runtime.ClassPath.Concat(runtime.IchorClassPath).Concat(runtime.IchorExternalFiles)
            .DistinctBy(a => a.CanonicalPath).Where(a => Path.GetDirectoryName(a.CanonicalPath) == runtime.OfflineRoot).ToImmutableArray();
        resources = resources.Add(new(runtime.OfflineRoot, runtimeView, "Genesis classpathDir and generated bake cache", true,
            "Moonrise-owned launch copy", ControlledGenesisDiscovery.ContractId, runtimeFiles));
        var minecraft = runtime.IchorExternalFiles.Single(a => a.Source == "minecraft.version:1.8.9");
        resources = resources.Add(new(Path.GetDirectoryName(minecraft.CanonicalPath)!, runtimeView, "Minecraft external artifact for Genesis classpathDir", true,
            "Moonrise-owned launch copy", "Minecraft 1.8.9", [minecraft]));
        var requiredJvmArgs = ImmutableArray.Create("-XX:+UseG1GC",
            "-Dlunar.dataDir=" + Path.Combine(Path.GetFullPath(sessionDirectory), "lunar-data"),
            "-Djava.library.path=" + Path.Combine(runtime.OfflineRoot, "natives") + Path.PathSeparator + Path.Combine(runtime.WorkingDirectory, "natives"),
            "-Dlunar.webosr.bundlePath=.", "-Dlunar.webosr.url=file:///index.html");
        // The 1.8.9 version metadata calls the asset index "1.8". Plan.AssetIndex retains
        // the actual verified index file, while Genesis forwards this Minecraft game argument.
        var args = ImmutableArray.Create("--gameDir", runtime.GameDirectory,
            "--assetsDir", Path.Combine(runtime.GameDirectory, "assets"),
            "--assetIndex", "1.8", "--width", "854", "--height", "480");
        var request = new PlanRequest(ControlledGenesisDiscovery.ContractId, "1.8.9", profileId,
            Backend.ControlledGenesis,
            new BaseLoader(runtime.LoaderId, "HD_U_M6_pre2", runtime.Evidence),
            [], [], new WeaveSelection(WeaveStrategy.Off, null, null, WeaveGeneration.Unknown, [], null),
            javaMode, customJava, javaCandidates, new JavaRequirement(17, 17, "x64"),
            minimumMemoryMb, maximumMemoryMb, userJvmArgumentsText, requiredJvmArgs,
            inheritedEnvironment?.ToImmutableArray() ?? [], runtime.WorkingDirectory, runtime.GameDirectory,
            runtime.MainClass, runtime.AssetIndex, runtime.ClassPath, runtime.IchorClassPath,
            runtime.IchorExternalFiles, runtime.NativeArtifacts, args) { ResourceRoots = resources, RuntimeFileDirectory = runtimeView };
        return CompatibilityPlanner.Build(request);
    }

    private static ImmutableArray<JavaCandidate> DiscoverJreCandidates(string lunarJreDirectory)
    {
        if (!Directory.Exists(lunarJreDirectory)) return [];
        var candidates = ImmutableArray.CreateBuilder<JavaCandidate>();
        foreach (var location in Directory.EnumerateDirectories(lunarJreDirectory).OrderBy(p => p, StringComparer.Ordinal).Take(128))
        {
            var direct = JavaRuntimes.Inspect(location);
            if (direct != null) candidates.Add(direct);
            foreach (var nested in Directory.EnumerateDirectories(location).OrderBy(p => p, StringComparer.Ordinal).Take(16))
            {
                var candidate = JavaRuntimes.Inspect(nested);
                if (candidate != null) candidates.Add(candidate);
            }
        }
        return candidates.DistinctBy(c => c.Executable, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    }
}
