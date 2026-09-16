using System.Xml.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Moonrise.Tests;

public sealed class DesignRedesignTests
{
    [Fact]
    public void PrimaryNavigationIsHorizontalHeaderOrderedAndDiagnosticsIsNotPermanent()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var home = xaml.IndexOf("x:Name=\"HomeTabButton\"", StringComparison.Ordinal);
        var catalog = xaml.IndexOf("x:Name=\"CatalogTabButton\"", StringComparison.Ordinal);
        var library = xaml.IndexOf("x:Name=\"LibraryTabButton\"", StringComparison.Ordinal);
        var settings = xaml.IndexOf("x:Name=\"SettingsTabButton\"", StringComparison.Ordinal);

        Assert.True(home >= 0 && home < catalog && catalog < library && library < settings);
        Assert.DoesNotContain("DiagnosticsTabButton", xaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"72\"/><RowDefinition Height=\"68\"/>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GitHubButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"YouTubeButton\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeKeepsTheRequestedClassicDesktopComposition()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");

        Assert.Contains("Width=\"1180\" Height=\"760\" MinWidth=\"980\" MinHeight=\"660\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"1.45*\"/><ColumnDefinition Width=\"18\"/><ColumnDefinition Width=\"*\"/>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LaunchProfileCard\" Style=\"{StaticResource LaunchCard}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LaunchButton\" Content=\"Launch\"", xaml, StringComparison.Ordinal);

        var mods = xaml.IndexOf("x:Name=\"ModsLoadoutCard\"", StringComparison.Ordinal);
        var agents = xaml.IndexOf("x:Name=\"AgentsLoadoutCard\"", StringComparison.Ordinal);
        Assert.True(mods >= 0 && agents > mods);
    }

    [Fact]
    public void DeveloperToolsRemainHiddenUntilDeveloperModeOrVisualQa()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"DevelopersTabButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml[xaml.IndexOf("x:Name=\"DevelopersTabButton\"", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("_settings.DeveloperMode || _visualQaMode", code, StringComparison.Ordinal);
        Assert.Contains("DeveloperModeCheckBox_Click", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignSystemDictionariesAreValidAndExposeImportantStyles()
    {
        var themeDirectory = Path.Combine(RepositoryRoot(), "src", "Moonrise", "Themes");
        var expected = new[]
        {
            "MoonriseColors.xaml",
            "MoonriseTypography.xaml",
            "MoonriseSpacing.xaml",
            "MoonriseIcons.xaml",
            "MoonriseAnimations.xaml",
            "MoonriseControls.xaml"
        };
        foreach (var file in expected)
            Assert.NotNull(XDocument.Parse(File.ReadAllText(Path.Combine(themeDirectory, file))).Root);

        var controls = File.ReadAllText(Path.Combine(themeDirectory, "MoonriseControls.xaml"));
        foreach (var style in new[] { "PrimaryButton", "GhostButton", "DangerButton", "IconButton", "NavButton", "SearchInput", "SmoothSwitch", "Badge", "Card", "Skeleton", "Toast" })
            Assert.Contains($"x:Key=\"{style}\"", controls, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizationAndRealPackageFiltersRemainConnected()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");

        foreach (var filter in new[] { "AllKindButton", "ModsKindButton", "AgentsKindButton", "UnclassifiedKindButton" })
            Assert.Contains($"x:Name=\"{filter}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PackageKindButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("GetSelectedPackageFolder", code, StringComparison.Ordinal);
        Assert.Contains("_paths.UnclassifiedPackagesDirectory", code, StringComparison.Ordinal);
        Assert.Contains("T(\"Перетащите JAR-файлы сюда\", \"Drop JAR files here\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchVisualStatesAndStaleErrorClearingAreExplicit()
    {
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");
        foreach (var state in new[] { "Подготовка запуска", "Ожидание Minecraft", "Minecraft запущен", "Требуется действие", "Запуск завершился ошибкой" })
            Assert.Contains(state, code, StringComparison.Ordinal);
        Assert.Contains("StatusDetailText.Text = detail ?? string.Empty", code, StringComparison.Ordinal);
        Assert.Contains("LaunchProgressPanel.Visibility", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityResolutionDoesNotReadWpfControlsFromWorkerThread()
    {
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");

        Assert.Contains("var selectedVersion = SelectedVersion;", code, StringComparison.Ordinal);
        Assert.Contains("_compatibilityBuilds.Resolve(selectedVersion, enabledMods)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_compatibilityBuilds.Resolve(SelectedVersion, enabledMods)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogAndEscapeCommandsRemainFunctional()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");
        Assert.Contains("DeleteConfirmationCancelButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("DeleteConfirmationConfirmButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewKeyDown=\"MainWindow_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CatalogList.SelectedItem = null", code, StringComparison.Ordinal);
        Assert.Contains("PackagesList.SelectedItem = null", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionUiContainsNoAccountPlanOrDesignReferenceRuntimeContent()
    {
        var root = RepositoryRoot();
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        Assert.False(Regex.IsMatch(xaml, @"\b(Premium|subscription|avatar|login)\b", RegexOptions.IgnoreCase));
        Assert.DoesNotContain("Neutral Weave Demo", xaml, StringComparison.OrdinalIgnoreCase);

        var productionText = Directory.EnumerateFiles(Path.Combine(root, "src", "Moonrise"), "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml" or ".csproj")
            .Select(File.ReadAllText);
        foreach (var text in productionText)
            Assert.DoesNotContain("docs/reference", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CosmicAssetIsPackagedAndReferenceBoardsAreNot()
    {
        var project = Read("src", "Moonrise", "Moonrise.csproj");
        Assert.Contains("Assets\\Backgrounds\\moonrise-cosmic-hero.png", project, StringComparison.Ordinal);
        Assert.DoesNotContain("sample-catalog.json", project, StringComparison.Ordinal);
        Assert.DoesNotContain("docs\\reference", project, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(segments).ToArray()));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Moonrise.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Moonrise repository root was not found.");
    }
}
