using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LegacyWeaveCompatibilityTests
{
    [Fact]
    public void RecoveryLaunch_CopiesLegacyApiModWithoutChangingSource()
    {
        using var temp = new TemporaryDirectory();
        var original = Path.Combine(temp.Path, "legacy.jar");
        using (var archive = ZipFile.Open(original, ZipArchiveMode.Create))
        {
            Write(archive, "weave.mod.json",
                """{"entrypoints":["sample.Legacy"],"namespace":"mcp-named"}""");
            Write(archive, "sample/Legacy.class",
                "net/weavemc/loader/api/Hook org.objectweb.asm.tree.ClassNode net/weavemc/loader/api/command/CommandBus");
        }

        var sourceHash = ComputeSha256(original);
        var parser = new JarMetadataParser();
        var package = parser.ParseWeaveMod(original);
        var enabledDirectory = new EnabledModDirectoryService(parser).Create(
            Path.Combine(temp.Path, "launch-1"),
            [package],
            legacyWeaveLayout: true);
        var copied = Path.Combine(enabledDirectory, "legacy.jar");

        Assert.Equal(sourceHash, ComputeSha256(original));
        Assert.Equal(sourceHash, ComputeSha256(copied));
        Assert.EndsWith(Path.Combine(".weave", "mods"), enabledDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-legacy-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
