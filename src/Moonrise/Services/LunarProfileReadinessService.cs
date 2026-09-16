using System.Text;
using System.Text.RegularExpressions;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed partial class LunarProfileReadinessService
{
    private static readonly TimeSpan LauncherReadyFallbackDelay = TimeSpan.FromSeconds(1);

    public static string GetDefaultLauncherLogPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lunarclient", "logs", "launcher", "main.log");

    public static long CaptureCheckpoint(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (IOException) { return 0; }
    }

    public async Task<(string Client, string Version)> WaitForSelectedProfileAsync(
        string logPath,
        long checkpoint,
        LauncherProfile expectedProfile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var offset = Math.Max(0, checkpoint);
        var pending = new StringBuilder();
        var expectedProfileObserved = false;
        DateTimeOffset? launcherReadyObservedUtc = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(logPath))
                {
                    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length < offset) { offset = 0; pending.Clear(); }
                    stream.Position = offset;
                    using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
                    pending.Append(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
                    offset = stream.Position;
                    var text = pending.ToString();
                    var lastLine = text.LastIndexOf('\n');
                    if (lastLine >= 0)
                    {
                        var complete = text[..(lastLine + 1)];
                        pending.Clear(); pending.Append(text[(lastLine + 1)..]);
                        if (TryParseSelectedProfile(complete, out var selected))
                            return selected;

                        foreach (var line in complete.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (IsExpectedProfileEvidence(line, expectedProfile.Id))
                                expectedProfileObserved = true;
                            if (IsLauncherReadyEvidence(line))
                                launcherReadyObservedUtc ??= DateTimeOffset.UtcNow;
                        }

                        if (expectedProfileObserved && launcherReadyObservedUtc is not null)
                            return Expected(expectedProfile);
                    }
                }
            }
            catch (IOException) { }

            // Moonrise writes the exact existing profile ID into Lunar's own
            // settings before launch. If a future launcher update changes only
            // the profile log sentence, a ready official launcher is enough to
            // continue after a short grace period instead of timing out for a
            // full minute. Authentication or launch failures remain observable
            // in the subsequent Java/Minecraft monitor.
            if (launcherReadyObservedUtc is { } readyUtc &&
                DateTimeOffset.UtcNow - readyUtc >= LauncherReadyFallbackDelay)
            {
                return Expected(expectedProfile);
            }
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Lunar Launcher did not become ready for the selected profile within {Math.Ceiling(timeout.TotalSeconds)} seconds.");
    }

    internal static bool TryParseSelectedProfile(
        string text,
        out (string Client, string Version) selected)
    {
        var matches = SelectedProfileRegex().Matches(text);
        if (matches.Count > 0)
        {
            var match = matches[^1];
            selected = (
                match.Groups["type"].Value.ToLowerInvariant(),
                match.Groups["version"].Value);
            return true;
        }
        selected = default;
        return false;
    }

    private static bool IsExpectedProfileEvidence(string line, string expectedProfileId) =>
        line.Contains(expectedProfileId, StringComparison.OrdinalIgnoreCase) &&
        line.Contains("profile", StringComparison.OrdinalIgnoreCase);

    private static bool IsLauncherReadyEvidence(string line) =>
        line.Contains("ready", StringComparison.OrdinalIgnoreCase) &&
        (line.Contains("[Window]", StringComparison.OrdinalIgnoreCase) ||
         line.Contains("Ready signal", StringComparison.OrdinalIgnoreCase));

    private static (string Client, string Version) Expected(LauncherProfile profile) =>
        (profile.Client.ToLowerInvariant(), profile.GameVersion);

    // Match the stable structured fields instead of one English sentence.
    // Known Lunar forms include "Profile found in settings, using ..." and
    // 3.7.16's "Profile selected, using Minecraft ...".
    [GeneratedRegex(@"\[Metadata\][^\r\n]*\bProfile\b[^\r\n]*?\btype\s*[:=]\s*(?<type>[A-Za-z0-9._-]+)\s*[,;]\s*version\s*[:=]\s*(?<version>[A-Za-z0-9._-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectedProfileRegex();
}
