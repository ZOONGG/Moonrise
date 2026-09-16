using System.Text.Json;
using System.Text.Json.Serialization;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class CatalogManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public CatalogIndex Parse(ReadOnlySpan<byte> payload)
    {
        CatalogIndex catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<CatalogIndex>(payload, JsonOptions)
                ?? throw new InvalidDataException("The catalog is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The catalog JSON is invalid.", exception);
        }

        Validate(catalog);
        return catalog;
    }

    public byte[] Serialize(CatalogIndex catalog)
    {
        Validate(catalog);
        return JsonSerializer.SerializeToUtf8Bytes(catalog, JsonOptions);
    }

    public void Validate(CatalogIndex catalog)
    {
        if (catalog.SchemaVersion != CatalogIndex.SupportedSchemaVersion)
            throw new InvalidDataException($"Unsupported catalog schema version: {catalog.SchemaVersion}.");
        if (catalog.GeneratedAt == default)
            throw new InvalidDataException("Catalog generatedAt is required.");
        if (catalog.Packages.GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Catalog package identifiers must be unique.");
        if (catalog.Packages.GroupBy(package => package.Slug, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Catalog package slugs must be unique.");

        foreach (var package in catalog.Packages) ValidatePackage(package);
    }

    private static void ValidatePackage(PackageManifest package)
    {
        if (!IsIdentifier(package.Id) || !IsIdentifier(package.Slug) || string.IsNullOrWhiteSpace(package.Name) ||
            string.IsNullOrWhiteSpace(package.Summary))
            throw new InvalidDataException("A catalog package has invalid required metadata.");
        ValidateOptionalHttpsUrl(package.Homepage, "homepage");
        ValidateOptionalHttpsUrl(package.Repository, "repository");
        ValidateOptionalHttpsUrl(package.Icon, "icon");
        foreach (var screenshot in package.Screenshots) ValidateOptionalHttpsUrl(screenshot.Url, "screenshot");
        if (package.Restricted && (package.Featured || package.Recommended))
            throw new InvalidDataException($"Restricted package '{package.Id}' cannot be featured or recommended.");
        if (package.RiskLevel is PackageRiskLevel.UnfairAdvantage or PackageRiskLevel.CheatClient && !package.Restricted)
            throw new InvalidDataException($"High-risk package '{package.Id}' must be restricted.");
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var release in package.Releases)
        {
            if (string.IsNullOrWhiteSpace(release.Version) || !versions.Add(release.Version))
                throw new InvalidDataException($"Package '{package.Id}' has an invalid or duplicate release version.");
            ValidateArtifact(package, release.Artifact);
        }
    }

    private static void ValidateArtifact(PackageManifest package, PackageArtifact artifact)
    {
        var downloadable = artifact.DownloadMode is PackageDownloadMode.GithubRelease or
            PackageDownloadMode.UpstreamDirect or PackageDownloadMode.UpstreamRaw or PackageDownloadMode.MoonriseRelease;
        if (!downloadable)
        {
            ValidateOptionalHttpsUrl(artifact.UpstreamReleaseUrl, "external release");
            return;
        }

        ValidateOptionalHttpsUrl(artifact.AssetUrl, "asset");
        if (string.IsNullOrWhiteSpace(artifact.AssetUrl) || string.IsNullOrWhiteSpace(artifact.FileName) ||
            !artifact.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Package '{package.Id}' has an incomplete downloadable artifact.");
        if (artifact.FileSize is <= 0)
            throw new InvalidDataException($"Package '{package.Id}' has an invalid file size.");
        if (artifact.Sha256 is null || artifact.Sha256.Length != 64 || artifact.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"Package '{package.Id}' has an invalid SHA-256 value.");
    }

    private static bool IsIdentifier(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static void ValidateOptionalHttpsUrl(string? value, string field)
    {
        if (value is null) return;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"Catalog {field} URL must use HTTPS.");
    }

    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
