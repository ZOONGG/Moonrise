namespace Moonrise.Services;

public static class JavaToolOptionsBuilder
{
    public static string Build(
        string? existingValue,
        string weaveAgentPath,
        string modsDirectory,
        IEnumerable<string>? additionalAgentPaths = null)
    {
        var additions = new List<string>
        {
            $"-javaagent:{Quote(weaveAgentPath)}",
            $"-Dweave.mods.directory={Quote(modsDirectory)}",
            "-Dweave.api.minecraft.enabled=true",
            "-Dweave.dump.bytecode.enabled=false"
        };
        foreach (var agent in additionalAgentPaths ?? [])
        {
            var fullPath = Path.GetFullPath(agent);
            if (string.Equals(fullPath, Path.GetFullPath(weaveAgentPath), StringComparison.OrdinalIgnoreCase)) continue;
            additions.Add($"-javaagent:{Quote(fullPath)}");
        }

        var additionText = string.Join(" ", additions);
        return string.IsNullOrWhiteSpace(existingValue) ? additionText : $"{existingValue.TrimEnd()} {additionText}";
    }

    public static string Quote(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOfAny(['\0', '"', '\r', '\n']) >= 0)
            throw new ArgumentException("The value contains unsafe characters.", nameof(value));
        return $"\"{value}\"";
    }
}
