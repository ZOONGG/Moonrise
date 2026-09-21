using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Moonrise.Infrastructure;
using Moonrise.Models;
using Moonrise.Services;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Moonrise;

public partial class MainWindow : Window
{
    private const string LaunchDeepLink = "lunarclient://launch";
    private const string BwhPackageSha256 = "6DA5B2A67E05E07DEAE2779B4AA3D1B70E5028DED227C41487FF783F5D609A57";
    private const string BwhMaintainedBuildSha256 = "C39F6F1C891825CD309F543D1EE112522CAC3660AC60D9E96A5A21F20756752D";
    private readonly AppPaths _paths = AppPaths.Discover();
    private readonly JarMetadataParser _jarParser = new();
    private readonly PackageCatalogService _packages;
    private readonly LocalPackageLibrary _library;
    private readonly PackageLaunchResolver _launchResolver;
    private readonly MaintainedCompatibilityBuildService _compatibilityBuilds;
    private readonly LegacyWeaveDirectoryAdapterService _legacyWeaveAdapter;
    private readonly WeaveModApiInspector _weaveApiInspector = new();
    private readonly LaunchSessionService _launchSessions = new();
    private readonly LauncherExecutableDetector _launcherDetector = new();
    private readonly LauncherProfileService _profileService = new();
    private readonly LunarProfileReadinessService _profileReadiness = new();
    private readonly WeaveAgentService _weaveAgent = new();
    private readonly NativeBridgeLauncher _bridgeLauncher = new();
    private readonly NativeBridgeDeploymentService _nativeBridgeDeployment = new();
    private readonly BwhNetworkAgentDeploymentService _bwhNetworkAgentDeployment;
    private readonly ProcessInspector _processInspector = new();
    private readonly LaunchPreflightService _launchPreflight = new(new PackageCompatibilityAnalyzer());
    private readonly CrashAnalyzer _crashAnalyzer = new();
    private readonly PackageCrashDiagnosticsService _packageCrashDiagnostics = new();
    private readonly CatalogManifestService _catalogManifests = new();
    private readonly DeveloperPackageInspector _developerInspector = new();
    private readonly ModpackProfileService _loadouts = new();
    private readonly ClientPluginService _clientPluginService = new();
    private readonly AppearancePackService _appearancePacks = new();
    private readonly ThemePackService _themePacks = new();
    private readonly ThemeService _themeService;
    private readonly MotionController _motion = new();
    private readonly ToastController _toasts;
    private readonly ApplicationUpdateService _applicationUpdates = new();
    private readonly ObservableCollection<PackageInfo> _visiblePackages = [];
    private readonly ObservableCollection<string> _diagnostics = [];
    private readonly ObservableCollection<PackageManifest> _catalogEntries = [];
    private readonly List<PackageManifest> _catalogPackages = [];
    private readonly ObservableCollection<ModpackProfile> _savedLoadouts = [];
    private readonly ObservableCollection<ClientChoice> _clientChoices = [];
    private readonly ObservableCollection<LanguagePack> _languageChoices = [];
    private readonly ObservableCollection<ThemePack> _themeChoices = [];
    private readonly HashSet<int> _activeGameProcessIds = [];
    private readonly HashSet<string> _activeLaunchPackageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedIncomingFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISessionCountProvider _sessionCountProvider;
    private readonly AppSettingsService _settingsService;
    private readonly MoonriseStorageService _storage;
    private readonly MoonriseSettings _settings;
    private CancellationTokenSource? _monitorCancellation;
    private int _launchInProgress;
    private SafeLogger? _logger;
    private SafeLogger? _lunarWindowLogger;
    private bool _initialized;
    private bool _russian;
    private string _languageCode;
    private LanguagePack? _activeLanguage;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayOpenItem;
    private Forms.ToolStripMenuItem? _trayExitItem;
    private PackageInfo? _pendingDeletePackage;
    private Action? _pendingConfirmationAction;
    private UpdateRelease? _availableUpdate;
    private string? _pendingActivationArgument;
    private bool _catalogReady;
    private DeveloperInspection? _developerInspection;
    private PackageManifest? _developerDraft;
    private string? _developerJarPath;
    private readonly List<FileSystemWatcher> _packageWatchers = [];
    private CancellationTokenSource? _packageScanCancellation;
    private LunarBackgroundLaunchService? _activeLunarBackground;
    private CancellationTokenSource? _lunarWindowCancellation;
    private Task? _lunarWindowTask;
    private string? _lastCrashBundlePath;
    private string? _lastCrashReportPath;
    private bool _launchWaitCancelledByUser;
    private BwhApiRelayService? _bwhApiRelay;
    private bool _explicitExitRequested;
    private readonly bool _visualQaMode;
    private StatusLevel _lastStatusLevel = StatusLevel.Working;
    private HttpClient? _supportHttpClient;
    private SupportFlowController? _supportFlow;
    private CancellationTokenSource? _supportDialogCancellation;
    private bool _supportShowingMethods = true;
    private string? _renderedSupportCheckoutUrl;
    private int _supportCopyToastGeneration;
    private string? _lastSupportDiagnosticKey;

    private IEnumerable<PackageInfo> _mods => _library.Packages.Where(item => item.Kind == PackageKind.WeaveMod);
    private IEnumerable<PackageInfo> _agents => _library.Packages.Where(item => item.Kind == PackageKind.JavaAgent);

    public MainWindow(string? activationArgument = null, bool visualQaMode = false)
    {
        _toasts = new ToastController(Dispatcher);
        _themeService = new ThemeService(_themePacks);
        _visualQaMode = visualQaMode;
        if (visualQaMode && Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA_DATA_ROOT") is { Length: > 0 } qaRoot)
            _paths = new AppPaths(qaRoot, AppContext.BaseDirectory, AppMode.Development);
        _packages = new PackageCatalogService(_jarParser);
        _library = new LocalPackageLibrary(_paths, _jarParser);
        _launchResolver = new PackageLaunchResolver(_jarParser, _paths);
        _compatibilityBuilds = new MaintainedCompatibilityBuildService(_paths, _jarParser);
        _bwhNetworkAgentDeployment = new BwhNetworkAgentDeploymentService(_paths);
        _legacyWeaveAdapter = new LegacyWeaveDirectoryAdapterService(_paths);
        _sessionCountProvider = new LocalSessionCountProvider(() => _activeGameProcessIds.Count);
        _settingsService = new AppSettingsService(_paths.SettingsPath);
        _storage = new MoonriseStorageService(_paths);
        _settings = _settingsService.Load();
        _pendingActivationArgument = activationArgument;
        _languageCode = string.IsNullOrWhiteSpace(_settings.Language) ? "ru" : _settings.Language;
        _russian = string.Equals(_languageCode, "ru", StringComparison.OrdinalIgnoreCase);
        ConfigureMotionResources();
        InitializeComponent();
        InitializeAppearancePacks();
        if (!_visualQaMode) InitializeTrayIcon();
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        AboutVersionText.Text = $"Moonrise {version}";
        AboutInlineVersionText.Text = version;
        PackagesList.ItemsSource = _visiblePackages;
        DiagnosticsList.ItemsSource = _diagnostics;
        CatalogList.ItemsSource = _catalogEntries;
        ToastHost.ItemsSource = _toasts.Visible;
        LoadoutComboBox.ItemsSource = _savedLoadouts;
        ClientComboBox.ItemsSource = _clientChoices;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        StateChanged += (_, _) =>
        {
            LanguagePopup.IsOpen = false;
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        };
        Loaded += MainWindow_Loaded;
        ApplyLanguage();
        ApplyTheme();
        ApplyResponsiveLayout(Width, Height);
    }

    private void ConfigureMotionResources()
    {
        if (!_motion.ReducedMotion)
            return;
        var nearlyImmediate = new Duration(TimeSpan.FromMilliseconds(1));
        foreach (var key in new[] { "FastDuration", "ControlDuration", "PopupDuration", "ToggleDuration", "NormalDuration", "PageDuration", "DrawerDuration", "ModalDuration", "ToastDuration" })
            Application.Current.Resources[key] = nearlyImmediate;
    }

    internal void HandleExternalActivation(string? activationArgument)
    {
        RestoreFromExternalActivation();
        if (string.IsNullOrWhiteSpace(activationArgument))
            return;
        if (!_catalogReady)
        {
            _pendingActivationArgument = activationArgument;
            return;
        }

        _ = HandleDeepLinkAsync(activationArgument);
    }

    private void RestoreFromExternalActivation()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _paths.EnsureUserDirectories();
            _logger = new SafeLogger(_paths.LogsDirectory);
            AddDiagnostic($"Moonrise data root: {_paths.RootDirectory}");
            var staleLaunches = _launchSessions.CleanupStale(_paths.TempDirectory);
            AddDiagnostic($"Stale launch directories cleaned: {staleLaunches}");
            _library.Load();
            if (_library.LastReconciliationResult is { MetadataRebuilt: true })
                AddDiagnostic("Package metadata was rebuilt from the authoritative category folders.");
            if (_library.LastLayoutMigrationResult is { AlreadyComplete: false } layoutMigration)
            {
                AddDiagnostic(
                    $"Package layout migrated: installed={layoutMigration.PackageFilesMigrated}; add={layoutMigration.AddFilesMigrated}");
            }
            var migrated = _library.MigrateLegacyPackagesOnce(_settings.DisabledMods, _settings.DisabledAgents);
            AddDiagnostic($"Legacy package records migrated: {migrated}");
            LoadPackages();
            RecoverBwhRelayForRunningMinecraft();
            InitializePackageWatchers();
            await ScanPackageFoldersAsync(showSummary: false);
            LoadSavedLoadouts();
            LoadClientPlugins();
            PopulateVersions();

            var candidates = await Task.Run(_launcherDetector.Detect);
            LauncherPathTextBox.Text = LauncherExecutableDetector.IsSupportedLauncher(_settings.LauncherExecutablePath)
                ? _settings.LauncherExecutablePath
                : candidates.FirstOrDefault() ?? string.Empty;
            CloseAfterLaunchCheckBox.IsChecked = _settings.CloseAfterLaunch;
            _settings.BackgroundLunarLaunch = true;
            RevealLunarCheckBox.IsChecked = _settings.RevealLunarWhenActionRequired;
            CheckForUpdatesCheckBox.IsChecked = _settings.CheckForUpdates;
            PrereleaseUpdatesCheckBox.IsChecked = _settings.IncludePrereleaseUpdates;
            DeveloperModeCheckBox.IsChecked = _settings.DeveloperMode;
            CustomCatalogUrlTextBox.Text = _settings.CustomTestCatalogUrl;
            DevelopersTabButton.Visibility = _settings.DeveloperMode || _visualQaMode ? Visibility.Visible : Visibility.Collapsed;
            SelectSavedVersion();
            RefreshProfileState();
            RefreshStorageSummary();

            SetStatus(T("Проверка компонентов…", "Checking components…"), StatusLevel.Working);
            SetStatus(T("Готов к запуску", "Ready to launch"), StatusLevel.Ready,
                T("Компоненты запуска проверены.", "Launch components verified."),
                T("Готово", "Ready"));
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Initialization error: {exception.Message}");
            SetStatus(T("Нужна настройка", "Setup required"), StatusLevel.Error, exception.Message);
            LaunchButton.IsEnabled = false;
        }
        SaveSettings();
        LoadCachedCatalog();

