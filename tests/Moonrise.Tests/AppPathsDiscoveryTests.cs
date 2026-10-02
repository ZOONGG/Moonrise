using Moonrise.Infrastructure;
using Xunit;

namespace Moonrise.Tests;

public sealed class AppPathsDiscoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "moonrise-discovery-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false, false, AppMode.Installed)]
    [InlineData(false, true, false, AppMode.Portable)]
    [InlineData(true, false, false, AppMode.Installed)]
    [InlineData(true, true, false, AppMode.Portable)]
    [InlineData(true, false, true, AppMode.Development)]
    [InlineData(true, true, true, AppMode.Development)]
    [InlineData(false, false, true, AppMode.Installed)]
    [InlineData(false, true, true, AppMode.Portable)]
    public void DiscoveryHonorsModePriorityWithoutReadingFileContents(
        bool search, bool marker, bool solution, AppMode expectedMode)
    {
        var repository = Path.Combine(root, "repository");
        var application = Path.Combine(repository, "bin");
        var local = Path.Combine(root, "local");
        Directory.CreateDirectory(application);
        var lockedFiles = new List<FileStream>();
        try
        {
            // Synthetic fixtures only. Exclusive locks make accidental content reads fail.
            foreach (var relative in new[] { "accounts.json", "tokens.json", ".lunarclient/accounts.json",
                         ".lunarclient/settings/launcher.json", ".lunarclient/db/profiles.db" })
            {
                var path = Path.Combine(application, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                lockedFiles.Add(new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
            }
            if (marker)
                lockedFiles.Add(new FileStream(Path.Combine(application, AppPaths.PortableMarkerFileName),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
            if (solution)
                lockedFiles.Add(new FileStream(Path.Combine(repository, "Moonrise.sln"),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));

            var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray();
            var paths = AppPaths.Discover(application, local, search);

            Assert.Equal(expectedMode, paths.Mode);
            Assert.Equal(expectedMode == AppMode.Portable, paths.IsPortable);
            Assert.Equal(expectedMode switch
            {
                AppMode.Portable => Path.Combine(application, "Moonrise-data"),
                AppMode.Development => Path.Combine(local, "Moonrise", "dev"),
                _ => Path.Combine(local, "Moonrise")
            }, paths.RootDirectory);
            Assert.Equal(expectedMode == AppMode.Development ? repository : application, paths.InstallationDirectory);
            Assert.Equal(Path.Combine(paths.InstallationDirectory, "runtime", "bridge", "Moonrise.Native.dll"), paths.NativeBridgePath);
            Assert.Equal(before, Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray());
            Assert.All(lockedFiles, file => Assert.Equal(0, file.Length));
            Assert.False(Directory.Exists(paths.RootDirectory));
        }
        finally
        {
            foreach (var file in lockedFiles) file.Dispose();
        }
    }

    [Fact]
    public void ParentMarkerAndDirectoryNamedLikeMarkerDoNotEnablePortableMode()
    {
        var application = Path.Combine(root, "bin");
        Directory.CreateDirectory(Path.Combine(application, AppPaths.PortableMarkerFileName));
        File.WriteAllText(Path.Combine(root, AppPaths.PortableMarkerFileName), string.Empty);

        var paths = AppPaths.Discover(application, Path.Combine(root, "local"), true);

        Assert.Equal(AppMode.Installed, paths.Mode);
    }

    [Fact]
    public void PortableRelocationKeepsDataAndUpdatesBesideExecutable()
    {
        var original = Path.Combine(root, "original");
        var relocated = Path.Combine(root, "relocated");
        Directory.CreateDirectory(original);
        File.WriteAllText(Path.Combine(original, AppPaths.PortableMarkerFileName), string.Empty);
        var paths = AppPaths.Discover(original, Path.Combine(root, "local"));
        paths.EnsureUserDirectories();
        File.WriteAllText(paths.SettingsPath, "{}");
        Directory.Move(original, relocated);

        var moved = AppPaths.Discover(relocated, Path.Combine(root, "other-local"));

        Assert.Equal(AppMode.Portable, moved.Mode);
        Assert.True(File.Exists(moved.SettingsPath));
        Assert.Equal(Path.Combine(relocated, "Moonrise-data", "cache", "updates"), moved.UpdateDirectory);
        Assert.False(Directory.Exists(Path.Combine(root, "local")));
        Assert.False(Directory.Exists(Path.Combine(root, "other-local")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
