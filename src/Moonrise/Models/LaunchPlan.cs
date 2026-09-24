namespace Moonrise.Models;

public enum WeaveRuntimeMode
{
    Disabled,
    Current,
    Legacy,
    Custom
}

public sealed record PackageRuntimeDescriptor(
    string PackageId,
    PackageKind Kind,
    string Path,
    string? AgentOptions = null);

public sealed record LaunchAgent(
    string Path,
    string? Options,
    string? PackageId,
    bool IsWeaveLoader);

public sealed record LaunchJvmProperty(string Name, string Value);

public sealed record LaunchPlan(
    WeaveRuntimeMode WeaveMode,
    IReadOnlyList<string> ModPaths,
    IReadOnlyList<LaunchAgent> Agents,
    IReadOnlyList<string> JvmArguments,
    IReadOnlyList<LaunchJvmProperty> JvmProperties);
