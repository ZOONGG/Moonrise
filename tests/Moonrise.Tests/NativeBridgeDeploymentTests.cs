using System.Security.Cryptography;
using Moonrise.Infrastructure;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class NativeBridgeDeploymentTests
{
    [Fact]
    public void Ensure_ExtractsAndRepairsMissingCompanionFile()
    {
        using var temp = new TemporaryDirectory();
        var expected = "native-bridge-test-bytes"u8.ToArray();
        var expectedHash = Convert.ToHexString(SHA256.HashData(expected));
        var target = Path.Combine(temp.Path, "cache", "runtime", "bridge", "Moonrise.Native.dll");
        var service = new NativeBridgeDeploymentService(
            () => new MemoryStream(expected, writable: false),
            expectedHash);

        Assert.Equal(target, service.Ensure(target));
        Assert.Equal(expected, File.ReadAllBytes(target));

        File.WriteAllText(target, "corrupt");
        Assert.Equal(target, service.Ensure(target));
        Assert.Equal(expected, File.ReadAllBytes(target));
    }

    [Fact]
    public void BundledBridge_HasPinnedProductionHash()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();

        var deployed = new NativeBridgeDeploymentService().Ensure(paths.NativeBridgeCachePath);

        Assert.Equal(NativeBridgeDeploymentService.ExpectedSha256, ComputeSha256(deployed));
        Assert.True(new FileInfo(deployed).Length > 100_000);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-native-bridge-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
