using System.Net.Http;
using System.Security.Cryptography;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class BundledCatalogTests
{
    [Fact]
    public async Task ShippedCatalogInstallsBothKindsAndPreservesLibrarySelectionAndOriginals()
    {
        using var temp = new TestDirectory();
        var paths = AppPaths.DiscoverLocal(temp.Path, temp.Path);
        var parser = new JarMetadataParser();
        var library = new LocalPackageLibrary(paths, parser);
        library.Load();
        using var http = new HttpClient(new NoNetwork());
        var installer = new ManagedPackageInstaller(http, parser);
        var catalog = PackageCatalogService.LoadBundledCatalog();
        Assert.Equal(new[] { "moonrise-cosmetics", "veyra" }, catalog.Select(p => p.Id));
        foreach (var package in catalog)
        {
            Assert.True(package.CanInstall);
            var release = package.LatestRelease!;
            var source = PackageCatalogService.ResolveBundledArtifact(package, release, AppContext.BaseDirectory);
            var before = File.ReadAllBytes(source);
            var staged = await installer.InstallAsync(package, release, Path.Combine(temp.Path, "staging"), library.Packages);
            var imported = library.Import(staged, "catalog");
            Assert.Equal(PackageImportStatus.Imported, imported.Status);
            File.Delete(staged);
            var item = imported.Package!;
            Assert.Equal(package.Type, item.Kind);
            Assert.Equal(package.VersionLabel, item.Version);
            Assert.False(item.IsEnabled);
            Assert.Equal(before, File.ReadAllBytes(item.FullPath));
            library.SetEnabled(item, true);
            staged = await installer.InstallAsync(package, release, Path.Combine(temp.Path, "staging"), library.Packages);
            Assert.Equal(PackageImportStatus.AlreadyImported, library.Import(staged, "catalog").Status);
            File.Delete(staged);
            Assert.Equal(before, File.ReadAllBytes(source));
        }
        library = new LocalPackageLibrary(paths, parser);
        library.Load();
        Assert.Equal(2, library.Packages.Count);
        Assert.All(library.Packages, p => Assert.True(p.IsEnabled));
        var selection = new PackageLaunchResolver(parser, paths).Resolve(library.Packages);
        Assert.Single(selection.WeaveMods);
        Assert.Single(selection.JavaAgents);
        foreach (var item in library.Packages.ToArray()) library.SetEnabled(item, false);
        selection = new PackageLaunchResolver(parser, paths).Resolve(library.Packages);
        Assert.Empty(selection.WeaveMods);
        Assert.Empty(selection.JavaAgents);
    }

    [Fact]
    public async Task ModifiedBundleIsRejectedBeforeLibraryImport()
    {
        using var temp = new TestDirectory();
        var package = PackageCatalogService.LoadBundledCatalog()[0];
        var release = package.LatestRelease!;
        var path = PackageCatalogService.ResolveBundledArtifact(package, release, temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "corrupt");
        using var http = new HttpClient(new NoNetwork());
        await Assert.ThrowsAsync<CryptographicException>(() => new ManagedPackageInstaller(http, new JarMetadataParser(), temp.Path)
            .InstallAsync(package, release, Path.Combine(temp.Path, "stage"), []));
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "stage"), "*", SearchOption.AllDirectories));
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Bundled installation must not use the network.");
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Moonrise-catalog-test-" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
