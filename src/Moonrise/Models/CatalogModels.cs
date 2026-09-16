using System.Text.Json.Serialization;

namespace Moonrise.Models;

public sealed class CatalogIndex
{
    public const int SupportedSchemaVersion = 1;
    public int SchemaVersion { get; init; } = SupportedSchemaVersion;
    public DateTimeOffset GeneratedAt { get; init; }
    public CatalogSignatureStatus SignatureStatus { get; init; } = CatalogSignatureStatus.UnsignedDevelopment;
    public List<PackageManifest> Packages { get; init; } = [];
}

public sealed class PackageManifest
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public PackageKind Type { get; init; }
    public required string Name { get; init; }
    public required string Summary { get; init; }
    public string Description { get; init; } = string.Empty;
    public List<PackageAuthor> Authors { get; init; } = [];
    public string? Homepage { get; init; }
    public string? Repository { get; init; }
    public PackageLicense? License { get; init; }
    public List<string> Tags { get; init; } = [];
    public string? Icon { get; init; }
    public List<PackageScreenshot> Screenshots { get; init; } = [];
    public List<string> SupportedMinecraftVersions { get; init; } = [];
    public List<string> SupportedClientVersions { get; init; } = [];
    public bool Archived { get; init; }
    public PackageRiskLevel RiskLevel { get; init; } = PackageRiskLevel.Unknown;
    public PackageInstallPolicy InstallPolicy { get; init; } = PackageInstallPolicy.SourceOnly;
    public bool Restricted { get; init; }
    public bool Featured { get; init; }
    public bool Recommended { get; init; }
    public List<PackageRelease> Releases { get; init; } = [];
    public List<PackageDependency> Dependencies { get; init; } = [];
    public List<PackageConflict> Conflicts { get; init; } = [];
    public List<PackageWarning> Warnings { get; init; } = [];
    public PackageStatistics? Statistics { get; init; }

    [JsonIgnore] public PackageRelease? LatestRelease => Releases
        .OrderByDescending(release => release.PublishedAt)
        .FirstOrDefault();
    [JsonIgnore] public string AuthorLabel => string.Join(", ", Authors.Select(author => author.Name));
    [JsonIgnore] public string VersionLabel => LatestRelease?.Version ?? "—";
    [JsonIgnore] public string VersionsLabel => string.Join(", ", SupportedMinecraftVersions);
    [JsonIgnore] public bool CanInstall => !Archived &&
        InstallPolicy is PackageInstallPolicy.OneClick or PackageInstallPolicy.ConfirmationRequired &&
        LatestRelease?.Artifact.DownloadMode is PackageDownloadMode.GithubRelease or
            PackageDownloadMode.UpstreamDirect or PackageDownloadMode.UpstreamRaw or PackageDownloadMode.MoonriseRelease;
    [JsonIgnore] public string RiskLabel => RiskLevel switch
    {
        PackageRiskLevel.Normal => "Normal / Обычный",
        PackageRiskLevel.Experimental => "Experimental / Экспериментальный",
        PackageRiskLevel.ServerDependent => "Server-dependent / Зависит от сервера",
        PackageRiskLevel.UnfairAdvantage => "Unfair advantage / Нечестное преимущество",
        PackageRiskLevel.CheatClient => "Cheat client / Чит-клиент",
        _ => "Unknown / Неизвестно"
    };
}

public sealed record PackageAuthor(string Name, string? Url = null);
public sealed record PackageSource(string? Repository, string? Homepage, string DistributionMethod);
public sealed record PackageLicense(string Name, string? Url = null);
public sealed record PackageDependency(string PackageId, string? VersionRange = null, bool Optional = false);
public sealed record PackageConflict(string PackageId, string? Reason = null);
public sealed record PackageCompatibility(IReadOnlyList<string> MinecraftVersions, IReadOnlyList<string> ClientVersions);
public sealed record PackageScreenshot(string Url, string? Caption = null);
public sealed record PackageWarning(
    string Code,
    string Message,
    Dictionary<string, string>? LocalizedMessage = null);
public sealed record PackageStatistics(long? Downloads = null, long? MoonriseInstalls = null);

public sealed class PackageRelease
{
    public required string Version { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public string Changelog { get; init; } = string.Empty;
    public required PackageArtifact Artifact { get; init; }
    public List<PackageDependency> Dependencies { get; init; } = [];
    public List<PackageConflict> Conflicts { get; init; } = [];
}

public sealed class PackageArtifact
{
    public PackageDownloadMode DownloadMode { get; init; }
    public string? AssetUrl { get; init; }
    public string? UpstreamReleaseUrl { get; init; }
    public string? FileName { get; init; }
    public long? FileSize { get; init; }
    public string? Sha256 { get; init; }
    public int? JavaVersion { get; init; }
    public string? WeaveLoaderRange { get; init; }
    public List<string> MinecraftVersions { get; init; } = [];
}

public enum PackageDownloadMode
{
    GithubRelease,
    UpstreamDirect,
    UpstreamRaw,
    MoonriseRelease,
    SourceOnly,
    ExternalPage,
    Unavailable
}

public enum CatalogSignatureStatus { UnsignedDevelopment, SignedProduction }
public enum PackageRiskLevel { Normal, Experimental, ServerDependent, UnfairAdvantage, CheatClient, Unknown }
public enum PackageInstallPolicy { OneClick, ConfirmationRequired, SourceOnly, ExternalDownload, Unavailable }

public sealed record CatalogLoadResult(
    CatalogIndex Catalog,
    bool IsOffline,
    DateTimeOffset? LastSuccessfulUpdate,
    string? ETag = null);
