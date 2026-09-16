using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Moonrise.Services;

public sealed record WeaveLoaderRelease(
    string Version,
    string OfficialAssetUrl,
    string ExpectedSha256,
    string ExpectedPremainClass);

public sealed class WeaveAgentService
{
    public const string Version = "1.3.4";
    public const string OfficialAssetUrl = "https://github.com/Weave-MC/Weave-Loader/releases/download/1.3.4/Weave-Loader-Agent-1.3.4.jar";
    public const string ExpectedSha256 = "013b7b9a4ca01a473a1ffc0b031c843c70fe61616e9048fb326e03794c8457b7";
    public const string ExpectedPremainClass = "net.weavemc.loader.impl.bootstrap.AgentKt";
    public static readonly WeaveLoaderRelease Current = new(
        Version,
        OfficialAssetUrl,
        ExpectedSha256,
        ExpectedPremainClass);
    public static readonly WeaveLoaderRelease Legacy02 = new(
        "0.2.6",
        "https://github.com/Weave-MC/Weave-Loader/releases/download/v0.2.6/Weave-Loader-Agent-0.2.6.jar",
        "c4200d145c0d3d78f57eab4ddee3a64ff1a3176e65f2e2b15a31777a0142b524",
        "net.weavemc.loader.bootstrap.AgentKt");
    private readonly HttpClient _httpClient;

    public WeaveAgentService(HttpClient? httpClient = null) => _httpClient = httpClient ?? new HttpClient();

    public async Task EnsureAsync(string targetPath, CancellationToken cancellationToken = default)
        => await EnsureAsync(targetPath, Current, cancellationToken);

    public async Task EnsureAsync(
        string targetPath,
        WeaveLoaderRelease release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (Validate(targetPath, release, out _)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        var temporary = targetPath + $".download-{Guid.NewGuid():N}";
        try
        {
            using var response = await _httpClient.GetAsync(release.OfficialAssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                await response.Content.CopyToAsync(output, cancellationToken);
            if (!Validate(temporary, release, out var error))
                throw new InvalidDataException($"Official Weave Loader {release.Version} validation failed: {error}");
            File.Move(temporary, targetPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public bool Validate(string path, out string error)
        => Validate(path, Current, out error);

    public bool Validate(string path, WeaveLoaderRelease release, out string error)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!File.Exists(path)) { error = "file not found"; return false; }
        try
        {
            using (var stream = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (!string.Equals(hash, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                { error = $"SHA-256 mismatch ({hash})"; return false; }
            }
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry("META-INF/MANIFEST.MF");
            if (entry is null) { error = "manifest missing"; return false; }
            using var reader = new StreamReader(entry.Open());
            var manifest = reader.ReadToEnd().Replace("\r\n ", string.Empty, StringComparison.Ordinal).Replace("\n ", string.Empty, StringComparison.Ordinal);
            if (!manifest.Split('\n').Any(line => line.Trim() == $"Premain-Class: {release.ExpectedPremainClass}"))
            { error = "unexpected Premain-Class"; return false; }
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        { error = exception.Message; return false; }
    }
}
