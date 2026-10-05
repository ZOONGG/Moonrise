using System.Collections.Immutable;
using System.IO.Compression;
using Moonrise.Compatibility;
using Xunit;

namespace Moonrise.Tests;

public sealed class ControlledGenesisResourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Moonrise resource tests with spaces", Guid.NewGuid().ToString("N"));
    private const string Ui = "1111111111111111111111111111111111111111";
    private string Native => Path.Combine(_root, "source", "offline", "multiver", "natives");
    private string Work => Path.Combine(_root, "owned", "launch");
    private string FileAt(string path, string content = "resource")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
    private string Fixture()
    {
        foreach (var name in new[] { "cacert.pem", "icudt67l.dat", "mediaControls.css", "mediaControls.js", "mediaControlsLocalizedStrings.js" })
            FileAt(Path.Combine(Native, "resources", name));
        FileAt(Path.Combine(_root, "source", "ui", Ui, "index.html"));
        FileAt(Path.Combine(_root, "source", "ui", Ui, "static", "index.js"));
        FileAt(Path.Combine(_root, "source", "textures", "assets", "lunar", "logo.png"));
        FileAt(Path.Combine(_root, "source", "textures", "assets", "lunar", "cosmetics", "index"));
        FileAt(Path.Combine(_root, "source", "textures", "assets", "lunar", "jit_index"));
        FileAt(Path.Combine(_root, "source", "textures", "assets", "lunar", "emotes", "actions.bobj"));
        var jar = Path.Combine(_root, "source", "offline", "multiver", "lunar.jar");
        using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("lunarBuildData.txt").Open())) writer.Write("uiGitHash=" + new string('2', 40) + "\n");
        return ArtifactSnapshots.Capture(jar, "fixture", false).Sha256;
    }
    private ImmutableArray<RuntimeResourceRoot> Discover(string sha) => ControlledGenesisResources.Discover(
        ControlledGenesisDiscovery.ContractId, Path.Combine(_root, "source"), Work, sha, Ui);

    [Fact] public void VersionedDiscoveryFreezesExpectedWebUiAndTexturePaths()
    {
        var sha = Fixture(); var roots = Discover(sha);
        Assert.Equal(roots, Discover(sha), new RootsComparer());
        Assert.Equal(Path.Combine(Work, "natives", "web", "resources"), roots[0].PreparedRoot);
        Assert.Equal(5, roots[0].Files.Length);
        Assert.Contains("sourceSha1=" + Ui, roots[1].VersionEvidence);
        Assert.All(roots, r => Assert.Equal("Moonrise-owned launch copy", r.Ownership));
        Assert.Throws<NotSupportedException>(() => ControlledGenesisResources.Discover("future", _root, Work));
        Assert.Throws<NotSupportedException>(() => Discover(new string('0', 64)));
    }
    [Fact] public void MissingRequiredWebResourceFailsBeforePreparation()
    {
        Fixture(); File.Delete(Path.Combine(Native, "resources", "icudt67l.dat"));
        Assert.Contains("icudt67l.dat", Assert.Throws<NotSupportedException>(() => ControlledGenesisResources.DiscoverWeb(Native)).Message);
        Assert.False(Directory.Exists(Work));
    }
    [Fact] public void AlreadyProvisionedWebSubdirectoryIsDiscovered()
    {
        Fixture(); Directory.Move(Path.Combine(Native, "resources"), Path.Combine(Native, "web-resources"));
        Directory.CreateDirectory(Path.Combine(Native, "web"));
        Directory.Move(Path.Combine(Native, "web-resources"), Path.Combine(Native, "web", "resources"));
        Assert.All(ControlledGenesisResources.DiscoverWeb(Native), f => Assert.Contains(Path.Combine("web", "resources"), f.CanonicalPath));
    }
    [Theory]
    [InlineData("ui")]
    [InlineData("jit")]
    public void MissingBootstrapResourceIsAnExplicitPreflightError(string kind)
    {
        var sha = Fixture();
        File.Delete(kind == "ui" ? Path.Combine(_root, "source", "ui", Ui, "index.html") : Path.Combine(_root, "source", "textures", "assets", "lunar", "jit_index"));
        Assert.Throws<NotSupportedException>(() => Discover(sha));
        Assert.False(Directory.Exists(Work));
    }
    [Fact] public void PreparationRejectsInjectedAccountInputAndSourceTraversal()
    {
        var sha = Fixture(); var roots = Discover(sha);
        var account = FileAt(Path.Combine(roots[1].SourceRoot, "accounts.json"), "synthetic account fixture");
        var accountArtifact = ArtifactSnapshots.Capture(account, "synthetic fixture", false);
        var injected = roots.SetItem(1, roots[1] with { Files = roots[1].Files.Add(accountArtifact) });
        Assert.Throws<NotSupportedException>(() => ControlledGenesisResources.Prepare(injected, Work));
        Assert.False(Directory.Exists(Work));
        var outside = FileAt(Path.Combine(_root, "outside source", "index.html"));
        injected = roots.SetItem(1, roots[1] with { Files = [ArtifactSnapshots.Capture(outside, "fixture", false)] });
        Assert.Throws<InvalidOperationException>(() => ControlledGenesisResources.Prepare(injected, Work));
        Assert.False(Directory.Exists(Work));
    }
    [Fact] public void PreparationExcludesSecretsPreservesSourcesAndCleansOnlyOwnedWork()
    {
        var sha = Fixture();
        var ui = Path.Combine(_root, "source", "ui", Ui);
        foreach (var name in new[] { "accounts.json", "accessToken.json", "credentials.js", "session.json", "auth.json", "settings.ini", "accounts/nested.png" }) FileAt(Path.Combine(ui, name));
        var roots = Discover(sha); var originals = roots.SelectMany(r => r.Files).ToImmutableArray();
        ControlledGenesisResources.Prepare(roots, Work);
        Assert.True(File.Exists(Path.Combine(Work, "lunar-data", "ui", "index.html")));
        Assert.False(File.Exists(Path.Combine(Work, "lunar-data", "ui", "accounts.json")));
        Assert.False(Directory.Exists(Path.Combine(Work, "lunar-data", "ui", "accounts")));
        Assert.True(File.Exists(Path.Combine(Work, "lunar-data", "textures", "assets", "lunar", "jit_index")));
        Assert.True(File.Exists(Path.Combine(Work, "lunar-data", "textures", "assets", "lunar", "emotes", "actions.bobj")));
        Assert.Empty(ArtifactSnapshots.Validate(originals));
        var sibling = FileAt(Path.Combine(_root, "owned", "unrelated", "keep.txt"));
        ControlledGenesisResources.Cleanup(Path.Combine(_root, "owned"), Work);
        Assert.False(Directory.Exists(Work)); Assert.True(File.Exists(sibling)); Assert.Empty(ArtifactSnapshots.Validate(originals));
        Assert.Throws<InvalidOperationException>(() => ControlledGenesisResources.Cleanup(_root, ui));
    }
    [Fact] public void TraversalAndChangedInputAreRejected()
    {
        var sha = Fixture(); var roots = Discover(sha);
        Assert.Throws<InvalidOperationException>(() => ControlledGenesisResources.EnsureBounded(_root, Path.Combine(_root, "..", "outside")));
        Assert.Throws<InvalidOperationException>(() => ControlledGenesisResources.Prepare(roots.SetItem(0, roots[0] with { PreparedRoot = Path.Combine(_root, "outside") }), Work));
        Assert.False(Directory.Exists(Path.Combine(_root, "outside")));
        File.AppendAllText(roots[0].Files[0].CanonicalPath, "changed");
        Assert.Throws<InvalidOperationException>(() => ControlledGenesisResources.Prepare(roots, Path.Combine(_root, "other work")));
    }
    private sealed class RootsComparer : IEqualityComparer<ImmutableArray<RuntimeResourceRoot>>
    {
        public bool Equals(ImmutableArray<RuntimeResourceRoot> a, ImmutableArray<RuntimeResourceRoot> b) =>
            System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);
        public int GetHashCode(ImmutableArray<RuntimeResourceRoot> obj) => 0;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
