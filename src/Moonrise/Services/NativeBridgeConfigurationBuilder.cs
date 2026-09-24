using System.Text;
using Moonrise.Models;

namespace Moonrise.Services;

public static class NativeBridgeConfigurationBuilder
{
    public static string Build(
        string primaryAgentPath,
        string compatibilityDirectory,
        IReadOnlyList<string>? additionalAgentPaths = null)
    {
        var fullPrimaryAgentPath = NormalizePath(primaryAgentPath);
        var builder = new StringBuilder("MNR3\n");
        AppendLine(builder, fullPrimaryAgentPath);
        AppendLine(builder, NormalizePath(compatibilityDirectory));
        var emittedAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            fullPrimaryAgentPath
        };
        foreach (var agentPath in additionalAgentPaths ?? [])
        {
            var fullAgentPath = NormalizePath(agentPath);
            if (emittedAgents.Add(fullAgentPath))
            {
                AppendLine(builder, fullAgentPath);
            }
        }

        return builder.ToString();
    }

    public static string BuildLaunchPlan(
        LaunchPlan plan,
        string compatibilityDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder("MNR4\n");
        AppendTaggedLine(builder, "mode", plan.WeaveMode.ToString());
        AppendTaggedLine(builder, "mods", NormalizePath(compatibilityDirectory));

        foreach (var agent in plan.Agents)
        {
            var path = NormalizePath(agent.Path);
            var options = NormalizeField(agent.Options);
            var packageId = NormalizeField(agent.PackageId);
            AppendTaggedLine(
                builder,
                "agent",
                path,
                options ?? string.Empty,
                packageId ?? string.Empty,
                agent.Role.ToString());
        }

        foreach (var argument in plan.JvmArguments)
            AppendTaggedLine(builder, "arg", NormalizeField(argument) ?? string.Empty);

        foreach (var property in plan.JvmProperties)
        {
            AppendTaggedLine(
                builder,
                "prop",
                NormalizeField(property.Name) ?? string.Empty,
                NormalizeField(property.Value) ?? string.Empty);
        }

        return builder.ToString();
    }

    private static void AppendTaggedLine(
        StringBuilder builder,
        string tag,
        params string[] fields)
    {
        builder.Append(tag);
        foreach (var field in fields)
            builder.Append('\t').Append(field);
        builder.Append('\n');
    }

    private static string? NormalizeField(string? value)
    {
        if (value is null)
            return null;
        if (value.IndexOfAny(['\0', '\r', '\n', '\t']) >= 0)
            throw new ArgumentException("Bridge configuration field contains unsafe characters.", nameof(value));
        return value;
    }

    private static void AppendLine(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.IndexOfAny(['\0', '\r', '\n', '\t', '"']) >= 0)
        {
            throw new ArgumentException("The path contains unsafe characters.", nameof(path));
        }

        return Path.GetFullPath(path);
    }
}

public static class BridgeConfigurationFile
{
    public static void WriteAtomic(
        string bridgeConfigPath,
        LaunchPlan plan,
        string compatibilityDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeConfigPath);
        ArgumentNullException.ThrowIfNull(plan);
        WriteAtomicContent(
            bridgeConfigPath,
            NativeBridgeConfigurationBuilder.BuildLaunchPlan(plan, compatibilityDirectory));
    }

    public static void WriteAtomic(
        string bridgeConfigPath,
        string primaryAgentPath,
        string enabledModsDirectory,
        IReadOnlyList<string>? additionalAgentPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeConfigPath);
        WriteAtomicContent(
            bridgeConfigPath,
            NativeBridgeConfigurationBuilder.Build(
                primaryAgentPath,
                enabledModsDirectory,
                additionalAgentPaths));
    }

    private static void WriteAtomicContent(string bridgeConfigPath, string content)
    {
        var fullPath = Path.GetFullPath(bridgeConfigPath);
        var configDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Unable to determine bridge configuration directory.");
        Directory.CreateDirectory(configDirectory);
        var temporaryPath = Path.Combine(
            configDirectory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
