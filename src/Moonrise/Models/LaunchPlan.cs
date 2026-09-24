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

public enum LaunchAgentRole
{
    NetworkAdapter,
    CompatibilityAdapter,
    WeaveLoader,
    PackageAgent
}

public sealed record TechnicalLaunchAgentDescriptor(
    string RuntimeId,
    string Path,
    LaunchAgentRole Role,
    string? AgentOptions = null);

public sealed record LaunchAgent(
    string Path,
    string? Options,
    string RuntimeId,
    LaunchAgentRole Role)
{
    public bool IsWeaveLoader => Role == LaunchAgentRole.WeaveLoader;
}

public sealed record LaunchJvmProperty(string Name, string Value);

public sealed record LaunchPlan(
    WeaveRuntimeMode WeaveMode,
    IReadOnlyList<string> ModPaths,
    IReadOnlyList<LaunchAgent> Agents,
    IReadOnlyList<string> JvmArguments,
    IReadOnlyList<LaunchJvmProperty> JvmProperties);
