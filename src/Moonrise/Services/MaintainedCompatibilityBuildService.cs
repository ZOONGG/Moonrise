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

public sealed record MoonriseOwnedSuccessorRule(
    string Id,
    string TargetSha256,
    string SuccessorSha256,
    string SuccessorFileName,
    string SuccessorIdentifier,
    string MinecraftVersion,
    string WeaveLoaderVersion);

public sealed record MoonriseOwnedSuccessorUse(
    MoonriseOwnedSuccessorRule Rule,
    PackageInfo RequestedPackage,
    PackageInfo SuccessorPackage);

public sealed record MoonriseOwnedSuccessorResolution(
    IReadOnlyList<PackageInfo> LaunchMods,
    IReadOnlyList<MoonriseOwnedSuccessorUse> AppliedSuccessors);

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

    private static readonly MoonriseOwnedSuccessorRule[] ProductionSuccessorRules =
    [
        new(
            "moonrise-stormy-52ed-successor-veyra-ddc7",
            "52ED9DA4C4F570FD43B32671E0D94FA5FC9E441168645D2E3DE054169374B021",
            "DDC75A6697C706A323D4862B85CF886AA9E24BB0F90AA907D4CA3337C4B9D9E4",
            "ddc75a6697c706a323d4862b85cf886aa9e24bb0f90aa907d4ca3337c4b9d9e4.jar",
            "veyra",
            "1.8.9",
            WeaveAgentService.Version)
    ];

    private readonly AppPaths _paths;
    private readonly JarMetadataParser _parser;
    private readonly IReadOnlyList<MaintainedCompatibilityBuildRule> _rules;
    private readonly IReadOnlyList<MoonriseOwnedSuccessorRule> _successorRules;

    public MaintainedCompatibilityBuildService(
        AppPaths paths,
        JarMetadataParser parser,
        IEnumerable<MaintainedCompatibilityBuildRule>? rules = null,
        IEnumerable<MoonriseOwnedSuccessorRule>? successorRules = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _rules = (rules ?? ProductionRules).ToArray();
        _successorRules = (successorRules ?? ProductionSuccessorRules).ToArray();
    }

    public MoonriseOwnedSuccessorResolution ResolveMoonriseOwnedSuccessors(
        string minecraftVersion,
        IEnumerable<PackageInfo> enabledMods)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        ArgumentNullException.ThrowIfNull(enabledMods);

        var launchMods = new List<PackageInfo>();
        var applied = new List<MoonriseOwnedSuccessorUse>();
        foreach (var requested in enabledMods)
        {
            var rule = _successorRules.FirstOrDefault(candidate =>
                string.Equals(candidate.TargetSha256, requested.Sha256, StringComparison.OrdinalIgnoreCase));
            if (rule is null)
            {
                launchMods.Add(requested);
                continue;
            }

            if (!string.Equals(rule.MinecraftVersion, minecraftVersion, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(rule.WeaveLoaderVersion, WeaveAgentService.Version, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{requested.OriginalFileName}: Moonrise-owned successor '{rule.Id}' does not target Minecraft {minecraftVersion} and Weave Loader {WeaveAgentService.Version}.");
            }

            var successorPath = Path.Combine(
                _paths.AdaptersDirectory,
                "maintained-builds",
                rule.SuccessorFileName);
            if (!File.Exists(successorPath))
            {
                throw new FileNotFoundException(
                    $"{requested.OriginalFileName}: required Moonrise-owned successor '{rule.Id}' is missing.",
                    successorPath);
            }

            var actualHash = LocalPackageLibrary.ComputeSha256(successorPath);
            if (!string.Equals(actualHash, rule.SuccessorSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{requested.OriginalFileName}: Moonrise-owned successor '{rule.Id}' failed its SHA-256 check.");
            }

            var successor = _parser.ParseWeaveMod(successorPath);
            if (!string.Equals(successor.Identifier, rule.SuccessorIdentifier, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(successor.Sha256, rule.SuccessorSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{requested.OriginalFileName}: Moonrise-owned successor '{rule.Id}' has unexpected metadata.");
            }

            launchMods.Add(successor);
            applied.Add(new MoonriseOwnedSuccessorUse(rule, requested, successor));
        }

        return new MoonriseOwnedSuccessorResolution(
            launchMods
                .GroupBy(package => package.Sha256, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray(),
            applied);
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
