using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Moonrise.Services;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Xunit;

namespace Moonrise.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moonrise-update-tests-" + Guid.NewGuid().ToString("N"));
    public UpdateServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private UpdateService Create(string current = "0.1.0-beta.1", Func<bool>? busy = null, IUpdateSource? source = null, UpdateActivityGate? activity = null)
    {
        var packages = Path.Combine(_root, "packages");
        Directory.CreateDirectory(packages);
        var locator = new TestVelopackLocator("Moonrise", current, packages,
            _root, _root, Path.Combine(_root, "Update.exe"), "beta");
        var manager = new UpdateManager(source ?? new SimpleFileSource(new DirectoryInfo(_root)),
            new UpdateOptions { ExplicitChannel = "beta" }, locator);
        return new UpdateService(manager, activity ?? new UpdateActivityGate(), busy ?? (() => false));
    }

    private void Feed(string version, bool corrupt = false)
    {
        var name = $"Moonrise-{version}-full.nupkg";
        var path = Path.Combine(_root, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("Moonrise.nuspec").Open()))
            writer.Write($"<?xml version=\"1.0\"?><package><metadata><id>Moonrise</id><version>{version}</version><authors>Moonrise</authors><description>test</description></metadata></package>");
        var bytes = File.ReadAllBytes(path);
        var asset = new VelopackAsset
        {
            PackageId = "Moonrise", Version = SemanticVersion.Parse(version),
            Type = VelopackAssetType.Full, FileName = name, Size = bytes.Length,
            SHA1 = Convert.ToHexString(SHA1.HashData(bytes)),
            SHA256 = corrupt ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes))
        };
        File.WriteAllText(Path.Combine(_root, "releases.beta.json"),
            JsonSerializer.Serialize(new { Assets = new[] { new { asset.PackageId, Version = version, Type = "Full", asset.FileName, asset.Size, asset.SHA1, asset.SHA256 } } }));
    }

    [Fact]
    public async Task LocalBetaFeedDownloadsAndStagesWithoutTouchingUserData()
    {
        Feed("0.1.0-beta.2");
        var sentinel = Path.Combine(_root, "user-settings.json");
        File.WriteAllText(sentinel, "preserve");
        var service = Create();
        Assert.True(service.IsEnabled);
        var update = Assert.IsType<UpdateInfo>(await service.CheckAsync());
        Assert.Equal("0.1.0-beta.2", update.TargetFullRelease.Version.ToString());
        await service.DownloadAsync(update);
        Assert.NotNull(service.PendingRestart);
        Assert.Equal("preserve", File.ReadAllText(sentinel));
    }

    [Theory]
    [InlineData("0.1.0-beta.2")]
    [InlineData("0.1.0")]
    public async Task EqualOrOlderBetaDoesNotDowngrade(string current)
    {
        Feed("0.1.0-beta.2");
        Assert.Null(await Create(current).CheckAsync());
    }

    [Fact]
    public async Task CorruptDownloadCannotBecomePending()
    {
        Feed("0.1.0-beta.2", corrupt: true);
        var service = Create();
        var update = Assert.IsType<UpdateInfo>(await service.CheckAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => service.DownloadAsync(update));
        Assert.Null(service.PendingRestart);
    }

    [Fact]
    public async Task GameBlocksRestartAfterDownload()
    {
        Feed("0.1.0-beta.2");
        var service = Create(busy: () => true);
        await service.DownloadAsync(Assert.IsType<UpdateInfo>(await service.CheckAsync()));
        Assert.False(service.TryApplyAndRestart());
    }

    [Fact]
    public async Task RestartRemovesTrayBeforeHandoffAndRestoresItOnFailure()
    {
        Feed("0.1.0-beta.2");
        var service = Create();
        await service.CheckAndDownloadAsync(true);
        var events = new List<string>();
        service.ApplyOverride = _ =>
        {
            events.Add("handoff");
            throw new IOException("Simulated updater failure");
        };
        Assert.Throws<IOException>(() => service.TryApplyAndRestart(
            () => events.Add("remove"), () => events.Add("restore")));
        Assert.Equal(new[] { "remove", "handoff", "restore" }, events);
        events.Clear();
        service.ApplyOverride = _ => events.Add("handoff");
        Assert.True(service.TryApplyAndRestart(
            () => events.Add("remove"), () => events.Add("restore")));
        Assert.Equal(new[] { "remove", "handoff" }, events);
    }

    [Fact]
    public async Task BlockedRestartKeepsTrayUntouched()
    {
        Feed("0.1.0-beta.2");
        var service = Create(busy: () => true);
        await service.CheckAndDownloadAsync(true);
        Assert.False(service.TryApplyAndRestart(
            () => Assert.Fail("Blocked restart removed tray"),
            () => Assert.Fail("Blocked restart recreated tray")));
    }

    [Fact]
    public void ActivityLeaseBlocksApplyAndApplyBlocksNewOperations()
    {
        var gate = new UpdateActivityGate();
        using (gate.EnterOperation()) Assert.Null(gate.TryEnterApply(() => false));
        Assert.Null(gate.TryEnterApply(() => true));
        using (gate.TryEnterApply(() => false))
            Assert.Throws<InvalidOperationException>(() => gate.EnterOperation());
        using var next = gate.EnterOperation();
    }

    [Fact]
    public async Task AutomaticDownloadSurvivesRestartWithoutFeedOrRedownload()
    {
        Feed("0.1.0-beta.2");
        var service = Create();
        var found = 0;
        await service.CheckAndDownloadAsync(true, _ => found++);
        Assert.NotNull(service.PendingRestart);
        File.Delete(Path.Combine(_root, "releases.beta.json"));
        var restarted = Create();
        Assert.NotNull(restarted.PendingRestart);
        Assert.Null(await restarted.CheckAndDownloadAsync(true, _ => found++));
        Assert.Equal(1, found);
    }

    [Fact]
    public async Task DisabledAutomaticDownloadStillDiscoversForManualDownload()
    {
        Feed("0.1.0-beta.2");
        var service = Create();
        var update = Assert.IsType<UpdateInfo>(await service.CheckAndDownloadAsync(false));
        Assert.Null(service.PendingRestart);
        await service.DownloadAsync(update);
        Assert.NotNull(service.PendingRestart);
    }

    [Fact]
    public async Task ConcurrentAutomaticDownloadsStageOnlyOnce()
    {
        Feed("0.1.0-beta.2");
        var service = Create();
        await Task.WhenAll(service.CheckAndDownloadAsync(true), service.CheckAndDownloadAsync(true));
        Assert.NotNull(service.PendingRestart);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "packages"), "*.nupkg"));
    }

    [Fact]
    public async Task FeedFailureLeavesInstalledVersionAndAllowsRetry()
    {
        File.WriteAllText(Path.Combine(_root, "releases.beta.json"), "invalid response");
        var service = Create();
        await Assert.ThrowsAnyAsync<Exception>(() => service.CheckAndDownloadAsync(true));
        Assert.Null(service.PendingRestart);
        Feed("0.1.0-beta.2");
        await service.CheckAndDownloadAsync(true);
        Assert.NotNull(service.PendingRestart);
    }

    [Fact]
    public async Task StableReleaseInBetaFeedIsRejected()
    {
        Feed("0.2.0");
        var service = Create();
        Assert.Null(await service.CheckAndDownloadAsync(true));
        Assert.Null(service.PendingRestart);
    }

    [Fact]
    public async Task PendingRestartIsAllowedAfterDownloadCheckAndOperationComplete()
    {
        Feed("0.1.0-beta.2");
        var gate = new UpdateActivityGate();
        var launching = false;
        var service = Create();
        await service.CheckAndDownloadAsync(true);
        // A fresh service reads the staged package without needing the feed.
        service = Create(busy: () => launching, activity: gate);
        var applied = 0;
        service.ApplyOverride = _ => applied++;
        Assert.Null(await service.CheckAndDownloadAsync(true));
        Assert.True(service.TryApplyAndRestart());
        Assert.Null(service.RestartBlockReason);
        launching = true;
        Assert.False(service.TryApplyAndRestart());
        Assert.Equal("game-or-launch-active", service.RestartBlockReason);
        launching = false;
        Assert.True(service.TryApplyAndRestart());
        Assert.Equal(2, applied);
        using (gate.EnterOperation())
        {
            Assert.False(service.TryApplyAndRestart());
            Assert.Equal("moonrise-operation-active", service.RestartBlockReason);
        }
        Assert.True(service.TryApplyAndRestart());
        Assert.Null(service.RestartBlockReason);
    }

    [Theory]
    [InlineData("java", @"C:\tools\jdk\bin\java.exe", "Gradle daemon", 10, false)]
    [InlineData("javaw", @"C:\tools\jdk\bin\javaw.exe", "", 0, false)]
    [InlineData("javaw", @"C:\Users\test\.lunarclient\jre\bin\javaw.exe", "", 0, true)]
    [InlineData("javaw", @"C:\tools\jdk\bin\javaw.exe", "Minecraft 1.8.9", 10, true)]
    public void OnlyMinecraftJavaMetadataBlocksRestart(string name, string path, string title, long window, bool blocked)
    {
        Assert.Equal(blocked, UpdateService.IsMinecraftProcess(new Moonrise.Models.ProcessRecord(
            123, name, path, window, title, window != 0, true)));
    }

    private sealed class OfflineSource : IUpdateSource
    {
        public Task<VelopackAssetFeed> GetReleaseFeed(Velopack.Logging.IVelopackLogger logger,
            string? appId, string channel, Guid? stagingId, VelopackAsset? latestLocalRelease)
            => throw new System.Net.Http.HttpRequestException("offline");

        public Task DownloadReleaseEntry(Velopack.Logging.IVelopackLogger logger, VelopackAsset releaseEntry,
            string localFile, Action<int> progress, CancellationToken cancelToken)
            => throw new System.Net.Http.HttpRequestException("offline");
    }

    [Fact]
    public async Task NetworkFailureDoesNotStageOrPreventFurtherOperations()
    {
        var service = Create(source: new OfflineSource());
        await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => service.CheckAndDownloadAsync(true));
        Assert.Null(service.PendingRestart);
        Assert.False(service.TryApplyAndRestart());
        await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => service.CheckAndDownloadAsync(true));
    }

    [Fact]
    public void NotificationClaimsSurviveSettingsRoundTrip()
    {
        var store = new AppSettingsService(Path.Combine(_root, "settings.json"));
        var settings = store.Load();
        Assert.True(settings.AutoDownloadUpdates);
        Assert.True(UpdateNotifications.TryClaim(settings, "0.1.0-beta.2", false));
        Assert.True(UpdateNotifications.TryClaim(settings, "0.1.0-beta.2", true));
        store.Save(settings);
        settings = store.Load();
        Assert.False(UpdateNotifications.TryClaim(settings, "0.1.0-beta.2", false));
        Assert.False(UpdateNotifications.TryClaim(settings, "0.1.0-beta.2", true));
        Assert.True(UpdateNotifications.TryClaim(settings, "0.1.0-beta.3", false));
    }

    [Fact]
    public async Task DevelopmentBuildDoesNotRequestFeed()
    {
        var service = new UpdateService(new UpdateManager(new SimpleFileSource(new DirectoryInfo(_root)), locator: new WindowsVelopackLocator(null, null)), new UpdateActivityGate(), () => false);
        Assert.False(service.IsEnabled);
        Assert.Null(await service.CheckAsync());
        Assert.False(service.TryApplyAndRestart());
    }
}

