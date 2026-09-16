namespace Moonrise.Models;

public sealed class ModpackProfile
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Client { get; init; } = "lunar";
    public string GameVersion { get; init; } = "1.8.9";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<ProfilePackageReference> Packages { get; init; } = [];
}

public sealed record ProfilePackageReference(
    PackageKind Kind,
    string Identifier,
    string Version,
    string Sha256,
    string FileName,
    bool Enabled);
