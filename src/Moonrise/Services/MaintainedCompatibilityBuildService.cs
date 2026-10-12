using Moonrise.Infrastructure;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed record MaintainedCompatibilityBuildRule(
    string Id,
    string TargetSha256,
    string BuildSha256,
    string BuildFileName,
    string MinecraftVersion,
    string WeaveLoaderVersion);

public sealed record MaintainedCompatibilityBuildUse(
    MaintainedCompatibilityBuildRule Rule,
    PackageInfo OriginalPackage,
    PackageInfo CompatibilityBuild);

public sealed record MaintainedCompatibilityBuildResolution(
    IReadOnlyList<PackageInfo> LaunchMods,
    IReadOnlyList<MaintainedCompatibilityBuildUse> AppliedBuilds)
{
    public string? LegacyPathBlocker { get; init; }
}

public sealed class MaintainedCompatibilityBuildService
{
    private static readonly MaintainedCompatibilityBuildRule[] ProductionRules =
    [
        new(
            "moonrise-local-bwh-c39f-weave-1.3.4",
            "C39F6F1C891825CD309F543D1EE112522CAC3660AC60D9E96A5A21F20756752D",
            "6DA5B2A67E05E07DEAE2779B4AA3D1B70E5028DED227C41487FF783F5D609A57",
            "6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57.jar",
            "1.8.9",
            WeaveAgentService.Version),
        new(
            "moonrise-local-unrestricted-c9c9-weave-1.3.4",
            "C9C9DC9E75E37291E2992CA7FBD0FF6EB851B811391D9C938DEC14AB73315C05",
            "ADB858F75DA30EACE656D380021DCA0FD0A047ED6AABDAAA68F758F7965A4833",
            "adb858f75da30eace656d380021dca0fd0a047ed6aabdaaa68f758f7965a4833.jar",
            "1.8.9",
            WeaveAgentService.Version)
    ];


    private static readonly MaintainedCompatibilityBuildRule[] AgentRules =
    [
        new("moonrise-cosmetics-production-1.3.2",
            "BFDCFC1E5E00BB922708885CE35159DB0B1E3DE989240BF6F98BD1F269B55AB6",
            "EFBAC1DC8F6B28EE1A13E294FF7DC03880C384306F1FFAB88738E3DCD1DC4354",
            "moonrise-cosmetics-1.3.2.jar", "1.8.9", WeaveAgentService.Version)
    ];

    private readonly AppPaths _paths;
    private readonly JarMetadataParser _parser;
    private readonly IReadOnlyList<MaintainedCompatibilityBuildRule> _rules;
    private readonly IReadOnlyList<MaintainedCompatibilityBuildRule> _agentRules;
    private readonly Action<LaunchPlan> _validateLegacyContract;

    public MaintainedCompatibilityBuildService(
        AppPaths paths,
        JarMetadataParser parser,
        IEnumerable<MaintainedCompatibilityBuildRule>? rules = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _rules = (rules ?? ProductionRules).ToArray();
        _agentRules = AgentRules;
        _validateLegacyContract = plan => LegacyMappingResourceAdapter.ValidateRuntimeContract(
            plan.Agents.Single(agent => agent.IsWeaveLoader).Path);
    }

    internal MaintainedCompatibilityBuildService(AppPaths paths, JarMetadataParser parser,
        IEnumerable<MaintainedCompatibilityBuildRule>? rules, Action<LaunchPlan> validateLegacyContract,
        IEnumerable<MaintainedCompatibilityBuildRule>? agentRules = null)
        : this(paths, parser, rules)
    {
        _validateLegacyContract = validateLegacyContract;
        _agentRules = (agentRules ?? AgentRules).ToArray();
    }

    public MaintainedCompatibilityBuildResolution Resolve(string minecraftVersion, IEnumerable<PackageInfo> enabledMods)
        => ResolveCore(minecraftVersion, enabledMods, _rules, agentBuilds: false);

    public MaintainedCompatibilityBuildResolution ResolveAgents(string minecraftVersion, IEnumerable<PackageInfo> enabledAgents)
        => ResolveCore(minecraftVersion, enabledAgents, _agentRules, agentBuilds: true);

