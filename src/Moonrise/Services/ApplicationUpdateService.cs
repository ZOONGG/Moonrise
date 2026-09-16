using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class ApplicationUpdateService
{
    private const uint ErrorSuccess = 0;
    private const uint TrustENoSignature = 0x800B0100;
    private const uint TrustEProviderUnknown = 0x800B0001;
    private const uint TrustESubjectFormUnknown = 0x800B0003;

    public static readonly Uri ReleasesApi = new("https://api.github.com/repos/ZOONGG/Moonrise/releases?per_page=30");

    public async Task<UpdateRelease?> CheckAsync(
        Version currentVersion,
        HttpClient httpClient,
        bool includePrereleases = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(httpClient);

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.UserAgent.ParseAdd("Moonrise-Updater/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The GitHub Releases response is invalid.");

        return document.RootElement
            .EnumerateArray()
            .Where(release => !GetBoolean(release, "draft") &&
                              (includePrereleases || !GetBoolean(release, "prerelease")))
            .Select(ParseRelease)
            .Where(release => release is not null && release.Version > currentVersion)
            .OrderByDescending(release => release!.Version)
            .FirstOrDefault();
    }

    public async Task<StagedUpdate> StageAsync(
        UpdateRelease release,
        HttpClient httpClient,
        string updateRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(httpClient);

        var directory = Path.Combine(Path.GetFullPath(updateRoot), $"Moonrise-{release.Version}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var setupPath = Path.Combine(directory, release.SetupName);
        var checksumPath = setupPath + ".sha256";
        await DownloadAsync(httpClient, release.SetupUrl, setupPath, cancellationToken);
        await DownloadAsync(httpClient, release.ChecksumUrl, checksumPath, cancellationToken);

        var expected = ParseChecksum(await File.ReadAllTextAsync(checksumPath, cancellationToken), release.SetupName);
        await using var setupStream = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(setupStream, cancellationToken));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("The installer SHA-256 does not match the checksum published with the GitHub Release.");

        var hasTrustedSignature = GetAuthenticodeTrust(setupPath);
        return new StagedUpdate(release, directory, setupPath, hasTrustedSignature);
    }

    public void StartInstaller(StagedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!File.Exists(update.SetupPath))
            throw new FileNotFoundException("The staged Moonrise installer is missing.", update.SetupPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(update.SetupPath),
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(update.SetupPath)!
        };
        startInfo.ArgumentList.Add("/CURRENTUSER");
        startInfo.ArgumentList.Add("/SP-");
        startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
        Process.Start(startInfo)?.Dispose();
    }

    internal static string ParseChecksum(string text, string expectedFileName)
    {
        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .FirstOrDefault(item => item.Length > 0)
            ?? throw new InvalidDataException("The installer checksum file is empty.");
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is < 1 or > 2 ||
            fields[0].Length != 64 ||
            fields[0].Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The installer checksum file is invalid.");
        if (fields.Length == 2 &&
            !string.Equals(fields[1].TrimStart('*'), expectedFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installer checksum names a different file.");
        return fields[0];
    }

    private static UpdateRelease? ParseRelease(JsonElement root)
    {
        var tag = GetRequiredString(root, "tag_name");
        if (!TryParseVersion(tag, out var version))
            return null;

        var expectedSetupName = $"Moonrise-Setup-{version}-x64.exe";
        var assets = root.GetProperty("assets").EnumerateArray().Select(asset => new ReleaseAsset(
            GetRequiredString(asset, "name"),
            ParseOfficialAssetUri(GetRequiredString(asset, "browser_download_url")))).ToArray();
        var setup = assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, expectedSetupName, StringComparison.OrdinalIgnoreCase));
        if (setup is null)
            return null;
        var checksum = assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, expectedSetupName + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (checksum is null)
            throw new InvalidDataException($"Release {tag} is missing the installer checksum.");

        var releasePage = new Uri(GetRequiredString(root, "html_url"), UriKind.Absolute);
        if (!string.Equals(releasePage.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(releasePage.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The release page is not hosted on GitHub.");

        return new UpdateRelease(
            version,
            tag,
            GetBoolean(root, "prerelease"),
            releasePage,
            root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
                ? body.GetString() ?? string.Empty
                : string.Empty,
            setup.Url,
            checksum.Url,
            setup.Name);
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var normalized = tag.Trim().TrimStart('v', 'V');
        var prereleaseSeparator = normalized.IndexOf('-');
        if (prereleaseSeparator >= 0)
            normalized = normalized[..prereleaseSeparator];
        return Version.TryParse(normalized, out version!);
    }

    private static Uri ParseOfficialAssetUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !(string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The update asset is not hosted on an approved GitHub HTTPS endpoint.");
        return uri;
    }

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? throw new InvalidDataException($"Release property '{propertyName}' is empty.")
            : throw new InvalidDataException($"Release property '{propertyName}' is missing.");

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        property.GetBoolean();

    private static async Task DownloadAsync(HttpClient client, Uri uri, string path, CancellationToken cancellationToken)
    {
        _ = ParseOfficialAssetUri(uri.AbsoluteUri);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(output, cancellationToken);
    }

    private static bool GetAuthenticodeTrust(string executable)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var fileInfo = new WinTrustFileInfo
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = Path.GetFullPath(executable)
        };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 1,
                UnionChoice = 1,
                FileInfoPointer = fileInfoPointer,
                StateAction = 1,
                ProviderFlags = 0x00000080
            };
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
            trustData.StateAction = 2;
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
            if (result == ErrorSuccess)
                return true;
            if (result is TrustENoSignature or TrustEProviderUnknown or TrustESubjectFormUnknown)
                return false;
            throw new CryptographicException(
                $"The installer has an invalid or untrusted Authenticode signature (0x{result:X8}).",
                new Win32Exception(unchecked((int)result)));
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    private sealed record ReleaseAsset(string Name, Uri Url);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfoPointer;
        public uint StateAction;
        public IntPtr StateData;
        public string? UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr windowHandle, ref Guid actionId, ref WinTrustData trustData);
}
