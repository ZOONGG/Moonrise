using Moonrise.Infrastructure;
using Moonrise.Services;
using System.IO.Compression;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Moonrise.Tests;

public sealed class BwhApiRelayServiceTests
{
    [Theory]
    [InlineData("/v2/player/?uuid=0123456789abcdef0123456789abcdef")]
    [InlineData("/v2/player/?uuid=01234567-89ab-cdef-0123-456789abcdef")]
    public void TryBuildUpstreamUri_AcceptsOnlyPlayerUuid(string target)
    {
        Assert.True(BwhApiRelayService.TryBuildUpstreamUri(target, out var upstream));
        Assert.Equal("https", upstream!.Scheme);
        Assert.Equal("api.hypixel.net", upstream.Host);
        Assert.Equal("/v2/player", upstream.AbsolutePath);
    }

    [Theory]
    [InlineData("/v2/player/?uuid=not-a-uuid")]
    [InlineData("/v2/player/?name=player")]
    [InlineData("/v2/guild/?uuid=0123456789abcdef0123456789abcdef")]
    [InlineData("https://example.com/v2/player/?uuid=0123456789abcdef0123456789abcdef")]
    public void TryBuildUpstreamUri_RejectsAnythingOutsideAllowList(string target)
    {
        Assert.False(BwhApiRelayService.TryBuildUpstreamUri(target, out var upstream));
        Assert.Null(upstream);
    }

    [Fact]
    public async Task Relay_RejectsMissingKeyOnLoopbackWithoutCallingUpstream()
    {
        using var relay = new BwhApiRelayService(port: 0);
        relay.Start();
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", relay.ListeningPort);
        var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes(
            "GET /v2/player/?uuid=0123456789abcdef0123456789abcdef HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request);
        using var response = new MemoryStream();
        await stream.CopyToAsync(response);
        var text = Encoding.UTF8.GetString(response.ToArray());
        Assert.StartsWith("HTTP/1.1 401 Unauthorized", text, StringComparison.Ordinal);
        Assert.Contains("missing API key", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentDeployment_ExtractsPinnedJavaAgent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"moonrise-bwh-agent-tests-{Guid.NewGuid():N}");
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureUserDirectories();
            var deployed = new BwhNetworkAgentDeploymentService(paths).Ensure();
            using var stream = File.OpenRead(deployed);
            Assert.Equal(
                BwhNetworkAgentDeploymentService.ExpectedSha256,
                Convert.ToHexString(SHA256.HashData(stream)));
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifest = archive.GetEntry("META-INF/MANIFEST.MF");
            Assert.NotNull(manifest);
            using var reader = new StreamReader(manifest!.Open(), Encoding.UTF8);
            Assert.Contains("Premain-Class: moonrise.bwh.BwhNetworkAgent", reader.ReadToEnd(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
