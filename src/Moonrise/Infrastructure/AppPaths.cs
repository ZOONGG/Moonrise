using System.Text;

namespace Moonrise.Infrastructure;

public enum AppMode
{
    Installed,
    Portable,
    Development
}

public sealed class AppPaths
{
    public const string PortableMarkerFileName = "Moonrise.portable";
    public const string ApplicationDirectoryName = "Moonrise";
    public const string DevelopmentDirectoryName = "dev";

    public AppPaths(string rootDirectory) : this(rootDirectory, rootDirectory, AppMode.Portable) { }

    public AppPaths(string rootDirectory, string installationDirectory, AppMode mode)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        InstallationDirectory = Path.GetFullPath(installationDirectory);
        Mode = mode;
    }

    public string RootDirectory { get; }
    public string InstallationDirectory { get; }
    public AppMode Mode { get; }
    public bool IsPortable => Mode == AppMode.Portable;
    public string PackagesDirectory => Path.Combine(RootDirectory, "packages");
    public string WeavePackagesDirectory => Path.Combine(PackagesDirectory, "weave");
    public string AgentPackagesDirectory => Path.Combine(PackagesDirectory, "agents");
    public string UnclassifiedPackagesDirectory => Path.Combine(PackagesDirectory, "unclassified");
    public string PackageMetadataDirectory => Path.Combine(PackagesDirectory, "metadata");
    public string PackageIndexPath => Path.Combine(PackageMetadataDirectory, "index.json");
    public string PackageReadmePath => Path.Combine(PackagesDirectory, "README.txt");
    public string PackageLayoutMigrationMarkerPath => Path.Combine(PackageMetadataDirectory, ".layout-migrated-v4");
    public string PackageLayoutMigrationLogPath => Path.Combine(PackageMetadataDirectory, "layout-migration-v4.log");
    public string PackageMetadataRecoveryDirectory => Path.Combine(PackageMetadataDirectory, "recovery");
    public string LegacyPreservedMetadataDirectory => Path.Combine(PackageMetadataDirectory, "legacy-preserved");
    public string LegacyAddPackagesDirectory => Path.Combine(PackagesDirectory, "add");
    public string LegacyInstalledPackagesDirectory => Path.Combine(PackagesDirectory, "installed");
    public string LegacyOriginalsPackagesDirectory => Path.Combine(PackagesDirectory, "originals");
    public string LegacyIncomingPackagesDirectory => Path.Combine(PackagesDirectory, "incoming");
    public string LegacyMoonriseOwnedPackagesDirectory => Path.Combine(PackagesDirectory, "moonrise-owned");
    public string LegacyImportedPackagesDirectory => Path.Combine(PackagesDirectory, "imported");
    public string AddPackagesDirectory => LegacyAddPackagesDirectory;
    public string InstalledPackagesDirectory => LegacyInstalledPackagesDirectory;
    public string InstalledWeavePackagesDirectory => WeavePackagesDirectory;
    public string InstalledAgentPackagesDirectory => AgentPackagesDirectory;
    public string InstalledUnclassifiedPackagesDirectory => UnclassifiedPackagesDirectory;
    public string IncomingPackagesDirectory => UnclassifiedPackagesDirectory;
    public string OriginalsPackagesDirectory => LegacyOriginalsPackagesDirectory;
    public string MoonriseOwnedPackagesDirectory => LegacyMoonriseOwnedPackagesDirectory;
    public string ImportedPackagesDirectory => LegacyImportedPackagesDirectory;
    public string MoonriseOwnedModsDirectory => Path.Combine(LegacyMoonriseOwnedPackagesDirectory, "mods");
    public string MoonriseOwnedAgentsDirectory => Path.Combine(LegacyMoonriseOwnedPackagesDirectory, "agents");
    public string AdaptersDirectory => Path.Combine(RootDirectory, "adapters");
    public string CacheDirectory => Path.Combine(RootDirectory, "cache");
    public string SettingsDirectory => Path.Combine(RootDirectory, "settings");
    public string UserModsDirectory => Path.Combine(LegacyImportedPackagesDirectory, "mods");
    public string UserAgentsDirectory => Path.Combine(LegacyImportedPackagesDirectory, "agents");
    public string ProfilesDirectory => Path.Combine(SettingsDirectory, "profiles");
    public string PluginsDirectory => Path.Combine(RootDirectory, "plugins");
    public string LanguagePacksDirectory => Path.Combine(SettingsDirectory, "language-packs");
    public string ThemesDirectory => Path.Combine(SettingsDirectory, "themes");
    public string RuntimeDirectory => Path.Combine(CacheDirectory, "runtime");
    public string WeaveDirectory => Path.Combine(RuntimeDirectory, "weave");
    public string WeaveAgentPath => Path.Combine(WeaveDirectory, "weave-loader.jar");
    public string LegacyWeaveAgentPath => Path.Combine(WeaveDirectory, "weave-loader-0.2.6.jar");
    public string TempDirectory => Path.Combine(RootDirectory, "temp");
    public string SessionRoot => TempDirectory;
    public string PluginRequestDirectory => Path.Combine(TempDirectory, "plugin-requests");
    public string UpdateDirectory => Path.Combine(CacheDirectory, "updates");
    public string NativeBridgePath => Path.Combine(InstallationDirectory, "runtime", "bridge", "Moonrise.Native.dll");
    public string NativeBridgeCachePath => Path.Combine(RuntimeDirectory, "bridge", "Moonrise.Native.dll");
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public string CrashesDirectory => Path.Combine(LogsDirectory, "crashes");
    public string CrashReportsDirectory => CrashesDirectory;
    public string SettingsPath => Path.Combine(SettingsDirectory, "moonrise-settings.json");
    public string CatalogCacheDirectory => Path.Combine(CacheDirectory, "catalog");
    public string ManagedPackagesDirectory => PackagesDirectory;
    public string MigrationMarkerPath => Path.Combine(SettingsDirectory, ".legacy-layout-migrated-v1");

    public void EnsureUserDirectories()
    {
        Directory.CreateDirectory(PackagesDirectory);
        Directory.CreateDirectory(WeavePackagesDirectory);
        Directory.CreateDirectory(AgentPackagesDirectory);
        Directory.CreateDirectory(UnclassifiedPackagesDirectory);
        Directory.CreateDirectory(PackageMetadataDirectory);
        Directory.CreateDirectory(AdaptersDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(LanguagePacksDirectory);
        Directory.CreateDirectory(ThemesDirectory);
        Directory.CreateDirectory(WeaveDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(PluginRequestDirectory);
        Directory.CreateDirectory(UpdateDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CrashesDirectory);
        Directory.CreateDirectory(CatalogCacheDirectory);
        EnsurePackageReadme();
    }

    private void EnsurePackageReadme()
    {
        const string readme =
            "Moonrise packages / Пакеты Moonrise\r\n" +
            "\r\n" +
            "RU:\r\n" +
            "- Копируйте Weave-моды в папку weave.\r\n" +
            "- Копируйте Java-агенты в папку agents.\r\n" +
            "- Файлы, тип которых не удалось определить, появляются в папке unclassified и остаются отключёнными.\r\n" +
            "- Также можно использовать «Добавить JAR» или перетаскивание.\r\n" +
            "- Moonrise автоматически обнаруживает новые файлы; кнопка повторного сканирования не требуется.\r\n" +
            "- Не изменяйте содержимое папки metadata вручную.\r\n" +
            "- Ручное изменение установленного JAR может привести к ошибке проверки целостности.\r\n" +
            "- Moonrise не изменяет файлы пакетов.\r\n" +
            "\r\n" +
            "EN:\r\n" +
            "- Copy Weave mods into the weave folder.\r\n" +
            "- Copy Java agents into the agents folder.\r\n" +
            "- Files whose type cannot be resolved appear in unclassified and remain disabled.\r\n" +
            "- You can also use Add JAR or drag and drop.\r\n" +
            "- Moonrise detects new files automatically; no Rescan button is required.\r\n" +
            "- Do not edit the metadata folder manually.\r\n" +
            "- Manually changing an installed JAR may fail integrity verification.\r\n" +
            "- Moonrise does not edit package files.\r\n";
        if (!File.Exists(PackageReadmePath) ||
            !string.Equals(File.ReadAllText(PackageReadmePath), readme, StringComparison.Ordinal))
        {
            File.WriteAllText(PackageReadmePath, readme, new UTF8Encoding(false));
        }
    }

    public static AppPaths Discover()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Discover(AppContext.BaseDirectory, localAppData, searchForDevelopmentRoot: true);
    }

    public static AppPaths Discover(
        string installationDirectory,
        string localApplicationDataDirectory,
        bool searchForDevelopmentRoot = false)
    {
        installationDirectory = Path.GetFullPath(installationDirectory);
        if (searchForDevelopmentRoot)
        {
            var current = new DirectoryInfo(installationDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Moonrise.sln")))
                {
                    var developmentRoot = Path.Combine(
                        Path.GetFullPath(localApplicationDataDirectory),
                        ApplicationDirectoryName,
                        DevelopmentDirectoryName);
                    return new AppPaths(developmentRoot, current.FullName, AppMode.Development);
                }
                current = current.Parent;
            }
        }

        var dataRoot = Path.Combine(
            Path.GetFullPath(localApplicationDataDirectory),
            ApplicationDirectoryName);
        return new AppPaths(dataRoot, installationDirectory, AppMode.Installed);
    }
}
