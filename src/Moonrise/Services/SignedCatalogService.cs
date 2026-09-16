using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class SignedCatalogService
{
    private const int CurrentSchemaVersion = 1;
    private const long MaximumPackageSize = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public VerifiedCatalogResult Load(string catalogPath, string signaturePath, string publicKeyPath)
    {
        var payload = File.ReadAllBytes(catalogPath);
        var signatureText = File.ReadAllText(signaturePath).Trim();
        var publicKeyPem = File.ReadAllText(publicKeyPath);
        return Verify(payload, signatureText, publicKeyPem);
    }

    public VerifiedCatalogResult Verify(byte[] payload, string signatureBase64, string publicKeyPem)
    {
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureBase64); }
        catch (FormatException exception) { throw new InvalidDataException("The catalog signature is not valid Base64.", exception); }

        using var rsa = RSA.Create();
        try { rsa.ImportFromPem(publicKeyPem); }
        catch (ArgumentException exception) { throw new InvalidDataException("The catalog public key is invalid.", exception); }
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("The catalog signature is invalid.");

        var catalog = JsonSerializer.Deserialize<VerifiedCatalog>(payload, JsonOptions)
            ?? throw new InvalidDataException("The signed catalog is empty.");
        Validate(catalog);
        var fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
        return new VerifiedCatalogResult(catalog, fingerprint);
    }

    public async Task<string> DownloadAndImportAsync(
        VerifiedCatalogPackage package,
        HttpClient httpClient,
        string destinationDirectory,
        PackageCatalogService packageCatalog,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package);
        var uri = new Uri(package.DownloadUrl, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Catalog packages must use HTTPS download URLs.");

        Directory.CreateDirectory(destinationDirectory);
        var temporary = Path.Combine(destinationDirectory, $".catalog-{Guid.NewGuid():N}.jar.tmp");
        try
        {
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumPackageSize)
                throw new InvalidDataException("The catalog package exceeds the 512 MB limit.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken);
                if (destination.Length <= 0 || destination.Length > MaximumPackageSize)
                    throw new InvalidDataException("The catalog package size is invalid.");
            }

            var actualHash = ComputeHash(temporary);
            if (!string.Equals(actualHash, package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("The downloaded package SHA-256 does not match the signed catalog.");
            return packageCatalog.Import(temporary, destinationDirectory, package.Kind);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(VerifiedCatalog catalog)
    {
        if (catalog.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported catalog schema version: {catalog.SchemaVersion}.");
        if (catalog.GeneratedAt == default)
            throw new InvalidDataException("The catalog generation time is required.");
        foreach (var package in catalog.Packages) ValidatePackage(package);
        if (catalog.Packages.GroupBy(package => package.Identifier, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("The catalog contains duplicate package identifiers.");
    }

    private static void ValidatePackage(VerifiedCatalogPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Identifier) || string.IsNullOrWhiteSpace(package.Name) ||
            string.IsNullOrWhiteSpace(package.Version))
            throw new InvalidDataException("A catalog package is missing required metadata.");
        if (!Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("A catalog package has an invalid HTTPS URL.");
        if (package.Sha256.Length != 64 || package.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("A catalog package has an invalid SHA-256 hash.");
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
