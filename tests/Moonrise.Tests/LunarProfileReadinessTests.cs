using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LunarProfileReadinessTests
{
    private const string ProfileId = "78285335-7848-4649-a886-1b14d2be1a46";
    private static readonly LauncherProfile ExpectedProfile = new(
        ProfileId,
        "Minecraft 1.8",
        "lunar",
        "1.8",
        "1.8.9");

    [Theory]
    [InlineData("[Metadata] Profile found in settings, using Minecraft (type: lunar, version: 1.8.9)")]
    [InlineData("[Metadata] Profile selected, using Minecraft 1.8 (ID: 78285335-7848-4649-a886-1b14d2be1a46, type: lunar, version: 1.8.9)...")]
    [InlineData("[Metadata] Profile prepared after update (type=lunar; version=1.8.9)")]
    public void StructuredProfileParser_SurvivesLauncherWordingChanges(string line)
    {
        Assert.True(LunarProfileReadinessService.TryParseSelectedProfile(line, out var selected));
        Assert.Equal("lunar", selected.Client);
        Assert.Equal("1.8.9", selected.Version);
    }

    [Fact]
    public async Task Readiness_RecognizesTheCurrentLunarLauncherLog()
    {
        using var temporary = new TemporaryLog();
        var readiness = new LunarProfileReadinessService().WaitForSelectedProfileAsync(
            temporary.Path,
            0,
            ExpectedProfile,
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        await File.AppendAllTextAsync(
            temporary.Path,
            $"[Metadata] Profile selected, using Minecraft 1.8 (ID: {ProfileId}, type: lunar, version: 1.8.9)...{Environment.NewLine}");

        var selected = await readiness;
        Assert.Equal(("lunar", "1.8.9"), selected);
    }

    [Fact]
    public async Task Readiness_FallsBackToExactProfileAndReadyMarkers()
    {
        using var temporary = new TemporaryLog();
        var readiness = new LunarProfileReadinessService().WaitForSelectedProfileAsync(
            temporary.Path,
            0,
            ExpectedProfile,
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        await File.AppendAllTextAsync(
            temporary.Path,
            $"[FutureProfiles] Prepared profile {ProfileId}{Environment.NewLine}" +
            $"[Window] Renderer ready for window 'main'{Environment.NewLine}");

        var selected = await readiness;
        Assert.Equal(("lunar", "1.8.9"), selected);
    }

    private sealed class TemporaryLog : IDisposable
    {
        public TemporaryLog()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-lunar-readiness-{Guid.NewGuid():N}.log");
            File.WriteAllText(Path, string.Empty);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
