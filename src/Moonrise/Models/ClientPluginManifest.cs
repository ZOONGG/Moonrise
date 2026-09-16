namespace Moonrise.Models;

public sealed class ClientPluginManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Executable { get; init; }
    public IReadOnlyList<string> SupportedGameVersions { get; init; } = [];
}

public sealed record RegisteredClientPlugin(ClientPluginManifest Manifest, string ExecutablePath);

public sealed record ClientChoice(string Id, string Name, RegisteredClientPlugin? Plugin)
{
    public bool IsBuiltIn => Plugin is null;
    public override string ToString() => Name;
}

public sealed class ClientPluginLaunchRequest
{
    public int SchemaVersion { get; init; } = 1;
    public required string ClientId { get; init; }
    public required string GameVersion { get; init; }
    public required string WorkingDirectory { get; init; }
    public IReadOnlyList<string> EnabledModPaths { get; init; } = [];
    public IReadOnlyList<string> EnabledAgentPaths { get; init; } = [];
    public bool SafeMode { get; init; }
}
