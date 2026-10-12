using Moonrise.Infrastructure;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LaunchCleanupOwnershipTests
{
    [Fact]
    public void StartupCleanupPreservesResearchRepositoriesAndCleansRealSessions()
    {
        var root = Path.Combine(Path.GetTempPath(), "moonrise-cleanup-test-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        var pack = Path.Combine(paths.TempDirectory, "launch-architecture-research", "repo", ".git", "objects", "pack", "pack-test.idx");
        Directory.CreateDirectory(Path.GetDirectoryName(pack)!);
        File.WriteAllText(pack, "synthetic git index");
        File.SetAttributes(pack, FileAttributes.ReadOnly);
        try
        {
            var service = new LaunchSessionService();
            var stale = service.Create(paths.TempDirectory);
            Assert.Equal(1, service.CleanupStale(paths.TempDirectory));
            Assert.False(Directory.Exists(stale.DirectoryPath));
            Assert.Equal("synthetic git index", File.ReadAllText(pack));
            Assert.Empty(service.LastCleanupWarnings);
            Directory.SetLastWriteTimeUtc(Path.Combine(paths.TempDirectory, "launch-architecture-research"),
                DateTime.UtcNow.AddHours(-2));
            var result = new MoonriseStorageService(paths).ClearTemporaryFiles(DateTimeOffset.UtcNow);
            Assert.Equal(0, result.DirectoriesDeleted);
            Assert.Equal("synthetic git index", File.ReadAllText(pack));
        }
        finally
        {
            File.SetAttributes(pack, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LockedSessionDoesNotAbortOtherCleanupAndReportsWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "moonrise-cleanup-test-" + Guid.NewGuid().ToString("N"));
        var service = new LaunchSessionService();
        var locked = service.Create(root);
        var removable = service.Create(root);
        try
        {
            using (var handle = new FileStream(Path.Combine(locked.DirectoryPath, "held.tmp"),
                       FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(1, service.CleanupStale(root));
                Assert.Single(service.LastCleanupWarnings);
                Assert.True(Directory.Exists(locked.DirectoryPath));
                Assert.False(Directory.Exists(removable.DirectoryPath));
            }
            Assert.Equal(1, service.CleanupStale(root));
            Assert.Empty(service.LastCleanupWarnings);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("launch-architecture-research", false)]
    [InlineData("launch-123", false)]
    [InlineData("launch-0123456789abcdef0123456789abcdef", true)]
    [InlineData("launch-01234567-89ab-cdef-0123-456789abcdef", false)]
    public void OnlyGeneratedSessionNamesAreOwned(string name, bool expected) =>
        Assert.Equal(expected, LaunchSessionService.IsLaunchDirectory(name));
}
