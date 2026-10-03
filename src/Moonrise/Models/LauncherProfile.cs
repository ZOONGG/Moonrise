namespace Moonrise.Models;

public sealed record LauncherProfile(
    string Id,
    string Name,
    string Client,
    string MajorVersion,
    string GameVersion)
{
    public IReadOnlyList<string> Loaders { get; init; } = [];
    public string? LoaderVersion { get; init; }
    public string? LunarModule { get; init; }
    public string DetailLabel => $"{Name} · {GameVersion} · {Client}" +
        (Loaders.Count > 0 ? $" · {string.Join(", ", Loaders)}" : "") +
        (!string.IsNullOrWhiteSpace(LunarModule) ? $" · {LunarModule}" : "") + $" · {Id}";
}
