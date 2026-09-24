using Moonrise.Models;

namespace Moonrise.Services;

public sealed class LaunchPlanBuilder
{
    public LaunchPlan Build(
        IEnumerable<PackageRuntimeDescriptor> packages,
        WeaveRuntimeMode weaveMode,
        string? weaveLoaderPath = null,
        IEnumerable<string>? jvmArguments = null,
        IEnumerable<LaunchJvmProperty>? jvmProperties = null)
    {
        ArgumentNullException.ThrowIfNull(packages);

        var enabled = packages.ToArray();
        var mods = enabled
            .Where(item => item.Kind == PackageKind.WeaveMod)
            .Select(item => NormalizePath(item.Path))
            .ToArray();

        if (weaveMode == WeaveRuntimeMode.Disabled && mods.Length > 0)
        {
            throw new InvalidOperationException(
                "Weave mods cannot be launched while the Weave runtime is disabled.");
        }

        var agents = new List<LaunchAgent>();
        if (weaveMode != WeaveRuntimeMode.Disabled)
        {
            if (string.IsNullOrWhiteSpace(weaveLoaderPath))
                throw new InvalidOperationException("The selected Weave runtime requires an explicit loader path.");

            agents.Add(new LaunchAgent(
                NormalizePath(weaveLoaderPath),
                Options: null,
                PackageId: null,
                IsWeaveLoader: true));
        }

        foreach (var package in enabled.Where(item => item.Kind == PackageKind.JavaAgent))
        {
            agents.Add(new LaunchAgent(
                NormalizePath(package.Path),
                NormalizeOption(package.AgentOptions),
                package.PackageId,
                IsWeaveLoader: false));
        }

        var arguments = (jvmArguments ?? [])
            .Select(NormalizeArgument)
            .ToArray();
        var properties = (jvmProperties ?? [])
            .Select(NormalizeProperty)
            .ToArray();

        return new LaunchPlan(weaveMode, mods, agents, arguments, properties);
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.IndexOfAny(['\0', '\r', '\n', '"']) >= 0)
            throw new ArgumentException("Runtime path contains unsafe characters.", nameof(path));
        return System.IO.Path.GetFullPath(path);
    }

    private static string? NormalizeOption(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("Java-agent options contain unsafe characters.", nameof(value));
        return value;
    }

    private static string NormalizeArgument(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("JVM argument contains unsafe characters.", nameof(value));
        return value;
    }

    private static LaunchJvmProperty NormalizeProperty(LaunchJvmProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (string.IsNullOrWhiteSpace(property.Name) ||
            property.Name.IndexOfAny(['\0', '\r', '\n', '=']) >= 0 ||
            property.Value.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("JVM property contains unsafe characters.", nameof(property));
        }
        return property;
    }
}
