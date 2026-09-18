using System.Xml.Linq;
using Xunit;

namespace Moonrise.Tests;

public sealed class UiFoundationStage1Tests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void RequiredFoundationDictionariesAreMergedAndLoadAsXml()
    {
        var app = Load("src", "Moonrise", "App.xaml");
        var sources = app.Descendants(Presentation + "ResourceDictionary")
            .Select(element => (string?)element.Attribute("Source"))
            .Where(source => source is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in FoundationFiles())
        {
            Assert.Contains($"Themes/{file}", sources);
            Assert.Equal(Presentation + "ResourceDictionary", Load("src", "Moonrise", "Themes", file).Root?.Name);
        }
    }

    [Fact]
    public void SemanticMoonlightBrushesAndComponentStylesExist()
    {
        var colors = Load("src", "Moonrise", "Themes", "MoonriseColors.xaml");
        var colorKeys = ResourceKeys(colors);
        foreach (var key in new[]
                 {
                     "BackgroundPrimary", "BackgroundSecondary", "SurfacePrimary", "SurfaceRaised", "SurfaceFloating",
                     "SurfaceHover", "SurfaceSelected", "BorderPrimary", "BorderSubtle", "TextPrimary", "TextSecondary",
                     "TextMuted", "AccentPurple", "AccentBlue", "AccentCyan", "Success", "Warning", "Danger"
                 })
            Assert.Contains(key, colorKeys);

        var controls = Load("src", "Moonrise", "Themes", "MoonriseControls.xaml");
        var controlKeys = ResourceKeys(controls);
        foreach (var key in new[]
                 {
                     "PrimaryButton", "SecondaryButton", "GhostButton", "DangerButton", "IconButton", "CompactButton",
                     "NavButton", "SearchInput", "SmoothSwitch", "StatusIndicator", "Badge", "Card",
                     "WeaveModIconTemplate", "AgentIconTemplate", "UnclassifiedIconTemplate"
                 })
            Assert.Contains(key, controlKeys);
        Assert.Contains(controls.Root!.Elements(Presentation + "Style"),
            style => (string?)style.Attribute("TargetType") == "ComboBox");
    }

    [Fact]
    public void ControlTemplateTriggersAreDirectChildrenOfTemplates()
    {
        var controls = Load("src", "Moonrise", "Themes", "MoonriseControls.xaml");
        var misplaced = controls.Descendants(Presentation + "ControlTemplate.Triggers")
            .Where(triggers => triggers.Parent?.Name != Presentation + "ControlTemplate")
            .ToArray();
        Assert.Empty(misplaced);
    }

    [Fact]
    public void RussianEnglishDeveloperAndPackageCommandsRemainConnected()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");

        Assert.Contains("LanguageOptionsPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("LanguagePackComboBox.SelectedItem = language", code, StringComparison.Ordinal);
        Assert.Contains("T(\"Главная\", \"Home\")", code, StringComparison.Ordinal);
        Assert.Contains("T(\"Настройки\", \"Settings\")", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DevelopersTabButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml[xaml.IndexOf("x:Name=\"DevelopersTabButton\"", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("DeveloperModeCheckBox_Click", xaml, StringComparison.Ordinal);

        foreach (var handler in new[]
                 {
                     "AddPackageButton_Click", "PackageEnabled_Click", "RemovePackageButton_Click",
                     "PackageKindButton_Click", "OpenPackageFolderButton_Click", "InstallCatalogPackageButton_Click"
                 })
            Assert.Contains(handler, xaml, StringComparison.Ordinal);
    }

    private static HashSet<string> ResourceKeys(XDocument document) =>
        document.Root!.Elements()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);

    private static string[] FoundationFiles() =>
    [
        "MoonriseColors.xaml", "MoonriseTypography.xaml", "MoonriseSpacing.xaml",
        "MoonriseIcons.xaml", "MoonriseAnimations.xaml", "MoonriseControls.xaml"
    ];

    private static XDocument Load(params string[] segments) => XDocument.Parse(Read(segments));

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
