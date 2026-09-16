using System.Text;

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

    private static void AppendLine(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.IndexOfAny(['\0', '\r', '\n', '"']) >= 0)
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
        string primaryAgentPath,
        string enabledModsDirectory,
        IReadOnlyList<string>? additionalAgentPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeConfigPath);
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
                writer.Write(NativeBridgeConfigurationBuilder.Build(
                    primaryAgentPath,
                    enabledModsDirectory,
                    additionalAgentPaths));
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
