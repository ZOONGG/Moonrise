namespace Moonrise.Models;

public sealed class VerifiedCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset GeneratedAt { get; init; }
    public List<VerifiedCatalogPackage> Packages { get; init; } = [];
}

public sealed class VerifiedCatalogPackage
{
    public required string Identifier { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public PackageKind Kind { get; init; } = PackageKind.WeaveMod;
    public required string DownloadUrl { get; init; }
    public required string Sha256 { get; init; }
    public IReadOnlyList<string> GameVersions { get; init; } = [];
    public string VersionLabel => $"{Version} · {string.Join(", ", GameVersions)}";
}

public sealed record VerifiedCatalogResult(
    VerifiedCatalog Catalog,
    string SigningKeyFingerprint);
