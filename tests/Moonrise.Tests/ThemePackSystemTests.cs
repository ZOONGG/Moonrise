using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class ThemePackSystemTests
{
    [Fact]
    public void BuiltInsAreDistinctAndPorcelainIsActuallyLight()
    {
        var themes = ThemePackService.CreateBuiltIns().ToDictionary(item => item.Id);
        Assert.Equal(["moonlight", "ember", "porcelain"], themes.Keys);
        Assert.True(Luminance(themes["porcelain"].Colors["AppBackground"]) > .8);
        Assert.True(Luminance(themes["porcelain"].Colors["TextPrimary"]) < .08);

        var differingEmberTokens = themes["moonlight"].Colors.Count(pair =>
            themes["ember"].Colors[pair.Key] != pair.Value);
        Assert.True(differingEmberTokens > 15);
        Assert.NotEqual(themes["moonlight"].Geometry.CardRadius, themes["ember"].Geometry.CardRadius);
        Assert.NotEqual(themes["moonlight"].Variants.Cards, themes["ember"].Variants.Cards);
    }

    [Fact]
    public void CustomThemeLoadsAndGeometryIsClamped()
    {
        using var temp = new TemporaryDirectory();
        var directory = Path.Combine(temp.Path, "custom-purple");
        Directory.CreateDirectory(directory);
        WriteManifest(directory, """
        {"schemaVersion":1,"id":"custom-purple","name":"Custom Purple","author":"Tester","version":"1.0.0","base":"moonlight",
         "colors":{"AccentPrimary":"#C066FF"},"geometry":{"cardRadius":999,"controlHeight":0},
         "variants":{"cards":"bordered","navigation":"left-accent","buttons":"solid"}}
        """);

        var theme = new ThemePackService().LoadCustomTheme(directory);

        Assert.Equal("#C066FF", theme.Colors["AccentPrimary"]);
        Assert.Equal(28, theme.Geometry.CardRadius);
        Assert.Equal(34, theme.Geometry.ControlHeight);
        Assert.False(theme.IsBuiltIn);
    }

    [Fact]
    public void InvalidJsonAndUnsupportedSchemaAreRejectedWithoutRemovingBuiltIns()
    {
        using var temp = new TemporaryDirectory();
        var invalid = Path.Combine(temp.Path, "invalid");
        var future = Path.Combine(temp.Path, "future");
        Directory.CreateDirectory(invalid);
        Directory.CreateDirectory(future);
        WriteManifest(invalid, "{");
        WriteManifest(future, """{"schemaVersion":99,"id":"future-theme","name":"Future","version":"1.0.0","base":"moonlight"}""");
        var diagnostics = new List<string>();

        var themes = new ThemePackService().LoadThemes(temp.Path, diagnostics.Add);

        Assert.Equal(["moonlight", "ember", "porcelain"], themes.Select(item => item.Id));
        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, item => item.Contains("schemaVersion", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("C:\\Windows\\secret.png")]
    [InlineData("\\\\server\\share\\secret.png")]
    public void UnsafeAssetPathsAreRejected(string asset)
    {
        using var temp = new TemporaryDirectory();
        var directory = Path.Combine(temp.Path, "unsafe-theme");
        Directory.CreateDirectory(directory);
        WriteManifest(directory,
            """{"schemaVersion":1,"id":"unsafe-theme","name":"Unsafe","version":"1.0.0","base":"moonlight","background":{"mode":"image","asset":""" +
            JsonSerializer.Serialize(asset) + "}}" );

        Assert.Throws<InvalidDataException>(() => new ThemePackService().LoadCustomTheme(directory));
    }

    [Fact]
    public void MissingAssetIsRejected()
    {
        using var temp = new TemporaryDirectory();
        var directory = Path.Combine(temp.Path, "missing-asset");
        Directory.CreateDirectory(directory);
        WriteManifest(directory, """
        {"schemaVersion":1,"id":"missing-asset","name":"Missing","version":"1.0.0","base":"moonlight",
         "background":{"mode":"image","asset":"assets/missing.png"}}
        """);

        Assert.Throws<InvalidDataException>(() => new ThemePackService().LoadCustomTheme(directory));
    }

    [Fact]
    public void GeneratedTemplateIsImmediatelyValidAndDuplicateSafe()
    {
        using var temp = new TemporaryDirectory();
        var service = new ThemePackService();

        var first = service.CreateTemplate(temp.Path);
        var second = service.CreateTemplate(temp.Path);

        Assert.NotEqual(first, second);
        Assert.Equal("my-theme", service.LoadCustomTheme(first).Id);
        Assert.Equal("my-theme-2", service.LoadCustomTheme(second).Id);
        Assert.True(File.Exists(Path.Combine(first, "README.txt")));
    }

    [Theory]
    [InlineData("Obsidian")]
    [InlineData("Aurora")]
    public void LegacyThemeSelectionMigratesToMoonlight(string legacyTheme)
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, $$"""{"SettingsSchemaVersion":5,"Theme":"{{legacyTheme}}"}""");

        var settings = new AppSettingsService(path).Load();

        Assert.Equal("moonlight", settings.Theme);
        Assert.Equal(MoonriseSettings.CurrentSettingsSchemaVersion, settings.SettingsSchemaVersion);
    }

    [Fact]
    public void ImportCopiesOnlyInsideDestinationAndNeverOverwrites()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "source");
        var installed = Path.Combine(temp.Path, "installed");
        Directory.CreateDirectory(source);
        WriteManifest(source, """{"schemaVersion":1,"id":"imported-theme","name":"Imported","version":"1.0.0","base":"moonlight"}""");
        var service = new ThemePackService();

        var theme = service.ImportDirectory(source, installed);

        Assert.Equal(Path.Combine(installed, "imported-theme"), theme.SourceDirectory);
        Assert.True(File.Exists(Path.Combine(installed, "imported-theme", "theme.json")));
        Assert.Throws<IOException>(() => service.ImportDirectory(source, installed));
        Assert.Throws<InvalidDataException>(() => ThemePackService.EnsureInside(installed, Path.Combine(temp.Path, "escape.txt")));
    }

    [Fact]
    public void HotReloadKeepsPreviousThemeOnMalformedSaveAndWatcherIsDisposedWhenSwitching()
    {
        using var temp = new TemporaryDirectory();
        var directory = Path.Combine(temp.Path, "hot-theme");
        Directory.CreateDirectory(directory);
        WriteManifest(directory, """{"schemaVersion":1,"id":"hot-theme","name":"Hot","version":"1.0.0","base":"moonlight"}""");
        var packs = new ThemePackService();
        using var service = new ThemeService(packs);
        var resources = new ResourceDictionary();
        service.Apply(resources, packs.LoadCustomTheme(directory));
        Assert.True(service.HasActiveWatcher);

        WriteManifest(directory, "{");
        Thread.Sleep(650);

        Assert.Equal("hot-theme", service.CurrentThemeId);
        service.Apply(resources, ThemePackService.CreateBuiltIns().First());
        Assert.False(service.HasActiveWatcher);
    }

    [Fact]
    public void RuntimeDictionaryProjectsSemanticResourcesAndVariants()
    {
        using var service = new ThemeService();
        var resources = new ResourceDictionary();
        var porcelain = ThemePackService.CreateBuiltIns().Single(item => item.Id == "porcelain");

        service.Apply(resources, porcelain);

        var runtime = resources.MergedDictionaries.Single(item => item.Contains("MoonriseThemeRuntime"));
        Assert.IsType<SolidColorBrush>(runtime["TextPrimary"]);
        Assert.Equal("porcelain", runtime["ThemeId"]);
        Assert.Equal(new CornerRadius(12), runtime["CardRadius"]);
        Assert.Equal("soft", porcelain.Variants.Buttons);
    }

    [Fact]
    public void ThemeManagementCommandsAndLocalizationRemainConnected()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");
        foreach (var command in new[] { "OpenThemesButton_Click", "ReloadThemesButton_Click", "ImportThemeButton_Click", "CreateThemeTemplateButton_Click" })
            Assert.Contains(command, xaml, StringComparison.Ordinal);
        Assert.Contains("Theme packs", code, StringComparison.Ordinal);
        Assert.Contains("Пакеты тем", code, StringComparison.Ordinal);
        Assert.Contains("LanguagePackComboBox_SelectionChanged", xaml, StringComparison.Ordinal);
    }

    private static void WriteManifest(string directory, string json) => File.WriteAllText(Path.Combine(directory, "theme.json"), json);

    private static double Luminance(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= .03928 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(segments).ToArray()));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Moonrise.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Moonrise.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
