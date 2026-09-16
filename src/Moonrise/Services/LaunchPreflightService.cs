using Moonrise.Models;

namespace Moonrise.Services;

public sealed class LaunchPreflightService(PackageCompatibilityAnalyzer compatibilityAnalyzer)
{
    public LaunchPreflightResult Prepare(
        string gameVersion,
        IEnumerable<PackageInfo> mods,
        IEnumerable<PackageInfo> agents,
        bool safeMode)
    {
        var enabledMods = mods.Where(package => package.IsEnabled).ToArray();
        var enabledAgents = agents.Where(package => package.IsEnabled).ToArray();
        if (safeMode)
            return new LaunchPreflightResult([], [], new CompatibilityReport([]), SafeMode: true);

        var compatibility = compatibilityAnalyzer.Analyze(gameVersion, enabledMods.Concat(enabledAgents));
        return new LaunchPreflightResult(enabledMods, enabledAgents, compatibility, SafeMode: false);
    }
}
