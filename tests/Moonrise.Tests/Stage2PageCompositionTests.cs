using System.Xml.Linq;
using Xunit;

namespace Moonrise.Tests;

public sealed class Stage2PageCompositionTests
{
    [Fact]
    public void HomeCompositionKeepsRealLaunchAndLoadoutCommands()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");

        foreach (var command in new[]
                 {
                     "LaunchButton_Click", "ShowLunarButton_Click", "ClientComboBox_SelectionChanged",
                     "VersionComboBox_SelectionChanged", "LoadoutComboBox_SelectionChanged",
                     "SaveLoadoutButton_Click", "DeleteLoadoutButton_Click", "ExportLoadoutButton_Click",
                     "ImportLoadoutButton_Click", "OpenLibraryButton_Click"
                 })
            Assert.Contains(command, xaml, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"LaunchProfileCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ModsLoadoutCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AgentsLoadoutCard\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryFiltersSelectionDrawerAndDropTargetRemainFunctional()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");

        foreach (var filter in new[] { "AllKindButton", "ModsKindButton", "AgentsKindButton", "UnclassifiedKindButton" })
            Assert.Contains($"x:Name=\"{filter}\"", xaml, StringComparison.Ordinal);
        foreach (var command in new[]
                 {
                     "PackageKindButton_Click", "PackageEnabled_Click", "RemovePackageButton_Click",
                     "AddPackageButton_Click", "Library_Drop", "OpenSelectedPackageLocationButton_Click",
                     "ClosePackageDetailsButton_Click"
                 })
            Assert.Contains(command, xaml, StringComparison.Ordinal);

        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PackagesList.SelectedItem = null", code, StringComparison.Ordinal);
        Assert.Contains("DropTargetOverlay.Visibility", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsRowsKeepExistingControlsAndDeveloperVisibilityRules()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");

        foreach (var command in new[]
                 {
                     "BrowseLauncherButton_Click", "BackgroundLaunchCheckBox_Click", "RevealLunarCheckBox_Click",
                     "CloseAfterLaunchCheckBox_Click", "SafeLaunchCheckBox_Click", "CatalogAutoUpdateCheckBox_Click",
                     "LanguagePackComboBox_SelectionChanged", "ThemeComboBox_SelectionChanged",
                     "DeveloperModeCheckBox_Click", "OpenDiagnosticsButton_Click", "OpenLogsButton_Click"
                 })
            Assert.Contains(command, xaml, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"SettingsScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("_settings.DeveloperMode || _visualQaMode", code, StringComparison.Ordinal);
        Assert.Contains("DevelopersTabButton.Visibility = _settings.DeveloperMode", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveContainersAndLocalizedStage2LabelsResolve()
    {
        var xaml = XDocument.Parse(Read("src", "Moonrise", "MainWindow.xaml"));
        var rawXaml = xaml.ToString(SaveOptions.DisableFormatting);
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");

        foreach (var element in new[]
                 {
                     "LaunchProfileCard", "CatalogKindComboBox", "LibraryPage",
                     "PackageDetailsDrawer", "SettingsScrollViewer"
                 })
            Assert.Contains($"Name=\"{element}\"", rawXaml, StringComparison.Ordinal);

        Assert.Contains("SizeChanged=\"MainWindow_SizeChanged\"", rawXaml, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", code, StringComparison.Ordinal);
        Assert.Contains("T(\"Пакеты не найдены\", \"No packages found\")", code, StringComparison.Ordinal);
        Assert.Contains("T(\"Показать файл\", \"Open location\")", code, StringComparison.Ordinal);
        Assert.Contains("T(\"Перетащите JAR-файлы сюда\", \"Drop JAR files here\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage2AddsNoMockOnlyProductControls()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        foreach (var unsupported in new[] { "Account", "Subscription", "Premium plan", "Online players", "Fake downloads" })
            Assert.DoesNotContain(unsupported, xaml, StringComparison.OrdinalIgnoreCase);
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
