using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Moonrise.Models;
using Moonrise.Services;
using NSec.Cryptography;
using Xunit;

namespace Moonrise.Tests;

public sealed class CatalogPlatformTests
{
    [Fact]
    public void Settings_DefaultToPublishedProductionCatalog()
    {
        var settings = new MoonriseSettings();

        Assert.Equal(
            "https://zoongg.github.io/Moonrise-Catalog/catalog/index.json",
            settings.CatalogUrl);
        Assert.Empty(settings.CustomTestCatalogUrl);
    }

    [Fact]
    public void Manifest_ParsesRiskAndInstallPolicyFields()
    {
        var service = new CatalogManifestService();
        var catalog = service.Parse(service.Serialize(Catalog()));

        var package = Assert.Single(catalog.Packages);
        Assert.Equal(PackageDownloadMode.SourceOnly, package.LatestRelease!.Artifact.DownloadMode);
        Assert.Equal(PackageRiskLevel.Normal, package.RiskLevel);
        Assert.Equal(PackageInstallPolicy.SourceOnly, package.InstallPolicy);
        var json = Encoding.UTF8.GetString(service.Serialize(catalog));
        Assert.Contains("riskLevel", json, StringComparison.Ordinal);
        Assert.Contains("installPolicy", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_RejectsInvalidAndUnsupportedCatalogs()
    {
        var service = new CatalogManifestService();
        Assert.Throws<InvalidDataException>(() => service.Parse("{}"u8));
        Assert.Throws<InvalidDataException>(() => service.Parse("""{"schemaVersion":2,"generatedAt":"2026-01-01T00:00:00Z","packages":[]}"""u8));
        Assert.Throws<InvalidDataException>(() => service.Parse("""{"schemaVersion":1,"generatedAt":"2026-01-01T00:00:00Z","packages":[{"id":"../bad","slug":"bad","type":"weaveMod","name":"Bad","summary":"Bad","releases":[]}]}"""u8));
    }

    [Fact]
    public async Task CatalogClient_SendsValidatorsAndUsesCacheOffline()
    {
        using var temp = new TemporaryDirectory();
        var manifests = new CatalogManifestService();
        var payload = manifests.Serialize(Catalog());
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.OK, payload, "\"catalog-v1\""),
            request =>
            {
                Assert.Contains(request.Headers.IfNoneMatch, value => value.Tag == "\"catalog-v1\"");
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            },
            _ => throw new HttpRequestException("offline"),
            _ => throw new HttpRequestException("offline"),
            _ => throw new HttpRequestException("offline"));
        using var http = new HttpClient(handler);
        var client = new CatalogClient(http, manifests, new Uri("https://example.invalid/catalog.json"), temp.Path);

        Assert.False((await client.GetCatalogAsync()).IsOffline);
        Assert.False((await client.GetCatalogAsync()).IsOffline);
        var offline = await client.GetCatalogAsync();
        Assert.True(offline.IsOffline);
        Assert.Single(offline.Catalog.Packages);
    }

    [Fact]
    public async Task CatalogClient_VerifiesSignedProductionAndRejectsUnsignedProduction()
    {
        using var temp = new TemporaryDirectory();
        var manifests = new CatalogManifestService();
        var unsignedPayload = manifests.Serialize(Catalog());
        using (var unsignedHttp = new HttpClient(new BytesHandler(unsignedPayload)))
        {
            var production = new CatalogClient(unsignedHttp, manifests, new Uri("https://example.invalid/index.json"), temp.Path, null, allowUnsignedCatalog: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => production.GetCatalogAsync());
        }

        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var signedCatalog = new CatalogIndex
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            SignatureStatus = CatalogSignatureStatus.SignedProduction,
            Packages = Catalog().Packages
        };
        var payload = manifests.Serialize(signedCatalog);
        var signature = algorithm.Sign(key, payload);
        var publicKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        using var signedHttp = new HttpClient(new SignedCatalogHandler(payload, signature));
        var client = new CatalogClient(signedHttp, manifests, new Uri("https://example.invalid/index.json"), temp.Path, publicKey, allowUnsignedCatalog: false);

        var result = await client.GetCatalogAsync();

