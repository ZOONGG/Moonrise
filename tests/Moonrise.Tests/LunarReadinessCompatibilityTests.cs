using System.Text.Json;
using Moonrise.Models;
using Moonrise.Services;
using Moonrise.Tests.Fixtures;
using Xunit;

namespace Moonrise.Tests;

public sealed class LunarReadinessCompatibilityTests
{
    private static readonly LauncherProfile Profile = new("synthetic-profile-a", "Synthetic", "lunar", "1.8", "1.8.9");
    private const string Metadata = "[Metadata] Lunar versions metadata fetched successfully\n[Metadata] Setting up virtual profiles\n";
    private const string Selected = Metadata + "[Metadata] Profile selected (type: lunar, version: 1.8.9)";
    private const string Ready = "[Window] Renderer ready for window 'main'\n";

    private static Task<(string Client, string Version)> Wait(SyntheticLunarFiles files, long checkpoint = 0,
        double seconds = 3, CancellationToken token = default) =>
        new LunarProfileReadinessService().WaitForSelectedProfileAsync(files.Log, checkpoint, Profile, TimeSpan.FromSeconds(seconds), token);

    [Theory]
    [InlineData("[Metadata] Profile found in settings, using Minecraft (type: lunar, version: 1.8.9)")]
    [InlineData("[Metadata] Profile selected, using Minecraft 1.8 (ID: synthetic-profile-a, type: lunar, version: 1.8.9)...")]
    [InlineData("[Metadata] Profile prepared after update (type=LUNAR; version=1.8.9)")]
    public async Task CompleteCurrentAndChangedWordingLines_AreParsed(string line)
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, Metadata + line + "\r\n");
        Assert.Equal(("lunar", "1.8.9"), await Wait(files));
    }

    [Fact]
    public async Task PartialAppend_IsNotParsedUntilNewlineAndKeepsAllFragments()
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, Selected[..35]);
        var waiting = Wait(files);
        await File.AppendAllTextAsync(files.Log, Selected[35..]);
        await Task.Delay(250);
        Assert.False(waiting.IsCompleted);
        await File.AppendAllTextAsync(files.Log, "\r\n");
        Assert.Equal(("lunar", "1.8.9"), await waiting);
    }

    [Fact]
    public async Task Checkpoint_IgnoresOldEvidenceAndUsesByteOffsets()
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, "Синтетический лог\n" + Selected + "\n" + Ready);
        var checkpoint = LunarProfileReadinessService.CaptureCheckpoint(files.Log);
        Assert.Equal(new FileInfo(files.Log).Length, checkpoint);
        var waiting = Wait(files, checkpoint);
        await Task.Delay(250);
        Assert.False(waiting.IsCompleted);
        await File.AppendAllTextAsync(files.Log, Metadata + "[Metadata] Profile changed (type: vanilla, version: 1.20.1)\n");
        // Report the actual structured observation so the caller can reject a mismatch.
        Assert.Equal(("vanilla", "1.20.1"), await waiting);
    }

    [Fact]
    public async Task LastCompleteStructuredObservation_Wins()
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, Selected + "\n[Metadata] Profile changed (type: vanilla, version: 1.20.1)\n");
        Assert.Equal(("vanilla", "1.20.1"), await Wait(files));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedOrRotatedLog_IsReadFromStart(bool rotate)
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, new string('x', 240) + "\n");
        var waiting = Wait(files, LunarProfileReadinessService.CaptureCheckpoint(files.Log));
        if (rotate) File.Move(files.Log, files.Log + ".old");
        File.WriteAllText(files.Log, Selected + "\n" + (rotate ? new string('y', 300) + "\n" : ""));
        Assert.Equal(("lunar", "1.8.9"), await waiting);
    }

    [Fact]
    public async Task SameLengthRewrite_IsDetected()
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, new string('x', Selected.Length) + "\n");
        var waiting = Wait(files, LunarProfileReadinessService.CaptureCheckpoint(files.Log));
        File.WriteAllText(files.Log, Selected + "\n");
        Assert.Equal(("lunar", "1.8.9"), await waiting);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_DiscardsOldPendingTextAndReadyEvidence(bool deleteFirst)
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, Ready + Selected[..30] + new string(' ', 200));
        var waiting = Wait(files, seconds: 1.8);
        if (deleteFirst)
        {
            File.Delete(files.Log);
            await Task.Delay(250);
        }
        File.WriteAllText(files.Log, "version: 1.8.9)\n");
        await Assert.ThrowsAsync<TimeoutException>(() => waiting);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[Window] Renderer not ready for window 'main'\n")]
    [InlineData("[Window] already created\n")]
    [InlineData("[Window] not-ready\n")]
    [InlineData("[Other] ready\n")]
    [InlineData("[FutureProfiles] Prepared profile synthetic-profile-a\n")]
    public async Task MissingOrNegativeEvidence_TimesOut(string text)
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, text);
        await Assert.ThrowsAsync<TimeoutException>(() => Wait(files, seconds: 1.4));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_IsHonoredWithoutModifyingFiles(bool preCancelled)
    {
        using var files = new SyntheticLunarFiles();
        using var cancellation = new CancellationTokenSource();
        if (preCancelled) cancellation.Cancel();
        var inventory = files.Files();
        var waiting = Wait(files, token: cancellation.Token);
        if (!preCancelled) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(inventory, files.Files());
        Assert.Equal("", File.ReadAllText(files.Log));
    }

    [Fact]
    public async Task ExpiredTimeoutAndPreCancelledExpiredTimeout_AreDistinct()
    {
        using var files = new SyntheticLunarFiles();
        await Assert.ThrowsAsync<TimeoutException>(() => Wait(files, seconds: 0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(files, seconds: 0, token: new CancellationToken(true)));
    }

    [Fact]
    public async Task Readiness_DoesNotOpenOrCreateSiblingPrivateFiles()
    {
        using var files = new SyntheticLunarFiles();
        var locks = new List<FileStream>();
        try
        {
            foreach (var name in new[] { "accounts.json", "session.json", "tokens.json" })
                locks.Add(new FileStream(Path.Combine(Path.GetDirectoryName(files.Log)!, name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
            var inventory = files.Files();
            File.WriteAllText(files.Log, Selected + "\n");
            Assert.Equal(("lunar", "1.8.9"), await Wait(files));
            Assert.Equal(inventory, files.Files());
            Assert.All(locks, stream => Assert.Equal(0, stream.Length));
        }
        finally { foreach (var stream in locks) stream.Dispose(); }
    }

    [Fact]
    public async Task MissingLog_CanAppearLaterAndNegativeCheckpointStartsAtZero()
    {
        using var files = new SyntheticLunarFiles();
        File.Delete(files.Log);
        Assert.Equal(0, LunarProfileReadinessService.CaptureCheckpoint(files.Log));
        var waiting = Wait(files, -10);
        File.WriteAllText(files.Log, Selected + "\n");
        Assert.Equal(("lunar", "1.8.9"), await waiting);
    }

    [Fact]
    public async Task ExactIdEvidence_WithReadyAvoidsGraceDelay()
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, Metadata + "[FutureProfiles] Prepared profile synthetic-profile-a\n" + Ready);
        Assert.Equal(("lunar", "1.8.9"), await Wait(files, seconds: 0.7));
    }

    [Theory]
    [InlineData("synthetic-profile-a-extra")]
    [InlineData("prefix-synthetic-profile-a")]
    [InlineData("SYNTHETIC-PROFILE-A")]
    public async Task SimilarButNotExactId_DoesNotBypassGraceDelay(string id)
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, Metadata + $"[FutureProfiles] Prepared profile {id}\n" + Ready);
        await Assert.ThrowsAsync<TimeoutException>(() => Wait(files, seconds: 0.65));
    }

    [Theory]
    [InlineData("[Window] Renderer ready for window 'main'\n")]
    [InlineData("[Launcher] Ready signal received\n")]
    public async Task ReadyOnlyCannotDispatchWithoutMetadataOrClaimPackageCompatibility(string ready)
    {
        using var files = new SyntheticLunarFiles();
        File.WriteAllText(files.Log, ready);
        var waiting = Wait(files);
        Assert.False(waiting.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(() => waiting);
        var report = new SanitizedLaunchReport(Path.Combine(files.Root, "reports"));
        report.Set("launchStage", "launcher-ready");
        report.Save();
        using var json = JsonDocument.Parse(File.ReadAllText(report.Path));
        Assert.Equal("untested", json.RootElement.GetProperty("compatibilityStatus").GetString());
        Assert.Equal("not-observed", json.RootElement.GetProperty("usableWindowOutcome").GetString());
    }
}
