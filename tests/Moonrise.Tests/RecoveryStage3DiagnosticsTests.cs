using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class RecoveryStage3DiagnosticsTests
{
    [Fact]
    public void AppendedLauncherLogDoesNotAttributePreviousCrashToCurrentTimeout()
    {
        var started = DateTimeOffset.Parse("2026-10-05T12:33:40+06:00");
        var failed = DateTimeOffset.Parse("2026-10-05T12:37:10+06:00");
        const string log = "[2026-10-05T12:32:37+06:00] Upload (ID: LCLU-CBWNBIHNVYQX)\n" +
                           "[2026-10-05T12:33:54+06:00] Attempted to launch without metadata, skipping...\n";
        Assert.Null(PackageCrashDiagnosticsService.FindCrashIdentifier(log, started, failed));
        Assert.Equal("LCLU-NEW123", PackageCrashDiagnosticsService.FindCrashIdentifier(
            log + "[2026-10-05T12:36:00+06:00] Upload (ID: LCLU-NEW123)\n", started, failed));
    }

    [Fact]
    public void SameNameProfiles_SelectExactIdAndRestoreSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "profiles.db");
            var settings = Path.Combine(root, "launcher.json");
            File.WriteAllText(settings, "{\"settings\":{\"gameProfile\":\"original\"}}");
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE profiles(id TEXT,name TEXT,type TEXT,major_game_version TEXT,game_version TEXT,loaders TEXT,lunar_module TEXT);" +
                    "INSERT INTO profiles VALUES ('a','Minecraft 1.8','lunar','1.8','1.8.9','[\"ichor\"]','lunar');" +
                    "INSERT INTO profiles VALUES ('b','Minecraft 1.8','lunar','1.8','1.8.9','[\"forge\"]','forge');";
                command.ExecuteNonQuery();
            }
            var service = new LauncherProfileService(path, settings);
            Assert.Equal(2, service.GetProfiles().Select(profile => profile.DetailLabel).Distinct().Count());
            using (var selected = service.BeginExactProfileSelection("lunar", "1.8.9", "b"))
            {
                Assert.Equal("b", selected.Profile.Id);
                Assert.Contains("forge", selected.Profile.DetailLabel);
            }
            using var json = JsonDocument.Parse(File.ReadAllText(settings));
            Assert.Equal("original", json.RootElement.GetProperty("settings").GetProperty("gameProfile").GetString());
            Assert.Throws<InvalidOperationException>(() => service.BeginExactProfileSelection("lunar", "1.8.9", "missing"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("java-exited-before-usable-window")]
    [InlineData("minecraft-timeout")]
    public void CrashBundle_CapturesActualCrashAndHsErrPathsAndRedactsTheirText(string failureStage)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var started = DateTimeOffset.UtcNow.AddSeconds(-5);
            var logs = Path.Combine(root, "runtime");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "crash-test.txt"), "mixin failed accessToken=hidden-crash-token");
            File.WriteAllText(Path.Combine(logs, "hs_err_pid123.log"), "fatal JVM error refreshToken=hidden-jvm-token");
            File.WriteAllText(Path.Combine(logs, "accounts.json"), "never-capture-this");
            var launch = new SanitizedLaunchReport(Path.Combine(root, "logs"));
            launch.Save();
            var result = new PackageCrashDiagnosticsService().Capture(new PackageCrashCaptureRequest(
                started, DateTimeOffset.UtcNow, failureStage, 1,
                [], null, null, launch.Path, "success", [], [logs]), Path.Combine(root, "crashes"));
            var paths = File.ReadAllText(Path.Combine(result.DirectoryPath, "relevant-log-paths.txt"));
            Assert.Contains("crash-test.txt", paths);
            Assert.Contains("hs_err_pid123.log", paths);
            var text = string.Join("\n", Directory.EnumerateFiles(result.DirectoryPath).Select(File.ReadAllText));
            Assert.Contains("mixin failed", text);
            Assert.Contains("fatal JVM error", text);
            Assert.DoesNotContain("hidden-crash-token", text);
            Assert.DoesNotContain("hidden-jvm-token", text);
            Assert.DoesNotContain("never-capture-this", text);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileDetails_ReadObservedColumnsWithoutRequiringNewSchema(bool modern)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "profiles.db");
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE profiles(id TEXT,name TEXT,type TEXT,major_game_version TEXT,game_version TEXT" +
                    (modern ? ",loaders TEXT,loader_version TEXT,lunar_module TEXT" : "") + ");";
                command.ExecuteNonQuery();
                command.CommandText = "INSERT INTO profiles VALUES ('id','Minecraft 1.8','lunar','1.8','1.8.9'" +
                    (modern ? ",'[\"ichor\"]',NULL,'lunar'" : "") + ");";
                command.ExecuteNonQuery();
            }
            var before = File.ReadAllBytes(path);
            var profile = Assert.Single(new LauncherProfileService(path).GetProfiles());
            Assert.Contains("id", profile.DetailLabel);
            Assert.Equal(modern ? new[] { "ichor" } : [], profile.Loaders);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("[null,42]")]
    public void UnknownLoaderEncoding_DoesNotInventAProfileType(string value) =>
        Assert.Empty(LauncherProfileService.ParseLoaders(value));

    [Fact]
    public void LaunchReport_PreservesStageEvidenceAndNeverClaimsPackageCompatibility()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var report = new SanitizedLaunchReport(root);
            report.Set("launchStage", "waiting-for-java");
            report.Set("sanitizedException", "accessToken=secret-value");
            report.Set("launchStage", "java-exited-before-usable-window");
            report.Set("javaExitCode", 1);
            report.Set("cleanupResult", "success");
            report.Save();
            var text = File.ReadAllText(report.Path);
            Assert.DoesNotContain("secret-value", text);
            using var json = JsonDocument.Parse(text);
            Assert.Equal("untested", json.RootElement.GetProperty("compatibilityStatus").GetString());
            Assert.Equal("not-observed", json.RootElement.GetProperty("usableWindowOutcome").GetString());
            var stages = json.RootElement.GetProperty("stageTimeline").EnumerateArray().ToArray();
            Assert.Equal(3, stages.Length);
            Assert.True(stages[2].GetProperty("ElapsedMilliseconds").GetInt64() >= stages[1].GetProperty("ElapsedMilliseconds").GetInt64());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
