using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LunarAccountProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MoonriseAccountTests-" + Guid.NewGuid());
    private string Store => Path.Combine(_root, "accounts.json");
    public LunarAccountProviderTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private LunarAccountProvider Provider(bool installed = true) => new(() => installed, () => Store);
    private static string Fixture(bool second = false, string active = "first") =>
        "{\"activeAccountLocalId\":\"" + active + "\",\"accounts\":{\"first\":{\"localId\":\"first\",\"minecraftProfile\":{\"id\":\"11111111111111111111111111111111\",\"name\":\"SyntheticOne\"},\"accessToken\":\"SECRET_SENTINEL\",\"refreshToken\":\"REFRESH_SENTINEL\"}" +
        (second ? ",\"second\":{\"localId\":\"second\",\"minecraftProfile\":{\"id\":\"22222222222222222222222222222222\",\"name\":\"SyntheticTwo\"}}" : "") + "}}";
    [Fact]
    public async Task DiscoversProjectionWithoutWritingOrExposingSecrets()
    {
        var text = Fixture(); File.WriteAllText(Store, text);
        var snapshot = await Provider().RefreshAsync();
        Assert.Null(snapshot.Diagnostic);
        Assert.Equal("SyntheticOne", Assert.Single(snapshot.Accounts).Name);
        Assert.True(snapshot.Accounts[0].IsSelected);
        Assert.Equal(text, File.ReadAllText(Store));
        Assert.DoesNotContain("SENTINEL", snapshot.ToString());
        Assert.DoesNotContain("SENTINEL", snapshot.Accounts[0].ToString());
    }
    [Fact]
    public async Task ExternalAdditionRemovalAndSelectionPublishWholeSnapshots()
    {
        var provider = Provider(); File.WriteAllText(Store, Fixture());
        var first = await provider.RefreshAsync();
        var replacement = Path.Combine(_root, "replacement");
        File.WriteAllText(replacement, Fixture(true, "second")); File.Move(replacement, Store, true);
        var second = await provider.RefreshAsync();
        Assert.Single(first.Accounts); Assert.Equal(2, second.Accounts.Count);
        Assert.Equal("SyntheticTwo", second.Accounts.Single(a => a.IsSelected).Name);
        File.WriteAllText(Store, Fixture());
        Assert.Single((await provider.RefreshAsync()).Accounts);
        Assert.Equal(2, second.Accounts.Count);
        File.WriteAllText(Store, "{\"accounts\":{}}");
        Assert.Empty((await provider.RefreshAsync()).Accounts);
    }
    [Theory]
    [InlineData("{\"SECRET_SENTINEL\":")]
    [InlineData("{\"accounts\":{\"bad\":{}}}")]
    [InlineData("{\"accounts\":[]}")]
    public async Task InvalidStoreRetainsLastValidSnapshotAndSanitizesDiagnostics(string invalid)
    {
        var provider = Provider(); File.WriteAllText(Store, Fixture());
        var valid = await provider.RefreshAsync(); File.WriteAllText(Store, invalid);
        var failed = await provider.RefreshAsync();
        Assert.Same(valid.Accounts, failed.Accounts);
        Assert.Equal("ACCOUNT_STORE_INVALID", failed.Diagnostic);
        Assert.DoesNotContain("SENTINEL", failed.ToString());
        File.WriteAllText(Store, "{\"accounts\":{}}");
        Assert.Empty((await provider.RefreshAsync()).Accounts);
    }
    [Fact]
    public async Task MissingAndUninstalledAreExplicitAndDoNotCreateStore()
    {
        Assert.Equal("ACCOUNT_STORE_MISSING", (await Provider().RefreshAsync()).Diagnostic);
        Assert.Equal("LUNAR_NOT_INSTALLED", (await Provider(false).RefreshAsync()).Diagnostic);
        Assert.False(File.Exists(Store));
    }
    [Fact]
    public async Task MissingStoreRetainsSnapshotUntilValidSourceReturns()
    {
        var provider = Provider(); File.WriteAllText(Store, Fixture());
        var valid = await provider.RefreshAsync();
        File.Delete(Store);
        var missing = await provider.RefreshAsync();
        Assert.Equal("ACCOUNT_STORE_MISSING", missing.Diagnostic);
        Assert.Same(valid.Accounts, missing.Accounts);
        Assert.False(File.Exists(Store));
        File.WriteAllText(Store, Fixture(true, "second"));
        var recovered = await provider.RefreshAsync();
        Assert.Null(recovered.Diagnostic);
        Assert.Equal(2, recovered.Accounts.Count);
        Assert.Equal("SyntheticTwo", recovered.Accounts.Single(a => a.IsSelected).Name);
        File.WriteAllText(Store, "{\"accounts\":{}}");
        Assert.Empty((await provider.RefreshAsync()).Accounts);
    }
    [Fact]
    public async Task LockedWriterRetainsSnapshotAndRecovers()
    {
        var provider = Provider(); File.WriteAllText(Store, Fixture());
        var valid = await provider.RefreshAsync();
        using (var writer = new FileStream(Store, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            var failed = await provider.RefreshAsync();
            Assert.Equal("ACCOUNT_STORE_UNREADABLE", failed.Diagnostic);
            Assert.Same(valid.Accounts, failed.Accounts);
        }
        Assert.Null((await provider.RefreshAsync()).Diagnostic);
    }
    [Fact]
    public async Task TemporaryPartialWriteRecoversWithinRetryWindow()
    {
        var provider = Provider(); File.WriteAllText(Store, Fixture());
        var valid = await provider.RefreshAsync();
        File.WriteAllText(Store, "{");
        var refresh = provider.RefreshAsync();
        await Task.Delay(250);
        var replacement = Path.Combine(_root, "partial-write-replacement");
        File.WriteAllText(replacement, Fixture(true));
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { File.Move(replacement, Store, true); break; }
                catch (IOException) when (attempt < 50) { await Task.Delay(10); }
            }
        }
        finally { await refresh; } // Never remove the fixture while its reader is active.
        var recovered = await refresh;
        Assert.Null(recovered.Diagnostic);
        Assert.Equal(2, recovered.Accounts.Count);
        Assert.Single(valid.Accounts);
    }
    [Fact]
    public async Task DiscoveryFailureIsSanitized()
    {
        var provider = new LunarAccountProvider(() => throw new Exception("SECRET_SENTINEL"), () => Store);
        Assert.Equal("ACCOUNT_SYNC_FAILED", (await provider.RefreshAsync()).Diagnostic);
    }
}
