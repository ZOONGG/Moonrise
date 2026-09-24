using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class Stage1RecoveryTests
{
    [Fact]
    public void BridgeContract_ManagedAndNativeUseOnlyExactEnvironmentPath()
    {
        using var temp = new TemporaryDirectory();
        var config = Path.Combine(temp.Path, "launch-1", "bridge-config.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, "MNR3\n");

        var environment = NativeBridgeLauncher.BuildEnvironmentBlock(config);
        var nativeSource = ReadNativeSource();

        Assert.Contains(
            $"{NativeBridgeLauncher.BridgeConfigEnvironmentVariable}={Path.GetFullPath(config)}\0",
            environment,
            StringComparison.Ordinal);
        Assert.Contains("GetEnvironmentVariableW(", nativeSource, StringComparison.Ordinal);
        Assert.Contains("g_bridge_config_name", nativeSource, StringComparison.Ordinal);
        Assert.Contains("bridge-config-env-missing-or-empty", nativeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("wcscat(path, L\"bridge-config.txt\")", nativeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MNR1", nativeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MNR2", nativeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeContract_MissingConfigFailsClearlyAndAtomicWriteLeavesNoTemporaryFile()
    {
        using var temp = new TemporaryDirectory();
        var missing = Path.Combine(temp.Path, "launch-missing", "bridge-config.txt");
        var exception = Assert.Throws<FileNotFoundException>(() =>
            NativeBridgeLauncher.BuildEnvironmentBlock(missing));
        Assert.Contains("configuration was not found", exception.Message, StringComparison.OrdinalIgnoreCase);

        var launch = Path.Combine(temp.Path, "launch-valid");
        var config = Path.Combine(launch, "bridge-config.txt");
        BridgeConfigurationFile.WriteAtomic(
            config,
            @"C:\Moonrise\weave-loader.jar",
            @"C:\Moonrise\enabled-mods",
            [@"C:\Moonrise\Moonrise-Cosmetics.jar"]);

        Assert.Equal(
            "MNR3\nC:\\Moonrise\\weave-loader.jar\nC:\\Moonrise\\enabled-mods\nC:\\Moonrise\\Moonrise-Cosmetics.jar\n",
            File.ReadAllText(config));
        Assert.Empty(Directory.EnumerateFiles(launch, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void AppPaths_DevelopmentModeStaysUnderLocalAppDataMoonriseRoot()
    {
        using var temp = new TemporaryDirectory();
        var repository = Path.Combine(temp.Path, "repository");
        var output = Path.Combine(repository, "src", "Moonrise", "bin");
        var localAppData = Path.Combine(temp.Path, "local");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(repository, "Moonrise.sln"), string.Empty);

        var paths = AppPaths.Discover(output, localAppData, searchForDevelopmentRoot: true);
        paths.EnsureUserDirectories();

        Assert.Equal(AppMode.Development, paths.Mode);
        Assert.Equal(Path.Combine(localAppData, "Moonrise", "dev"), paths.RootDirectory);
        Assert.StartsWith(
            Path.Combine(localAppData, "Moonrise") + Path.DirectorySeparatorChar,
            paths.RootDirectory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(repository, "packages")));
        Assert.All(
            new[]
            {
                paths.PackagesDirectory,
                paths.AdaptersDirectory,
                paths.CacheDirectory,
                paths.SettingsDirectory,
                paths.LogsDirectory,
                paths.TempDirectory
            },
            directory => Assert.StartsWith(paths.RootDirectory, directory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "PrivateAssets")]
    public void RecoveryAssets_ImportAndLaunchCopiesRemainByteForByteUnchanged()
    {
        Assert.Equal(Stage1RecoveryPackageService.Veyra.Sha256, Hash(Stage1RecoveryPackageService.Veyra.SourcePath));
        Assert.Equal(Stage1RecoveryPackageService.Cosmetics.Sha256, Hash(Stage1RecoveryPackageService.Cosmetics.SourcePath));

        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        var parser = new JarMetadataParser();
        var packages = new Stage1RecoveryPackageService(parser).EnsureImported(paths);
        using var session = new LaunchSessionService().Create(paths.TempDirectory);
        var enabled = new EnabledModDirectoryService(parser).Create(
            session.DirectoryPath,
            [packages.Veyra]);
        Stage1RecoveryPackageService.VerifyUnchanged(packages);

        Assert.Equal(packages.Veyra.Sha256, Hash(Path.Combine(enabled, packages.Veyra.FileName)));
        Assert.Equal(packages.CosmeticsDefinition.Sha256, Hash(packages.Cosmetics.FullPath));
        Assert.Equal(696643, new FileInfo(packages.VeyraDefinition.SourcePath).Length);
        Assert.Equal(17991, new FileInfo(packages.CosmeticsDefinition.SourcePath).Length);
        Assert.Equal("dev.veyra.weave.Main", packages.Veyra.Entrypoint);
        Assert.Equal("moonrise.cosmetics.CosmeticsAgent", packages.Cosmetics.Entrypoint);
    }

    [Fact]
    public void RecoverySelection_IncludesOnlyEnabledRecoveryPackages()
    {
        var veyraDefinition = new RecoveryPackageDefinition(
            "veyra",
            @"C:\fixtures\Veyra.jar",
            "Veyra.jar",
            new string('B', 64),
            PackageKind.WeaveMod);
        var cosmeticsDefinition = new RecoveryPackageDefinition(
            "moonrise-cosmetics",
            @"C:\fixtures\Moonrise-Cosmetics.jar",
            "Moonrise-Cosmetics.jar",
            new string('C', 64),
            PackageKind.JavaAgent);
        var veyra = Package(veyraDefinition);
        var cosmetics = Package(cosmeticsDefinition);
        var unrelated = new PackageInfo
        {
            FileName = "BWH.jar",
            DisplayName = "BWH",
            Identifier = "bwh",
            FullPath = @"C:\unrelated\BWH.jar",
            Kind = PackageKind.WeaveMod,
            Sha256 = new string('A', 64),
            IsEnabled = true
        };
        var set = new RecoveryPackageSet(
            veyraDefinition,
            cosmeticsDefinition,
            veyra,
            cosmetics);

        var selected = Stage1RecoveryPackageService.SelectEnabled(set, [unrelated, veyra], [cosmetics]);

        Assert.Same(veyra, selected.Veyra);
        Assert.Same(cosmetics, selected.Cosmetics);
        cosmetics.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() =>
            Stage1RecoveryPackageService.SelectEnabled(set, [unrelated, veyra], [cosmetics]));
    }

    [Fact]
    public void JavaAgentArgument_UsesCorrectWindowsQuoting()
    {
        var options = JavaToolOptionsBuilder.Build(
            null,
            @"C:\Moonrise Cache\weave-loader.jar",
            @"C:\Moonrise Cache\enabled-mods",
            [@"C:\Moonrise Cache\Moonrise-Cosmetics.jar"]);

        Assert.Contains(
            "-javaagent:\"C:\\Moonrise Cache\\Moonrise-Cosmetics.jar\"",
            options,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherProfileSelection_PreservesUnrelatedFieldsAndRestoresOriginalValue()
    {
        using var temp = new TemporaryDirectory();
        var database = Path.Combine(temp.Path, "profiles.db");
        var launcherJson = Path.Combine(temp.Path, "launcher.json");
        CreateProfilesDatabase(database);
        File.WriteAllText(
            launcherJson,
            """{"settings":{"gameProfile":"old-profile","theme":"dark"},"unrelated":{"nested":[1,2,3]},"credentialHint":"unchanged"}""");
        var service = new LauncherProfileService(database, launcherJson);

        using (var selection = service.BeginExactProfileSelection("lunar", "1.8.9"))
        {
            using var selected = JsonDocument.Parse(File.ReadAllText(launcherJson));
            Assert.Equal("profile-189", selected.RootElement.GetProperty("settings").GetProperty("gameProfile").GetString());
            Assert.Equal("dark", selected.RootElement.GetProperty("settings").GetProperty("theme").GetString());
            Assert.Equal(3, selected.RootElement.GetProperty("unrelated").GetProperty("nested").GetArrayLength());
            Assert.Equal("unchanged", selected.RootElement.GetProperty("credentialHint").GetString());
        }

        using var restored = JsonDocument.Parse(File.ReadAllText(launcherJson));
        Assert.Equal("old-profile", restored.RootElement.GetProperty("settings").GetProperty("gameProfile").GetString());
        Assert.Equal("dark", restored.RootElement.GetProperty("settings").GetProperty("theme").GetString());
        Assert.Equal("unchanged", restored.RootElement.GetProperty("credentialHint").GetString());
    }

    [Fact]
    public void LaunchSessions_CleanAfterSuccessFailureAndStartupRecovery()
    {
        using var temp = new TemporaryDirectory();
        var service = new LaunchSessionService();

        var successful = service.Create(temp.Path);
        var successfulPath = successful.DirectoryPath;
        successful.Dispose();
        Assert.False(Directory.Exists(successfulPath));

        var failed = service.Create(temp.Path);
        var failedPath = failed.DirectoryPath;
        try
        {
            File.WriteAllText(Path.Combine(failedPath, "diagnostic.txt"), "safe");
            throw new InvalidOperationException("simulated preparation failure");
        }
        catch (InvalidOperationException)
        {
            failed.Dispose();
        }
        Assert.False(Directory.Exists(failedPath));

        var staleOne = service.Create(temp.Path);
        var staleTwo = service.Create(temp.Path);
        var staleOnePath = staleOne.DirectoryPath;
        var staleTwoPath = staleTwo.DirectoryPath;
        Assert.Equal(2, service.CleanupStale(temp.Path));
        Assert.False(Directory.Exists(staleOnePath));
        Assert.False(Directory.Exists(staleTwoPath));
    }

    [Fact]
    public void LaunchReport_RedactsTokenLikeValues()
    {
        using var temp = new TemporaryDirectory();
        var report = new SanitizedLaunchReport(temp.Path);
        report.Set("sanitizedException", "accessToken=secret-a password: secret-b Authorization: Bearer secret-c");
        report.Save();
        var json = File.ReadAllText(report.Path);

        Assert.DoesNotContain("secret-a", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-b", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-c", json, StringComparison.Ordinal);
        Assert.Contains("<redacted>", json, StringComparison.Ordinal);
    }

    private static PackageInfo Package(RecoveryPackageDefinition definition) => new()
    {
        FileName = definition.FileName,
        DisplayName = definition.Id,
        Identifier = definition.Id,
        FullPath = definition.SourcePath,
        Kind = definition.Kind,
        Sha256 = definition.Sha256,
        IsEnabled = true
    };

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ReadNativeSource([CallerFilePath] string sourceFile = "")
    {
        var testDirectory = Path.GetDirectoryName(sourceFile)!;
        var repository = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return File.ReadAllText(Path.Combine(repository, "native", "Moonrise.Native", "bridge.c"));
    }

    private static void CreateProfilesDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE profiles (
                id TEXT NOT NULL,
                name TEXT NOT NULL,
                type TEXT NOT NULL,
                major_game_version TEXT NOT NULL,
                game_version TEXT NOT NULL
            );
            INSERT INTO profiles VALUES ('profile-189', 'Lunar 1.8.9', 'lunar', '1.8', '1.8.9');
            """;
        command.ExecuteNonQuery();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"moonrise-stage1-tests-{Guid.NewGuid():N}");
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