    private MaintainedCompatibilityBuildResolution ResolveCore(
        string minecraftVersion, IEnumerable<PackageInfo> enabledMods,
        IReadOnlyList<MaintainedCompatibilityBuildRule> rules, bool agentBuilds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        ArgumentNullException.ThrowIfNull(enabledMods);

        var originals = enabledMods.ToArray();
        string? legacyBlocker = null;
        // Standalone hooks-only launch only. Multi-mod selections retain the
        // existing exact-hash resolution, including every BWH launch.
        if (!agentBuilds && minecraftVersion == "1.8.9" && originals.Length == 1 && File.Exists(originals[0].FullPath) &&
            new WeaveModApiInspector().IsLegacyHooksOnly(originals[0]))
        {
            if (!LocalPackageLibrary.ComputeSha256(originals[0].FullPath).Equals(originals[0].Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Legacy hooks-only original failed its SHA-256 check.");
            var plan = new CurrentLaunchPlanFactory().Create(originals, [], _paths.LegacyWeaveAgentPath,
                useLegacyWeave: true, legacyWeaveAdapterPath: Path.Combine(_paths.AdaptersDirectory,
                    "runtime", LegacyWeaveDirectoryAdapterService.Id + ".jar"));
            try
            {
                _validateLegacyContract(plan);
                return new MaintainedCompatibilityBuildResolution(originals, []);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // No binary is removed or generated during preflight. Preserve
                // the working maintained fallback when the pinned runtime is absent.
                legacyBlocker = error.Message;
            }
        }

        var launchMods = new List<PackageInfo>();
        var applied = new List<MaintainedCompatibilityBuildUse>();
        foreach (var original in originals)
        {
            var rule = rules.FirstOrDefault(candidate =>
                string.Equals(candidate.TargetSha256, original.Sha256, StringComparison.OrdinalIgnoreCase));
            if (rule is null)
            {
                launchMods.Add(original);
                continue;
            }

            if (!string.Equals(rule.MinecraftVersion, minecraftVersion, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(rule.WeaveLoaderVersion, WeaveAgentService.Version, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{original.OriginalFileName}: local compatibility build '{rule.Id}' does not target Minecraft {minecraftVersion} and Weave Loader {WeaveAgentService.Version}.");
            }

            var buildPath = Path.Combine(_paths.AdaptersDirectory, "maintained-builds", rule.BuildFileName);
            if (!File.Exists(buildPath) && _paths.Mode == AppMode.Development)
            {
                // Development/isolated roots reuse only exact, verified local
                // artifacts from the ordinary per-user adapter library.
                var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Moonrise", "adapters", "maintained-builds", rule.BuildFileName);
                if (File.Exists(shared)) buildPath = shared;
            }
            if (!File.Exists(buildPath))
            {
                // An already imported maintained artifact can satisfy the same
                // exact rule regardless of its display filename.
                foreach (var directory in new[] { agentBuilds ? _paths.AgentPackagesDirectory : _paths.WeavePackagesDirectory,
                             Path.Combine(_paths.InstallationDirectory, "runtime", "maintained-builds") })
                {
                    if (!Directory.Exists(directory)) continue;
                    var found = Directory.EnumerateFiles(directory, "*.jar").FirstOrDefault(path =>
                        LocalPackageLibrary.ComputeSha256(path).Equals(rule.BuildSha256, StringComparison.OrdinalIgnoreCase));
                    if (found is not null) { buildPath = found; break; }
                }
            }
            if (!File.Exists(buildPath))
            {
                throw new FileNotFoundException(
                    $"{original.OriginalFileName}: required local compatibility build '{rule.Id}' is missing.",
                    buildPath);
            }

            var actualBuildHash = LocalPackageLibrary.ComputeSha256(buildPath);
            if (!string.Equals(actualBuildHash, rule.BuildSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{original.OriginalFileName}: local compatibility build '{rule.Id}' failed its SHA-256 check.");
            }

            var build = agentBuilds ? _parser.ParseJavaAgent(buildPath) : _parser.ParseWeaveMod(buildPath);
            if (!string.Equals(build.Sha256, rule.BuildSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{original.OriginalFileName}: parsed compatibility-build hash changed.");

            launchMods.Add(build);
            applied.Add(new MaintainedCompatibilityBuildUse(rule, original, build));
        }

        return new MaintainedCompatibilityBuildResolution(launchMods, applied) { LegacyPathBlocker = legacyBlocker };
    }
}
