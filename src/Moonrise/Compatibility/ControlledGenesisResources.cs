using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Moonrise.Compatibility;

public sealed record RuntimeResourceRoot(string SourceRoot, string PreparedRoot, string Purpose,
    bool Writable, string Ownership, string VersionEvidence, ImmutableArray<ArtifactSnapshot> Files)
{
    public bool SourceReadOnly => true;
    public string SourceOwnership => "installed Lunar provisioning";
}

/// <summary>Resource-only view for the pinned Genesis contract. Never imports settings or account state.</summary>
public static class ControlledGenesisResources
{
    private const string SupportedLunarSha256 = "0763a381214da346e889565c695b12c794e08cfa2eb4053c3c46e3208774630e";
    // Current installed launcher main.log repeatedly confirms this source archive on
    // 2026-10-04/05. Scoped to the pinned lunar.jar, not a Genesis-wide default.
    private const string SupportedUiSourceSha1 = "b6d39b271abae35e95be9bdadf2270e908d59548";
    private static readonly string[] WebFiles = ["cacert.pem", "icudt67l.dat", "mediaControls.css", "mediaControls.js", "mediaControlsLocalizedStrings.js"];
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".html", ".js", ".css", ".png", ".jpg", ".jpeg", ".webp", ".svg", ".gif", ".ttf", ".otf", ".woff", ".woff2", ".json", ".ogg", ".mp3", ".mp4", ".bobj", ".fsh", ".mcmeta", ".molang" };
    private static readonly Regex SecretName = new("account|token|credential|password|secret|session|auth", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ImmutableArray<RuntimeResourceRoot> Discover(string contractId, string lunarRoot, string sessionRoot)
        => Discover(contractId, lunarRoot, sessionRoot, SupportedLunarSha256, SupportedUiSourceSha1);

    internal static ImmutableArray<RuntimeResourceRoot> Discover(string contractId, string lunarRoot, string sessionRoot, string expectedLunarSha256, string uiSourceSha1)
    {
        if (contractId != ControlledGenesisDiscovery.ContractId)
            throw new NotSupportedException("Unsupported Genesis resource contract.");
        var root = Path.GetFullPath(lunarRoot);
        var session = Path.GetFullPath(sessionRoot);
        var native = Path.Combine(root, "offline", "multiver", "natives");
        var lunarJar = Path.Combine(root, "offline", "multiver", "lunar.jar");
        EnsureBounded(root, lunarJar);
        if (ArtifactSnapshots.Capture(lunarJar, "lunar.resource-runtime", false).Sha256 != expectedLunarSha256)
            throw new NotSupportedException("Lunar snapshot is outside the verified resource contract.");
        using var jar = ZipFile.OpenRead(Path.Combine(root, "offline", "multiver", "lunar.jar"));
        var entry = jar.GetEntry("lunarBuildData.txt") ?? throw new NotSupportedException("Lunar resource build metadata is missing.");
        using var reader = new StreamReader(entry.Open());
        var hash = Regex.Match(reader.ReadToEnd(), @"(?m)^uiGitHash=([a-f0-9]{40})\s*$").Groups[1].Value;
        if (hash.Length != 40) throw new NotSupportedException("Unsupported Lunar UI resource version metadata.");
        var web = DiscoverWeb(native);
        // The installed launcher selects ui.sourceSha1 from its launch metadata response.
        // uiGitHash is a Git revision, not that archive hash. Never equate them or
        // choose an arbitrary installed bundle when the selection evidence is absent.
        if (!Regex.IsMatch(uiSourceSha1, "^[a-f0-9]{40}$")) throw new NotSupportedException("Unsupported UI source archive identifier.");
        var uiSource = Path.Combine(root, "ui", uiSourceSha1);
        EnsureBounded(root, uiSource);
        _ = Capture(uiSource, "index.html", "lunar.ui");
        _ = Capture(Path.Combine(root, "textures", "assets"), Path.Combine("lunar", "jit_index"), "lunar.textures");
        var webSource = Path.GetDirectoryName(web[0].CanonicalPath)!;
        return [new(webSource, Path.Combine(session, "natives", "web", "resources"), "Ultralight ICU, CA and media resources", true, "Moonrise-owned launch copy", contractId, web),
            new(uiSource, Path.Combine(session, "lunar-data", "ui"), "Lunar WebOSR UI bundle", true, "Moonrise-owned launch copy", "official launcher resource selection:sourceSha1=" + uiSourceSha1 + "; lunarBuildData.txt:uiGitHash=" + hash, Scan(uiSource, "lunar.ui")),
            new(Path.Combine(root, "textures", "assets"), Path.Combine(session, "lunar-data", "textures", "assets"), "Lunar texture assets", true, "Moonrise-owned launch copy", contractId, Scan(Path.Combine(root, "textures", "assets"), "lunar.textures"))];
    }

    internal static ImmutableArray<ArtifactSnapshot> DiscoverWeb(string nativeRoot)
    {
        var source = Path.Combine(nativeRoot, "web", "resources");
        if (!File.Exists(Path.Combine(source, "icudt67l.dat"))) source = Path.Combine(nativeRoot, "resources");
        return WebFiles.Select(name => Capture(source, name, "ultralight.resources")).ToImmutableArray();
    }

    internal static ImmutableArray<ArtifactSnapshot> Scan(string source, string purpose)
    {
        if (!Directory.Exists(source)) throw new NotSupportedException("Required resource root is missing: " + purpose);
        var result = ImmutableArray.CreateBuilder<ArtifactSnapshot>();
        long bytes = 0;
        var entries = 0;
        void Visit(string directory)
        {
            EnsureBounded(source, directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (++entries > 20000) throw new NotSupportedException("Resource view exceeds its provisioning limit.");
                EnsureBounded(source, path);
                if (SecretName.IsMatch(Path.GetFileName(path))) continue;
                if (Directory.Exists(path)) Visit(path);
                else if (Extensions.Contains(Path.GetExtension(path)) || Path.GetFileName(path) is "index" or "jit_index")
                {
                    var artifact = Capture(source, Path.GetRelativePath(source, path), purpose);
                    result.Add(artifact); bytes += artifact.Size;
                }
                if (bytes > 500L * 1024 * 1024)
                    throw new NotSupportedException("Resource view exceeds its provisioning limit.");
            }
        }
        Visit(source);
        if (result.Count == 0) throw new NotSupportedException("Required resource root is empty: " + purpose);
        return result.ToImmutable();
    }

    private static ArtifactSnapshot Capture(string root, string relative, string purpose)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        EnsureBounded(root, path);
        if (!File.Exists(path)) throw new NotSupportedException("Missing provisioned resource: " + purpose + "/" + relative);
        return ArtifactSnapshots.Capture(path, purpose + ":" + relative.Replace('\\', '/'), false);
    }

    internal static void EnsureBounded(string root, string path)
    {
        root = Path.GetFullPath(root);
        path = Path.GetFullPath(path);
        if (path != root && !path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resource path escapes its allowed root.");
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("Resource paths may not traverse reparse points.");
    }

    public static void Prepare(ImmutableArray<RuntimeResourceRoot> roots, string sessionRoot)
    {
        if (roots.SelectMany(r => r.Files).Sum(f => f.Size) > 500L * 1024 * 1024)
            throw new NotSupportedException("Prepared resource view exceeds its provisioning limit.");
        if (!ArtifactSnapshots.Validate(roots.SelectMany(r => r.Files).ToImmutableArray()).IsEmpty)
            throw new InvalidOperationException("Resource inputs changed after freeze.");
        if (Directory.Exists(sessionRoot)) throw new InvalidOperationException("Prepared resource directory already exists.");
        EnsureBounded(sessionRoot, sessionRoot);
        foreach (var resource in roots)
        {
            EnsureBounded(sessionRoot, resource.PreparedRoot);
            foreach (var file in resource.Files)
            {
                EnsureBounded(resource.SourceRoot, file.CanonicalPath);
                var relative = Path.GetRelativePath(resource.SourceRoot, file.CanonicalPath);
                if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => SecretName.IsMatch(part)))
                    throw new NotSupportedException("Secret/account-looking input is forbidden in the resource view.");
                EnsureBounded(sessionRoot, Path.Combine(resource.PreparedRoot, relative));
            }
        }
        Directory.CreateDirectory(sessionRoot);
        File.WriteAllText(Path.Combine(sessionRoot, ".moonrise-resource-owner"), "controlled-genesis-resources-v1");
        foreach (var resource in roots)
        {
            EnsureBounded(sessionRoot, resource.PreparedRoot);
            foreach (var file in resource.Files)
            {
                EnsureBounded(resource.SourceRoot, file.CanonicalPath);
                var destination = Path.Combine(resource.PreparedRoot, Path.GetRelativePath(resource.SourceRoot, file.CanonicalPath));
                EnsureBounded(sessionRoot, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file.CanonicalPath, destination, overwrite: false);
                if (ArtifactSnapshots.Capture(destination, "prepared", false).Sha256 != file.Sha256)
                    throw new InvalidOperationException("Prepared resource fingerprint changed.");
            }
        }
    }

    public static void Cleanup(string allowedParent, string preparedRoot)
    {
        EnsureBounded(allowedParent, preparedRoot);
        if (Path.GetFullPath(allowedParent).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(preparedRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot clean the resource parent root.");
        if (!Directory.Exists(preparedRoot)) return;
        var marker = Path.Combine(preparedRoot, ".moonrise-resource-owner");
        EnsureBounded(preparedRoot, marker);
        if (!File.Exists(marker) || File.ReadAllText(marker) != "controlled-genesis-resources-v1")
            throw new InvalidOperationException("Cleanup requires a Moonrise resource ownership marker.");
        void Verify(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                EnsureBounded(preparedRoot, path);
                if (Directory.Exists(path)) Verify(path);
            }
        }
        Verify(preparedRoot);
        Directory.Delete(preparedRoot, recursive: true);
    }
}
