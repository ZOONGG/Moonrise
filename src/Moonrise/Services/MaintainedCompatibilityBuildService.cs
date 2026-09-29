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
    IReadOnlyList<MaintainedCompatibilityBuildUse> AppliedBuilds);

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

    private readonly AppPaths _paths;
    private readonly JarMetadataParser _parser;
    private readonly IReadOnlyList<MaintainedCompatibilityBuildRule> _rules;

    public MaintainedCompatibilityBuildService(
        AppPaths paths,
        JarMetadataParser parser,
        IEnumerable<MaintainedCompatibilityBuildRule>? rules = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _rules = (rules ?? ProductionRules).ToArray();
    }

    public MaintainedCompatibilityBuildResolution Resolve(
        string minecraftVersion,
        IEnumerable<PackageInfo> enabledMods)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        ArgumentNullException.ThrowIfNull(enabledMods);

        var launchMods = new List<PackageInfo>();
        var applied = new List<MaintainedCompatibilityBuildUse>();
        foreach (var original in enabledMods)
        {
            var rule = _rules.FirstOrDefault(candidate =>
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

            var build = _parser.ParseWeaveMod(buildPath);
            if (!string.Equals(build.Sha256, rule.BuildSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{original.OriginalFileName}: parsed compatibility-build hash changed.");

            launchMods.Add(build);
            applied.Add(new MaintainedCompatibilityBuildUse(rule, original, build));
        }

        return new MaintainedCompatibilityBuildResolution(launchMods, applied);
    }
}
