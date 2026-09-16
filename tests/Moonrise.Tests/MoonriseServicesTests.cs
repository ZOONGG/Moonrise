using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class MoonriseServicesTests
{
    [Fact]
    public void Settings_RoundTripClientVersionLanguageAndDisabledPackages()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "moonrise-settings.json");
        var service = new AppSettingsService(path);
        service.Save(new MoonriseSettings
        {
            Client = "lunar",
            MinecraftVersion = "1.8.9",
            Language = "en",
            LauncherExecutablePath = @"C:\Apps\Lunar Client.exe",
            DisabledMods = ["one.jar"],
            DisabledAgents = ["agent.jar"]
        });

        var settings = service.Load();
        Assert.Equal("lunar", settings.Client);
        Assert.Equal("1.8.9", settings.MinecraftVersion);
        Assert.Equal("en", settings.Language);
        Assert.Equal(["one.jar"], settings.DisabledMods);
        Assert.Equal(["agent.jar"], settings.DisabledAgents);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Settings_MigratesExistingInstallToBackgroundLunarLaunchOnce()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "moonrise-settings.json");
        File.WriteAllText(path, """{"BackgroundLunarLaunch":false,"Language":"ru"}""");
        var service = new AppSettingsService(path);

        var migrated = service.Load();

        Assert.True(migrated.BackgroundLunarLaunch);
        Assert.Equal(MoonriseSettings.CurrentSettingsSchemaVersion, migrated.SettingsSchemaVersion);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(document.RootElement.GetProperty("BackgroundLunarLaunch").GetBoolean());
    }

    [Fact]
    public void Settings_RespectsExplicitBackgroundChoiceAfterMigration()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "moonrise-settings.json");
        File.WriteAllText(
            path,
            $$"""{"SettingsSchemaVersion":{{MoonriseSettings.CurrentSettingsSchemaVersion}},"BackgroundLunarLaunch":false}""");

        var settings = new AppSettingsService(path).Load();

        Assert.False(settings.BackgroundLunarLaunch);
    }

    [Fact]
    public void Settings_RestoresMoonlightDesignOnceAndPreservesLaterThemeChoices()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "moonrise-settings.json");
        File.WriteAllText(path, """{"SettingsSchemaVersion":2,"Theme":"obsidian"}""");
        var service = new AppSettingsService(path);

        var migrated = service.Load();

        Assert.Equal("moonlight", migrated.Theme);
        Assert.Equal(MoonriseSettings.CurrentSettingsSchemaVersion, migrated.SettingsSchemaVersion);

        migrated.Theme = "aurora";
        service.Save(migrated);

        Assert.Equal("aurora", service.Load().Theme);
    }

    [Fact]
    public void JarParser_ReadsGenericWeaveMetadata()
    {
        using var temp = new TemporaryDirectory();
        var jar = CreateJar(temp.Path, "sample.jar", new Dictionary<string, string>
        {
            ["weave.mod.json"] = """{"name":"Sample","modId":"sample","version":"2.1","entrypoints":["sample.Main"]}"""
        });
        var package = new JarMetadataParser().ParseWeaveMod(jar);
        Assert.Equal(PackageKind.WeaveMod, package.Kind);
        Assert.Equal("Sample", package.DisplayName);
        Assert.Equal("sample", package.Identifier);
        Assert.Equal("sample.Main", package.Entrypoint);
    }

    [Fact]
    public void JarParser_ReadsPremainJavaAgent()
    {
        using var temp = new TemporaryDirectory();
        var jar = CreateJar(temp.Path, "agent.jar", new Dictionary<string, string>
        {
            ["META-INF/MANIFEST.MF"] = "Manifest-Version: 1.0\r\nPremain-Class: example.Agent\r\nImplementation-Title: Example Agent\r\nImplementation-Version: 3.0\r\n"
        });
        var package = new JarMetadataParser().ParseJavaAgent(jar);
        Assert.Equal(PackageKind.JavaAgent, package.Kind);
        Assert.Equal("Example Agent", package.DisplayName);
        Assert.Equal("example.Agent", package.Entrypoint);
    }

    [Fact]
    public void JarParser_UsesMainManifestVersionInsteadOfNamedSectionVersion()
    {
        using var temp = new TemporaryDirectory();
        var jar = CreateJar(temp.Path, "agent-sections.jar", new Dictionary<string, string>
        {
            ["META-INF/MANIFEST.MF"] =
                "Manifest-Version: 1.0\r\nPremain-Class: example.Agent\r\nImplementation-Version: 1.3.4\r\n\r\n" +
                "Name: shaded/asm/\r\nImplementation-Version: 9.7\r\n"
        });

        Assert.Equal("1.3.4", new JarMetadataParser().ParseJavaAgent(jar).Version);
    }

    [Fact]
    public void EnabledModDirectory_DoesNotPatchLegacyMetadataInLaunchCopy()
    {
        using var temp = new TemporaryDirectory();
        var original = CreateJar(temp.Path, "Legacy Mod.jar", new Dictionary<string, string>
        {
            ["weave.mod.json"] =
                """{"entrypoints":["example.Legacy"],"mixinConfigs":["legacy.mixins.json"]}"""
        });
        var originalBytes = File.ReadAllBytes(original);
        var parser = new JarMetadataParser();
        var package = parser.ParseWeaveMod(original);

        var session = new EnabledModDirectoryService(parser).Create(
            Path.Combine(temp.Path, "sessions"), [package]);
        var sessionJar = Path.Combine(session, package.FileName);

        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        Assert.Equal(originalBytes, File.ReadAllBytes(sessionJar));
    }

    [Fact]
    public void EnabledModDirectory_PreservesCompatibleJarBytes()
    {
        using var temp = new TemporaryDirectory();
        var original = CreateJar(temp.Path, "compatible.jar", new Dictionary<string, string>
        {
            ["weave.mod.json"] =
                """{"name":"Compatible","modId":"compatible","namespace":"mcp-named","entryPoints":["example.Main"]}""",
            ["example/Main.class"] = "already-compatible-bytecode"
        });
        var originalBytes = File.ReadAllBytes(original);
        var parser = new JarMetadataParser();
        var package = parser.ParseWeaveMod(original);

        var session = new EnabledModDirectoryService(parser).Create(
            Path.Combine(temp.Path, "sessions"), [package]);

        Assert.Equal(originalBytes, File.ReadAllBytes(Path.Combine(session, package.FileName)));
    }

    [Fact]
    public void JavaOptions_IncludeEveryEnabledAgentWithoutLosingExistingOptions()
    {
        var result = JavaToolOptionsBuilder.Build(
            "-Xmx2G", @"C:\Moonrise\weave.jar", @"C:\Moonrise\mods",
            [@"C:\Moonrise\agents\one.jar", @"C:\Moonrise\agents\two.jar"]);
        Assert.StartsWith("-Xmx2G ", result, StringComparison.Ordinal);
        Assert.Contains("-javaagent:\"C:\\Moonrise\\weave.jar\"", result, StringComparison.Ordinal);
        Assert.Contains("-javaagent:\"C:\\Moonrise\\agents\\one.jar\"", result, StringComparison.Ordinal);
        Assert.Contains("-javaagent:\"C:\\Moonrise\\agents\\two.jar\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("proof", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeBridgeConfiguration_CarriesOnlyRequiredLaunchPaths()
    {
        var result = NativeBridgeConfigurationBuilder.Build(
            @"C:\Moonrise\weave.jar",
            @"C:\Moonrise\mods",
            [@"C:\Moonrise\agents\one.jar", @"C:\Moonrise\agents\two.jar"]);

        Assert.Equal(
            "MNR3\n" +
            "C:\\Moonrise\\weave.jar\n" +
            "C:\\Moonrise\\mods\n" +
            "C:\\Moonrise\\agents\\one.jar\n" +
            "C:\\Moonrise\\agents\\two.jar\n",
            result);
    }

    [Fact]
    public void PackageCatalog_ImportsAndRemovesOnlyFromUserFolder()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateJar(temp.Path, "source.jar", new Dictionary<string, string>
        {
            ["weave.mod.json"] = """{"name":"Imported","modId":"imported","entrypoints":[]}"""
        });
        var userFolder = Path.Combine(temp.Path, "user-mods");
        var parser = new JarMetadataParser();
        var catalog = new PackageCatalogService(parser);
        var imported = catalog.Import(source, userFolder, PackageKind.WeaveMod);
        Assert.True(File.Exists(imported));
        var package = parser.ParseWeaveMod(imported);
        catalog.Remove(package, userFolder);
        Assert.False(File.Exists(imported));
    }

    [Fact]
    public void AppPaths_CreateOnlyAuthoritativePackageDirectories()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureUserDirectories();
        Assert.Equal(Path.Combine(temp.Path, "packages", "weave"), paths.WeavePackagesDirectory);
        Assert.Equal(Path.Combine(temp.Path, "packages", "agents"), paths.AgentPackagesDirectory);
        Assert.Equal(Path.Combine(temp.Path, "packages", "unclassified"), paths.UnclassifiedPackagesDirectory);
        Assert.Equal(Path.Combine(temp.Path, "packages", "metadata"), paths.PackageMetadataDirectory);
        Assert.Equal(Path.Combine(temp.Path, "adapters"), paths.AdaptersDirectory);
        Assert.Equal(Path.Combine(temp.Path, "cache", "catalog"), paths.CatalogCacheDirectory);
        Assert.Equal(Path.Combine(temp.Path, "temp"), paths.SessionRoot);
        Assert.Equal(Path.Combine(temp.Path, "settings", "moonrise-settings.json"), paths.SettingsPath);
        Assert.False(Directory.Exists(paths.UserModsDirectory));
        Assert.False(Directory.Exists(paths.UserAgentsDirectory));
        Assert.False(Directory.Exists(paths.MoonriseOwnedPackagesDirectory));
        Assert.Equal(
            ["agents", "metadata", "unclassified", "weave"],
            Directory.EnumerateDirectories(paths.PackagesDirectory)
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    [Fact]
    public void AppPaths_DiscoverAlwaysUsesPerUserMoonriseRoot()
    {
        using var temp = new TemporaryDirectory();
        var application = Path.Combine(temp.Path, "application");
        var localAppData = Path.Combine(temp.Path, "local");
        Directory.CreateDirectory(application);

        var installed = AppPaths.Discover(application, localAppData);

        Assert.Equal(AppMode.Installed, installed.Mode);
        Assert.Equal(Path.Combine(localAppData, "Moonrise"), installed.RootDirectory);
        Assert.Equal(application, installed.InstallationDirectory);

        File.WriteAllText(Path.Combine(application, AppPaths.PortableMarkerFileName), string.Empty);
        var portable = AppPaths.Discover(application, localAppData);

        Assert.Equal(AppMode.Installed, portable.Mode);
        Assert.Equal(Path.Combine(localAppData, "Moonrise"), portable.RootDirectory);
        Assert.False(portable.IsPortable);
    }

    [Fact]
    public void LegacyMigration_CopiesOnlyMoonriseOwnedDataAndPreservesConflicts()
    {
        using var temp = new TemporaryDirectory();
        var installation = Path.Combine(temp.Path, "app");
        var data = Path.Combine(temp.Path, "data");
        Directory.CreateDirectory(Path.Combine(installation, "user-mods"));
        Directory.CreateDirectory(Path.Combine(installation, "accounts"));
        File.WriteAllText(Path.Combine(installation, "user-mods", "legacy.jar"), "legacy");
        File.WriteAllText(Path.Combine(installation, "moonrise-settings.json"), """{"language":"en"}""");
        File.WriteAllText(Path.Combine(installation, "accounts", "account.json"), "must-not-copy");
        var paths = new AppPaths(data, installation, AppMode.Installed);
        Directory.CreateDirectory(paths.UserModsDirectory);
        File.WriteAllText(Path.Combine(paths.UserModsDirectory, "legacy.jar"), "newer");

        var result = new LegacyDataMigrationService().Migrate(paths);

        Assert.Equal("newer", File.ReadAllText(Path.Combine(paths.UserModsDirectory, "legacy.jar")));
        Assert.True(File.Exists(paths.SettingsPath));
        Assert.False(Directory.Exists(Path.Combine(data, "accounts")));
        Assert.Equal(1, result.CopiedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.True(File.Exists(paths.MigrationMarkerPath));
        Assert.Equal(0, new LegacyDataMigrationService().Migrate(paths).CopiedFiles);
    }

    [Theory]
    [InlineData("moonrise://catalog", MoonriseDeepLinkAction.Catalog, null)]
    [InlineData("moonrise://package/example.package-1", MoonriseDeepLinkAction.Package, "example.package-1")]
    [InlineData("moonrise://install/example_package", MoonriseDeepLinkAction.Install, "example_package")]
    public void DeepLinks_AcceptSupportedSanitizedForms(
        string value,
        MoonriseDeepLinkAction action,
        string? packageId)
    {
        Assert.True(MoonriseDeepLink.TryParse(value, out var link, out _));
        Assert.Equal(action, link!.Action);
        Assert.Equal(packageId, link.PackageId);
    }

    [Theory]
    [InlineData("https://example.invalid/catalog")]
    [InlineData("moonrise://install/../../escape")]
    [InlineData("moonrise://install/example?approve=true")]
    [InlineData("moonrise://install/%2Fescape")]
    [InlineData("moonrise://unknown")]
    public void DeepLinks_RejectInvalidOrAmbiguousForms(string value)
    {
        Assert.False(MoonriseDeepLink.TryParse(value, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void LoggerRedaction_HidesTokens()
    {
        var value = TokenRedactor.Redact("Authorization: Bearer-secret access_token=abc123");
        Assert.DoesNotContain("Bearer-secret", value, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", value, StringComparison.Ordinal);
    }

    [Fact]
    public void ModpackProfile_ExportsOnlyPortableMetadataAndAppliesByHash()
    {
        using var temp = new TemporaryDirectory();
        var jar = CreateJar(temp.Path, "portable.jar", new Dictionary<string, string>
        {
            ["weave.mod.json"] =
                """{"name":"Portable","modId":"portable","version":"1.2","entryPoints":[],"minecraftVersions":["1.8.9"]}"""
        });
        var package = new JarMetadataParser().ParseWeaveMod(jar);
        var service = new ModpackProfileService();
        var profile = service.Create("Competitive", "lunar", "1.8.9", [package]);
        var exportPath = Path.Combine(temp.Path, "competitive.json");

        service.Export(profile, exportPath);
        var json = File.ReadAllText(exportPath);

        Assert.DoesNotContain(Convert.ToBase64String(File.ReadAllBytes(jar)), json, StringComparison.Ordinal);
        Assert.DoesNotContain(package.FullPath, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(package.Sha256, json, StringComparison.Ordinal);
        package.IsEnabled = false;
        Assert.Single(service.Apply(service.Load(exportPath), [package]));
        Assert.True(package.IsEnabled);
    }

    [Fact]
    public void CompatibilityAnalyzer_FindsVersionConflictDuplicateAndMissingRequirement()
    {
        var packages = new[]
        {
            Package("one.jar", "shared", ["1.20.1"], ["enemy"], ["library"]),
            Package("two.jar", "shared", ["1.8.9"]),
            Package("enemy.jar", "enemy", ["1.8.9"])
        };

        var report = new PackageCompatibilityAnalyzer().Analyze("1.8.9", packages);

        Assert.False(report.CanLaunch);
        Assert.Contains(report.Issues, issue => issue.Code == "duplicate-id");
        Assert.Contains(report.Issues, issue => issue.Code == "version-incompatible");
        Assert.Contains(report.Issues, issue => issue.Code == "declared-conflict");
        Assert.Contains(report.Issues, issue => issue.Code == "missing-requirement");
    }

    [Fact]
    public void LaunchPreflight_SafeModeKeepsSelectionButExcludesThirdPartyPackages()
    {
        var package = Package("selected.jar", "selected", ["1.8.9"]);
        var result = new LaunchPreflightService(new PackageCompatibilityAnalyzer())
            .Prepare("1.8.9", [package], [], safeMode: true);

        Assert.True(result.SafeMode);
        Assert.Empty(result.Mods);
        Assert.Empty(result.Agents);
        Assert.True(package.IsEnabled);
        Assert.True(result.Compatibility.CanLaunch);
    }

    [Fact]
    public void CrashAnalyzer_RedactsSecretsAndClassifiesManagedDiagnostics()
    {
        using var temp = new TemporaryDirectory();
        var result = new CrashAnalyzer().Analyze(
            42,
            1,
            ["Authorization: private-token", "java.lang.OutOfMemoryError"],
            temp.Path);

        Assert.Equal("memory", result.Category);
        Assert.True(File.Exists(result.ReportPath));
        var report = File.ReadAllText(result.ReportPath);
        Assert.DoesNotContain("private-token", report, StringComparison.Ordinal);
        Assert.Contains("OutOfMemoryError", report, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedCatalog_VerifiesSignatureAndRejectsTampering()
    {
        using var rsa = RSA.Create(2048);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new VerifiedCatalog
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Packages =
            [
                new VerifiedCatalogPackage
                {
                    Identifier = "portable",
                    Name = "Portable",
                    Version = "1.0",
                    DownloadUrl = "https://example.invalid/portable.jar",
                    Sha256 = new string('A', 64),
                    GameVersions = ["1.8.9"]
                }
            ]
        });
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var service = new SignedCatalogService();

        var result = service.Verify(payload, Convert.ToBase64String(signature), rsa.ExportSubjectPublicKeyInfoPem());

        Assert.Single(result.Catalog.Packages);
        Assert.Equal(64, result.SigningKeyFingerprint.Length);
        payload[^1] ^= 1;
        Assert.Throws<CryptographicException>(() =>
            service.Verify(payload, Convert.ToBase64String(signature), rsa.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void ClientPlugins_StayInsidePluginRootAndReceiveTokenFreeRequests()
    {
        using var temp = new TemporaryDirectory();
        var pluginFolder = Path.Combine(temp.Path, "plugins", "sample");
        Directory.CreateDirectory(pluginFolder);
        File.WriteAllBytes(Path.Combine(pluginFolder, "adapter.exe"), [0]);
        var manifestPath = Path.Combine(pluginFolder, "sample.moonrise-plugin.json");
        File.WriteAllText(manifestPath,
            """{"schemaVersion":1,"id":"sample-client","name":"Sample","executable":"adapter.exe","supportedGameVersions":["1.8.9"]}""");
        var service = new ClientPluginService();
        var plugin = service.Load(manifestPath, Path.Combine(temp.Path, "plugins"));

        var requestPath = service.WriteLaunchRequest(
            plugin, "1.8.9", [Package("one.jar", "one", ["1.8.9"])], [], false,
            Path.Combine(temp.Path, "requests"));
        var request = File.ReadAllText(requestPath);

        Assert.Contains("one.jar", request, StringComparison.Ordinal);
        Assert.DoesNotContain("token", request, StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(manifestPath,
            """{"schemaVersion":1,"id":"escape-client","name":"Escape","executable":"../../escape.exe","supportedGameVersions":["1.8.9"]}""");
        Assert.Throws<InvalidDataException>(() => service.Load(manifestPath, Path.Combine(temp.Path, "plugins")));
    }

    [Fact]
    public void AppearancePacks_LoadCustomLanguageAndExactlyThreeBuiltInThemes()
    {
        using var temp = new TemporaryDirectory();
        var languages = Path.Combine(temp.Path, "languages");
        var themes = Path.Combine(temp.Path, "themes");
        Directory.CreateDirectory(languages);
        Directory.CreateDirectory(themes);
        File.WriteAllText(Path.Combine(languages, "de.moonrise-language.json"),
            """{"schemaVersion":1,"code":"de","name":"Deutsch","translations":{"Launch":"Starten"}}""");
        var service = new AppearancePackService();

        var language = Assert.Single(service.LoadLanguages(languages), item => item.Code == "de");
        var themeIds = service.LoadThemes(themes).Select(item => item.Id).ToArray();

        Assert.Equal("Starten", service.Translate(language, "Launch"));
        Assert.Equal("Settings", service.Translate(language, "Settings"));
        Assert.Equal(["moonlight", "obsidian", "aurora"], themeIds);
        Assert.Equal("Deutsch", language.ToString());
    }

    [Fact]
    public async Task ApplicationUpdates_DiscoverNewVersionAndRequiredAssets()
    {
        const string response =
            """[{"tag_name":"v2.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/ZOONGG/Moonrise/releases/tag/v2.0.0","body":"Release notes","assets":[{"name":"Moonrise-Setup-2.0.0-x64.exe","browser_download_url":"https://github.com/ZOONGG/Moonrise/releases/download/v2.0.0/Moonrise-Setup-2.0.0-x64.exe"},{"name":"Moonrise-Setup-2.0.0-x64.exe.sha256","browser_download_url":"https://github.com/ZOONGG/Moonrise/releases/download/v2.0.0/Moonrise-Setup-2.0.0-x64.exe.sha256"}]}]""";
        using var client = new HttpClient(new ResponseHandler(response));

        var update = await new ApplicationUpdateService().CheckAsync(new Version(1, 0, 0), client);

        Assert.NotNull(update);
        Assert.Equal(new Version(2, 0, 0), update.Version);
        Assert.Equal("Moonrise-Setup-2.0.0-x64.exe", update.SetupName);
        Assert.Equal("Release notes", update.ReleaseNotes);
    }

    [Fact]
    public async Task ApplicationUpdates_ExcludePrereleasesUnlessEnabled()
    {
        const string response =
            """[{"tag_name":"v2.1.0-beta.1","draft":false,"prerelease":true,"html_url":"https://github.com/ZOONGG/Moonrise/releases/tag/v2.1.0-beta.1","body":"","assets":[{"name":"Moonrise-Setup-2.1.0-x64.exe","browser_download_url":"https://github.com/ZOONGG/Moonrise/releases/download/v2.1.0-beta.1/Moonrise-Setup-2.1.0-x64.exe"},{"name":"Moonrise-Setup-2.1.0-x64.exe.sha256","browser_download_url":"https://github.com/ZOONGG/Moonrise/releases/download/v2.1.0-beta.1/Moonrise-Setup-2.1.0-x64.exe.sha256"}]}]""";
        using var stableClient = new HttpClient(new ResponseHandler(response));
        using var prereleaseClient = new HttpClient(new ResponseHandler(response));
        var service = new ApplicationUpdateService();

        Assert.Null(await service.CheckAsync(new Version(1, 0, 0), stableClient));
        var update = await service.CheckAsync(new Version(1, 0, 0), prereleaseClient, includePrereleases: true);

        Assert.NotNull(update);
        Assert.True(update.IsPrerelease);
    }

    [Fact]
    public async Task ApplicationUpdates_RejectHashMismatch()
    {
        using var temp = new TemporaryDirectory();
        var setupName = "Moonrise-Setup-2.0.0-x64.exe";
        var release = new UpdateRelease(
            new Version(2, 0, 0),
            "v2.0.0",
            false,
            new Uri("https://github.com/ZOONGG/Moonrise/releases/tag/v2.0.0"),
            string.Empty,
            new Uri($"https://github.com/ZOONGG/Moonrise/releases/download/v2.0.0/{setupName}"),
            new Uri($"https://github.com/ZOONGG/Moonrise/releases/download/v2.0.0/{setupName}.sha256"),
            setupName);
        using var client = new HttpClient(new UpdateAssetHandler(
            Encoding.UTF8.GetBytes("not-an-installer"),
            $"{new string('0', 64)}  {setupName}"));

        await Assert.ThrowsAsync<CryptographicException>(() =>
            new ApplicationUpdateService().StageAsync(release, client, temp.Path));
    }

    private static PackageInfo Package(
        string fileName,
        string identifier,
        IReadOnlyList<string> versions,
        IReadOnlyList<string>? conflicts = null,
        IReadOnlyList<string>? requires = null) => new()
        {
            FileName = fileName,
            DisplayName = fileName,
            Identifier = identifier,
            FullPath = Path.Combine(Path.GetTempPath(), fileName),
            Kind = PackageKind.WeaveMod,
            Sha256 = new string('A', 64),
            CompatibleGameVersions = versions,
            Conflicts = conflicts ?? [],
            Requires = requires ?? []
        };

    private static string CreateJar(string directory, string fileName, IReadOnlyDictionary<string, string> entries)
    {
        var path = Path.Combine(directory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"moonrise-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }

    private sealed class ResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
    }

    private sealed class UpdateAssetHandler(byte[] setup, string checksum) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                ? new ByteArrayContent(Encoding.ASCII.GetBytes(checksum))
                : new ByteArrayContent(setup);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
