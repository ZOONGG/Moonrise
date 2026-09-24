using Moonrise.Models;

namespace Moonrise.Services;

public sealed class CurrentLaunchPlanFactory
{
    private readonly LaunchPlanBuilder _builder = new();

    public LaunchPlan Create(
        IReadOnlyList<PackageInfo> weaveMods,
        IReadOnlyList<PackageInfo> javaAgents,
        string? weaveLoaderPath,
        bool useLegacyWeave,
        string? legacyWeaveAdapterPath = null,
        string? networkAdapterPath = null)
    {
        ArgumentNullException.ThrowIfNull(weaveMods);
        ArgumentNullException.ThrowIfNull(javaAgents);

        if (weaveMods.Count > 0 && string.IsNullOrWhiteSpace(weaveLoaderPath))
        {
            throw new InvalidOperationException(
                "Weave mods require an explicit Weave loader path.");
        }

        if (weaveMods.Count == 0 &&
            (useLegacyWeave ||
             !string.IsNullOrWhiteSpace(legacyWeaveAdapterPath) ||
             !string.IsNullOrWhiteSpace(networkAdapterPath)))
        {
            throw new InvalidOperationException(
                "Technical Weave adapters cannot be loaded when no Weave mods are enabled.");
        }

        if (!useLegacyWeave && !string.IsNullOrWhiteSpace(legacyWeaveAdapterPath))
        {
            throw new InvalidOperationException(
                "A legacy Weave adapter cannot be used with the current Weave runtime.");
        }

        if (useLegacyWeave && weaveMods.Count > 0 && string.IsNullOrWhiteSpace(legacyWeaveAdapterPath))
        {
            throw new InvalidOperationException(
                "Legacy Weave mods require the legacy directory compatibility adapter.");
        }

        var packages = weaveMods
            .Concat(javaAgents)
            .Select(item => new PackageRuntimeDescriptor(
                item.PackageId,
                item.Kind,
                item.FullPath))
            .ToArray();

        var technicalAgents = new List<TechnicalLaunchAgentDescriptor>();
        if (!string.IsNullOrWhiteSpace(networkAdapterPath))
        {
            technicalAgents.Add(new TechnicalLaunchAgentDescriptor(
                "moonrise-bwh-network-adapter",
                networkAdapterPath,
                LaunchAgentRole.NetworkAdapter));
        }

        if (!string.IsNullOrWhiteSpace(legacyWeaveAdapterPath))
        {
            technicalAgents.Add(new TechnicalLaunchAgentDescriptor(
                LegacyWeaveDirectoryAdapterService.Id,
                legacyWeaveAdapterPath,
                LaunchAgentRole.CompatibilityAdapter));
        }

        var weaveMode = weaveMods.Count == 0
            ? WeaveRuntimeMode.Disabled
            : useLegacyWeave
                ? WeaveRuntimeMode.Legacy
                : WeaveRuntimeMode.Current;

        return _builder.Build(
            packages,
            weaveMode,
            weaveMode == WeaveRuntimeMode.Disabled ? null : weaveLoaderPath!,
            technicalAgents: technicalAgents);
    }
}