        _catalogReady = true;
        if (!string.IsNullOrWhiteSpace(_pendingActivationArgument))
        {
            var activationArgument = _pendingActivationArgument;
            _pendingActivationArgument = null;
            await HandleDeepLinkAsync(activationArgument);
        }
        if (_visualQaMode)
            await ApplyVisualQaStateAsync();
    }

    private async Task ApplyVisualQaStateAsync()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA_WIDTH"), out var qaWidth))
            Width = Math.Max(MinWidth, qaWidth);
        if (int.TryParse(Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA_HEIGHT"), out var qaHeight))
            Height = Math.Max(MinHeight, qaHeight);

        var state = Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA_STATE")?.Trim().ToLowerInvariant();
        if (state is not ("developers" or "diagnostics"))
            DevelopersTabButton.Visibility = Visibility.Collapsed;
        switch (state)
        {
            case "home-en":
                ShowPage(HomePage, HomeTabButton);
                break;
            case "catalog":
                ShowPage(CatalogPage, CatalogTabButton);
                break;
            case "library":
                ShowPage(LibraryPage, LibraryTabButton);
                break;
            case "library-selected":
                ShowPage(LibraryPage, LibraryTabButton);
                PackagesList.SelectedIndex = PackagesList.Items.Count > 0 ? 0 : -1;
                break;
            case "library-drag":
                ShowPage(LibraryPage, LibraryTabButton);
                ConfigureDropTarget(valid: true);
                ShowDropTarget();
                break;
            case "settings":
                ShowPage(SettingsPage, SettingsTabButton);
                break;
            case "settings-bottom":
                ShowPage(SettingsPage, SettingsTabButton);
                break;
            case "settings-en":
                ShowPage(SettingsPage, SettingsTabButton);
                break;
            case "settings-appearance":
                ShowPage(SettingsPage, SettingsTabButton);
                break;
            case "developers":
                DevelopersTabButton.Visibility = Visibility.Visible;
                ShowPage(DevelopersPage, DevelopersTabButton);
                break;
            case "diagnostics":
                OpenDiagnosticsButton_Click(OpenDiagnosticsButton, new RoutedEventArgs());
                break;
            case "dialog":
                ShowPage(LibraryPage, LibraryTabButton);
                OpenConfirmationDialog(
                    T("Удалить пакет?", "Remove package?"),
                    T("Локальный JAR-файл будет удалён.", "The local JAR will be deleted."),
                    "example-package.jar",
                    T("Удалить", "Remove"),
                    null);
                DeleteConfirmationConfirmButton.IsEnabled = false;
                break;
            case "empty":
                ShowPage(LibraryPage, LibraryTabButton);
                _visiblePackages.Clear();
                PackagesList.Visibility = Visibility.Collapsed;
                EmptyLibraryPanel.Visibility = Visibility.Visible;
                LibraryCountText.Text = FormatPackageCount(0);
                break;
            case "loading":
                ShowPage(CatalogPage, CatalogTabButton);
                CatalogList.Visibility = Visibility.Collapsed;
                CatalogEmptyPanel.Visibility = Visibility.Collapsed;
                CatalogLoadingPanel.Visibility = Visibility.Visible;
                break;
            case "combobox":
                ShowPage(HomePage, HomeTabButton);
                _ = Dispatcher.BeginInvoke(new Action(() => ClientComboBox.IsDropDownOpen = true), System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            case "language-popup":
                ShowPage(HomePage, HomeTabButton);
                _ = Dispatcher.BeginInvoke(new Action(() => LanguagePopup.IsOpen = true), System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            case "context-menu":
                ShowPage(LibraryPage, LibraryTabButton);
                _ = Dispatcher.BeginInvoke(new Action(OpenFirstPackageContextMenu), System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            case "toast":
                ShowPage(HomePage, HomeTabButton);
                ShowToast(ToastKind.Success, T("Пакет импортирован", "Package imported"), "example-package.jar");
                ShowToast(ToastKind.Info, T("Настройки сохранены", "Settings saved"));
                break;
            case "stress":
                _ = RunVisualStressTestAsync();
                break;
            case "support-methods":
            case "support-amount":
            case "support-custom":
            case "support-checkout":
            case "support-success":
            case "support-expired":
            case "support-failure":
            case "support-crypto-methods":
            case "support-crypto-amount":
            case "support-crypto-checkout":
            case "support-crypto-success":
                await ApplySupportVisualQaStateAsync(state);
                break;
        }

        SettingsScrollViewer.ScrollToTop();
        if (state == "settings-bottom")
            _ = Dispatcher.BeginInvoke(
                new Action(SettingsScrollViewer.ScrollToEnd),
                System.Windows.Threading.DispatcherPriority.Loaded);
        else if (state == "settings-appearance")
            _ = Dispatcher.BeginInvoke(
                new Action(() => ThemeComboBox.BringIntoView()),
                System.Windows.Threading.DispatcherPriority.Loaded);

        var qaTheme = Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA_THEME")?.Trim().ToLowerInvariant();
        if (_themeChoices.FirstOrDefault(theme => string.Equals(theme.Id, qaTheme, StringComparison.OrdinalIgnoreCase)) is { } visualTheme)
        {
            ThemeComboBox.SelectedItem = visualTheme;
            ApplyTheme();
        }

        var qaLanguage = state is "home-en" or "settings-en"
            ? "en"
            : Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA_LANGUAGE")?.Trim().ToLowerInvariant();
        if (qaLanguage is "en" or "ru")
        {
            _languageCode = qaLanguage;
            _russian = qaLanguage == "ru";
            _activeLanguage = _languageChoices.FirstOrDefault(pack => string.Equals(pack.Code, qaLanguage, StringComparison.OrdinalIgnoreCase));
            LanguagePackComboBox.SelectedItem = _activeLanguage;
            ApplyLanguage();
            RefreshVisiblePackages();
            RefreshCounts();
        }

        if (Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA_SCREENSHOT") is { Length: > 0 } screenshotPath)
        {
            await Dispatcher.InvokeAsync(
                () => { },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await Task.Delay(450);
            CaptureVisualQaScreenshot(screenshotPath);
            if (string.Equals(
                    Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA_EXIT_AFTER_SCREENSHOT"),
                    "1",
                    StringComparison.Ordinal))
                _ = Dispatcher.BeginInvoke(Close, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private void CaptureVisualQaScreenshot(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException("The screenshot output directory is unavailable.");
        Directory.CreateDirectory(directory);
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(stream);
    }

    private void OpenFirstPackageContextMenu()
    {
        if (PackagesList.Items.Count == 0)
            return;
        PackagesList.UpdateLayout();
        if (PackagesList.ItemContainerGenerator.ContainerFromIndex(0) is not DependencyObject container)
            return;
        var owner = FindContextMenuOwner(container);
        if (owner?.ContextMenu is null)
            return;
        owner.ContextMenu.PlacementTarget = owner;
        owner.ContextMenu.IsOpen = true;
    }

    private static FrameworkElement? FindContextMenuOwner(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement { ContextMenu: not null } element)
                return element;
            if (FindContextMenuOwner(child) is { } match)
                return match;
        }
        return null;
    }

    private async Task RunVisualStressTestAsync()
    {
        var pages = new (FrameworkElement Page, System.Windows.Controls.RadioButton Tab)[]
        {
            (HomePage, HomeTabButton), (CatalogPage, CatalogTabButton),
            (LibraryPage, LibraryTabButton), (SettingsPage, SettingsTabButton)
        };
        var originalTheme = _settings.Theme;
        for (var index = 0; index < 28; index++)
        {
            var destination = pages[index % pages.Length];
            ShowPage(destination.Page, destination.Tab);
            if (index % 4 == 0 && PackagesList.Items.Count > 0)
                PackagesList.SelectedIndex = 0;
            else if (index % 4 == 1)
                PackagesList.SelectedItem = null;
            if (index % 7 == 0)
                ThemeComboBox.SelectedItem = _themeChoices[(index / 7) % _themeChoices.Count];
            await Task.Delay(28);
        }
        ClientComboBox.IsDropDownOpen = true;
        await Task.Delay(80);
        ClientComboBox.IsDropDownOpen = false;
        for (var index = 0; index < 6; index++)
            ShowToast(ToastKind.Info, $"Stress notification {index + 1}");
        ThemeComboBox.SelectedItem = _themeChoices.FirstOrDefault(theme => string.Equals(theme.Id, originalTheme, StringComparison.OrdinalIgnoreCase)) ?? _themeChoices[0];
        ShowPage(HomePage, HomeTabButton);
        ShowToast(ToastKind.Success, "Visual stress test complete", "28 page switches · drawers · themes · ComboBox · bounded toasts", autoDismiss: false);
    }

    private void LoadPackages()
    {
        RefreshVisiblePackages();
        RefreshCounts();
    }

    private void LoadDirectory(string directory, PackageKind kind, IEnumerable<string> disabledNames, ObservableCollection<PackageInfo> target)
    {
        var disabled = disabledNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(directory);
        foreach (var path in Directory.EnumerateFiles(directory, "*.jar").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var package = kind == PackageKind.WeaveMod ? _jarParser.ParseWeaveMod(path) : _jarParser.ParseJavaAgent(path);
                package.IsEnabled = !disabled.Contains(package.FileName);
                target.Add(package);
            }
            catch (Exception exception)
            {
                AddDiagnostic($"Skipped {Path.GetFileName(path)}: {exception.Message}");
            }
        }
    }

    private void PopulateVersions()
    {
        VersionComboBox.ItemsSource = new[] { "1.8.9" };
    }

    private void LoadClientPlugins()
    {
        _clientChoices.Clear();
        _clientChoices.Add(new ClientChoice("lunar", "Lunar Client", null));
        ClientComboBox.SelectedItem = _clientChoices[0];
    }

    private static Version VersionKey(string value) => Version.TryParse(value, out var version) ? version : new Version();

    private void SelectSavedVersion()
    {
        var match = VersionComboBox.Items.Cast<string>().FirstOrDefault(item =>
            string.Equals(item, _settings.MinecraftVersion, StringComparison.OrdinalIgnoreCase));
        VersionComboBox.SelectedItem = match ?? "1.8.9";
    }

    private void RefreshVisiblePackages()
    {
        _visiblePackages.Clear();
        var query = AllKindButton.IsChecked == true
            ? _library.Packages
            : AgentsKindButton.IsChecked == true
                ? _agents
                : UnclassifiedKindButton.IsChecked == true
                    ? _library.Packages.Where(item => item.Kind is PackageKind.Ambiguous or PackageKind.Unclassified)
                    : _mods;
        var search = LibrarySearchBox.Text.Trim();
        foreach (var package in query.Where(package => string.IsNullOrWhiteSpace(search) ||
                     package.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                     package.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)))
            _visiblePackages.Add(package);
        EmptyLibraryPanel.Visibility = _visiblePackages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PackagesList.Visibility = _visiblePackages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        LibraryCountText.Text = FormatPackageCount(_visiblePackages.Count);
    }

    private void RefreshCounts()
    {
        var mods = _mods.Count(package => package.IsEnabled);
        var agents = _agents.Count(package => package.IsEnabled);
        ModsCountText.Text = string.Format(T("{0} включено", "{0} enabled"), mods);
        AgentsCountText.Text = string.Format(T("{0} включено", "{0} enabled"), agents);
        HeroPackageSummaryText.Text = _russian
            ? $"Weave-моды: {mods}  ·  Java-агенты: {agents}"
            : $"Weave mods: {mods}  ·  Java agents: {agents}";
        SessionCountText.Text = string.Format(T("Активных сессий: {0}", "Active sessions: {0}"), _sessionCountProvider.ActiveSessionCount);
    }

    private void LoadSavedLoadouts(string? selectId = null)
    {
        var selectedId = selectId ?? _settings.ActiveLoadoutId;
        _savedLoadouts.Clear();
        foreach (var profile in _loadouts.LoadAll(_paths.ProfilesDirectory)) _savedLoadouts.Add(profile);
        LoadoutComboBox.SelectedItem = _savedLoadouts.FirstOrDefault(profile =>
            string.Equals(profile.Id, selectedId, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshProfileState()
    {
        var client = SelectedClient;
        var version = SelectedVersion;
        if (SelectedClientChoice?.Plugin is { } plugin)
        {
            ProfileStateText.Text = plugin.Manifest.Name;
            ProfileStateText.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Success");
            return;
        }
        try
        {
            var profile = _profileService.GetProfiles().FirstOrDefault(item =>
                string.Equals(item.Client, client, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.GameVersion, version, StringComparison.OrdinalIgnoreCase));
            ProfileStateText.Text = profile is null
                ? T("Создайте профиль в Lunar", "Create it in Lunar first")
                : profile.Name;
            ProfileStateText.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,
                profile is null ? "Warning" : "Success");
        }
        catch
        {
            ProfileStateText.Text = T("Lunar ещё не настроен", "Lunar is not configured yet");
            ProfileStateText.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Warning");
        }
    }

    private ClientChoice? SelectedClientChoice => ClientComboBox.SelectedItem as ClientChoice;
    private string SelectedClient => SelectedClientChoice?.Id ?? "lunar";
    private string SelectedVersion => VersionComboBox.SelectedItem as string ?? "1.8.9";
    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!LanguagePopup.IsOpen) ReloadLanguagePacks();
        LanguagePopup.IsOpen = !LanguagePopup.IsOpen;
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (LanguagePopup.IsOpen && !LanguageButton.IsMouseOver && LanguagePopup.Child?.IsMouseOver != true)
            LanguagePopup.IsOpen = false;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (SupportOverlay.Visibility == Visibility.Visible)
        {
            CloseSupportDialog();
            e.Handled = true;
            return;
        }
        if (LanguagePopup.IsOpen)
        {
            LanguagePopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (DeleteConfirmationOverlay.Visibility == Visibility.Visible)
        {
            DeleteConfirmationCancelButton_Click(DeleteConfirmationCancelButton, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (CatalogList.SelectedItem is not null)
        {
            CatalogList.SelectedItem = null;
            e.Handled = true;
            return;
        }

        if (PackagesList.SelectedItem is not null)
        {
            PackagesList.SelectedItem = null;
            e.Handled = true;
            return;
        }

        if (DiagnosticsPage.Visibility == Visibility.Visible)
        {
            ShowPage(SettingsPage, SettingsTabButton);
            e.Handled = true;
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e) => LanguagePopup.IsOpen = false;

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (LanguagePopup is not null) LanguagePopup.IsOpen = false;
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_initialized) ReloadLanguagePacks();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (LanguagePopup is not null) LanguagePopup.IsOpen = false;
        ApplyResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void ApplyResponsiveLayout(double windowWidth, double windowHeight)
    {
        if (!double.IsFinite(windowWidth) || windowWidth <= 0 || !double.IsFinite(windowHeight) || windowHeight <= 0)
            return;

        var side = windowWidth < 1040 ? 22d : 30d;
        HomePage.Margin = new Thickness(side, 24, side, 28);
        foreach (var page in new FrameworkElement[] { LibraryPage, CatalogPage, SettingsPage, DiagnosticsPage, DevelopersPage })
            page.Margin = new Thickness(side, 24, side, 28);
    }

    private void RefreshLanguageMenu()
    {
        LanguageOptionsPanel.Children.Clear();
        foreach (var language in _languageChoices)
        {
            var selected = string.Equals(language.Code, _languageCode, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Content = language.Name + (selected ? "  ✓" : ""),
                Style = (Style)FindResource("LanguageOptionStyle")
            };
            if (selected)
                button.SetResourceReference(Control.BackgroundProperty, "SurfaceSelected");
            else
                button.Background = Brushes.Transparent;
            button.Click += (_, _) =>
            {
                LanguagePopup.IsOpen = false;
                LanguagePackComboBox.SelectedItem = language;
            };
            LanguageOptionsPanel.Children.Add(button);
        }
    }

    private void ReloadLanguagePacks()
    {
        var code = _languageCode;
        var languages = _appearancePacks.LoadLanguages(_paths.LanguagePacksDirectory,
            error => AddDiagnostic($"Language pack: {error}"));
        if (languages.Count == _languageChoices.Count && languages.Zip(_languageChoices).All(pair =>
                pair.First.Code == pair.Second.Code && pair.First.Name == pair.Second.Name &&
                pair.First.Translations.Count == pair.Second.Translations.Count &&
                pair.First.Translations.All(item => pair.Second.Translations.GetValueOrDefault(item.Key) == item.Value)))
            return;
        _languageChoices.Clear();
        foreach (var language in languages) _languageChoices.Add(language);
        LanguagePackComboBox.SelectedItem = _languageChoices.FirstOrDefault(pack =>
            string.Equals(pack.Code, code, StringComparison.OrdinalIgnoreCase)) ?? _languageChoices.First(pack => string.Equals(pack.Code, "en", StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyLanguage()
    {
        LanguageCodeText.Text = _languageCode.Split('-')[0].ToUpperInvariant();
        LanguageButton.ToolTip = _activeLanguage?.Name;
        RefreshLanguageMenu();
        CatalogComingSoonText.Text = T("Скоро", "Coming soon");
        DiscordButton.ToolTip = T("Открыть Discord Moonrise", "Open Moonrise Discord");
        System.Windows.Automation.AutomationProperties.SetName(DiscordButton, (string)DiscordButton.ToolTip);
        SupportButton.ToolTip = T("Поддержать Moonrise", "Support Moonrise");
        System.Windows.Automation.AutomationProperties.SetName(SupportButton, (string)SupportButton.ToolTip);
        SupportTitle.Text = T("Поддержать Moonrise", "Support Moonrise");
        SupportSubtitle.Text = T("Moonrise — бесплатный проект с открытым исходным кодом.", "Moonrise is free and open source.");
        SupportIntroText.Text = T("Если вам нравится проект, вы можете поддержать его развитие.", "If you enjoy the project, you can support its development.");
        SupportTelegramMethodTitle.Text = T("Звёзды Telegram", "Telegram Stars");
        SupportTelegramMethodHint.Text = T("Быстрая оплата в Telegram", "Fast checkout in Telegram");
        SupportCryptoTitle.Text = T("Криптовалюта", "Crypto");
        SupportCryptoHint.Text = T("Оплата из любого криптокошелька", "Pay from any crypto wallet");
        SupportLoadingText.Text = T("Загрузка способов оплаты…", "Loading payment methods…");
        SupportChooseAmountTitle.Text = T("Выберите сумму", "Choose amount");
        SupportBackToMethodsButton.Content = T("Назад", "Back");
        SupportCustomAmountLabel.Text = T("ДРУГАЯ СУММА", "CUSTOM AMOUNT");
        SupportTermsText.Text = T("Продолжая, вы принимаете условия поддержки.", "By continuing, you accept the support terms.");
        SupportTermsHint.Text = T("Добровольная поддержка не открывает платные функции и не включает регулярные платежи.", "Voluntary support does not unlock paid features or start recurring payments.");
        SupportContinueButton.Content = T("Продолжить", "Continue");
        SupportCheckoutMethodText.Text = T("Звёзды Telegram", "Telegram Stars");
        SupportOpenTelegramButton.Content = T("Открыть Telegram", "Open Telegram");
        SupportOpenTelegramHint.Text = T("Безопасно продолжите оплату в Telegram.", "Continue securely in Telegram.");
        SupportScanText.Text = T("Отсканируйте телефоном", "Scan with your phone");
        SupportThankYouTitle.Text = T("Спасибо за поддержку!", "Thank you for your support!");
        SupportThankYouText.Text = T("Ваша поддержка помогает развивать Moonrise и сохранять проект бесплатным.", "Your support helps Moonrise grow and remain free.");
        SupportDoneButton.Content = T("Готово", "Done");
        SupportCreateNewButton.Content = T("Создать новый платёж", "Create new payment");
        System.Windows.Automation.AutomationProperties.SetName(SupportCloseButton, T("Закрыть окно поддержки", "Close support"));
        SupportCloseButton.ToolTip = T("Закрыть", "Close");
        OpenThemesButton.Content = T("Открыть папку тем", "Open themes folder");
        ReloadThemesButton.Content = T("Обновить темы", "Reload themes");
        ImportThemeButton.Content = T("Импортировать папку", "Import folder");
        CreateThemeTemplateButton.Content = T("Создать шаблон темы", "Create theme template");
        HomeTabButton.Content = T("Главная", "Home"); CatalogTabButton.Content = T("Каталог", "Catalog");
        LibraryTabButton.Content = T("Библиотека", "Library"); SettingsTabButton.Content = T("Настройки", "Settings"); DevelopersTabButton.Content = T("Разработчикам", "Developers");
        DeveloperSectionLabel.Text = T("ДЛЯ РАЗРАБОТЧИКОВ", "FOR DEVELOPERS");
        HomeTitle.Text = T("Главная", "Home");
        HomeSubtitle.Text = T("Профиль Lunar Client и конфигурация следующего запуска.", "Your Lunar Client profile and next launch configuration at a glance.");
        HeroDescriptionText.Text = T("Официальный профиль Lunar с включёнными локальными пакетами.", "Official Lunar profile with your enabled local packages.");
        ClientHint.Text = SelectedClientChoice?.IsBuiltIn == false
            ? T("Запуск через установленный плагин клиента.", "Launch through an installed client plugin.")
            : T("Официальный профиль лаунчера.", "Official launcher profile.");
        VersionLabel.Text = T("ВЕРСИЯ MINECRAFT", "MINECRAFT VERSION"); ProfileLabel.Text = T("ПРОФИЛЬ ЛАУНЧЕРА", "LAUNCHER PROFILE");
        LoadoutTitle.Text = T("Конфигурация запуска", "Launch configuration"); LoadoutSubtitle.Text = T("Пакеты, активные для выбранного запуска.", "Packages enabled for this launch.");
        LoadoutLabel.Text = T("ПРОФИЛЬ НАБОРА", "LOADOUT PROFILE"); SaveLoadoutButton.Content = T("Сохранить", "Save"); DeleteLoadoutButton.Content = T("Удалить", "Delete"); ExportLoadoutButton.Content = T("Экспорт", "Export"); ImportLoadoutButton.Content = T("Импорт", "Import");
        ModsCardTitle.Text = T("Weave-моды", "Weave mods"); AgentsCardTitle.Text = T("Java-агенты", "Java agents"); ManagePackagesButton.Content = T("Управление пакетами", "Manage packages");
        EnabledPackagesTitle.Text = T("Включённые пакеты", "Enabled packages");
        EnabledPackagesSubtitle.Text = T("Будут загружены вместе с Minecraft.", "Packages that will load with Minecraft.");
        LaunchButton.Content = Volatile.Read(ref _launchInProgress) == 1
            ? T("Запуск…", "Launching…")
            : T("Запустить", "Launch");
        LibraryTitle.Text = T("Библиотека пакетов", "Package library"); LibrarySubtitle.Text = T("Управление локальными Weave-модами и Java-агентами.", "Manage local Weave mods and Java agents.");
        UpdateOpenPackageFolderButton(); AddPackageButton.Content = T("Добавить JAR", "Add JAR"); AllKindButton.Content = T("Все", "All"); ModsKindButton.Content = T("Weave-моды", "Weave mods"); AgentsKindButton.Content = T("Java-агенты", "Java agents"); UnclassifiedKindButton.Content = T("Без типа", "Unclassified"); DropTargetText.Text = T("Перетащите JAR-файлы сюда", "Drop JAR files here");
        DropTargetHint.Text = T("Weave-моды и Java-агенты определяются автоматически.", "Weave mods and Java agents are detected automatically.");
        PackageNameHeader.Text = T("ПАКЕТ", "PACKAGE"); EntrypointHeader.Text = T("ВЕРСИЯ", "VERSION"); PackageTypeHeader.Text = T("ТИП", "TYPE"); StateHeader.Text = T("СТАТУС", "STATUS");
        EmptyLibraryTitle.Text = T("Папка пуста", "This folder is empty"); EmptyLibraryText.Text = T("Добавьте совместимый JAR-пакет.", "Add a compatible JAR to continue.");
        LibraryDetailsTitle.Text = T("Сведения о пакете", "Package details"); DetailsVersionLabel.Text = T("ВЕРСИЯ", "VERSION"); DetailsCompatibilityLabel.Text = T("СОВМЕСТИМОСТЬ", "COMPATIBILITY"); DetailsPathLabel.Text = T("ПУТЬ К ФАЙЛУ", "FILE PATH"); DetailsImportedLabel.Text = T("ДОБАВЛЕН", "IMPORTED"); DrawerEscapeHint.Text = T("Esc — закрыть", "Press Esc to close"); OpenSelectedPackageLocationButton.Content = T("Показать файл", "Open location"); ClosePackageDetailsButton.ToolTip = T("Закрыть сведения", "Close details");
        CatalogTitle.Text = T("Каталог", "Catalog"); CatalogSubtitle.Text = T("Пакеты с явными правилами установки и предупреждениями.", "Packages with explicit install policies and warnings.");
        RefreshCatalogButton.Content = CatalogEmptyRefreshButton.Content = T("Обновить", "Refresh"); CatalogEmptyText.Text = T("Пакеты не найдены", "No packages found"); CatalogStateText.Text = T("Обновите каталог или измените фильтры.", "Refresh the catalog or change the filters."); CatalogLoadingText.Text = T("Загрузка каталога…", "Loading catalog…");
        CatalogAllItem.Content = T("Все", "All"); CatalogModsItem.Content = T("Weave-моды", "Weave mods"); CatalogAgentsItem.Content = T("Java-агенты", "Java agents");
        CatalogNameSortItem.Content = T("По названию", "Name"); CatalogUpdatedSortItem.Content = T("Недавно обновлённые", "Recently updated"); CatalogDownloadsSortItem.Content = T("Загрузки", "Downloads"); CatalogInstallsSortItem.Content = T("Установки Moonrise", "Moonrise installs");
        CatalogCompatibilityLabel.Text = T("СОВМЕСТИМОСТЬ", "COMPATIBILITY"); CatalogLicenseLabel.Text = T("ЛИЦЕНЗИЯ", "LICENSE");
        DiagnosticsTitle.Text = T("Диагностика", "Diagnostics"); DiagnosticsSubtitle.Text = T("Локальные события Moonrise.", "Local Moonrise events.");
        CopyDiagnosticsButton.Content = T("Копировать", "Copy"); OpenLogsButton.Content = OpenDiagnosticsLogsButton.Content = T("Открыть логи", "Open logs");
        SettingsTitle.Text = T("Настройки", "Settings"); SettingsSubtitle.Text = T("Изменения применяются автоматически.", "Changes are applied automatically.");
        LaunchSettingsSectionTitle.Text = T("Запуск", "Launch"); PackagesSettingsSectionTitle.Text = T("Пакеты", "Packages"); AppearanceSettingsSectionTitle.Text = T("Оформление и язык", "Appearance and language"); DangerZoneTitle.Text = T("Опасная зона", "Danger zone");
        LauncherPathTitle.Text = "Lunar Launcher"; LauncherPathHint.Text = T("Исполняемый файл лаунчера", "Launcher executable"); BrowseLauncherButton.Content = T("Выбрать", "Browse");
        CloseAfterTitle.Text = T("Закрывать после запуска", "Close after launch"); CloseAfterHint.Text = T("Закрыть Moonrise после запуска игрового процесса.", "Close Moonrise when the game process starts.");
        RevealLunarTitle.Text = T("Показывать Lunar, если требуется действие", "Show Lunar when action is required"); RevealLunarHint.Text = T("Показать официальный лаунчер для входа, обновления или другого действия.", "Reveal the official launcher for authentication, updates, or other interaction.");
        StorageTitle.Text = T("Пакеты Moonrise занимают:", "Moonrise packages use:");
        StorageHint.Text = T("Откройте папку данных Moonrise.", "Open the Moonrise data directory.");
        OpenRootButton.Content = T("Открыть папку", "Open folder");
        ClearTemporaryFilesButton.Content = T("Очистить временные файлы", "Clear temporary files");
        PluginsTitle.Text = T("Плагины клиентов", "Client plugins"); PluginsHint.Text = T("Подключайте новые клиенты через изолированный JSON-протокол запуска.", "Add clients through the isolated JSON launch protocol."); OpenPluginsButton.Content = T("Открыть папку", "Open folder");
        LanguagePacksTitle.Text = T("Языковые пакеты", "Language packs"); LanguagePacksHint.Text = T("Добавьте файл .moonrise-language.json в папку и выберите язык.", "Add a .moonrise-language.json file to the folder and select a language."); OpenLanguagePacksButton.Content = T("Открыть папку", "Open folder");
        ThemesTitle.Text = T("Пакеты тем", "Theme packs"); ThemesHint.Text = T("Встроенные стили и безопасные локальные темы.", "Built-in identities and safe local custom themes.");
        UpdatesTitle.Text = T("Обновления", "Updates"); UpdatesHint.Text = T("Проверять официальные выпуски Moonrise на GitHub. До появления сертификата выпуски не подписаны.", "Check official Moonrise releases on GitHub. Releases are unsigned until a certificate is available."); PrereleaseUpdatesText.Text = T("Предварительные версии", "Prerelease channel"); CheckUpdateButton.Content = T("Проверить", "Check"); InstallUpdateButton.Content = T("Установить и перезапустить", "Install and restart");
        DiagnosticsSettingsTitle.Text = T("Диагностика", "Diagnostics"); DiagnosticsSettingsHint.Text = T("Локальные события запуска и установки.", "Local launch and installation events."); OpenDiagnosticsSettingsButton.Content = OpenDiagnosticsButton.Content = T("Открыть диагностику", "Open diagnostics");
        DeveloperModeTitle.Text = T("Режим разработчика", "Developer mode"); DeveloperModeHint.Text = T("Показать локальную проверку пакетов и инструменты манифеста.", "Show local package inspection and manifest tools.");
        DevelopersTitle.Text = T("Разработчикам", "Developers"); DevelopersSubtitle.Text = T("Проверяйте метаданные локального JAR без декомпиляции.", "Inspect local JAR metadata without decompiling it."); TestCatalogUrlTitle.Text = T("Тестовый каталог", "Custom test catalog"); ResetProductionCatalogButton.Content = T("Основной каталог", "Use production"); InspectJarButton.Content = T("Проверить JAR", "Inspect JAR"); ListClassesButton.Content = T("Список классов", "List classes"); PreviewCardButton.Content = T("Предпросмотр карточки", "Preview card"); ExportManifestButton.Content = T("Экспорт package JSON", "Export package JSON"); OpenSubmissionButton.Content = T("Открыть отправку", "Open submission");
        AboutTaglineText.Text = T("Современный менеджер модов и лаунчер для Lunar Client.", "A modern mod manager and launcher for Lunar Client.");
        AboutDisclaimerText.Text = T(
            "Неофициальный проект. Не связан с Lunar Client, Mojang, Microsoft или Weave.",
            "Unofficial project. Not affiliated with Lunar Client, Mojang, Microsoft, or Weave.");
        DeleteConfirmationTitle.Text = T("Удалить пакет?", "Remove package?");
        DeleteConfirmationSubtitle.Text = T("Файл будет удалён из локального хранилища.", "The file will be deleted from local storage.");
        DeleteConfirmationCancelButton.Content = T("Отмена", "Cancel");
        DeleteConfirmationConfirmButton.Content = T("Удалить", "Remove");
        ShowLunarButton.Content = T("Показать Lunar", "Show Lunar");
        CancelLaunchWaitButton.Content = T("Отменить ожидание", "Cancel waiting");
        OpenCrashReportButton.Content = T("Открыть отчёт", "Open report");
        if (_trayOpenItem is not null) _trayOpenItem.Text = T("Открыть Moonrise", "Open Moonrise");
        if (_trayExitItem is not null) _trayExitItem.Text = T("Выйти", "Exit");
        if (SupportOverlay.Visibility == Visibility.Visible)
            UpdateSupportState();
        RefreshProfileState();
        if (_initialized && Volatile.Read(ref _launchInProgress) == 0 && _lastStatusLevel == StatusLevel.Ready)
            SetStatus(
                T("Готов к запуску", "Ready to launch"),
                StatusLevel.Ready,
                T("Компоненты запуска проверены.", "Launch components verified."),
                T("Готово", "Ready"));
    }

    private string T(string russian, string english) =>
        _activeLanguage?.Translations.TryGetValue(english, out var translation) == true && !string.IsNullOrWhiteSpace(translation)
            ? translation : _russian ? russian : english;

    private void InitializeAppearancePacks()
    {
        var rejectedThemes = new List<string>();
        foreach (var language in _appearancePacks.LoadLanguages(_paths.LanguagePacksDirectory, error => AddDiagnostic($"Language pack: {error}"))) _languageChoices.Add(language);
        foreach (var theme in _themePacks.LoadThemes(_paths.ThemesDirectory, error =>
                 {
                     rejectedThemes.Add(error);
                     AddDiagnostic($"Theme pack rejected: {error}");
                 })) _themeChoices.Add(theme);
        LanguagePackComboBox.ItemsSource = _languageChoices;
        ThemeComboBox.ItemsSource = _themeChoices;
        ThemePreviewList.ItemsSource = _themeChoices;
        _activeLanguage = _languageChoices.FirstOrDefault(pack => string.Equals(pack.Code, _languageCode, StringComparison.OrdinalIgnoreCase))
            ?? _languageChoices.First(pack => string.Equals(pack.Code, "en", StringComparison.OrdinalIgnoreCase));
        _languageCode = _activeLanguage.Code;
        _russian = string.Equals(_languageCode, "ru", StringComparison.OrdinalIgnoreCase);
        LanguagePackComboBox.SelectedItem = _activeLanguage;
        var selectedTheme = _themeChoices.FirstOrDefault(theme => string.Equals(theme.Id, _settings.Theme, StringComparison.OrdinalIgnoreCase))
            ?? _themeChoices[0];
        ThemeComboBox.SelectedItem = selectedTheme;
        ThemePreviewList.SelectedItem = selectedTheme;
        if (rejectedThemes.Count > 0)
            ShowToast(ToastKind.Warning, T("Некорректная тема пропущена", "Invalid theme skipped"),
                T("Moonrise Standard оставлена активной; подробности в диагностике.", "Moonrise Standard was kept; see diagnostics for details."));
    }

    private async Task ApplySupportVisualQaStateAsync(string state)
    {
        var crypto = state.StartsWith("support-crypto-", StringComparison.Ordinal);
        _supportFlow?.Dispose();
        _supportFlow = new SupportFlowController(new VisualQaSupportApiClient(
            state switch
            {
                "support-success" or "support-crypto-success" => "paid",
                "support-expired" => "expired",
                "support-failure" => "failed",
                _ => "pending"
            }, crypto));
        _supportFlow.StateChanged += SupportFlow_StateChanged;
        _supportDialogCancellation?.Dispose();
        _supportDialogCancellation = new CancellationTokenSource();
        _supportShowingMethods = state is "support-methods" or "support-crypto-methods";
        _renderedSupportCheckoutUrl = null;
        _lastSupportDiagnosticKey = null;
        SupportQrImage.Source = null;
        SupportTermsCheckBox.IsChecked = false;
        SupportOverlay.Visibility = Visibility.Visible;
        SupportOverlay.IsHitTestVisible = true;
        await _supportFlow.LoadAsync(_supportDialogCancellation.Token);

        if (_supportShowingMethods)
            return;

        _supportFlow.SelectMethod(crypto ? "direct_crypto" : "telegram_stars");

        if (state is "support-amount" or "support-custom" or "support-crypto-amount")
        {
            UpdateSupportState();
            SupportCustomAmountTextBox.Text = crypto ? "5" : state == "support-amount" ? "100" : "5000";
            SupportTermsCheckBox.IsChecked = true;
            _ = Dispatcher.BeginInvoke(
                new Action(() => Keyboard.Focus(SupportCustomAmountTextBox)),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        await _supportFlow.BeginCheckoutAsync(
            crypto ? 1 : state == "support-success" ? 100 : 50,
            _russian ? "ru" : "en",
            _supportDialogCancellation.Token);
        if (crypto && _supportFlow.Method?.Assets?.FirstOrDefault() is { } asset)
            await _supportFlow.BeginDirectCryptoCheckoutAsync(
                asset,
                _supportDialogCancellation.Token);
        if (state is ("support-success" or "support-crypto-success" or "support-expired" or "support-failure") &&
            _supportFlow.ActivePollingTask is { } polling)
            await polling;
    }

    private void LanguagePackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagePackComboBox.SelectedItem is not LanguagePack language) return;
        _activeLanguage = language;
        _languageCode = language.Code;
        _russian = string.Equals(language.Code, "ru", StringComparison.OrdinalIgnoreCase);
        _settings.Language = language.Code;
        if (_initialized)
        {
            ApplyLanguage();
            RefreshCounts();
            RefreshVisiblePackages();
            if (!_visualQaMode)
                SaveSettings();
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is not ThemePack theme) return;
        if (ThemePreviewList.SelectedItem != theme)
            ThemePreviewList.SelectedItem = theme;
        _settings.Theme = theme.Id;
        ApplyTheme();
        if (_initialized && !_visualQaMode) SaveSettings();
    }

    private void ThemePreviewList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemePreviewList.SelectedItem is not ThemePack theme || ThemeComboBox.SelectedItem == theme)
            return;
        ThemeComboBox.SelectedItem = theme;
    }

    private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) < double.Epsilon && Math.Abs(e.HorizontalChange) < double.Epsilon)
            return;

        LanguagePackComboBox.IsDropDownOpen = false;
        ThemeComboBox.IsDropDownOpen = false;
    }

    private void ApplyTheme()
    {
        if (ThemeComboBox.SelectedItem is not ThemePack theme) return;
        try
        {
            _themeService.Apply(Application.Current.Resources, theme,
                diagnostic =>
                {
                    AddDiagnostic(diagnostic);
                    ShowToast(ToastKind.Warning, T("Тема не перезагружена", "Theme reload rejected"), diagnostic);
                },
                reloaded =>
                {
                    AddDiagnostic($"Theme hot reloaded: {reloaded.Id}");
                    ShowToast(ToastKind.Success, T("Тема обновлена", "Theme reloaded"), reloaded.Name);
                });
            _settings.Theme = _themeService.CurrentThemeId;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AddDiagnostic($"Theme '{theme.Id}' rejected: {exception.Message}");
            var fallback = _themeChoices.First(item => item.Id == "standard");
            _themeService.Apply(Application.Current.Resources, fallback);
            _settings.Theme = fallback.Id;
            if (ThemeComboBox.SelectedItem != fallback) ThemeComboBox.SelectedItem = fallback;
            ShowToast(ToastKind.Warning, T("Тема отклонена", "Theme rejected"), T("Используется Moonrise Standard.", "Moonrise Standard was restored."));
        }
    }

    private void ReloadThemesButton_Click(object sender, RoutedEventArgs e) => ReloadThemes();

    private void ReloadThemes()
    {
        var selectedId = _settings.Theme;
        _themeChoices.Clear();
        foreach (var theme in _themePacks.LoadThemes(_paths.ThemesDirectory, error => AddDiagnostic($"Theme pack rejected: {error}")))
            _themeChoices.Add(theme);
        var selected = _themeChoices.FirstOrDefault(theme => string.Equals(theme.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                       ?? _themeChoices.First(theme => theme.Id == "standard");
        ThemeComboBox.SelectedItem = selected;
        ThemePreviewList.SelectedItem = selected;
    }

    private void CreateThemeTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = _themePacks.CreateTemplate(_paths.ThemesDirectory);
            ReloadThemes();
            OpenFolder(directory);
            ShowToast(ToastKind.Success, T("Шаблон темы создан", "Theme template created"), Path.GetFileName(directory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError(exception.Message);
        }
    }

    private void ImportThemeButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = T("Выберите папку темы с theme.json", "Select a theme folder containing theme.json"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        try
        {
            var imported = _themePacks.ImportDirectory(dialog.SelectedPath, _paths.ThemesDirectory);
            ReloadThemes();
            ThemeComboBox.SelectedItem = _themeChoices.First(theme => theme.Id == imported.Id);
            ShowToast(ToastKind.Success, T("Тема импортирована", "Theme imported"), imported.Name);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowError(exception.Message);
        }
    }

    private string FormatPackageCount(int count)
    {
        if (!_russian) return $"{count} {(count == 1 ? "package" : "packages")}";

        var lastTwoDigits = Math.Abs(count) % 100;
        var lastDigit = lastTwoDigits % 10;
        var noun = lastTwoDigits is >= 11 and <= 14
            ? "пакетов"
            : lastDigit switch
            {
                1 => "пакет",
                2 or 3 or 4 => "пакета",
                _ => "пакетов"
            };
        return $"{count} {noun}";
    }

    private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        _settings.MinecraftVersion = SelectedVersion;
        RefreshProfileState();
        SaveSettings();
    }

    private void VersionComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        var comboBottom = VersionComboBox
            .TranslatePoint(new Point(0, VersionComboBox.ActualHeight), LaunchProfileCard)
            .Y;
        var availableHeight = LaunchProfileCard.ActualHeight
            - comboBottom
            - LaunchProfileCard.Padding.Bottom
            - LaunchProfileCard.BorderThickness.Bottom;
        VersionComboBox.MaxDropDownHeight = Math.Max(36, Math.Floor(availableHeight));
    }

    private void ClientComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || SelectedClientChoice is null) return;
        _settings.Client = SelectedClient;
        PopulateVersions();
        SelectSavedVersion();
        ClientHint.Text = SelectedClientChoice.IsBuiltIn
            ? T("Официальный профиль лаунчера.", "Official launcher profile.")
            : T("Запуск через установленный плагин клиента.", "Launch through an installed client plugin.");
        RefreshProfileState();
        SaveSettings();
    }

    private void PackageKindButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshVisiblePackages();
        UpdateOpenPackageFolderButton();
    }
    private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (_initialized) RefreshVisiblePackages(); }
    private void OpenLibraryButton_Click(object sender, RoutedEventArgs e) => ShowPage(LibraryPage, LibraryTabButton);
    private void ModsLoadoutCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ModsKindButton.IsChecked = true;
        AgentsKindButton.IsChecked = false;
        RefreshVisiblePackages();
        UpdateOpenPackageFolderButton();
        ShowPage(LibraryPage, LibraryTabButton);
    }

    private void AgentsLoadoutCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ModsKindButton.IsChecked = false;
        AgentsKindButton.IsChecked = true;
        RefreshVisiblePackages();
        UpdateOpenPackageFolderButton();
        ShowPage(LibraryPage, LibraryTabButton);
    }

    private void SaveLoadoutButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = _loadouts.Create(
            T($"Набор {DateTime.Now:dd.MM HH:mm}", $"Loadout {DateTime.Now:yyyy-MM-dd HH:mm}"),
            SelectedClient,
            SelectedVersion,
            _mods.Concat(_agents));
        _loadouts.Save(profile, _paths.ProfilesDirectory);
        _settings.ActiveLoadoutId = profile.Id;
        LoadSavedLoadouts(profile.Id);
        SaveSettings();
        AddDiagnostic($"Loadout saved: {profile.Name}; packages={profile.Packages.Count}");
    }

    private void LoadoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || LoadoutComboBox.SelectedItem is not ModpackProfile profile) return;
        _settings.ActiveLoadoutId = profile.Id;
        var version = VersionComboBox.Items.Cast<string>().FirstOrDefault(item =>
            string.Equals(item, profile.GameVersion, StringComparison.OrdinalIgnoreCase));
        if (version is not null) VersionComboBox.SelectedItem = version;
        var matched = _loadouts.Apply(profile, _mods.Concat(_agents));
        RefreshVisiblePackages();
        RefreshCounts();
        SaveSettings();
        AddDiagnostic($"Loadout applied: {profile.Name}; matched={matched.Count}/{profile.Packages.Count(item => item.Enabled)}");
    }

    private void DeleteLoadoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (LoadoutComboBox.SelectedItem is not ModpackProfile profile) return;
        var path = Path.GetFullPath(Path.Combine(_paths.ProfilesDirectory, $"{profile.Id}.moonrise.json"));
        var root = Path.GetFullPath(_paths.ProfilesDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(path)) File.Delete(path);
        _settings.ActiveLoadoutId = string.Empty;
        LoadSavedLoadouts();
        SaveSettings();
    }

    private void ExportLoadoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (LoadoutComboBox.SelectedItem is not ModpackProfile profile) return;
        var dialog = new SaveFileDialog
        {
            Filter = "Moonrise loadout (*.moonrise.json)|*.moonrise.json",
            FileName = $"{profile.Name}.moonrise.json"
        };
        if (dialog.ShowDialog(this) == true) _loadouts.Export(profile, dialog.FileName);
    }

    private void ImportLoadoutButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Moonrise loadout (*.moonrise.json)|*.moonrise.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var profile = _loadouts.Load(dialog.FileName);
            _loadouts.Save(profile, _paths.ProfilesDirectory);
            _settings.ActiveLoadoutId = profile.Id;
            LoadSavedLoadouts(profile.Id);
            SaveSettings();
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }
    private void HomeTabButton_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage, HomeTabButton);
    private void LibraryTabButton_Click(object sender, RoutedEventArgs e) => ShowPage(LibraryPage, LibraryTabButton);
    private void CatalogTabButton_Click(object sender, RoutedEventArgs e) => ShowPage(CatalogPage, CatalogTabButton);
    private void SettingsTabButton_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, SettingsTabButton);
    private void DevelopersTabButton_Click(object sender, RoutedEventArgs e) => ShowPage(DevelopersPage, DevelopersTabButton);

    private void ShowPage(FrameworkElement page, System.Windows.Controls.RadioButton? tab)
    {
        foreach (var item in new FrameworkElement[] { HomePage, LibraryPage, CatalogPage, DiagnosticsPage, SettingsPage, DevelopersPage })
        {
            item.BeginAnimation(OpacityProperty, null);
            item.Visibility = item == page ? Visibility.Visible : Visibility.Collapsed;
        }
        if (tab is not null)
            tab.IsChecked = true;
        _motion.EnterPage(page);
    }

    private async void RefreshCatalogButton_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync(forceRefresh: true);

    private void LoadCachedCatalog()
    {
        _catalogPackages.Clear();
        RefreshCatalogView();
        CatalogStateText.Text = T("Обновите каталог, чтобы загрузить актуальные данные.", "Refresh the catalog to load current data.");
    }

    private async Task LoadCatalogAsync(bool forceRefresh)
    {
        try
        {
            RefreshCatalogButton.IsEnabled = false;
            CatalogLoadingPanel.Visibility = Visibility.Visible;
            CatalogEmptyPanel.Visibility = Visibility.Collapsed;
            CatalogErrorPanel.Visibility = Visibility.Collapsed;
            CatalogList.Visibility = Visibility.Collapsed;
            if (!_motion.ReducedMotion)
                CatalogLoadingPanel.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(1050))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    }, HandoffBehavior.SnapshotAndReplace);
            using var httpClient = new HttpClient();
            var configuredUrl = _settings.DeveloperMode && !string.IsNullOrWhiteSpace(_settings.CustomTestCatalogUrl)
                ? _settings.CustomTestCatalogUrl : _settings.CatalogUrl;
            var client = new CatalogClient(
                httpClient,
                _catalogManifests,
                new Uri(configuredUrl, UriKind.Absolute),
                _paths.CatalogCacheDirectory,
                CatalogTrust.ProductionEd25519PublicKeyBase64,
                allowUnsignedCatalog: _settings.DeveloperMode);
            var result = await client.GetCatalogAsync(forceRefresh);
            _catalogPackages.Clear();
            _catalogPackages.AddRange(result.Catalog.Packages.Where(package =>
                !string.Equals(package.Name, "Neutral Weave Demo", StringComparison.OrdinalIgnoreCase)));
            RefreshCatalogView();
            CatalogErrorPanel.Visibility = Visibility.Collapsed;
            CatalogStateText.Text = result.IsOffline
                ? T("Сеть недоступна — показаны кешированные данные.", "Network unavailable — showing cached data.")
                : T("Каталог обновлён.", "Catalog updated.");
            AddDiagnostic($"Catalog loaded: entries={_catalogPackages.Count}; offline={result.IsOffline}");
        }
        catch (Exception exception)
        {
            _catalogPackages.Clear();
            RefreshCatalogView();
            CatalogEmptyPanel.Visibility = Visibility.Collapsed;
            CatalogErrorPanel.Visibility = Visibility.Visible;
            CatalogErrorTitle.Text = T("Каталог недоступен", "Catalog unavailable");
            CatalogErrorText.Text = T("Проверьте подключение и повторите попытку.", "Check your connection and try again.");
            CatalogErrorRetryButton.Content = T("Повторить", "Try again");
            AddDiagnostic($"Catalog error: {exception.Message}");
        }
        finally
        {
            CatalogLoadingPanel.BeginAnimation(OpacityProperty, null);
            CatalogLoadingPanel.Opacity = 1;
            CatalogLoadingPanel.Visibility = Visibility.Collapsed;
            RefreshCatalogButton.IsEnabled = true;
        }
    }

    private void CatalogFilter_Changed(object sender, RoutedEventArgs e) { if (_initialized) RefreshCatalogView(); }

    private void RefreshCatalogView()
    {
        var search = CatalogSearchBox.Text.Trim();
        IEnumerable<PackageManifest> query = _catalogPackages.Where(package => string.IsNullOrWhiteSpace(search) ||
            package.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || package.Summary.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            package.Authors.Any(author => author.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));
        query = CatalogKindComboBox.SelectedIndex switch { 1 => query.Where(item => item.Type == PackageKind.WeaveMod), 2 => query.Where(item => item.Type == PackageKind.JavaAgent), _ => query };
        query = CatalogSortComboBox.SelectedIndex switch
        {
            1 => query.OrderByDescending(item => item.LatestRelease?.PublishedAt),
            2 => query.OrderByDescending(item => item.Statistics?.Downloads ?? 0),
            3 => query.OrderByDescending(item => item.Statistics?.MoonriseInstalls ?? 0),
            _ => query.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        _catalogEntries.Clear();
        foreach (var package in query) _catalogEntries.Add(package);
        CatalogEmptyPanel.Visibility = _catalogEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CatalogEmptyText.Visibility = _catalogEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CatalogList.Visibility = _catalogEntries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void InstallCatalogPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PackageManifest package || package.LatestRelease is not { } release) return;
        if (package.InstallPolicy == PackageInstallPolicy.ConfirmationRequired &&
            !ConfirmCatalogInstall(package, fromDeepLink: false))
            return;
        await InstallCatalogPackageAsync(package, release, sender as Button);
    }

    private bool ConfirmCatalogInstall(PackageManifest package, bool fromDeepLink)
    {
        var warningEntry = package.Warnings.FirstOrDefault();
        var warning = warningEntry?.LocalizedMessage?.GetValueOrDefault(_russian ? "ru" : "en")
            ?? warningEntry?.Message;
        var message = fromDeepLink
            ? T(
                $"Ссылка запрашивает установку «{package.Name}». Проверьте карточку пакета и подтвердите установку.",
                $"This link requests installation of “{package.Name}”. Review the package card and confirm installation.")
            : T(
                "Этот пакет требует явного подтверждения. Правила сервера могут запрещать его функции.",
                "This package requires explicit confirmation. Server rules may prohibit its behavior.");
        if (!string.IsNullOrWhiteSpace(warning))
            message += Environment.NewLine + Environment.NewLine + warning;
        return MessageBox.Show(
            this,
            message,
            T("Подтверждение установки", "Confirm installation"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private async Task InstallCatalogPackageAsync(PackageManifest package, PackageRelease release, Button? button)
    {
        try
        {
            if (button is not null)
                button.IsEnabled = false;
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var installed = await new ManagedPackageInstaller(httpClient, _jarParser).InstallAsync(package, release, _paths.ManagedPackagesDirectory, _mods.Concat(_agents));
            _library.Import(installed, "catalog");
            AddDiagnostic($"Installed catalog package: {Path.GetFileName(installed)}");
            LoadPackages();
            SetStatus(T("Пакет установлен", "Package installed"), StatusLevel.Ready);
            ShowToast(ToastKind.Success, T("Пакет установлен", "Package installed"), package.Name);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            if (button is not null)
                button.IsEnabled = true;
        }
    }

    private async Task HandleDeepLinkAsync(string value)
    {
        if (!MoonriseDeepLink.TryParse(value, out var deepLink, out var error))
        {
            ShowError(T($"Недопустимая ссылка Moonrise: {error}", $"Invalid Moonrise link: {error}"));
            return;
        }

        ShowPage(CatalogPage, CatalogTabButton);
        if (deepLink!.Action == MoonriseDeepLinkAction.Catalog)
            return;

        var package = _catalogPackages.FirstOrDefault(item =>
            string.Equals(item.Id, deepLink.PackageId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Slug, deepLink.PackageId, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            ShowError(T("Пакет из ссылки не найден в проверенном каталоге.", "The linked package was not found in the verified catalog."));
            return;
        }

        CatalogSearchBox.Text = string.Empty;
        CatalogKindComboBox.SelectedIndex = 0;
        RefreshCatalogView();
        CatalogList.SelectedItem = package;
        CatalogList.ScrollIntoView(package);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        if (deepLink.Action != MoonriseDeepLinkAction.Install ||
            package.LatestRelease is not { } release ||
            !package.CanInstall)
            return;
        if (!ConfirmCatalogInstall(package, fromDeepLink: true))
            return;
        await InstallCatalogPackageAsync(package, release, null);
    }

    private void AddPackageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Java archives (*.jar)|*.jar", Multiselect = true, Title = T("Добавить пакет в Moonrise", "Add a package to Moonrise") };
        if (dialog.ShowDialog(this) != true) return;
        ImportFiles(dialog.FileNames);
    }

    private void RemovePackageButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PackageInfo package) return;
        _pendingDeletePackage = package;
        OpenConfirmationDialog(
            T("Удалить пакет?", "Remove package?"),
            T("Локальный JAR-файл будет удалён.", "The local JAR will be deleted."),
            package.FileName,
            T("Удалить", "Remove"),
            RemovePendingPackage);
    }

    private void DeleteConfirmationCancelButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingDeletePackage = null;
        _pendingConfirmationAction = null;
        CloseConfirmationDialog();
    }

    private void DeleteConfirmationConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _pendingConfirmationAction;
        _pendingConfirmationAction = null;
        CloseConfirmationDialog();
        action?.Invoke();
    }

    private void RemovePendingPackage()
    {
        var package = _pendingDeletePackage;
        _pendingDeletePackage = null;
        if (package is null) return;
        try
        {
            if (_activeLaunchPackageIds.Contains(package.PackageId))
                throw new InvalidOperationException(T(
                    "Пакет используется активным запуском.",
                    "The package is part of an active launch."));
            _library.Remove(package);
            AddDiagnostic($"Removed {package.FileName}");
            LoadPackages();
            RefreshStorageSummary();
            SaveSettings();
            ShowToast(ToastKind.Success, T("Пакет удалён", "Package removed"), package.DisplayName);
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private void OpenConfirmationDialog(string title, string subtitle, string detail, string confirmText, Action? action)
    {
        _pendingConfirmationAction = action;
        DeleteConfirmationTitle.Text = title;
        DeleteConfirmationSubtitle.Text = subtitle;
        DeleteConfirmationFileName.Text = detail;
        DeleteConfirmationConfirmButton.Content = confirmText;
        DeleteConfirmationConfirmButton.IsEnabled = action is not null;
        DeleteConfirmationOverlay.Visibility = Visibility.Visible;
        DeleteConfirmationOverlay.IsHitTestVisible = true;
        DeleteConfirmationOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
        if (!_motion.ReducedMotion && DeleteDialogPanel.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } },
                HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } },
                HandoffBehavior.SnapshotAndReplace);
        }
        DeleteConfirmationCancelButton.Focus();
    }

    private void CloseConfirmationDialog()
    {
        DeleteConfirmationOverlay.IsHitTestVisible = false;
        var animation = new DoubleAnimation(DeleteConfirmationOverlay.Opacity, 0, TimeSpan.FromMilliseconds(MotionController.FastMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            DeleteConfirmationOverlay.Visibility = Visibility.Collapsed;
            DeleteConfirmationOverlay.Opacity = 1;
            DeleteConfirmationConfirmButton.IsEnabled = true;
        };
        DeleteConfirmationOverlay.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void PackagesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDrawer(PackageDetailsDrawer, PackagesList.SelectedItem is not null, 16);

    private void CatalogList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDrawer(CatalogDetailsDrawer, CatalogList.SelectedItem is not null, 12);

    private void UpdateDrawer(FrameworkElement drawer, bool open, double offset)
    {
        if (open)
            _motion.ShowOverlay(drawer, offset, MotionController.DrawerMilliseconds);
        else if (drawer.Visibility == Visibility.Visible)
            _motion.HideOverlay(drawer, offset, MotionController.NormalMilliseconds, () => { });
    }

    private void PackageEnabled_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PackageInfo package)
            return;
        try
        {
            _library.SetEnabled(package, package.IsEnabled);
            RefreshCounts();
        }
        catch (Exception exception)
        {
            package.IsEnabled = false;
            ShowError(T(
                "Неклассифицированный пакет нельзя запустить.",
                exception.Message));
        }
    }

    private void OpenPackageFolderButton_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(GetSelectedPackageFolder());

    private void ClosePackageDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        PackagesList.SelectedItem = null;
        PackagesList.Focus();
    }

    private void OpenSelectedPackageLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesList.SelectedItem is not PackageInfo package)
            return;
        RevealPackageLocation(package);
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e) => OpenFolder(_paths.LogsDirectory);
    private void OpenRootButton_Click(object sender, RoutedEventArgs e) => OpenFolder(_paths.PackagesDirectory);
    private void OpenPluginsButton_Click(object sender, RoutedEventArgs e) => OpenFolder(_paths.PluginsDirectory);
    private void OpenLanguagePacksButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _appearancePacks.WriteLanguageExamples(_paths.LanguagePacksDirectory);
            OpenFolder(_paths.LanguagePacksDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError(exception.Message);
        }
    }
    private void OpenThemesButton_Click(object sender, RoutedEventArgs e) => OpenFolder(_paths.ThemesDirectory);
    private void DiscordButton_Click(object sender, RoutedEventArgs e) => OpenExternalUrl("https://discord.gg/RMZysft2g9");
    private void GitHubButton_Click(object sender, RoutedEventArgs e) => OpenExternalUrl("https://github.com/ZOONGG/Moonrise");
    private async void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        if (SupportOverlay.Visibility == Visibility.Visible)
            return;

        _supportShowingMethods = true;
        _renderedSupportCheckoutUrl = null;
        SupportCustomAmountTextBox.Text = string.Empty;
        SupportTermsCheckBox.IsChecked = false;
        SupportQrImage.Source = null;
        _supportDialogCancellation?.Cancel();
        _supportDialogCancellation?.Dispose();
        _supportDialogCancellation = new CancellationTokenSource();

        SupportOverlay.Visibility = Visibility.Visible;
        SupportOverlay.IsHitTestVisible = true;
        SupportOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
        if (!_motion.ReducedMotion && SupportDialogPanel.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(.98, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds)),
                HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(.98, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds)),
                HandoffBehavior.SnapshotAndReplace);
        }

        ShowOnlySupportPanel(SupportLoadingPanel);
        SupportCloseButton.Focus();
        var configuration = SupportApiConfiguration.Load();
        if (!configuration.IsConfigured)
        {
            ShowSupportTerminal(
                T("Поддержка пока недоступна", "Support is not available yet"),
                T("Сервис поддержки не настроен в этой сборке.", configuration.Error ?? "The support service is not configured in this build."),
                canCreateNew: false);
            return;
        }

        if (_supportFlow is null)
        {
            _supportHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _supportFlow = new SupportFlowController(new SupportApiClient(_supportHttpClient, configuration.BaseUri!));
            _supportFlow.StateChanged += SupportFlow_StateChanged;
        }

        await _supportFlow.LoadAsync(_supportDialogCancellation.Token);
    }

    private void SupportFlow_StateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ApplySupportStateChange);
            return;
        }
        ApplySupportStateChange();
    }

    private void ApplySupportStateChange()
    {
        if (_supportFlow is null)
            return;
        var diagnosticKey = $"{_supportFlow.State}|{_supportFlow.StatusPollCount}|{_supportFlow.LastErrorCode}";
        if (!string.Equals(_lastSupportDiagnosticKey, diagnosticKey, StringComparison.Ordinal))
        {
            _lastSupportDiagnosticKey = diagnosticKey;
            AddDiagnostic(
                $"Support flow state: {_supportFlow.State}; polls={_supportFlow.StatusPollCount}; error={_supportFlow.LastErrorCode ?? "none"}.");
        }
        UpdateSupportState();
    }

    private void UpdateSupportState()
    {
        if (SupportOverlay.Visibility != Visibility.Visible || _supportFlow is null)
            return;

        switch (_supportFlow.State)
        {
            case SupportFlowState.LoadingMethods:
                ShowOnlySupportPanel(SupportLoadingPanel);
                break;
            case SupportFlowState.ChoosingAmount:
                if (_supportShowingMethods)
                {
                    RenderSupportMethods();
                    ShowOnlySupportPanel(SupportMethodsPanel);
                    (SupportTelegramMethodButton.IsEnabled ? SupportTelegramMethodButton : SupportCryptoMethodButton).Focus();
                }
                else
                {
                    RenderSupportAmounts();
                    ShowOnlySupportPanel(SupportAmountPanel);
                    SupportPresetPanel.Children.OfType<RadioButton>().FirstOrDefault()?.Focus();
                }
                break;
            case SupportFlowState.ChoosingAsset:
                RenderSupportAssets();
                ShowOnlySupportPanel(SupportAssetPanel);
                break;
            case SupportFlowState.CreatingCheckout:
                ClearRenderedSupportCheckout();
                ShowOnlySupportPanel(SupportAmountPanel);
                SetSupportAmountControlsEnabled(false);
                SupportContinueButton.Content = T("Создание платежа…", "Creating payment…");
                break;
            case SupportFlowState.Waiting:
            case SupportFlowState.ConnectivityIssue:
                if (!_supportFlow.TryGetActiveCheckout(out var activeCheckout) || activeCheckout is null)
                    break;
                RenderSupportCheckout(activeCheckout);
                ShowOnlySupportPanel(SupportCheckoutPanel);
                SupportWaitingIndicator.Visibility = Visibility.Visible;
                SupportWaitingProgress.IsIndeterminate = !_motion.ReducedMotion;
                SupportWaitingText.Text = _supportFlow.State == SupportFlowState.ConnectivityIssue
                    ? T("Связь прервана. Повторяем попытку…", "Connection interrupted. Retrying…")
                    : T("Ожидаем оплату", "Waiting for payment");
                break;
            case SupportFlowState.Paid:
                RenderSupportSuccess();
                ShowOnlySupportPanel(SupportSuccessPanel);
                SupportDoneButton.Focus();
                break;
            case SupportFlowState.Expired:
                ShowSupportTerminal(
                    T("Срок платёжной сессии истёк.", "This payment session expired."),
                    T("Создайте новый платёж, чтобы продолжить.", "Create a new payment to continue."),
                    canCreateNew: true);
                break;
            case SupportFlowState.Refunded:
                ShowSupportTerminal(
                    T("Платёж возвращён.", "This payment was refunded."),
                    T("Эта платёжная сессия завершена.", "This payment session is complete."),
                    canCreateNew: true);
                break;
            case SupportFlowState.Failed:
                ShowSupportFailure(_supportFlow.LastErrorCode);
                break;
        }
    }

    private void RenderSupportMethods()
    {
        var stars = _supportFlow?.Methods.FirstOrDefault(item =>
            string.Equals(item.Id, "telegram_stars", StringComparison.OrdinalIgnoreCase));
        var crypto = _supportFlow?.Methods.FirstOrDefault(item =>
            string.Equals(item.Id, "direct_crypto", StringComparison.OrdinalIgnoreCase));
        SupportTelegramMethodButton.IsEnabled = stars?.Enabled == true;
        SupportTelegramMethodHint.Text = stars?.Enabled == true
            ? T("Только в Telegram", "Telegram only")
            : T("Сейчас недоступно", "Currently unavailable");
        SupportCryptoMethodButton.IsEnabled = crypto?.Enabled == true;
        SupportCryptoHint.Text = crypto?.Enabled == true
            ? T("Оплата из любого криптокошелька", "Pay from any crypto wallet")
            : T("Сейчас недоступно", "Currently unavailable");
    }

    private void RenderSupportAssets()
    {
        SupportChooseAssetTitle.Text = T("Выберите криптовалюту", "Choose an asset");
        SupportChooseAssetHint.Text = T("Сеть указана под названием монеты.", "The network is shown under each asset.");
        SupportBackToAmountButton.Content = T("Назад", "Back");
        SupportAssetButtons.Children.Clear();
        SupportAssetButtons.RowDefinitions.Clear();
        SupportAssetButtons.ColumnDefinitions.Clear();
        for (var column = 0; column < 3; column++)
            SupportAssetButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var assets = (_supportFlow?.Method?.Assets ?? []).ToArray();
        var rows = Math.Max(1, (assets.Length + 2) / 3);
        for (var row = 0; row < rows; row++)
            SupportAssetButtons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < assets.Length; index++)
        {
            var asset = assets[index];
            var row = index / 3;
            var column = index % 3;

            var icon = new Image
            {
                Source = SupportAssetIcon(asset.Asset),
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var button = new Button
            {
                Tag = asset,
                Height = 72,
                Margin = new Thickness(5),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)FindResource("ButtonBase"),
                Content = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(42) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    Children =
                    {
                        new Border
                        {
                            Width = 36,
                            Height = 36,
                            CornerRadius = new CornerRadius(18),
                            Background = (Brush)FindResource("SurfaceSecondary"),
                            Child = icon
                        },
                        new StackPanel
                        {
                            Margin = new Thickness(8, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new TextBlock { Text = asset.Asset, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = (Brush)FindResource("TextPrimary") },
                                new TextBlock { Text = asset.NetworkName, FontSize = 10, Foreground = (Brush)FindResource("TextMuted"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }
                            }
                        }
                    }
                }
            };
            Grid.SetColumn(((Grid)button.Content).Children[1], 1);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            System.Windows.Automation.AutomationProperties.SetName(button, $"{asset.DisplayName}, {asset.NetworkName}");
            button.Click += SupportAssetButton_Click;
            SupportAssetButtons.Children.Add(button);
        }
    }

    private ImageSource SupportAssetIcon(string asset) =>
        (ImageSource)FindResource(asset.ToUpperInvariant() switch
        {
            "BTC" => "BitcoinIcon",
            "ETH" => "EthereumIcon",
            "USDT" => "TetherIcon",
            "USDC" => "UsdcIcon",
            "BNB" => "BnbIcon",
            "SOL" => "SolanaIcon",
            "TRX" => "TronIcon",
            "DAI" => "DaiIcon",
            "LINK" => "ChainlinkIcon",
            _ => "CryptoMethodIcon"
        });

    private void RenderSupportAmounts()
    {
        var method = _supportFlow?.Method;
        if (method is null)
            return;
        SetSupportAmountControlsEnabled(true);
        SupportContinueButton.Content = T("Продолжить", "Continue");
        SupportCustomAmountTextBox.Tag = method.Id;
        SupportCustomAmountTextBox.IsEnabled = method.Custom.Enabled;
        SupportPresetPanel.Children.Clear();
        foreach (var amount in method.Presets)
        {
            var button = new RadioButton
            {
                Tag = amount,
                Content = CreateSupportAmountContent(amount),
                GroupName = "SupportAmount",
                Style = (Style)FindResource("SupportAmountSelector"),
                Margin = new Thickness(0, 0, 10, 0)
            };
            System.Windows.Automation.AutomationProperties.SetName(button, FormatSupportAmount(amount));
            button.Checked += SupportPresetButton_Checked;
            SupportPresetPanel.Children.Add(button);
        }
        RefreshSupportAmountSelection();
    }

    private void SupportTelegramMethodButton_Click(object sender, RoutedEventArgs e)
    {
        if (_supportFlow?.SelectMethod("telegram_stars") != true)
            return;
        _supportShowingMethods = false;
        UpdateSupportState();
    }

    private void SupportCryptoMethodButton_Click(object sender, RoutedEventArgs e)
    {
        if (_supportFlow?.SelectMethod("direct_crypto") != true)
            return;
        _supportShowingMethods = false;
        UpdateSupportState();
    }

    private void SupportBackToAmountButton_Click(object sender, RoutedEventArgs e) => _supportFlow?.ChangeAmount();

    private async void SupportAssetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SupportAsset asset } || _supportFlow is null)
            return;
        await _supportFlow.BeginDirectCryptoCheckoutAsync(asset, _supportDialogCancellation?.Token ?? default);
    }

    private void SupportBackToMethodsButton_Click(object sender, RoutedEventArgs e)
    {
        _supportShowingMethods = true;
        UpdateSupportState();
    }

    private void SupportPresetButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: int amount } || _supportFlow is null ||
            !_supportFlow.TryGetPresetInput(amount, SupportAmountCulture, out var value))
            return;
        SupportCustomAmountTextBox.Text = value;
        SupportCustomAmountTextBox.CaretIndex = value.Length;
    }

    private void SupportCustomAmountTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        RefreshSupportAmountSelection();

    private void SupportCustomAmountTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        NormalizeSupportCustomAmount();

    private void SupportCustomAmountTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            NormalizeSupportCustomAmount();
    }

    private void SupportCustomAmountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshSupportAmountSelection();

    private void SupportTermsCheckBox_Changed(object sender, RoutedEventArgs e) => RefreshSupportAmountSelection();

    private void RefreshSupportAmountSelection()
    {
        if (SupportContinueButton is null || _supportFlow?.Method is not { } method)
            return;
        var hasInput = !string.IsNullOrWhiteSpace(SupportCustomAmountTextBox.Text);
        var valid = _supportFlow.TryResolveAmountSelection(
            SupportCustomAmountTextBox.Text,
            SupportAmountCulture,
            out var amount,
            out var selectedPreset);
        foreach (var button in SupportPresetPanel.Children.OfType<RadioButton>())
        {
            var buttonAmount = (int)button.Tag;
            var selected = selectedPreset == buttonAmount;
            button.IsChecked = selected;
            System.Windows.Automation.AutomationProperties.SetItemStatus(
                button,
                selected ? T("Выбрано", "Selected") : T("Не выбрано", "Not selected"));
        }

        System.Windows.Automation.AutomationProperties.SetItemStatus(
            SupportCustomAmountTextBox,
            valid ? T("Допустимая сумма", "Valid amount") : T("Введите сумму", "Enter an amount"));
        SupportAmountValidationText.Text = !hasInput
            ? string.Empty
            : valid
                ? string.Empty
                : T("Введите сумму числом.", "Enter a numeric amount.");
        SupportContinueButton.IsEnabled = valid && SupportTermsCheckBox.IsChecked == true && method.Enabled;
        if (valid)
            System.Windows.Automation.AutomationProperties.SetHelpText(SupportContinueButton, FormatSupportAmount(amount));
    }

    private async void SupportContinueButton_Click(object sender, RoutedEventArgs e)
    {
        NormalizeSupportCustomAmount();
        if (_supportFlow is null ||
            !_supportFlow.TryNormalizeAmount(
                SupportCustomAmountTextBox.Text,
                SupportAmountCulture,
                out var amount) ||
            SupportTermsCheckBox.IsChecked != true)
        {
            RefreshSupportAmountSelection();
            return;
        }
        await _supportFlow.BeginCheckoutAsync(amount, _russian ? "ru" : "en", _supportDialogCancellation?.Token ?? default);
    }

    private void RenderSupportCheckout(SupportCheckoutView checkout)
    {
        var crypto = IsCryptoSupportMethod(checkout.Provider);
        var selectedAsset = crypto
            ? _supportFlow?.Method?.Assets?.FirstOrDefault(item =>
                string.Equals(item.Asset, checkout.Asset, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Network, checkout.Network, StringComparison.OrdinalIgnoreCase))
            : null;
        SupportCheckoutMethodText.Text = crypto
            ? selectedAsset?.DisplayName ?? checkout.Asset ?? T("Криптовалюта", "Crypto")
            : T("Звёзды Telegram", "Telegram Stars");
        SupportCheckoutAmountText.Text = crypto
            ? $"{checkout.CryptoAmount} {checkout.Asset}"
            : checkout.Amount.ToString("N0", SupportAmountCulture);
        SupportCheckoutStarsIcon.Visibility = crypto ? Visibility.Collapsed : Visibility.Visible;
        SupportQrFrame.Width = crypto ? 220 : 248;
        SupportQrFrame.Height = crypto ? 220 : 248;
        SupportQrImage.MaxWidth = crypto ? 192 : 220;
        SupportQrImage.MaxHeight = crypto ? 192 : 220;
        SupportOpenTelegramButton.Height = 50;
        SupportOpenTelegramButton.Margin = new Thickness(0, 18, 0, 0);
        SupportOpenTelegramButton.Content = T("Открыть Telegram", "Open Telegram");
        SupportOpenTelegramButton.Visibility = crypto ? Visibility.Collapsed : Visibility.Visible;
        SupportOpenTelegramButton.IsEnabled = !crypto;
        SupportOpenTelegramHint.Visibility = crypto ? Visibility.Collapsed : Visibility.Visible;
        SupportOpenTelegramHint.Text = T(
            "Безопасно продолжите оплату в Telegram.",
            "Continue securely in Telegram.");
        SupportScanText.Text = crypto
            ? T(
                "QR содержит адрес кошелька. Отправьте сумму, указанную выше.",
                "The QR contains the wallet address. Send the amount shown above.")
            : T("Отсканируйте телефоном", "Scan with your phone");
        SupportCryptoDetailsPanel.Visibility = crypto ? Visibility.Visible : Visibility.Collapsed;
        SupportAddressLabel.Text = T("Адрес кошелька", "Wallet address");
        SupportCopyAddressButton.ToolTip = T("Копировать адрес", "Copy address");
        System.Windows.Automation.AutomationProperties.SetName(
            SupportCopyAddressButton,
            T("Копировать адрес кошелька", "Copy wallet address"));
        SupportAddressText.Text = checkout.WalletAddress ?? string.Empty;
        SupportAddressText.ToolTip = checkout.WalletAddress;
        SupportNetworkWarningText.Text = T(
            $"Сеть: {checkout.NetworkName ?? checkout.Network}. Отправляйте средства только в этой сети.",
            $"Network: {checkout.NetworkName ?? checkout.Network}. Send funds only on this network.");
        // Direct-crypto QR codes intentionally contain only the receive address.
        // Payment URIs remain available for explicit wallet-opening flows, but many
        // exchange/wallet scanners reject BIP-21/EIP-681 payloads while accepting
        // a plain address.
        var qrPayload = crypto ? checkout.WalletAddress : checkout.CheckoutUrl;
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            SupportQrImage.Source = null;
            return;
        }
        if (string.Equals(_renderedSupportCheckoutUrl, qrPayload, StringComparison.Ordinal))
            return;
        try
        {
            var png = SupportQrCodeService.CreatePng(checkout.Provider, qrPayload);
            using var stream = new MemoryStream(png, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            SupportQrImage.Source = image;
            _renderedSupportCheckoutUrl = qrPayload;
        }
        catch (InvalidDataException)
        {
            SupportQrImage.Source = null;
            SupportOpenTelegramButton.IsEnabled = false;
        }
    }

    private void SupportOpenTelegramButton_Click(object sender, RoutedEventArgs e)
    {
        if (_supportFlow?.TryGetActiveCheckout(out var activeCheckout) != true || activeCheckout is null)
            return;
        var checkoutUrl = activeCheckout.CheckoutUrl;
        if (string.IsNullOrWhiteSpace(checkoutUrl))
            return;
        if (!CheckoutUrlValidator.TryValidateCheckout(activeCheckout.Provider, checkoutUrl, out _))
        {
            ShowToast(
                ToastKind.Error,
                T("Не удалось открыть оплату", "Could not open checkout"),
                T("Сервис вернул небезопасную ссылку.", "The service returned an unsafe checkout link."));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = checkoutUrl, UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ShowToast(
                ToastKind.Error,
                T("Не удалось открыть оплату", "Could not open checkout"),
                T("Повторите попытку позже.", "Please try again later."));
        }
    }

    private async void SupportCopyAddressButton_Click(object sender, RoutedEventArgs e)
    {
        if (_supportFlow?.Checkout?.WalletAddress is not { Length: > 0 } value)
            return;
        try
        {
            Clipboard.SetText(value);
            await ShowSupportCopyToastAsync(T("Адрес скопирован", "Address copied"));
        }
        catch (ExternalException)
        {
            SupportCopyToast.Visibility = Visibility.Collapsed;
            // The full address remains visible even if the clipboard is temporarily busy.
        }
    }

    private async Task ShowSupportCopyToastAsync(string message)
    {
        var generation = ++_supportCopyToastGeneration;
        SupportCopyToastText.Text = message;
        SupportCopyToast.Visibility = Visibility.Visible;
        await Task.Delay(1400);
        if (generation == _supportCopyToastGeneration)
            SupportCopyToast.Visibility = Visibility.Collapsed;
    }

    private void RenderSupportSuccess()
    {
        if (_supportFlow?.Checkout is { } checkout)
        {
            var crypto = IsCryptoSupportMethod(checkout.Provider);
            SupportSuccessStarsIcon.Visibility = crypto ? Visibility.Collapsed : Visibility.Visible;
            SupportThankYouStarsIcon.Visibility = crypto ? Visibility.Collapsed : Visibility.Visible;
            SupportThankYouAmountText.Text = crypto
                ? $"${checkout.Amount.ToString("0.##", SupportAmountCulture)}"
                : checkout.Amount.ToString("N0", SupportAmountCulture);
        }
        if (_motion.ReducedMotion)
            return;
        SupportSuccessPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
        if (SupportSuccessVisual.RenderTransform is ScaleTransform scale)
        {
            var ease = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(.88, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds + 80)) { EasingFunction = ease },
                HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(.88, 1, TimeSpan.FromMilliseconds(MotionController.ModalMilliseconds + 80)) { EasingFunction = ease },
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void ShowSupportFailure(string? code)
    {
        var text = code switch
        {
            "PROVIDER_NOT_CONFIGURED" or "METHOD_UNAVAILABLE" => T("Этот способ оплаты сейчас недоступен.", "This payment method is currently unavailable."),
            "UNSAFE_CHECKOUT_URL" => T("Сервис вернул небезопасную ссылку. Оплата не была открыта.", "The service returned an unsafe link. Checkout was not opened."),
            "NETWORK_UNAVAILABLE" => T("Не удалось подключиться к сервису поддержки.", "Could not connect to the support service."),
            "PAYMENT_NOT_FOUND" => T("Платёжная сессия больше недоступна.", "The payment session is no longer available."),
            _ => T("Не удалось продолжить платёж. Попробуйте ещё раз позже.", "Could not continue the payment. Please try again later.")
        };
        ShowSupportTerminal(T("Что-то пошло не так", "Something went wrong"), text, _supportFlow?.Method is not null);
    }

    private void ShowSupportTerminal(string title, string text, bool canCreateNew)
    {
        SupportTerminalTitle.Text = title;
        SupportTerminalText.Text = text;
        SupportCreateNewButton.Visibility = canCreateNew ? Visibility.Visible : Visibility.Collapsed;
        SupportCreateNewButton.Content = _supportFlow?.Method is null
            ? T("Повторить", "Retry")
            : T("Создать новый платёж", "Create new payment");
        ShowOnlySupportPanel(SupportTerminalPanel);
        if (canCreateNew)
            SupportCreateNewButton.Focus();
    }

    private async void SupportCreateNewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_supportFlow?.Method is null)
        {
            ShowOnlySupportPanel(SupportLoadingPanel);
            if (_supportFlow is not null)
                await _supportFlow.LoadAsync(_supportDialogCancellation?.Token ?? default);
            return;
        }
        SupportCustomAmountTextBox.Text = string.Empty;
        SupportTermsCheckBox.IsChecked = false;
        ClearRenderedSupportCheckout();
        _supportShowingMethods = false;
        _supportFlow.ChangeAmount();
    }

    private void SupportCloseButton_Click(object sender, RoutedEventArgs e) => CloseSupportDialog();

    private void CloseSupportDialog()
    {
        _supportDialogCancellation?.Cancel();
        _supportDialogCancellation?.Dispose();
        _supportDialogCancellation = null;
        _supportFlow?.Close();
        SupportQrImage.Source = null;
        _supportCopyToastGeneration++;
        SupportCopyToast.Visibility = Visibility.Collapsed;
        _renderedSupportCheckoutUrl = null;
        SupportOverlay.IsHitTestVisible = false;
        var animation = new DoubleAnimation(SupportOverlay.Opacity, 0, TimeSpan.FromMilliseconds(MotionController.FastMilliseconds));
        animation.Completed += (_, _) =>
        {
            SupportOverlay.Visibility = Visibility.Collapsed;
            SupportOverlay.Opacity = 1;
            SupportButton.Focus();
        };
        SupportOverlay.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ShowOnlySupportPanel(FrameworkElement panel)
    {
        foreach (var item in new FrameworkElement[]
                 {
                      SupportLoadingPanel, SupportMethodsPanel, SupportAmountPanel, SupportAssetPanel, SupportCheckoutPanel,
                     SupportSuccessPanel, SupportTerminalPanel
                 })
            item.Visibility = ReferenceEquals(item, panel) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearRenderedSupportCheckout()
    {
        _renderedSupportCheckoutUrl = null;
        SupportQrImage.Source = null;
        _supportCopyToastGeneration++;
        SupportCopyToast.Visibility = Visibility.Collapsed;
        SupportOpenTelegramButton.IsEnabled = false;
        SupportOpenTelegramButton.Visibility = Visibility.Collapsed;
        SupportOpenTelegramHint.Visibility = Visibility.Collapsed;
        SupportCryptoDetailsPanel.Visibility = Visibility.Collapsed;
    }

    private void SetSupportAmountControlsEnabled(bool enabled)
    {
        SupportPresetPanel.IsEnabled = enabled;
        SupportCustomAmountTextBox.IsEnabled = enabled && _supportFlow?.Method?.Custom.Enabled == true;
        SupportTermsCheckBox.IsEnabled = enabled;
        if (!enabled)
            SupportContinueButton.IsEnabled = false;
    }

    private string FormatSupportAmount(decimal amount) =>
        IsCryptoSupportMethod(_supportFlow?.Method?.Id)
            ? $"${amount.ToString("0.##", SupportAmountCulture)}"
            : $"{amount:N0} Stars";

    private FrameworkElement CreateSupportAmountContent(int amount)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = IsCryptoSupportMethod(_supportFlow?.Method?.Id)
                ? $"${amount:N0}"
                : amount.ToString("N0", SupportAmountCulture),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!IsCryptoSupportMethod(_supportFlow?.Method?.Id))
            panel.Children.Add(new Image
        {
            Source = (ImageSource)FindResource("TelegramStarsIcon"),
            Width = 21,
            Height = 21,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static bool IsCryptoSupportMethod(string? methodId) =>
        string.Equals(methodId, "direct_crypto", StringComparison.OrdinalIgnoreCase);

    private CultureInfo SupportAmountCulture
    {
        get
        {
            try
            {
                return CultureInfo.GetCultureInfo(_languageCode);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.CurrentCulture;
            }
        }
    }

    private void NormalizeSupportCustomAmount()
    {
        if (_supportFlow is null ||
            !_supportFlow.TryNormalizeAmount(
                SupportCustomAmountTextBox.Text,
                SupportAmountCulture,
                out var amount))
            return;

        var normalized = amount.ToString(SupportAmountCulture);
        if (!string.Equals(SupportCustomAmountTextBox.Text, normalized, StringComparison.Ordinal))
        {
            SupportCustomAmountTextBox.Text = normalized;
            SupportCustomAmountTextBox.CaretIndex = normalized.Length;
        }
        RefreshSupportAmountSelection();
    }

    private void YouTubeButton_Click(object sender, RoutedEventArgs e) => OpenExternalUrl("https://www.youtube.com/@MythicHypixel");

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", ArgumentList = { Path.GetFullPath(path) }, UseShellExecute = true });
    }

    private void RevealPackageLocation(PackageInfo package)
    {
        if (!File.Exists(package.FullPath))
        {
            ShowError(T("Управляемый файл пакета отсутствует.", "The managed package file is missing."));
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { "/select,", package.FullPath },
            UseShellExecute = true
        });
    }

    private string GetSelectedPackageFolder() =>
        ModsKindButton.IsChecked == true
            ? _paths.WeavePackagesDirectory
            : AgentsKindButton.IsChecked == true
                ? _paths.AgentPackagesDirectory
                : UnclassifiedKindButton.IsChecked == true
                    ? _paths.UnclassifiedPackagesDirectory
                    : _paths.PackagesDirectory;

    private void UpdateOpenPackageFolderButton()
    {
        if (OpenFolderButton is null)
            return;
        OpenFolderButton.Content = ModsKindButton.IsChecked == true
            ? T("Открыть папку Weave-модов", "Open Weave mods folder")
            : AgentsKindButton.IsChecked == true
                ? T("Открыть папку Java-агентов", "Open Java agents folder")
                : UnclassifiedKindButton.IsChecked == true
                    ? T("Открыть папку без типа", "Open unclassified folder")
                    : T("Открыть папку пакетов", "Open package folder");
    }

    private void ImportFiles(IEnumerable<string> paths)
    {
        var summary = _library.ImportMany(paths);
        foreach (var result in summary.Results)
        {
            if (result.Status == PackageImportStatus.Imported && result.Package is not null)
            {
                AddDiagnostic($"Imported {result.Package.OriginalFileName}; SHA-256={result.Package.Sha256}");
                ResolveAmbiguousImport(result.Package);
            }
            else if (result.Status != PackageImportStatus.AlreadyImported)
            {
                AddDiagnostic($"Import failed for {Path.GetFileName(result.SourcePath)}: {result.Message}");
            }
        }
        LoadPackages();
        RefreshStorageSummary();
        ShowImportSummary(summary);
    }

    private void ResolveAmbiguousImport(PackageInfo imported)
    {
        var package = _library.Packages.FirstOrDefault(item =>
            string.Equals(item.PackageId, imported.PackageId, StringComparison.OrdinalIgnoreCase));
        if (package?.Kind != PackageKind.Ambiguous)
            return;

        var choice = MessageBox.Show(
            this,
            T(
                $"«{package.OriginalFileName}» содержит и weave.mod.json, и Java-agent manifest.\n\nДа — Weave-мод\nНет — Java-агент\nОтмена — оставить отключённым.",
                $"“{package.OriginalFileName}” contains both weave.mod.json and a Java-agent manifest.\n\nYes — Weave mod\nNo — Java agent\nCancel — keep it disabled."),
            T("Выберите тип пакета", "Select package type"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes)
            _library.SelectType(package, PackageKind.WeaveMod, _settings.DeveloperMode);
        else if (choice == MessageBoxResult.No)
            _library.SelectType(package, PackageKind.JavaAgent, _settings.DeveloperMode);
    }

    private void ShowImportSummary(PackageImportSummary summary)
    {
        var message = T(
            $"Импортировано: {summary.Imported}\nУже было: {summary.AlreadyPresent}\nНедопустимых: {summary.Invalid}\nОшибок: {summary.Failed}",
            $"Imported: {summary.Imported}\nAlready present: {summary.AlreadyPresent}\nInvalid: {summary.Invalid}\nFailed: {summary.Failed}");
        var firstError = summary.Results.FirstOrDefault(item =>
            item.Status is PackageImportStatus.Invalid or PackageImportStatus.Failed);
        if (firstError is not null)
            message += Environment.NewLine + LocalizePackageError(firstError.Message);
        SetStatus(
            summary.Invalid + summary.Failed == 0
                ? T("Импорт завершён", "Import complete")
                : T("Импорт завершён с ошибками", "Import completed with errors"),
            summary.Invalid + summary.Failed == 0 ? StatusLevel.Ready : StatusLevel.Error,
            message);
        if (summary.Imported > 0 && summary.Invalid + summary.Failed == 0)
            ShowToast(ToastKind.Success, T("Пакет импортирован", "Package imported"), summary.Imported == 1 ? string.Empty : FormatPackageCount(summary.Imported));
        else if (summary.Invalid + summary.Failed > 0)
            ShowToast(ToastKind.Warning, T("Импорт завершён с ошибками", "Import completed with errors"), message);
    }

    private async Task ScanPackageFoldersAsync(bool showSummary)
    {
        var summary = await _library.ScanCategoryFoldersAsync(TimeSpan.FromMilliseconds(750));
        var reconciliation = _library.LastReconciliationResult;
        PackageImportResult? firstNewFailure = null;
        foreach (var result in summary.Results.Where(item =>
                     item.Status is PackageImportStatus.Invalid or PackageImportStatus.Failed))
        {
            var signature = $"{result.SourcePath}|{SafeFileSignature(result.SourcePath)}|{result.Message}";
            if (_reportedIncomingFailures.Add(signature))
            {
                AddDiagnostic($"Incoming import failed for {Path.GetFileName(result.SourcePath)}: {result.Message}");
                firstNewFailure ??= result;
            }
        }
        foreach (var result in summary.Results.Where(item => item.Status == PackageImportStatus.Imported && item.Package is not null))
        {
            ResolveAmbiguousImport(result.Package!);
        }
        LoadPackages();
        RefreshStorageSummary();
        if (reconciliation is { Removed: > 0 })
        {
            AddDiagnostic($"Package reconciliation removed {reconciliation.Removed} stale package entr{(reconciliation.Removed == 1 ? "y" : "ies")}.");
            SetStatus(
                T("Пакет удалён из библиотеки", "Package removed from Library"),
                StatusLevel.Ready,
                T(
                    "Пакет удалён из библиотеки, потому что его JAR-файл отсутствует.",
                    "The package was removed from the Library because its JAR file is missing."));
        }
        else if (summary.Results.Any(result =>
                 result.Status == PackageImportStatus.Imported &&
                 result.Message.Contains("detected category", StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus(
                T("Пакет перемещён в подходящую папку", "Package moved to the matching folder"),
                StatusLevel.Ready,
                T(
                    "Тип пакета определён автоматически; индекс обновлён.",
                    "The package type was detected automatically and the index was updated."));
        }
        else if (showSummary || summary.Imported > 0)
            ShowImportSummary(summary);
        else if (firstNewFailure is not null)
        {
            SetStatus(
                T("Не удалось добавить JAR", "JAR import failed"),
                StatusLevel.Error,
                $"{Path.GetFileName(firstNewFailure.SourcePath)}: {LocalizePackageError(firstNewFailure.Message)}");
        }
    }

    private static string SafeFileSignature(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return "missing";
        }
    }

    private void InitializePackageWatchers()
    {
        foreach (var watcher in _packageWatchers)
            watcher.Dispose();
        _packageWatchers.Clear();
        foreach (var directory in _library.CategoryDirectories)
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            watcher.Created += PackageFolderChanged;
            watcher.Changed += PackageFolderChanged;
            watcher.Renamed += PackageFolderChanged;
            watcher.Deleted += PackageFolderChanged;
            _packageWatchers.Add(watcher);
        }
    }

    private void PackageFolderChanged(object sender, FileSystemEventArgs e)
    {
        _packageScanCancellation?.Cancel();
        _packageScanCancellation?.Dispose();
        _packageScanCancellation = new CancellationTokenSource();
        var token = _packageScanCancellation.Token;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(1000, token);
                await ScanPackageFoldersAsync(showSummary: false);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void Library_DragEnter(object sender, DragEventArgs e)
    {
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        var valid = paths.Length > 0 && paths.All(path =>
            File.Exists(path) && string.Equals(Path.GetExtension(path), ".jar", StringComparison.OrdinalIgnoreCase));
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        ConfigureDropTarget(valid);
        ShowDropTarget();
        e.Handled = true;
    }

    private void Library_DragLeave(object sender, DragEventArgs e) => HideDropTarget();

    private void Library_Drop(object sender, DragEventArgs e)
    {
        HideDropTarget();
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        if (paths.Length > 0 && paths.All(path => File.Exists(path) && string.Equals(Path.GetExtension(path), ".jar", StringComparison.OrdinalIgnoreCase)))
            ImportFiles(paths);
        else
            ShowToast(ToastKind.Warning, T("Неподдерживаемый файл", "Unsupported file"), T("Перетащите один или несколько JAR-файлов.", "Drop one or more JAR files."), autoDismiss: false);
        e.Handled = true;
    }

    private void ConfigureDropTarget(bool valid)
    {
        DropTargetText.Text = valid
            ? T("Перетащите JAR-файлы сюда", "Drop JAR files here")
            : T("Поддерживаются только JAR-файлы", "Only JAR files are supported");
        DropTargetHint.Text = valid
            ? T("Тип пакета будет определён автоматически.", "Package type is detected automatically.")
            : T("Папки и другие типы файлов не будут импортированы.", "Folders and other file types will not be imported.");
        DropTargetOutline.Stroke = (Brush)FindResource(valid ? "AccentPurple" : "Danger");
        DropTargetIconFrame.BorderBrush = (Brush)FindResource(valid ? "BorderStrong" : "Danger");
    }

    private void ShowDropTarget()
    {
        DropTargetOverlay.Visibility = Visibility.Visible;
        DropTargetOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(_motion.ReducedMotion ? MotionController.FastMilliseconds : MotionController.NormalMilliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
    }

    private void HideDropTarget()
    {
        if (DropTargetOverlay.Visibility != Visibility.Visible)
            return;
        var animation = new DoubleAnimation(DropTargetOverlay.Opacity, 0, TimeSpan.FromMilliseconds(MotionController.FastMilliseconds));
        animation.Completed += (_, _) =>
        {
            DropTargetOverlay.Visibility = Visibility.Collapsed;
            DropTargetOverlay.Opacity = 1;
        };
        DropTargetOverlay.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void VerifyPackageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PackageInfo package)
            return;
        try
        {
            var valid = _library.VerifyIntegrity(package);
            LoadPackages();
            MessageBox.Show(
                this,
                valid
                    ? T("SHA-256 совпадает. Пакет не изменён.", "SHA-256 matches. The package is unchanged.")
                    : T("Проверка целостности не пройдена. Пакет отключён.", "Integrity check failed. The package was disabled."),
                T("Проверка целостности", "Integrity check"),
                MessageBoxButton.OK,
                valid ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void RevealPackageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PackageInfo package)
            return;
        RevealPackageLocation(package);
    }

    private void PackageDetailsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PackageInfo package)
            return;
        MessageBox.Show(
            this,
            T(
                $"Исходное имя: {package.OriginalFileName}\nТип: {LocalizedPackageType(package.Kind)}\nSHA-256: {package.Sha256}\nСовместимость: {package.CompatibilityStatus}\nИсточник: {package.SourceType}",
                $"Original filename: {package.OriginalFileName}\nType: {LocalizedPackageType(package.Kind)}\nSHA-256: {package.Sha256}\nCompatibility: {package.CompatibilityStatus}\nSource: {package.SourceType}"),
            package.DisplayName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SelectPackageTypeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PackageInfo package)
            return;
        if (package.Kind == PackageKind.Unclassified && !_settings.DeveloperMode)
        {
            ShowError(T(
                "Ручной выбор типа доступен только в режиме разработчика.",
                "Manual type selection is available only in Developer mode."));
            return;
        }
        var choice = MessageBox.Show(
            this,
            T("Да — Weave-мод\nНет — Java-агент", "Yes — Weave mod\nNo — Java agent"),
            T("Выберите тип пакета", "Select package type"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        try
        {
            if (choice == MessageBoxResult.Yes)
                _library.SelectType(package, PackageKind.WeaveMod, _settings.DeveloperMode);
            else if (choice == MessageBoxResult.No)
                _library.SelectType(package, PackageKind.JavaAgent, _settings.DeveloperMode);
            LoadPackages();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private string LocalizedPackageType(PackageKind kind) => kind switch
    {
        PackageKind.WeaveMod => T("Weave-мод", "Weave mod"),
        PackageKind.JavaAgent => T("Java-агент", "Java agent"),
        PackageKind.Ambiguous => T("Неоднозначный", "Ambiguous"),
        _ => T("Неклассифицированный", "Unclassified")
    };

    private string LocalizePackageError(string message)
    {
        if (!_russian)
            return message;
        if (message.Contains("already imported", StringComparison.OrdinalIgnoreCase))
            return "Пакет уже импортирован.";
        if (message.Contains("weave.mod.json is malformed", StringComparison.OrdinalIgnoreCase))
            return "Файл weave.mod.json повреждён.";
        if (message.Contains("still in progress", StringComparison.OrdinalIgnoreCase))
            return "Копирование файла ещё не завершено.";
        if (message.Contains("managed package file is missing", StringComparison.OrdinalIgnoreCase))
            return "Управляемый файл пакета отсутствует.";
        if (message.Contains("integrity", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("hash", StringComparison.OrdinalIgnoreCase))
            return "Проверка целостности пакета не пройдена.";
        if (message.Contains("Only .jar", StringComparison.OrdinalIgnoreCase))
            return "Поддерживаются только JAR-файлы.";
        if (message.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("archive", StringComparison.OrdinalIgnoreCase))
            return "JAR-файл недопустим.";
        return "Не удалось импортировать пакет: " + message;
    }

    public string PackageDetailsMenuText => T("Сведения о пакете", "Package details");
    public string VerifyIntegrityMenuText => T("Проверить целостность", "Verify integrity");
    public string RevealManagedFileMenuText => T("Показать управляемый файл", "Reveal managed file");
    public string SelectPackageTypeMenuText => T("Выбрать тип (расширенно)", "Select package type (advanced)");
    public string CatalogInstallText => T("Установить", "Install");
    public string DeletePackageText => T("Удалить", "Delete");
    public string WeaveTypeText => T("Weave-мод", "Weave mod");
    public string AgentTypeText => T("Java-агент", "Java agent");
    public string UnclassifiedTypeText => T("Без типа", "Unclassified");

    private static void OpenExternalUrl(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    private void BrowseLauncherButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Lunar Client (Lunar Client.exe)|Lunar Client.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            LauncherPathTextBox.Text = dialog.FileName;
            SaveSettings();
        }
    }

    private void LauncherPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized && LauncherPathTextBox.IsKeyboardFocusWithin) SaveSettings();
    }

    private void CloseAfterLaunchCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.CloseAfterLaunch = CloseAfterLaunchCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void RevealLunarCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.RevealLunarWhenActionRequired = RevealLunarCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void ClearTemporaryFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (Volatile.Read(ref _launchInProgress) == 1)
        {
            ShowError(T(
                "Дождитесь завершения текущего запуска.",
                "Wait for the current launch attempt to finish."));
            return;
        }
        OpenConfirmationDialog(
            T("Очистить временные файлы?", "Clear temporary files?"),
            T("Пользовательские JAR-файлы не будут затронуты.", "User JAR files will not be touched."),
            T("Кеш запусков и устаревшие временные данные", "Launch cache and stale temporary data"),
            T("Очистить", "Clear"),
            ClearTemporaryFilesConfirmed);
    }

    private void ClearTemporaryFilesConfirmed()
    {
        try
        {
            var result = _storage.ClearTemporaryFiles(DateTimeOffset.Now);
            RefreshStorageSummary();
            SetStatus(
                T("Временные файлы очищены", "Temporary files cleared"),
                StatusLevel.Ready,
                T(
                    $"Удалено файлов: {result.FilesDeleted}, папок: {result.DirectoriesDeleted}.",
                    $"Deleted files: {result.FilesDeleted}; folders: {result.DirectoriesDeleted}."));
            AddDiagnostic(
                $"Temporary cleanup: files={result.FilesDeleted}; directories={result.DirectoriesDeleted}; bytes={result.BytesFreed}");
            ShowToast(ToastKind.Success, T("Временные файлы очищены", "Temporary files cleared"));
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Temporary cleanup failed: {exception.Message}");
            ShowError(exception.Message);
        }
    }

    private void RefreshStorageSummary()
    {
        if (StorageSizeText is null)
            return;
        StorageSizeText.Text = FormatBytes(_storage.CalculatePackageStorageSize());
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    private void CheckForUpdatesCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void PrereleaseUpdatesCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.IncludePrereleaseUpdates = PrereleaseUpdatesCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void DeveloperModeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.DeveloperMode = DeveloperModeCheckBox.IsChecked == true;
        DevelopersTabButton.Visibility = _settings.DeveloperMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.DeveloperMode && DevelopersPage.Visibility == Visibility.Visible) ShowPage(SettingsPage, SettingsTabButton);
        SaveSettings();
    }

    private void CustomCatalogUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || !CustomCatalogUrlTextBox.IsKeyboardFocusWithin) return;
        _settings.CustomTestCatalogUrl = CustomCatalogUrlTextBox.Text.Trim();
        SaveSettings();
    }

    private async void ResetProductionCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        CustomCatalogUrlTextBox.Text = string.Empty;
        _settings.CustomTestCatalogUrl = string.Empty;
        SaveSettings();
        await LoadCatalogAsync(forceRefresh: true);
    }

    private void OpenDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(DiagnosticsPage, null);
    }

    private void InspectJarButton_Click(object sender, RoutedEventArgs e) => InspectDeveloperJar(includeClasses: false);
    private void ListClassesButton_Click(object sender, RoutedEventArgs e) => InspectDeveloperJar(includeClasses: true);

    private void InspectDeveloperJar(bool includeClasses)
    {
        var dialog = new OpenFileDialog { Filter = "Java archives (*.jar)|*.jar", Title = T("Проверить локальный JAR", "Inspect a local JAR") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _developerJarPath = dialog.FileName;
            _developerInspection = _developerInspector.Inspect(dialog.FileName, includeClasses);
            _developerDraft = _developerInspector.CreateDraft(_developerInspection);
            DeveloperOutputTextBox.Text = $"{_developerInspection.FileName}\n{_developerInspection.Kind}\nSHA-256: {_developerInspection.Sha256}\nJava: {_developerInspection.JavaClassVersion?.ToString() ?? "—"}\n\nweave.mod.json:\n{_developerInspection.WeaveMetadata}\n\nMANIFEST.MF:\n{_developerInspection.Manifest}\n\nEntry points:\n{string.Join(Environment.NewLine, _developerInspection.EntryPoints)}\n\nMixin configs:\n{string.Join(Environment.NewLine, _developerInspection.MixinConfigs)}\n\nHooks:\n{string.Join(Environment.NewLine, _developerInspection.Hooks)}\n\nClasses:\n{string.Join(Environment.NewLine, _developerInspection.Classes)}";
            ExportManifestButton.IsEnabled = true;
            PreviewCardButton.IsEnabled = true;
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private void PreviewCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_developerDraft is null) return;
        DeveloperOutputTextBox.Text = $"{_developerDraft.Name}\n{_developerDraft.AuthorLabel}\n{_developerDraft.Summary}\n{_developerDraft.Type} · {_developerDraft.VersionLabel}\n{T("Исходный код — JAR не опубликован", "Source only — no JAR published")}";
    }

    private void ExportManifestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_developerDraft is null) return;
        var dialog = new SaveFileDialog { Filter = "Moonrise package (*.json)|*.json", FileName = $"{_developerDraft.Slug}.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, _developerInspector.ExportDraft(_developerDraft));
    }

    private void OpenSubmissionButton_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUrl("https://github.com/ZOONGG/Moonrise/issues/new?title=Catalog%20submission");

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(showNoUpdateStatus: true);

    private async Task CheckForUpdatesAsync(bool showNoUpdateStatus)
    {
        try
        {
            CheckUpdateButton.IsEnabled = false;
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0);
            _availableUpdate = await _applicationUpdates.CheckAsync(
                current,
                httpClient,
                _settings.IncludePrereleaseUpdates);
            InstallUpdateButton.Visibility = _availableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateStatusText.Text = _availableUpdate is null
                ? T("Установлена актуальная версия.", "Moonrise is up to date.")
                : T($"Доступна версия {_availableUpdate.Version}.", $"Version {_availableUpdate.Version} is available.");
            if (showNoUpdateStatus || _availableUpdate is not null)
                SetStatus(UpdateStatusText.Text, StatusLevel.Ready);
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Update check warning: {exception.Message}");
            if (showNoUpdateStatus) ShowError(exception.Message);
        }
        finally { CheckUpdateButton.IsEnabled = true; }
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        var releaseNotes = string.IsNullOrWhiteSpace(_availableUpdate.ReleaseNotes)
            ? T("Примечания к выпуску отсутствуют.", "No release notes were provided.")
            : _availableUpdate.ReleaseNotes.Trim();
        if (releaseNotes.Length > 2500)
            releaseNotes = releaseNotes[..2500] + "…";
        var confirmation = T(
            $"Будет загружен официальный установщик Moonrise {_availableUpdate.Version} с GitHub и проверен по SHA-256.\n\nДо появления сертификата выпуски Moonrise не подписаны. Windows может показать предупреждение.\n\n{releaseNotes}\n\nПродолжить?",
            $"Moonrise {_availableUpdate.Version} will download the official Setup from GitHub and verify its SHA-256 checksum.\n\nMoonrise releases are unsigned until a signing certificate is available. Windows may display a warning.\n\n{releaseNotes}\n\nContinue?");
        if (MessageBox.Show(
                this,
                confirmation,
                T("Обновление Moonrise", "Moonrise update"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) != MessageBoxResult.Yes)
            return;
        try
        {
            InstallUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = T("Загрузка и проверка SHA-256…", "Downloading and verifying SHA-256…");
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var staged = await _applicationUpdates.StageAsync(_availableUpdate, httpClient, _paths.UpdateDirectory);
            if (!staged.HasTrustedAuthenticodeSignature &&
                MessageBox.Show(
                    this,
                    T(
                        "Контрольная сумма установщика верна, но цифровая подпись Authenticode отсутствует. Запустить проверенный установщик?",
                        "The installer checksum is valid, but no trusted Authenticode signature is present. Run the verified installer?"),
                    T("Неподписанный выпуск", "Unsigned release"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            _applicationUpdates.StartInstaller(staged);
            Close();
        }
        catch (Exception exception) { ShowError(exception.Message); }
        finally { InstallUpdateButton.IsEnabled = true; }
    }

    private void SaveSettings()
    {
        if (LauncherPathTextBox is null) return;
        _settings.Language = _languageCode;
        if (ThemeComboBox.SelectedItem is ThemePack theme) _settings.Theme = theme.Id;
        _settings.Client = SelectedClient;
        _settings.MinecraftVersion = SelectedVersion;
        _settings.LauncherExecutablePath = LauncherPathTextBox.Text.Trim();
        _settings.CloseAfterLaunch = CloseAfterLaunchCheckBox.IsChecked == true;
        _settings.SafeLaunch = false;
        _settings.BackgroundLunarLaunch = true;
        _settings.RevealLunarWhenActionRequired = RevealLunarCheckBox.IsChecked == true;
        _settings.LaunchTimeoutSeconds = Math.Clamp(_settings.LaunchTimeoutSeconds, 30, 600);
        _settings.CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true;
        _settings.IncludePrereleaseUpdates = PrereleaseUpdatesCheckBox.IsChecked == true;
        _settings.UpdateCatalogAutomatically = false;
        _settings.DeveloperMode = DeveloperModeCheckBox.IsChecked == true;
        _settings.CustomTestCatalogUrl = CustomCatalogUrlTextBox.Text.Trim();
        _settings.DisabledMods = _mods.Where(item => !item.IsEnabled).Select(item => item.FileName).ToList();
        _settings.DisabledAgents = _agents.Where(item => !item.IsEnabled).Select(item => item.FileName).ToList();
        _settingsService.Save(_settings);
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _launchInProgress, 1, 0) != 0) return;

        var launchStartedUtc = DateTimeOffset.UtcNow;
        var monitorOwnsLaunchGate = false;
        LaunchSession? launchSession = null;
        SanitizedLaunchReport? launchReport = null;
        LauncherProfileSelection? profileSelection = null;
        PackageLaunchSelection selection = new([], []);
        string? weaveHash = null;
        var weaveLoaderPath = _paths.WeaveAgentPath;
        var weaveLoaderRelease = WeaveAgentService.Current;
        string? legacyWeaveAdapterPath = null;
        string? pendingPackageFailureStage = null;
        _launchWaitCancelledByUser = false;
        CrashActionsPanel.Visibility = Visibility.Collapsed;
        OpenDiagnosticsButton.Visibility = Visibility.Collapsed;
        _lastCrashBundlePath = null;
        _lastCrashReportPath = null;
        LaunchButton.IsEnabled = false;
        LaunchButton.Content = T("Запуск…", "Launching…");
        SetLaunchProgress(T("Подготовка запуска", "Preparing launch"));
        SetStatus(T("Подготовка запуска", "Preparing launch"), StatusLevel.Working,
            T("Проверка профиля и пакетов.", "Validating profile and packages."));
        try
        {
            launchReport = new SanitizedLaunchReport(_paths.LogsDirectory);
            launchReport.Set("launchStage", "preflight");
            launchReport.Set("launchStartedUtc", launchStartedUtc.ToString("O"));
            if (!string.Equals(SelectedClient, "lunar", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(SelectedVersion, "1.8.9", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Moonrise recovery supports only official Lunar Client with Minecraft 1.8.9.");
            }

            var reconciliation = _library.Reconcile();
            if (reconciliation.Removed > 0)
            {
                LoadPackages();
                AddDiagnostic(
                    $"Pre-launch reconciliation removed {reconciliation.Removed} stale package entr{(reconciliation.Removed == 1 ? "y" : "ies")}; launch will continue.");
                SetStatus(
                    T("Список пакетов обновлён", "Package list updated"),
                    StatusLevel.Working,
                    T(
                        "Пакет удалён из библиотеки, потому что его JAR-файл отсутствует.",
                        "The package was removed from the Library because its JAR file is missing."));
            }
            try
            {
                selection = _launchResolver.Resolve(
                    _library.Packages, false);
            }
            catch (FileNotFoundException)
            {
                reconciliation = _library.Reconcile();
                LoadPackages();
                AddDiagnostic("A package disappeared during launch validation and was excluded.");
                selection = _launchResolver.Resolve(
                    _library.Packages, false);
            }
            var enabledMods = selection.WeaveMods;
            var enabledAgents = selection.JavaAgents;
            var selectedVersion = SelectedVersion;
            var selectedPackageIds = enabledMods
                .Concat(enabledAgents)
                .Select(item => item.PackageId)
                .ToArray();
            var enabledModDirectoryService = new EnabledModDirectoryService(_jarParser);
            var successorResolution = await Task.Run(() =>
                _compatibilityBuilds.ResolveMoonriseOwnedSuccessors(selectedVersion, enabledMods));
            if (successorResolution.AppliedSuccessors.Count > 0)
            {
                enabledMods = successorResolution.LaunchMods;
                selection = new PackageLaunchSelection(enabledMods, enabledAgents);
                foreach (var successor in successorResolution.AppliedSuccessors)
                {
                    AddDiagnostic(
                        $"Moonrise-owned successor applied: {successor.RequestedPackage.OriginalFileName}; " +
                        $"target SHA-256={successor.RequestedPackage.Sha256}; successor={successor.SuccessorPackage.DisplayName}; " +
                        $"successor SHA-256={successor.SuccessorPackage.Sha256}");
                }
                launchReport.Set(
                    "moonriseOwnedSuccessors",
                    successorResolution.AppliedSuccessors.Select(item => new
                    {
                        item.Rule.Id,
                        RequestedPackageId = item.RequestedPackage.PackageId,
                        RequestedSha256 = item.RequestedPackage.Sha256,
                        SuccessorPackageId = item.SuccessorPackage.PackageId,
                        SuccessorSha256 = item.SuccessorPackage.Sha256,
                        item.Rule.SuccessorIdentifier,
                        item.Rule.MinecraftVersion,
                        item.Rule.WeaveLoaderVersion
                    }).ToArray());
            }

            // Exact-hash maintained builds must be considered before choosing the
            // loader generation. Otherwise one old mod downgrades every package to
            // Weave 0.2.x and bypasses the known Weave 1.x compatibility builds.
            var compatibilityResolution = await Task.Run(() =>
                _compatibilityBuilds.Resolve(selectedVersion, enabledMods));
            if (compatibilityResolution.AppliedBuilds.Count > 0)
            {
                enabledMods = compatibilityResolution.LaunchMods;
                selection = new PackageLaunchSelection(enabledMods, enabledAgents);
                foreach (var appliedBuild in compatibilityResolution.AppliedBuilds)
                {
                    AddDiagnostic(
                        $"Compatibility build applied: {appliedBuild.OriginalPackage.OriginalFileName}; " +
                        $"target SHA-256={appliedBuild.OriginalPackage.Sha256}; adapter={appliedBuild.Rule.Id}; " +
                        $"build SHA-256={appliedBuild.CompatibilityBuild.Sha256}");
                }
                launchReport.Set(
                    "maintainedCompatibilityBuilds",
                    compatibilityResolution.AppliedBuilds.Select(item => new
                    {
                        item.Rule.Id,
                        TargetPackageId = item.OriginalPackage.PackageId,
                        TargetSha256 = item.OriginalPackage.Sha256,
                        BuildPackageId = item.CompatibilityBuild.PackageId,
                        BuildSha256 = item.CompatibilityBuild.Sha256,
                        item.Rule.MinecraftVersion,
                        item.Rule.WeaveLoaderVersion
                    }).ToArray());
                launchReport.Set(
                    "adapterIds",
                    compatibilityResolution.AppliedBuilds.Select(item => item.Rule.Id).ToArray());
            }
            var bwhNetworkBridgeRequired = compatibilityResolution.AppliedBuilds.Any(item =>
                string.Equals(item.Rule.Id, "moonrise-local-bwh-c39f-weave-1.3.4", StringComparison.OrdinalIgnoreCase)) ||
                enabledMods.Any(IsBwhPackage);

            var runtimeApiInspection = await Task.Run(() => _weaveApiInspector.Inspect(enabledMods));
            if (runtimeApiInspection.HasLegacy && runtimeApiInspection.HasCurrent)
            {
                var legacyNames = string.Join(", ", runtimeApiInspection.Mods
                    .Where(item => item.Generation is WeaveApiGeneration.Legacy02 or WeaveApiGeneration.Mixed)
                    .Select(item => item.Package.OriginalFileName));
                var currentNames = string.Join(", ", runtimeApiInspection.Mods
                    .Where(item => item.Generation is WeaveApiGeneration.Current or WeaveApiGeneration.Mixed)
                    .Select(item => item.Package.OriginalFileName));
                throw new InvalidDataException(T(
                    $"Нельзя одновременно загрузить моды для Weave 0.2.x ({legacyNames}) и Weave 1.x ({currentNames}). " +
                    "Moonrise не будет изменять сторонние JAR; отключите одну из этих групп.",
                    $"Weave 0.2.x mods ({legacyNames}) and Weave 1.x mods ({currentNames}) cannot be loaded together. " +
                    "Moonrise will not modify third-party JARs; disable one of these groups."));
            }

            var useLegacyWeave = runtimeApiInspection.IsLegacyOnly;
            if (useLegacyWeave)
            {
                weaveLoaderRelease = WeaveAgentService.Legacy02;
                weaveLoaderPath = _paths.LegacyWeaveAgentPath;
                launchReport.Set("adapterIds", new[] { LegacyWeaveDirectoryAdapterService.Id });
            }
            launchReport.Set("temporaryPackageExclusion", false);
            launchReport.Set("enabledPackageIds", selectedPackageIds);
            launchReport.Set("runtimePackageIds", enabledMods.Concat(enabledAgents).Select(item => item.PackageId).ToArray());
            launchReport.Set("enabledWeaveModCount", enabledMods.Count);
            launchReport.Set("enabledJavaAgentCount", enabledAgents.Count);
            AddDiagnostic($"Enabled Weave mods: {enabledMods.Count}");
            AddDiagnostic($"Enabled Java agents: {enabledAgents.Count}");
            var initialProcesses = _processInspector.Snapshot().Keys.ToHashSet();

            _monitorCancellation?.Cancel();
            _monitorCancellation?.Dispose();
            _monitorCancellation = new CancellationTokenSource();
            var token = _monitorCancellation.Token;

            var launcherPath = Path.GetFullPath(LauncherPathTextBox.Text.Trim());
            if (!LauncherExecutableDetector.IsSupportedLauncher(launcherPath))
                throw new InvalidDataException(T("Выберите официальный Lunar Client.exe.", "Select the official Lunar Client.exe."));

            var running = _launcherDetector.GetRunningLaunchers();
            if (running.Count > 0)
            {
                foreach (var process in running) process.Dispose();
                throw new InvalidOperationException(T("Сначала полностью закройте Lunar Launcher.", "Close Lunar Launcher completely before launching."));
            }

            profileSelection = _profileService.BeginExactProfileSelection(SelectedClient, SelectedVersion);
            var selectedProfile = profileSelection.Profile;
            var launcherLog = LunarProfileReadinessService.GetDefaultLauncherLogPath();
            var checkpoint = LunarProfileReadinessService.CaptureCheckpoint(launcherLog);

            launchSession = _launchSessions.Create(_paths.TempDirectory);
            string? enabledModsDirectory = null;
            if (enabledMods.Count > 0)
            {
                await _weaveAgent.EnsureAsync(weaveLoaderPath, weaveLoaderRelease);
                if (!_weaveAgent.Validate(weaveLoaderPath, weaveLoaderRelease, out var weaveError))
                    throw new InvalidDataException(weaveError);
                weaveHash = LocalPackageLibrary.ComputeSha256(weaveLoaderPath);
                if (useLegacyWeave)
                    legacyWeaveAdapterPath = _legacyWeaveAdapter.Ensure();
                enabledModsDirectory = enabledModDirectoryService.Create(
                    launchSession.DirectoryPath,
                    enabledMods,
                    legacyWeaveLayout: useLegacyWeave);
            }
            else if (enabledAgents.Count > 0)
            {
                enabledModsDirectory = enabledModDirectoryService.Create(
                    launchSession.DirectoryPath,
                    []);
            }

            launchReport.Set("selectedProfile", new
            {
                selectedProfile.Id,
                selectedProfile.Name,
                selectedProfile.Client,
                selectedProfile.GameVersion
            });
            launchReport.Set("weaveLoaderPath", enabledMods.Count > 0 ? weaveLoaderPath : null);
            launchReport.Set("weaveLoaderVersion", enabledMods.Count > 0 ? weaveLoaderRelease.Version : null);
            launchReport.Set("weaveLoaderSha256", weaveHash);
            launchReport.Set("legacyWeaveDirectoryAdapterPath", legacyWeaveAdapterPath);
            launchReport.Set("enabledMods", enabledMods.Select(item => new { item.PackageId, item.OriginalFileName, item.Sha256 }).ToArray());
            launchReport.Set("javaAgents", enabledAgents.Select(item => new { item.PackageId, item.OriginalFileName, item.Sha256 }).ToArray());
            launchReport.Set("preparedAgentCount", enabledAgents.Count);
            launchReport.Set("agentArgumentPrepared", enabledAgents.Count > 0);
            launchReport.Set("launchSessionPath", launchSession.DirectoryPath);
            launchReport.Set("launchStage", enabledMods.Count + enabledAgents.Count == 0
                ? "launcher-start"
                : "bridge-launch");

            int launcherProcessId;
            var bridgeReady = false;
            var backgroundLaunch = true;
            string? bwhNetworkAgentPath = null;
            if (bwhNetworkBridgeRequired)
            {
                _bwhApiRelay ??= new BwhApiRelayService(AddDiagnostic);
                _bwhApiRelay.Start();
                bwhNetworkAgentPath = _bwhNetworkAgentDeployment.Ensure();
                launchReport.Set("bwhExitLagNetworkBridge", true);
                launchReport.Set("bwhNetworkAgentSha256", BwhNetworkAgentDeploymentService.ExpectedSha256);
                AddDiagnostic($"BWH ExitLag network adapter prepared: {bwhNetworkAgentPath}");
            }
            SetLaunchProgress(T("Запуск Lunar в фоне", "Starting Lunar in the background"));
            if (enabledMods.Count + enabledAgents.Count == 0)
            {
                using var process = StartLauncherWithoutBridge(launcherPath, backgroundLaunch);
                launcherProcessId = process.Id;
            }
            else
            {
                var nativeBridgePath = _nativeBridgeDeployment.Ensure(_paths.NativeBridgeCachePath);
                launchReport.Set("nativeBridgePath", nativeBridgePath);
                launchReport.Set("nativeBridgeSha256", NativeBridgeDeploymentService.ExpectedSha256);
                var packagePrimaryAgent = enabledMods.Count > 0
                    ? legacyWeaveAdapterPath ?? weaveLoaderPath
                    : enabledAgents[0].FullPath;
                var packageAdditionalAgents = enabledMods.Count > 0
                    ? (useLegacyWeave
                        ? new[] { weaveLoaderPath }.Concat(enabledAgents.Select(item => item.FullPath)).ToArray()
                        : enabledAgents.Select(item => item.FullPath).ToArray())
                    : enabledAgents.Skip(1).Select(item => item.FullPath).ToArray();
                var primaryAgent = bwhNetworkAgentPath ?? packagePrimaryAgent;
                var additionalAgents = bwhNetworkAgentPath is null
                    ? packageAdditionalAgents
                    : new[] { packagePrimaryAgent }
                        .Concat(packageAdditionalAgents)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                launchReport.Set("bridgeConfigPath", launchSession.BridgeConfigPath);
                var result = await Task.Run(() => _bridgeLauncher.Launch(
                    launcherPath,
                    primaryAgent,
                    enabledModsDirectory!,
                    nativeBridgePath,
                    launchSession.BridgeConfigPath,
                    launcherArgument: null,
                    hideLauncherWindow: backgroundLaunch,
                    additionalAgentPaths: additionalAgents));
                launcherProcessId = result.Process.Id;
                bridgeReady = result.BridgeReady;
                result.Process.Dispose();
            }
            _lunarWindowLogger = new SafeLogger(
                _paths.LogsDirectory,
                "lunar-window-classification");
            _activeLunarBackground = new LunarBackgroundLaunchService(
                launcherProcessId,
                launcherPath,
                launchStartedUtc,
                _lunarWindowLogger.Write);
            AddDiagnostic($"Lunar window classification log: {_lunarWindowLogger.Path}");
            ShowLaunchControls(true);
            if (backgroundLaunch)
            {
                StartLunarWindowWatcher(_activeLunarBackground);
                await Task.Run(_activeLunarBackground.HideOwnedWindows, token);
            }
            else
            {
                _activeLunarBackground.ShowLunar();
            }

            AddDiagnostic($"Selected profile: {selectedProfile.Client} {selectedProfile.GameVersion} ({selectedProfile.Id})");
            if (enabledMods.Count > 0)
                AddDiagnostic($"Weave Loader {weaveLoaderRelease.Version}: {weaveLoaderPath}; SHA-256={weaveHash}");
            if (legacyWeaveAdapterPath is not null)
                AddDiagnostic($"Compatibility adapter: {LegacyWeaveDirectoryAdapterService.Id}; path={legacyWeaveAdapterPath}");
            foreach (var mod in enabledMods)
                AddDiagnostic($"Enabled mod prepared: {mod.OriginalFileName}; SHA-256={mod.Sha256}");
            foreach (var agent in enabledAgents)
                AddDiagnostic($"Java agent prepared: {agent.OriginalFileName}; SHA-256={agent.Sha256}; argument=true");
            AddDiagnostic($"Launch session: {launchSession.DirectoryPath}");
            AddDiagnostic(enabledMods.Count + enabledAgents.Count == 0
                ? $"Official launcher started without package injection; launcher PID: {launcherProcessId}"
                : $"Native bridge ready: {bridgeReady}; launcher PID: {launcherProcessId}");
            AddDiagnostic("Enabled packages are prepared; runtime loading awaits user confirmation.");
            launchReport.Set("detectedLauncherProcessIds", new[] { launcherProcessId });
            launchReport.Set("launchStage", "profile-readiness");
            launchReport.Save();

            SetLaunchProgress(T("Проверка профиля", "Checking profile"));
            SetStatus(T("Проверка профиля", "Checking profile"), StatusLevel.Working);
            var confirmed = await WaitForProfileWithLauncherAsync(
                launcherLog,
                checkpoint,
                selectedProfile,
                TimeSpan.FromSeconds(60),
                backgroundLaunch,
                token);
            if (!string.Equals(confirmed.Client, SelectedClient, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(confirmed.Version, SelectedVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Lunar selected {confirmed.Client} {confirmed.Version}, expected {SelectedClient} {SelectedVersion}.");

            await DispatchLaunchToExistingLunarAsync(launcherPath, backgroundLaunch, token);
            profileSelection.Restore();
            profileSelection = null;
            launchReport.Set("launchStage", "waiting-for-java");
            launchReport.Save();
            SetLaunchProgress(T("Ожидание Minecraft", "Waiting for Minecraft"));
            SetStatus(T("Ожидание Minecraft", "Waiting for Minecraft"), StatusLevel.Working,
                T("Применение выбранной конфигурации.", "Applying the selected configuration."));
            monitorOwnsLaunchGate = true;
            _activeLaunchPackageIds.Clear();
            foreach (var package in enabledMods.Concat(enabledAgents))
                _activeLaunchPackageIds.Add(package.PackageId);
            var monitoredSession = launchSession;
            launchSession = null;
            _ = MonitorGameAsync(
                initialProcesses,
                token,
                monitoredSession,
                launchReport,
                selection,
                launchStartedUtc,
                weaveHash,
                enabledMods.Count > 0 ? weaveLoaderPath : null,
                enabledMods.Count > 0 ? weaveLoaderRelease.Version : null,
                backgroundLaunch);
            launchReport = null;
        }
        catch (TimeoutException exception)
        {
            pendingPackageFailureStage = "lunar-profile-timeout";
            AddDiagnostic($"Lunar action required: {exception.Message}");
            launchReport?.Set("launchStage", "lunar-action-required");
            launchReport?.Set("sanitizedException", exception.Message);
            RevealActiveLunar(force: true, interactionRequired: true);
            SetLaunchProgress(T("Требуется действие в Lunar", "Action required in Lunar"));
            SetStatus(
                T("Требуется действие в Lunar", "Action required in Lunar"),
                StatusLevel.Warning,
                T(
                    "Может требоваться вход, обновление или другое действие. Официальный лаунчер показан.",
                    "Authentication, an update, or another action may be required. The official launcher is visible."));
            OpenDiagnosticsButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (_launchWaitCancelledByUser)
        {
            AddDiagnostic("Launch waiting was cancelled by the user; the official launcher was not terminated.");
            SetStatus(
                T("Ожидание отменено", "Waiting cancelled"),
                StatusLevel.Ready,
                T("Lunar продолжает работать.", "Lunar is still running."));
        }
        catch (Exception exception)
        {
            if (_activeLunarBackground is not null)
                pendingPackageFailureStage = "launch-failed-before-java";
            AddDiagnostic($"Launch error: {exception.Message}");
            launchReport?.Set("launchStage", "failed");
            launchReport?.Set("sanitizedException", exception.Message);
            if (_settings.RevealLunarWhenActionRequired)
                RevealActiveLunar(force: false);
            SetLaunchProgress(T("Запуск завершился ошибкой", "Launch failed"));
            SetStatus(T("Запуск завершился ошибкой", "Launch failed"), StatusLevel.Error, exception.Message);
            ShowError(exception.Message);
        }
        finally
        {
            profileSelection?.Dispose();
            if (launchSession is not null)
            {
                var cleanupResult = CleanupLaunchSession(launchSession, launchReport);
                launchReport?.Save();
                if (launchReport is not null &&
                    pendingPackageFailureStage is not null &&
                    selection.WeaveMods.Count + selection.JavaAgents.Count > 0)
                {
                    CreatePackageCrashBundle(
                        selection,
                        launchStartedUtc,
                        pendingPackageFailureStage,
                        null,
                        weaveHash,
                        selection.WeaveMods.Count > 0 ? weaveLoaderPath : null,
                        selection.WeaveMods.Count > 0 ? weaveLoaderRelease.Version : null,
                        launchReport,
                        cleanupResult);
                }
            }
            launchReport?.Save();
            if (!monitorOwnsLaunchGate) EndLaunchAttempt();
        }
    }

    private async Task DispatchLaunchToExistingLunarAsync(
        string launcherPath,
        bool backgroundLaunch,
        CancellationToken cancellationToken)
    {
        var background = _activeLunarBackground;
        await Task.Run(async () =>
        {
            var startInfo = OfficialLunarStartInfoFactory.Create(
                launcherPath,
                background: true,
                LaunchDeepLink);
            using var sender = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to send the launch command to Lunar Client.");
            AddDiagnostic($"Lunar launch command sent through hidden IPC sender: PID {sender.Id}");

            var hideUntil = backgroundLaunch ? DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3) : DateTimeOffset.UtcNow;
            while (backgroundLaunch && DateTimeOffset.UtcNow < hideUntil)
            {
                cancellationToken.ThrowIfCancellationRequested();
                background?.HideOwnedWindows();
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            if (!sender.HasExited)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(7));
                try
                {
                    await sender.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException(
                        "Lunar launch command sender did not exit; a duplicate launcher was prevented.");
                }
            }
        }, cancellationToken);
    }

    private static Process StartLauncherWithoutBridge(string launcherPath, bool background)
    {
        var startInfo = OfficialLunarStartInfoFactory.Create(launcherPath, background);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start Lunar Client.");
    }

    private async Task<(string Client, string Version)> WaitForProfileWithLauncherAsync(
        string launcherLog,
        long checkpoint,
        LauncherProfile expectedProfile,
        TimeSpan timeout,
        bool backgroundLaunch,
        CancellationToken cancellationToken)
    {
        var readiness = _profileReadiness.WaitForSelectedProfileAsync(
            launcherLog,
            checkpoint,
            expectedProfile,
            timeout,
            cancellationToken);
        var missingSamples = 0;
        while (!readiness.IsCompleted)
        {
            await Task.WhenAny(readiness, Task.Delay(500, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            var launcherTreeRunning = await Task.Run(
                () => _activeLunarBackground?.IsLauncherTreeRunning(),
                cancellationToken);
            if (launcherTreeRunning == false)
            {
                missingSamples++;
                if (missingSamples >= 6)
                    throw new InvalidOperationException(
                        T(
                            "Официальный Lunar завершился до запуска Minecraft.",
                            "The official Lunar launcher exited before Minecraft started."));
            }
            else
            {
                missingSamples = 0;
            }
        }
        return await readiness;
    }

    private async Task MonitorGameAsync(
        HashSet<int> initialProcessIds,
        CancellationToken cancellationToken,
        LaunchSession launchSession,
        SanitizedLaunchReport launchReport,
        PackageLaunchSelection selection,
        DateTimeOffset launchStartedUtc,
        string? weaveHash,
        string? weaveLoaderPath,
        string? weaveLoaderVersion,
        bool backgroundLaunch)
    {
        var deadline = DateTimeOffset.UtcNow +
            TimeSpan.FromSeconds(Math.Clamp(_settings.LaunchTimeoutSeconds, 30, 600));
        var visibleSamples = new Dictionary<int, int>();
        var exitTasks = new Dictionary<int, Task<int?>>();
        var minecraftStartupWindows = new MinecraftStartupWindowService(AddDiagnostic);
        var sawGameJvm = false;
        var gameOwnsSession = false;
        DateTimeOffset? candidatesMissingSince = null;
        DateTimeOffset? launcherMissingSince = null;
        int? earlyExitCode = null;
        string? failureStage = null;
        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500, cancellationToken);
                var candidates = await Task.Run(() =>
                {
                    var snapshot = _processInspector.Snapshot().Values.Where(process =>
                        !initialProcessIds.Contains(process.ProcessId) && IsJavaProcess(process.Name)).ToArray();
                    minecraftStartupWindows.Observe(snapshot);
                    return snapshot;
                }, cancellationToken);
                foreach (var candidate in candidates)
                {
                    if (!exitTasks.ContainsKey(candidate.ProcessId))
                        exitTasks[candidate.ProcessId] = ObserveExitCodeAsync(candidate.ProcessId);
                }
                if (candidates.Length > 0)
                {
                    sawGameJvm = true;
                    candidatesMissingSince = null;
                }
                else if (sawGameJvm)
                {
                    candidatesMissingSince ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - candidatesMissingSince >= TimeSpan.FromSeconds(5))
                    {
                        AddDiagnostic("Game JVM exited before a usable window appeared.");
                        earlyExitCode = await MostRecentExitCodeAsync(exitTasks);
                        failureStage = "java-exited-before-usable-window";
                        SetStatus(
                            selection.WeaveMods.Count + selection.JavaAgents.Count > 0
                                ? T("Игра завершилась во время загрузки пакетов.", "The game exited while loading packages.")
                                : T("Minecraft не запустился", "Minecraft did not launch"),
                            StatusLevel.Error,
                            T("Игровой процесс завершился до открытия окна.", "The game process ended before its window opened."));
                        return;
                    }
                }

                var launcherTreeRunning = candidates.Length > 0
                    ? true
                    : await Task.Run(
                        () => _activeLunarBackground?.IsLauncherTreeRunning(),
                        cancellationToken);
                if (candidates.Length == 0 && launcherTreeRunning == false)
                {
                    launcherMissingSince ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - launcherMissingSince >= TimeSpan.FromSeconds(5))
                    {
                        failureStage = "lunar-exited-before-java";
                        AddDiagnostic("Official Lunar exited before the target Java process appeared.");
                        SetStatus(
                            T("Запуск завершился ошибкой", "Launch failed"),
                            StatusLevel.Error,
                            T(
                                "Официальный Lunar завершился до запуска Minecraft.",
                                "The official Lunar launcher exited before Minecraft started."));
                        return;
                    }
                }
                else
                {
                    launcherMissingSince = null;
                }

                var liveCandidateIds = candidates.Select(process => process.ProcessId).ToHashSet();
                foreach (var staleProcessId in visibleSamples.Keys.Where(id => !liveCandidateIds.Contains(id)).ToArray())
                    visibleSamples.Remove(staleProcessId);

                ProcessRecord? game = null;
                foreach (var candidate in candidates)
                {
                    if (candidate.MainWindowHandle == 0 ||
                        !candidate.IsMainWindowVisible ||
                        !candidate.IsResponding ||
                        string.IsNullOrWhiteSpace(candidate.MainWindowTitle))
                    {
                        visibleSamples.Remove(candidate.ProcessId);
                        continue;
                    }

                    var samples = visibleSamples.GetValueOrDefault(candidate.ProcessId) + 1;
                    visibleSamples[candidate.ProcessId] = samples;
                    if (samples >= 6)
                    {
                        game = candidate;
                        break;
                    }
                }

                if (game is null) continue;
                AddDiagnostic($"Game JVM detected: PID {game.ProcessId}");
                AddDiagnostic("The target Java process was detected; package functionality remains unconfirmed until the user checks Minecraft.");
                if (_activeLunarBackground is { } lunarBackground)
                    await Task.Run(lunarBackground.MarkMinecraftDetected, cancellationToken);
                _activeGameProcessIds.Add(game.ProcessId);
                launchReport.Set("targetJavaProcessDetected", true);
                launchReport.Set("detectedJavaProcessIds", new[] { game.ProcessId });
                launchReport.Set("launchStage", "java-detected");
                launchReport.Save();
                SetLaunchProgress(T("Minecraft запущен", "Minecraft launched"));
                ShowLaunchControls(false);
                CrashActionsPanel.Visibility = Visibility.Collapsed;
                OpenDiagnosticsButton.Visibility = Visibility.Collapsed;
                _lastCrashBundlePath = null;
                _lastCrashReportPath = null;
                SetStatus(T("Minecraft запущен", "Minecraft launched"), StatusLevel.Ready,
                    _activeGameProcessIds.Count == 1
                        ? T("Игровое окно обнаружено. Проверьте включённые пакеты в игре.", "Game window detected. Verify the enabled packages in game.")
                        : T($"Активных экземпляров: {_activeGameProcessIds.Count}.", $"Active instances: {_activeGameProcessIds.Count}."));
                gameOwnsSession = true;
                var gameDetectedUtc = DateTimeOffset.UtcNow;
                _ = WatchGameLifetimeAsync(
                    game.ProcessId,
                    launchSession,
                    launchReport,
                    selection,
                    launchStartedUtc,
                    weaveHash,
                    weaveLoaderPath,
                    weaveLoaderVersion,
                    gameDetectedUtc);
                if (_settings.CloseAfterLaunch)
                {
                    if (_bwhApiRelay?.IsRunning == true)
                    {
                        AddDiagnostic("Moonrise remains in the tray while BWH uses the ExitLag network bridge.");
                        await Dispatcher.InvokeAsync(Hide);
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(Close);
                    }
                }
                return;
            }
            failureStage = "minecraft-timeout";
            RevealActiveLunar(force: true, interactionRequired: true);
            SetLaunchProgress(T("Требуется действие в Lunar", "Action required in Lunar"));
            SetStatus(
                T("Требуется действие в Lunar", "Action required in Lunar"),
                StatusLevel.Warning,
                T(
                    "Может требоваться вход, обновление или другое действие. Официальный лаунчер показан.",
                    "Authentication, an update, or another action may be required. The official launcher is visible."));
            OpenDiagnosticsButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            if (_launchWaitCancelledByUser)
            {
                SetStatus(
                    T("Ожидание отменено", "Waiting cancelled"),
                    StatusLevel.Ready,
                    T("Lunar продолжает работать.", "Lunar is still running."));
            }
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Game monitor error: {exception.Message}");
            SetStatus(T("Не удалось подтвердить запуск", "Unable to confirm launch"), StatusLevel.Warning, exception.Message);
        }
        finally
        {
            await Task.Run(minecraftStartupWindows.RestoreAll);
            if (!gameOwnsSession)
            {
                launchReport.Set("targetJavaProcessDetected", sawGameJvm);
                launchReport.Set("launchStage", failureStage ?? "monitor-ended");
                launchReport.Set("javaExitCode", earlyExitCode);
                var cleanupResult = CleanupLaunchSession(launchSession, launchReport);
                launchReport.Save();
                if (failureStage is "java-exited-before-usable-window" or "lunar-exited-before-java" &&
                    selection.WeaveMods.Count + selection.JavaAgents.Count > 0)
                {
                    CreatePackageCrashBundle(
                        selection,
                        launchStartedUtc,
                        failureStage,
                        earlyExitCode,
                        weaveHash,
                        weaveLoaderPath,
                        weaveLoaderVersion,
                        launchReport,
                        cleanupResult);
                }
            }
            EndLaunchAttempt();
        }
    }

    private static async Task<int?> ObserveExitCodeAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<int?> MostRecentExitCodeAsync(
        IReadOnlyDictionary<int, Task<int?>> exitTasks)
    {
        foreach (var task in exitTasks.Values.Reverse())
        {
            if (!task.IsCompleted)
                await Task.WhenAny(task, Task.Delay(1000));
            if (task.IsCompletedSuccessfully)
                return task.Result;
        }
        return null;
    }

    private static bool IsJavaProcess(string processName) =>
        string.Equals(processName, "java", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "javaw", StringComparison.OrdinalIgnoreCase);

    private static bool IsBwhPackage(PackageInfo package) =>
        string.Equals(package.Sha256, BwhPackageSha256, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(package.Sha256, BwhMaintainedBuildSha256, StringComparison.OrdinalIgnoreCase);

    private void RecoverBwhRelayForRunningMinecraft()
    {
        if (!_library.Packages.Any(package => package.IsEnabled && IsBwhPackage(package)))
            return;

        var runningGames = _processInspector.Snapshot().Values
            .Where(process => IsJavaProcess(process.Name))
            .Where(process => process.MainWindowHandle != 0 &&
                              process.IsMainWindowVisible &&
                              process.IsResponding &&
                              !string.IsNullOrWhiteSpace(process.MainWindowTitle))
            .ToArray();
        if (runningGames.Length == 0)
            return;

        try
        {
            _bwhApiRelay ??= new BwhApiRelayService(AddDiagnostic);
            _bwhApiRelay.Start();
            foreach (var game in runningGames)
            {
                if (!_activeGameProcessIds.Add(game.ProcessId))
                    continue;
                _ = WatchRecoveredGameLifetimeAsync(game.ProcessId);
            }
            AddDiagnostic($"BWH network bridge recovered for {runningGames.Length} running Minecraft instance(s).");
        }
        catch (Exception exception)
        {
            AddDiagnostic($"BWH network bridge recovery failed: {exception.GetType().Name}");
        }
    }

    private async Task WatchRecoveredGameLifetimeAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _activeGameProcessIds.Remove(processId);
                    StopBwhRelayAfterLastGame();
                });
            }
        }
    }

    private async Task WatchGameLifetimeAsync(
        int processId,
        LaunchSession launchSession,
        SanitizedLaunchReport launchReport,
        PackageLaunchSelection selection,
        DateTimeOffset launchStartedUtc,
        string? weaveHash,
        string? weaveLoaderPath,
        string? weaveLoaderVersion,
        DateTimeOffset gameDetectedUtc)
    {
        int? exitCode = null;
        var cleanupResult = "unknown";
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (ArgumentException)
        {
            exitCode = -1;
        }
        catch (InvalidOperationException)
        {
            exitCode = -1;
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Game lifetime monitor error for PID {processId}: {exception.Message}");
            launchReport.Set("sanitizedException", exception.Message);
        }
        finally
        {
            launchReport.Set("launchStage", "java-exited");
            launchReport.Set("javaExitCode", exitCode);
            cleanupResult = CleanupLaunchSession(launchSession, launchReport);
            launchReport.Save();
        }

        if (Dispatcher.HasShutdownStarted) return;
        var packageCrashCreated = false;
        var exitedShortlyAfterPackageLoad =
            DateTimeOffset.UtcNow - gameDetectedUtc <= TimeSpan.FromSeconds(20);
        if ((exitCode is not 0 || exitedShortlyAfterPackageLoad) &&
            selection.WeaveMods.Count + selection.JavaAgents.Count > 0)
        {
            CreatePackageCrashBundle(
                selection,
                launchStartedUtc,
                exitCode is not 0
                    ? "java-exited-nonzero"
                    : "java-exited-shortly-after-package-load",
                exitCode,
                weaveHash,
                weaveLoaderPath,
                weaveLoaderVersion,
                launchReport,
                cleanupResult);
            packageCrashCreated = true;
        }
        await Dispatcher.InvokeAsync(() => UpdateGameExitStatus(processId, exitCode, packageCrashCreated));
    }

    private void UpdateGameExitStatus(int processId, int? exitCode, bool packageCrashCreated)
    {
        if (!_activeGameProcessIds.Remove(processId)) return;
        AddDiagnostic($"Game JVM exited: PID {processId}; code={(exitCode?.ToString() ?? "unknown")}");

        if (Volatile.Read(ref _launchInProgress) == 1) return;
        if (_activeGameProcessIds.Count > 0)
        {
            SetStatus(T("Minecraft запущен", "Minecraft launched"), exitCode is 0 ? StatusLevel.Ready : StatusLevel.Warning,
                exitCode is 0
                    ? T($"Активных экземпляров: {_activeGameProcessIds.Count}.", $"Active instances: {_activeGameProcessIds.Count}.")
                    : T($"Один экземпляр завершился с ошибкой. Активных: {_activeGameProcessIds.Count}.", $"One instance crashed. Active: {_activeGameProcessIds.Count}."));
            return;
        }

        StopBwhRelayAfterLastGame();

        if (exitCode is 0)
        {
            SetStatus(T("Готов к запуску", "Ready to launch"), StatusLevel.Ready,
                T("Minecraft закрыт.", "Minecraft was closed."));
        }
        else
        {
            if (packageCrashCreated)
            {
                SetStatus(
                    T("Игра завершилась во время загрузки пакетов.", "The game exited while loading packages."),
                    StatusLevel.Error,
                    T(
                        "Санитизированный отчёт сохранён локально.",
                        "A sanitized local report was created."));
                CrashActionsPanel.Visibility = Visibility.Visible;
                OpenDiagnosticsButton.Visibility = Visibility.Collapsed;
                return;
            }
            var analysis = _crashAnalyzer.Analyze(processId, exitCode, _diagnostics, _paths.CrashReportsDirectory);
            AddDiagnostic($"Crash analysis: {analysis.Category}; report={analysis.ReportPath}");
            SetStatus(T("Minecraft завершился с ошибкой", "Minecraft crashed"), StatusLevel.Error,
                exitCode.HasValue
                    ? T($"Код завершения: {exitCode.Value}. Отчёт сохранён локально.", $"Exit code: {exitCode.Value}. A local report was saved.")
                    : T("Процесс завершился неожиданно. Отчёт сохранён локально.", "The process ended unexpectedly. A local report was saved."));
        }
    }

    private void EndLaunchAttempt()
    {
        _activeLaunchPackageIds.Clear();
        if (Interlocked.Exchange(ref _launchInProgress, 0) == 0 || Dispatcher.HasShutdownStarted) return;

        void UpdateButton()
        {
            LaunchButton.IsEnabled = true;
            LaunchButton.Content = T("Запустить", "Launch");
            ShowLaunchControls(false);
            LaunchProgressPanel.Visibility = Visibility.Collapsed;
        }

        if (Dispatcher.CheckAccess()) UpdateButton();
        else Dispatcher.BeginInvoke(UpdateButton);
    }

    private void SetLaunchProgress(string text)
    {
        void Update()
        {
            LaunchProgressText.Text = text;
            LaunchProgressPanel.Visibility = Visibility.Visible;
        }
        if (Dispatcher.CheckAccess()) Update();
        else Dispatcher.BeginInvoke(Update);
    }

    private void ShowLaunchControls(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ShowLunarButton.Visibility = visibility;
        CancelLaunchWaitButton.Visibility = visibility;
    }

    private void StartLunarWindowWatcher(LunarBackgroundLaunchService service)
    {
        _lunarWindowCancellation?.Cancel();
        _lunarWindowCancellation?.Dispose();
        _lunarWindowCancellation = new CancellationTokenSource();
        var cancellationToken = _lunarWindowCancellation.Token;
        _lunarWindowTask = Task.Run(
            () => service.WatchAsync(cancellationToken),
            cancellationToken);
    }

    private void RevealActiveLunar(bool force, bool interactionRequired = false)
    {
        if (!force && !_settings.RevealLunarWhenActionRequired)
            return;
        try
        {
            var revealed = interactionRequired
                ? _activeLunarBackground?.RequireInteraction()
                : _activeLunarBackground?.ShowLunar();
            if (revealed == true)
                AddDiagnostic("Official Lunar window revealed for user interaction.");
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Unable to reveal official Lunar window: {exception.Message}");
        }
    }

    private void ShowLunarButton_Click(object sender, RoutedEventArgs e) =>
        RevealActiveLunar(force: true);

    private void CancelLaunchWaitButton_Click(object sender, RoutedEventArgs e)
    {
        _launchWaitCancelledByUser = true;
        _monitorCancellation?.Cancel();
        RevealActiveLunar(force: true);
        _activeLunarBackground?.Cancel();
    }

    private void CreatePackageCrashBundle(
        PackageLaunchSelection selection,
        DateTimeOffset launchStartedUtc,
        string failureStage,
        int? exitCode,
        string? weaveHash,
        string? weaveLoaderPath,
        string? weaveLoaderVersion,
        SanitizedLaunchReport launchReport,
        string cleanupResult)
    {
        try
        {
            var packages = selection.WeaveMods.Concat(selection.JavaAgents).ToArray();
            var result = _packageCrashDiagnostics.Capture(
                new PackageCrashCaptureRequest(
                    launchStartedUtc,
                    DateTimeOffset.UtcNow,
                    failureStage,
                    exitCode,
                    packages,
                    weaveLoaderPath,
                    weaveHash,
                    launchReport.Path,
                    cleanupResult,
                    _diagnostics.ToArray(),
                    AdditionalLogRoots: [_paths.LogsDirectory],
                    MinecraftVersion: SelectedVersion,
                    LanguageCode: _languageCode,
                    WeaveLoaderVersion: weaveLoaderVersion),
                _paths.CrashesDirectory);
            _lastCrashBundlePath = result.DirectoryPath;
            _lastCrashReportPath = result.ReportPath;
            AddDiagnostic(
                $"Package crash bundle created: {result.DirectoryPath}; bytes={result.TotalBytes}; crashId={result.LunarCrashIdentifier ?? "not-found"}");
            void Update()
            {
                CrashActionsPanel.Visibility = Visibility.Visible;
                OpenDiagnosticsButton.Visibility = Visibility.Collapsed;
            }
            if (Dispatcher.CheckAccess()) Update();
            else Dispatcher.BeginInvoke(Update);
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Package crash bundle failed: {exception.Message}");
        }
    }

    private void OpenCrashReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastCrashBundlePath) ||
            string.IsNullOrWhiteSpace(_lastCrashReportPath))
            return;
        try
        {
            if (!File.Exists(_lastCrashReportPath))
                throw new FileNotFoundException("The failed-launch report is missing.", _lastCrashReportPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = _lastCrashReportPath,
                UseShellExecute = true
            })?.Dispose();
        }
        catch (Exception exception)
        {
            try
            {
                OpenFolder(_lastCrashBundlePath);
            }
            catch
            {
            }
            var message = T(
                "Не удалось открыть report.txt. Открыта папка отчёта.",
                "Could not open report.txt. The report folder was opened.");
            AddDiagnostic($"Unable to open failed-launch report: {exception.Message}");
            SetStatus(T("Не удалось открыть отчёт", "Could not open report"), StatusLevel.Error, message);
            MessageBox.Show(this, message, "Moonrise", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string? TryGetWeaveLoaderVersion()
    {
        return File.Exists(_paths.WeaveAgentPath)
            ? WeaveAgentService.Version
            : null;
    }

    private static string CleanupLaunchSession(
        LaunchSession launchSession,
        SanitizedLaunchReport? launchReport)
    {
        try
        {
            launchSession.Dispose();
            var result = launchSession.CleanupSucceeded ? "success" : "incomplete";
            launchReport?.Set("cleanupResult", result);
            return result;
        }
        catch (Exception exception)
        {
            launchReport?.Set("cleanupResult", "failed");
            launchReport?.Set("cleanupError", exception.Message);
            return "failed";
        }
    }

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(string.Join(Environment.NewLine, _diagnostics));
        SetStatus(T("Диагностика скопирована", "Diagnostics copied"), StatusLevel.Ready);
    }

    private void AddDiagnostic(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => AddDiagnostic(message)); return; }
        _diagnostics.Add($"{DateTime.Now:HH:mm:ss}  {TokenRedactor.Redact(message)}");
        while (_diagnostics.Count > 300) _diagnostics.RemoveAt(0);
        _logger?.Write(message);
    }

    private void SetStatus(string text, StatusLevel level, string? detail = null, string? headerText = null)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatus(text, level, detail, headerText)); return; }
        _lastStatusLevel = level;
        var brushKey = level switch
        {
            StatusLevel.Ready => "Success",
            StatusLevel.Warning => "Warning",
            StatusLevel.Error => "Danger",
            _ => "AccentPurple"
        };
        var brush = (Brush)FindResource(brushKey);
        OpenDiagnosticsButton.Visibility = level == StatusLevel.Error ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = text; HeaderStatusText.Text = headerText ?? text;
        LaunchStatusIndicator.Foreground = brush;
        LaunchStatusIndicator.Tag = level.ToString();
        HeaderStatusIndicator.Foreground = brush;
        HeaderStatusIndicator.Tag = level.ToString();
        StatusDot.Fill = brush;
        HeaderStatusDot.Fill = brush;
        StatusRing.Stroke = brush;
        HeaderStatusRing.Stroke = brush;
        StatusPulse.Fill = brush;
        HeaderStatusPulse.Fill = brush;
        StatusDetailText.Text = detail ?? string.Empty;
        foreach (var indicator in new[] { LaunchStatusIndicator, HeaderStatusIndicator })
        {
            indicator.BeginAnimation(OpacityProperty, null);
            indicator.Opacity = 1;
            if (level == StatusLevel.Working && !_motion.ReducedMotion)
                indicator.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.78, 1, TimeSpan.FromMilliseconds(1050))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    }, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void ShowError(string message)
    {
        AddDiagnostic($"UI error: {message}");
        var friendly = message.ReplaceLineEndings(" ").Trim();
        if (friendly.Length > 180)
            friendly = friendly[..177] + "…";
        ShowToast(ToastKind.Error, T("Не удалось выполнить действие", "Action could not be completed"), friendly, autoDismiss: false);
    }

    private void ShowToast(ToastKind kind, string title, string message = "", bool autoDismiss = true)
    {
        var lifetime = autoDismiss
            ? TimeSpan.FromMilliseconds(kind is ToastKind.Warning or ToastKind.Error ? 8000 : 4500)
            : (TimeSpan?)null;
        _toasts.Show(kind, title, message, lifetime);
    }

    private void ToastCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ToastMessage toast)
            _toasts.Dismiss(toast);
    }

    private void ToastBorder_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border toast)
            return;
        toast.RenderTransform = new TranslateTransform();
        toast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(_motion.ReducedMotion ? MotionController.FastMilliseconds : MotionController.ToastMilliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
        if (_motion.AllowsTranslation)
            ((TranslateTransform)toast.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(MotionController.ToastMilliseconds))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                }, HandoffBehavior.SnapshotAndReplace);
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (SupportOverlay.Visibility == Visibility.Visible)
            CloseSupportDialog();
        if (!BwhRelayLifetimePolicy.ShouldHideInsteadOfClose(
                _bwhApiRelay?.IsRunning == true,
                _explicitExitRequested))
            return;

        e.Cancel = true;
        Hide();
        AddDiagnostic("Moonrise window hidden; BWH network bridge remains active until Minecraft exits.");
    }

    private void StopBwhRelayAfterLastGame()
    {
        if (_bwhApiRelay is null ||
            !BwhRelayLifetimePolicy.ShouldStopAfterGameExit(
                _activeGameProcessIds.Count,
                Volatile.Read(ref _launchInProgress) == 1))
            return;

        _bwhApiRelay.Dispose();
        _bwhApiRelay = null;
        AddDiagnostic("BWH network bridge stopped after the last Minecraft instance exited.");
        if (BwhRelayLifetimePolicy.ShouldExitHiddenHost(IsVisible, _settings.CloseAfterLaunch))
        {
            _explicitExitRequested = true;
            Close();
        }
    }

    private void InitializeTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("Assets/moonrise-tray.ico", UriKind.Relative));
        if (resource?.Stream is null) return;

        using (resource.Stream)
        using (var source = new Drawing.Icon(resource.Stream))
        {
            _trayOpenItem = new Forms.ToolStripMenuItem("Open Moonrise");
            _trayExitItem = new Forms.ToolStripMenuItem("Exit");
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(_trayOpenItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_trayExitItem);
            _trayIcon = new Forms.NotifyIcon
            {
                ContextMenuStrip = menu,
                Icon = (Drawing.Icon)source.Clone(),
                Text = "Moonrise",
                Visible = true
            };
        }

        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(RestoreFromExternalActivation);
        _trayOpenItem.Click += (_, _) => Dispatcher.BeginInvoke(RestoreFromExternalActivation);
        _trayExitItem.Click += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _explicitExitRequested = true;
            Close();
        });
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _supportDialogCancellation?.Cancel();
        _supportDialogCancellation?.Dispose();
        _supportFlow?.Dispose();
        _supportHttpClient?.Dispose();
        _toasts.Clear();
        _themeService.Dispose();
        if (!_visualQaMode)
            SaveSettings();
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _packageScanCancellation?.Cancel();
        _packageScanCancellation?.Dispose();
        foreach (var watcher in _packageWatchers)
            watcher.Dispose();
        _packageWatchers.Clear();
        _lunarWindowCancellation?.Cancel();
        _lunarWindowCancellation?.Dispose();
        _bwhApiRelay?.Dispose();
        _bwhApiRelay = null;
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private sealed class VisualQaSupportApiClient(string status, bool crypto) : ISupportApiClient
    {
        public Task<SupportMethodsResponse> GetMethodsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SupportMethodsResponse([
                new SupportMethod(
                    "telegram_stars",
                    true,
                    "XTR",
                    [50, 100, 250, 500],
                    new SupportCustomAmount(true, 1, 10000)),
                new SupportMethod(
                    "direct_crypto",
                    true,
                    "USD",
                    [1, 5, 10, 25],
                    new SupportCustomAmount(true, 1, 1000000),
                    null,
                    [new SupportAsset("USDT", "Tether USD", "ethereum", "Ethereum (ERC-20)", 6, "token", true)])
            ]));

        public Task<SupportCheckoutResponse> CreateCheckoutAsync(
            string methodId,
            SupportCheckoutRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SupportCheckoutResponse(
                "visual-qa-payment",
                methodId,
                crypto ? "USDT" : "XTR",
                request.Amount,
                crypto ? null : "https://t.me/$moonrise-visual-qa",
                DateTimeOffset.UtcNow.AddMinutes(30),
                "visual-qa-memory-only-token",
                null,
                crypto ? request.Amount : null,
                crypto ? "USDT" : null,
                crypto ? "ethereum" : null,
                crypto ? "Ethereum (ERC-20)" : null,
                crypto ? "1.003821" : null,
                crypto ? "0x1111111111111111111111111111111111111111" : null,
                crypto ? "ethereum:0xdAC17F958D2ee523a2206206994597C13D831ec7@1/transfer?address=0x1111111111111111111111111111111111111111&uint256=1003821" : null));

        public Task<SupportStatusResult> GetPaymentStatusAsync(
            string paymentIntentId,
            string statusToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SupportStatusResult(
                new PublicPaymentStatus(
                    paymentIntentId,
                    crypto ? "direct_crypto" : "telegram_stars",
                    crypto ? "USDT" : "XTR",
                    crypto ? 1 : status == "paid" ? 100 : 50,
                    status,
                    DateTimeOffset.UtcNow.AddMinutes(30),
                    status == "paid" ? DateTimeOffset.UtcNow : null),
                TimeSpan.FromSeconds(30)));
    }

    private enum StatusLevel { Working, Ready, Warning, Error }
}
