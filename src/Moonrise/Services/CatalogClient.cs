using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moonrise.Models;
using NSec.Cryptography;

namespace Moonrise.Services;

public interface ICatalogService
{
    Task<CatalogLoadResult> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

public sealed class CatalogClient(
    HttpClient httpClient,
    CatalogManifestService manifests,
    Uri catalogUri,
    string cacheDirectory,
    string? trustedEd25519PublicKeyBase64 = null,
    bool allowUnsignedCatalog = true) : ICatalogService
{
    private const int MaximumCatalogBytes = 8 * 1024 * 1024;
    private const int MaximumSignatureBytes = 4096;
    private readonly string _cacheDirectory = Path.GetFullPath(cacheDirectory);
    private string CatalogPath => Path.Combine(_cacheDirectory, "catalog.json");
    private string SignaturePath => Path.Combine(_cacheDirectory, "catalog.json.sig");
    private string MetadataPath => Path.Combine(_cacheDirectory, "catalog-cache.json");

    public async Task<CatalogLoadResult> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (catalogUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The production catalog URL must use HTTPS.");

        var metadata = ReadMetadata();
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, catalogUri);
                if (!forceRefresh && EntityTagHeaderValue.TryParse(metadata?.ETag, out var etag))
                    request.Headers.IfNoneMatch.Add(etag);
                if (!forceRefresh && metadata?.LastModified is { } modified)
                    request.Headers.IfModifiedSince = modified;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.NotModified && File.Exists(CatalogPath))
                    return new CatalogLoadResult(ReadCache(), false, metadata?.UpdatedAt, metadata?.ETag);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumCatalogBytes)
                    throw new InvalidDataException("The catalog exceeds the 8 MB limit.");
                var payload = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                if (payload.Length == 0 || payload.Length > MaximumCatalogBytes)
                    throw new InvalidDataException("The catalog size is invalid.");
                var catalog = manifests.Parse(payload);
                var signature = await ValidateDownloadedCatalogAsync(payload, catalog, timeout.Token);
                var now = DateTimeOffset.UtcNow;
                WriteCache(payload, signature, new CatalogCacheMetadata(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, now));
                return new CatalogLoadResult(catalog, false, now, response.Headers.ETag?.ToString());
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or InvalidDataException or CryptographicException)
            {
                lastError = exception;
                if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(200 * (1 << attempt)), cancellationToken);
            }
        }

        if (File.Exists(CatalogPath))
            return new CatalogLoadResult(ReadCache(), true, metadata?.UpdatedAt, metadata?.ETag);
        throw new InvalidOperationException("The catalog is unavailable and no local cache exists.", lastError);
    }

    public void ResetCache()
    {
        if (File.Exists(CatalogPath)) File.Delete(CatalogPath);
        if (File.Exists(SignaturePath)) File.Delete(SignaturePath);
        if (File.Exists(MetadataPath)) File.Delete(MetadataPath);
    }

    private CatalogIndex ReadCache()
    {
        var payload = File.ReadAllBytes(CatalogPath);
        var catalog = manifests.Parse(payload);
        var signature = File.Exists(SignaturePath) ? File.ReadAllBytes(SignaturePath) : null;
        ValidateSignature(payload, catalog, signature);
        return catalog;
    }

    private async Task<byte[]?> ValidateDownloadedCatalogAsync(
        byte[] payload,
        CatalogIndex catalog,
        CancellationToken cancellationToken)
    {
        if (catalog.SignatureStatus == CatalogSignatureStatus.UnsignedDevelopment)
        {
            ValidateSignature(payload, catalog, null);
            return null;
        }

        if (string.IsNullOrWhiteSpace(trustedEd25519PublicKeyBase64))
            throw new CryptographicException("No trusted production catalog public key is configured.");

        var signatureUri = new Uri(catalogUri.AbsoluteUri + ".sig", UriKind.Absolute);
        using var response = await httpClient.GetAsync(signatureUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumSignatureBytes)
            throw new InvalidDataException("The catalog signature is too large.");
        var encoded = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (encoded.Length == 0 || encoded.Length > MaximumSignatureBytes)
            throw new InvalidDataException("The catalog signature size is invalid.");
        byte[] signature;
        try { signature = Convert.FromBase64String(Encoding.ASCII.GetString(encoded).Trim()); }
        catch (FormatException exception) { throw new InvalidDataException("The catalog signature is not valid Base64.", exception); }
        ValidateSignature(payload, catalog, signature);
        return signature;
    }

    private void ValidateSignature(byte[] payload, CatalogIndex catalog, byte[]? signature)
    {
        if (catalog.SignatureStatus == CatalogSignatureStatus.UnsignedDevelopment)
        {
            if (!allowUnsignedCatalog)
                throw new CryptographicException("Unsigned catalogs are allowed only in developer mode.");
            return;
        }

        if (string.IsNullOrWhiteSpace(trustedEd25519PublicKeyBase64) || signature is null)
            throw new CryptographicException("The signed production catalog has no trusted key or cached signature.");
        try
        {
            var rawKey = Convert.FromBase64String(trustedEd25519PublicKeyBase64);
            var algorithm = SignatureAlgorithm.Ed25519;
            var publicKey = PublicKey.Import(algorithm, rawKey, KeyBlobFormat.RawPublicKey);
            if (!algorithm.Verify(publicKey, payload, signature))
                throw new CryptographicException("The production catalog Ed25519 signature is invalid.");
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The trusted catalog public key is invalid.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The trusted catalog public key is invalid.", exception);
        }
    }

    private CatalogCacheMetadata? ReadMetadata()
    {
        try
        {
            return File.Exists(MetadataPath)
                ? JsonSerializer.Deserialize<CatalogCacheMetadata>(File.ReadAllText(MetadataPath))
                : null;
        }
        catch { return null; }
    }

    private void WriteCache(byte[] payload, byte[]? signature, CatalogCacheMetadata metadata)
    {
        Directory.CreateDirectory(_cacheDirectory);
        AtomicWrite(CatalogPath, payload);
        if (signature is null)
        {
            if (File.Exists(SignaturePath)) File.Delete(SignaturePath);
        }
        else
        {
            AtomicWrite(SignaturePath, signature);
        }
        AtomicWrite(MetadataPath, JsonSerializer.SerializeToUtf8Bytes(metadata));
    }

    private static void AtomicWrite(string path, byte[] payload)
    {
        var temporary = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllBytes(temporary, payload);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record CatalogCacheMetadata(string? ETag, DateTimeOffset? LastModified, DateTimeOffset UpdatedAt);
}
