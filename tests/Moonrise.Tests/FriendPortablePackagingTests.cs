using Moonrise.Infrastructure;
using Moonrise.Services;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Xunit;

namespace Moonrise.Tests;

public sealed class FriendPortablePackagingTests
{
    [Fact]
    public async Task PortableBuildDisablesEvenInstalledUpdaterAndUsesSharedData()
    {
        // Run explicitly with -p:MoonriseFriendPortable=true.
        if (!UpdateService.IsFriendPortableBuild) return;
        Assert.Null(UpdateService.CreateGithubManager());
        var disabled = new UpdateService(null, new UpdateActivityGate(), () => false);
        Assert.False(disabled.IsEnabled);
        Assert.Null(disabled.PendingRestart);
        Assert.Null(await disabled.CheckAsync());
        Assert.Null(await disabled.CheckAndDownloadAsync(true));
        await disabled.DownloadAsync(null!);
        Assert.False(disabled.TryApplyAndRestart());
        var root = Path.Combine(Path.GetTempPath(), "moonrise-portable-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var locator = new TestVelopackLocator("Moonrise", "0.1.0-beta.1", root,
                root, root, Path.Combine(root, "Update.exe"), "beta");
            var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(root)),
                new UpdateOptions { ExplicitChannel = "beta" }, locator);
            Assert.True(manager.IsInstalled);
            var service = new UpdateService(manager, new UpdateActivityGate(), () => false);
            Assert.False(service.IsEnabled);
            Assert.Null(service.PendingRestart);
            Assert.Null(await service.CheckAsync());
            Assert.Null(await service.CheckAndDownloadAsync(true));
            await service.DownloadAsync(null!);
            Assert.False(service.TryApplyAndRestart());
            var paths = AppPaths.Discover(root, Path.Combine(root, "unused-appdata"), true);
            Assert.False(paths.IsPortable);
            Assert.Equal(Path.Combine(root, "unused-appdata", "Moonrise"), paths.RootDirectory);
            Assert.Empty(Directory.EnumerateFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
