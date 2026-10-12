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

        var deployed = service.Ensure(target);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(target)!, expectedHash, "Moonrise.Native.dll"), deployed);
        Assert.Equal(expected, File.ReadAllBytes(deployed));

        File.WriteAllText(deployed, "corrupt");
        Assert.Equal(deployed, service.Ensure(target));
        Assert.Equal(expected, File.ReadAllBytes(deployed));
    }

    [Fact]
    public void Ensure_DoesNotReplaceLockedPreviousBridge()
    {
        using var temp = new TemporaryDirectory();
        var target = Path.Combine(temp.Path, "Moonrise.Native.dll");
        File.WriteAllText(target, "previous-bridge");
        using var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        var expected = "new-bridge"u8.ToArray();
        var service = new NativeBridgeDeploymentService(() => new MemoryStream(expected),
            Convert.ToHexString(SHA256.HashData(expected)));

        var deployed = service.Ensure(target);

        Assert.NotEqual(target, deployed);
        Assert.Equal(expected, File.ReadAllBytes(deployed));
        Assert.Equal("previous-bridge", File.ReadAllText(target));
        Assert.Equal(deployed, service.Ensure(target));
    }

    [Fact]
    public void BundledBridge_HasPinnedProductionHash()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();

        var deployed = new NativeBridgeDeploymentService().Ensure(paths.NativeBridgeCachePath);

        Assert.Equal(NativeBridgeDeploymentService.ExpectedSha256, ComputeSha256(deployed));
        using var image = new BinaryReader(File.OpenRead(deployed));
        Assert.Equal(0x5A4D, image.ReadUInt16());
        image.BaseStream.Position = 0x3C;
        var peOffset = image.ReadInt32();
        image.BaseStream.Position = peOffset;
        Assert.Equal(0x00004550u, image.ReadUInt32());
        Assert.Equal(0x8664, image.ReadUInt16());
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