        Assert.Equal(CatalogSignatureStatus.SignedProduction, result.Catalog.SignatureStatus);
    }

    [Fact]
    public void Policy_HandlesArchivedSourceOnlyRestrictedExternalAndMissingArtifacts()
    {
        var bytes = JarBytes(new Dictionary<string, string> { ["weave.mod.json"] = """{"name":"Demo","modId":"demo","entryPoints":[]}""" });
        var (_, release) = Downloadable(bytes);
        var archived = Manifest(release, archived: true, PackageRiskLevel.Normal, PackageInstallPolicy.ConfirmationRequired);
        var sourceOnly = Manifest(new PackageRelease
        {
            Version = "source",
            PublishedAt = DateTimeOffset.UtcNow,
            Artifact = new PackageArtifact { DownloadMode = PackageDownloadMode.SourceOnly }
        }, false, PackageRiskLevel.Normal, PackageInstallPolicy.SourceOnly);
        var restricted = Manifest(release, false, PackageRiskLevel.UnfairAdvantage, PackageInstallPolicy.ConfirmationRequired, restricted: true);
        var external = Manifest(new PackageRelease
        {
            Version = "external",
            PublishedAt = DateTimeOffset.UtcNow,
            Artifact = new PackageArtifact { DownloadMode = PackageDownloadMode.ExternalPage, UpstreamReleaseUrl = "https://example.invalid/release" }
        }, false, PackageRiskLevel.CheatClient, PackageInstallPolicy.ExternalDownload, restricted: true);
        var missing = new PackageManifest
        {
            Id = "missing", Slug = "missing", Type = PackageKind.WeaveMod, Name = "Missing", Summary = "Missing asset",
            RiskLevel = PackageRiskLevel.Unknown, InstallPolicy = PackageInstallPolicy.Unavailable
        };

        Assert.False(archived.CanInstall);
        Assert.False(sourceOnly.CanInstall);
        Assert.True(restricted.CanInstall);
        Assert.False(external.CanInstall);
        Assert.False(missing.CanInstall);
    }

    [Fact]
    public async Task Installer_VerifiesHashStructureTypeAndInstallsAtomically()
    {
        using var temp = new TemporaryDirectory();
        var jar = JarBytes(new Dictionary<string, string>
        {
            ["weave.mod.json"] = """{"name":"Demo","modId":"demo","version":"1.0","entryPoints":[]}"""
        });
        var (package, release) = Downloadable(jar);
        using var http = new HttpClient(new BytesHandler(jar));

        var installed = await new ManagedPackageInstaller(http, new JarMetadataParser())
            .InstallAsync(package, release, temp.Path, []);

        Assert.True(File.Exists(installed));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(jar)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installed))));
        Assert.Equal(PackageKind.WeaveMod, ManagedPackageInstaller.DetectType(installed));
    }

    [Fact]
    public async Task Installer_RejectsMissingDependencyAndConflict()
    {
        using var temp = new TemporaryDirectory();
        var jar = JarBytes(new Dictionary<string, string> { ["weave.mod.json"] = """{"name":"Demo","modId":"demo","entryPoints":[]}""" });
        var (package, release) = Downloadable(jar, dependencies: [new PackageDependency("library")]);
        using var http = new HttpClient(new BytesHandler(jar));
        var installer = new ManagedPackageInstaller(http, new JarMetadataParser());
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(package, release, temp.Path, []));

        (package, release) = Downloadable(jar, conflicts: [new PackageConflict("enemy")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(package, release, temp.Path, [Installed("enemy")]));
    }

    [Fact]
    public void Installer_BlocksTraversalAndPreservesManualImports()
    {
        using var temp = new TemporaryDirectory();
        var traversal = Path.Combine(temp.Path, "traversal.jar");
        using (var archive = ZipFile.Open(traversal, ZipArchiveMode.Create)) archive.CreateEntry("../escape.class");
        Assert.Throws<InvalidDataException>(() => ManagedPackageInstaller.ValidateArchive(traversal));

        var manualRoot = Path.Combine(temp.Path, "user-mods");
        Directory.CreateDirectory(manualRoot);
        var manual = Path.Combine(manualRoot, "manual.jar");
        File.WriteAllBytes(manual, [1, 2, 3]);
        var installer = new ManagedPackageInstaller(new HttpClient(), new JarMetadataParser());
        Assert.Throws<InvalidOperationException>(() => installer.Uninstall(manual, Path.Combine(temp.Path, "managed")));
        Assert.True(File.Exists(manual));
    }

    [Fact]
    public async Task Installer_HashFailureLeavesExistingVersionUntouched()
    {
        using var temp = new TemporaryDirectory();
        var jar = JarBytes(new Dictionary<string, string> { ["weave.mod.json"] = """{"name":"Demo","modId":"demo","entryPoints":[]}""" });
        var (package, release) = Downloadable(jar);
        release = new PackageRelease
        {
            Version = release.Version,
            PublishedAt = release.PublishedAt,
            Artifact = new PackageArtifact
            {
                DownloadMode = release.Artifact.DownloadMode,
                AssetUrl = release.Artifact.AssetUrl,
                FileName = release.Artifact.FileName,
                FileSize = release.Artifact.FileSize,
                Sha256 = new string('0', 64)
            }
        };
        package = PackageWithRelease(release);
        var destination = Path.Combine(temp.Path, "mods", "demo.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, [9, 9, 9]);
        using var http = new HttpClient(new BytesHandler(jar));

        await Assert.ThrowsAsync<CryptographicException>(() => new ManagedPackageInstaller(http, new JarMetadataParser()).InstallAsync(package, release, temp.Path, []));
        Assert.Equal([9, 9, 9], File.ReadAllBytes(destination));
    }

    [Fact]
    public void DeveloperInspector_GeneratesDraftWithoutDecompilingClasses()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "developer.jar");
        File.WriteAllBytes(path, JarBytes(new Dictionary<string, string>
        {
            ["weave.mod.json"] = """{"name":"Developer","modId":"developer","entryPoints":["demo.Main"],"mixinConfigs":["demo.mixins.json"]}""",
            ["demo/Main.class"] = Encoding.Latin1.GetString([0xCA, 0xFE, 0xBA, 0xBE, 0, 0, 0, 61])
        }));
        var inspector = new DeveloperPackageInspector();
        var inspection = inspector.Inspect(path);
        var draft = inspector.CreateDraft(inspection);

        Assert.Equal(17, inspection.JavaClassVersion);
        Assert.Empty(inspection.Classes);
        Assert.Contains("demo.Main", inspection.EntryPoints);
        Assert.Equal(PackageDownloadMode.SourceOnly, draft.LatestRelease!.Artifact.DownloadMode);
    }

    [Fact]
    public void UpdateDetection_UsesPackageIdentityAndVersion()
    {
        var release = new PackageRelease { Version = "2.0.0", PublishedAt = DateTimeOffset.UtcNow, Artifact = new PackageArtifact { DownloadMode = PackageDownloadMode.SourceOnly } };
        var catalog = PackageWithRelease(release);
        var installed = Installed("demo", "1.0.0");
        Assert.True(new PackageUpdateService().IsUpdateAvailable(catalog, installed));
    }

    private static CatalogIndex Catalog() => new()
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        Packages = [PackageWithRelease(new PackageRelease { Version = "1.0.0", PublishedAt = DateTimeOffset.UtcNow, Artifact = new PackageArtifact { DownloadMode = PackageDownloadMode.SourceOnly } })]
    };

    private static (PackageManifest Package, PackageRelease Release) Downloadable(byte[] bytes, List<PackageDependency>? dependencies = null, List<PackageConflict>? conflicts = null)
    {
        var release = new PackageRelease
        {
            Version = "1.0.0",
            PublishedAt = DateTimeOffset.UtcNow,
            Artifact = new PackageArtifact
            {
                DownloadMode = PackageDownloadMode.UpstreamDirect,
                AssetUrl = "https://example.invalid/demo.jar",
                FileName = "demo.jar",
                FileSize = bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
            }
        };
        return (new PackageManifest
        {
            Id = "demo", Slug = "demo", Type = PackageKind.WeaveMod, Name = "Demo", Summary = "Demo package",
            RiskLevel = PackageRiskLevel.Normal, InstallPolicy = PackageInstallPolicy.OneClick,
            Releases = [release], Dependencies = dependencies ?? [], Conflicts = conflicts ?? []
        }, release);
    }

    private static PackageManifest PackageWithRelease(PackageRelease release) => new()
    {
        Id = "demo", Slug = "demo", Type = PackageKind.WeaveMod, Name = "Demo", Summary = "Demo package",
        RiskLevel = PackageRiskLevel.Normal,
        InstallPolicy = release.Artifact.DownloadMode == PackageDownloadMode.UpstreamDirect
            ? PackageInstallPolicy.OneClick
            : PackageInstallPolicy.SourceOnly,
        Releases = [release]
    };

    private static PackageManifest Manifest(
        PackageRelease release,
        bool archived,
        PackageRiskLevel risk,
        PackageInstallPolicy policy,
        bool restricted = false) => new()
    {
        Id = "policy-" + policy, Slug = "policy-" + policy, Type = PackageKind.WeaveMod,
        Name = "Policy", Summary = "Policy package", Archived = archived, RiskLevel = risk,
        InstallPolicy = policy, Restricted = restricted, Releases = [release]
    };

    private static PackageInfo Installed(string id, string version = "1.0.0") => new()
    {
        FileName = id + ".jar", DisplayName = id, Identifier = id, FullPath = Path.Combine(Path.GetTempPath(), id + ".jar"),
        Kind = PackageKind.WeaveMod, Version = version
    };

    private static byte[] JarBytes(IReadOnlyDictionary<string, string> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.Latin1);
                writer.Write(content);
            }
        return stream.ToArray();
    }

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] payload, string etag)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(payload) };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    private sealed class SignedCatalogHandler(byte[] payload, byte[] signature) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                ? new ByteArrayContent(Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)))
                : new ByteArrayContent(payload);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _index;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses[Math.Min(_index++, responses.Length - 1)](request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"moonrise-catalog-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
