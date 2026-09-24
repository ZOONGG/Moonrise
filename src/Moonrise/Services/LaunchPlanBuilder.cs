using Moonrise.Models;

namespace Moonrise.Services;

public sealed class LaunchPlanBuilder
{
    public LaunchPlan Build(
        IEnumerable<PackageRuntimeDescriptor> packages,
        WeaveRuntimeMode weaveMode,
        string? weaveLoaderPath = null,
        IEnumerable<string>? jvmArguments = null,
        IEnumerable<LaunchJvmProperty>? jvmProperties = null,
        IEnumerable<TechnicalLaunchAgentDescriptor>? technicalAgents = null)
    {
        ArgumentNullException.ThrowIfNull(packages);

        var enabled = packages.ToArray();
        var unsupported = enabled.FirstOrDefault(item =>
            item.Kind is not (PackageKind.WeaveMod or PackageKind.JavaAgent));
        if (unsupported is not null)
        {
            throw new InvalidOperationException(
                $"Package '{unsupported.PackageId}' has unsupported runtime kind '{unsupported.Kind}'.");
        }

        foreach (var package in enabled)
            ValidatePackageId(package.PackageId);

        var duplicatePackageId = enabled
            .GroupBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePackageId is not null)
        {
            throw new InvalidOperationException(
                $"Package '{duplicatePackageId.Key}' appears more than once in the launch plan.");
        }

        var normalizedPaths = enabled
            .Select(item => (Package: item, Path: NormalizePath(item.Path)))
            .ToArray();
        var duplicatePath = normalizedPaths
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePath is not null)
        {
            throw new InvalidOperationException(
                $"Runtime path '{duplicatePath.Key}' appears more than once in the launch plan.");
        }

        var mods = normalizedPaths
            .Where(item => item.Package.Kind == PackageKind.WeaveMod)
            .Select(item => item.Path)
            .ToArray();

        if (weaveMode == WeaveRuntimeMode.Disabled && mods.Length > 0)
        {
            throw new InvalidOperationException(
                "Weave mods cannot be launched while the Weave runtime is disabled.");
        }

        var agents = new List<LaunchAgent>();
        foreach (var technical in technicalAgents ?? [])
        {
            ValidateRuntimeId(technical.RuntimeId);
            if (technical.Role is not (LaunchAgentRole.NetworkAdapter or LaunchAgentRole.CompatibilityAdapter))
            {
                throw new InvalidOperationException(
                    $"Technical launch agent '{technical.RuntimeId}' has unsupported role '{technical.Role}'.");
            }
            agents.Add(new LaunchAgent(
                NormalizePath(technical.Path),
                NormalizeOption(technical.AgentOptions),
                technical.RuntimeId,
                technical.Role));
        }

        if (weaveMode != WeaveRuntimeMode.Disabled)
        {
            if (string.IsNullOrWhiteSpace(weaveLoaderPath))
                throw new InvalidOperationException("The selected Weave runtime requires an explicit loader path.");

            agents.Add(new LaunchAgent(
                NormalizePath(weaveLoaderPath),
                Options: null,
                RuntimeId: WeaveRuntimeId(weaveMode),
                Role: LaunchAgentRole.WeaveLoader));
        }

        foreach (var item in normalizedPaths.Where(item => item.Package.Kind == PackageKind.JavaAgent))
        {
            agents.Add(new LaunchAgent(
                item.Path,
                NormalizeOption(item.Package.AgentOptions),
                item.Package.PackageId,
                LaunchAgentRole.PackageAgent));
        }

        var duplicateAgentPath = agents
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAgentPath is not null)
        {
            throw new InvalidOperationException(
                $"Java-agent path '{duplicateAgentPath.Key}' appears more than once in the launch plan.");
        }

        var duplicateRuntimeId = agents
            .GroupBy(item => item.RuntimeId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRuntimeId is not null)
        {
            throw new InvalidOperationException(
                $"Launch-agent runtime ID '{duplicateRuntimeId.Key}' appears more than once in the launch plan.");
        }

        var arguments = (jvmArguments ?? [])
            .Select(NormalizeArgument)
            .ToArray();
        var properties = (jvmProperties ?? [])
            .Select(NormalizeProperty)
            .ToArray();
        var duplicateProperty = properties
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProperty is not null)
        {
            throw new InvalidOperationException(
                $"JVM property '{duplicateProperty.Key}' appears more than once in the launch plan.");
        }

        return new LaunchPlan(weaveMode, mods, agents, arguments, properties);
    }

    private static string WeaveRuntimeId(WeaveRuntimeMode mode) => mode switch
    {
        WeaveRuntimeMode.Current => "weave-loader-current",
        WeaveRuntimeMode.Legacy => "weave-loader-legacy",
        WeaveRuntimeMode.Custom => "weave-loader-custom",
        _ => throw new InvalidOperationException("Disabled Weave runtime does not have a loader ID.")
    };

    private static void ValidateRuntimeId(string runtimeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeId);
        if (runtimeId.IndexOfAny(['\0', '\r', '\n', '\t', '"']) >= 0)
            throw new ArgumentException("Runtime ID contains unsafe characters.", nameof(runtimeId));
    }

    private static void ValidatePackageId(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (packageId.IndexOfAny(['\0', '\r', '\n', '\t', '"']) >= 0)
            throw new ArgumentException("Package ID contains unsafe characters.", nameof(packageId));
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.IndexOfAny(['\0', '\r', '\n', '\t', '"']) >= 0)
            throw new ArgumentException("Runtime path contains unsafe characters.", nameof(path));
        return System.IO.Path.GetFullPath(path);
    }

    private static string? NormalizeOption(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.IndexOfAny(['\0', '\r', '\n', '\t', '"']) >= 0)
            throw new ArgumentException("Java-agent options contain unsafe characters.", nameof(value));
        return value;
    }

    private static string NormalizeArgument(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOfAny(['\0', '\r', '\n', '\t']) >= 0)
            throw new ArgumentException("JVM argument contains unsafe characters.", nameof(value));
        if (value.StartsWith("-javaagent:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Java agents must be expressed through structured launch agents, not raw JVM arguments.");
        }
        if (value.StartsWith("-D", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "JVM properties must be expressed through structured launch properties, not raw JVM arguments.");
        }
        return value;
    }

    private static LaunchJvmProperty NormalizeProperty(LaunchJvmProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (string.IsNullOrWhiteSpace(property.Name) ||
            property.Name.IndexOfAny(['\0', '\r', '\n', '\t', '"', '=']) >= 0 ||
            property.Value.IndexOfAny(['\0', '\r', '\n', '\t', '"']) >= 0)
        {
            throw new ArgumentException("JVM property contains unsafe characters.", nameof(property));
        }
        return property;
    }
}
