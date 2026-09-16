using System.Windows.Threading;
using System.Xml.Linq;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class Stage3VisualPolishTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Moonlight.xaml")]
    [InlineData("Obsidian.xaml")]
    [InlineData("Aurora.xaml")]
    public void ThemeResourceDictionariesLoadAndExposeSemanticResources(string file)
    {
        var palette = XDocument.Parse(Read("src", "Moonrise", "Themes", "Palettes", file));
        var keys = palette.Root!.Elements()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var key in new[]
                 {
                     "BackgroundPrimary", "SurfacePrimary", "SurfaceRaised", "SurfaceFloating", "SurfaceSelected",
                     "BorderPrimary", "BorderSubtle", "FocusRing", "TextPrimary", "TextSecondary", "TextMuted",
                     "AccentPurple", "AccentBlue", "AccentCyan", "Success", "Warning", "Danger", "PrimaryGradient",
                     "SidebarGradient", "HeroOverlay", "DrawerSurface", "OverlaySurface", "ScrollThumb"
                 })
            Assert.Contains(key, keys);
    }

    [Fact]
    public void ThemeServiceExposesOnlyTheThreeBuiltInPalettes()
    {
        Assert.Equal(["moonlight", "obsidian", "aurora"], ThemeService.BuiltInThemeSources.Keys);
        var packs = new AppearancePackService().LoadThemes(Path.Combine(Path.GetTempPath(), "moonrise-stage3-theme-test"));
        Assert.Equal(["moonlight", "obsidian", "aurora"], packs.Select(pack => pack.Id));
    }

    [Fact]
    public void SelectedThemePersistsThroughSettingsService()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var service = new AppSettingsService(path);
        service.Save(new MoonriseSettings { Theme = "aurora" });
        Assert.Equal("aurora", service.Load().Theme);
    }

    [Fact]
    public void MotionPolicyDisablesNonessentialTranslationAndUsesReplacementHandoff()
    {
        var reduced = new MotionController(reducedMotion: true);
        Assert.True(reduced.ReducedMotion);
        Assert.False(reduced.AllowsTranslation);
        Assert.Contains("HandoffBehavior.SnapshotAndReplace", Read("src", "Moonrise", "Services", "MotionController.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ToastQueueNeverExceedsThreeVisibleItems()
    {
        var controller = new ToastController(Dispatcher.CurrentDispatcher);
        for (var index = 0; index < 5; index++)
            controller.Show(ToastKind.Info, $"Toast {index}");
        Assert.Equal(ToastController.MaximumVisible, controller.Visible.Count);
        Assert.Equal("Toast 2", controller.Visible[0].Title);
        controller.Clear();
    }

    [Fact]
    public void DrawersEscapeDialogsAndPackageCommandsRemainConnected()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");
        foreach (var handler in new[]
                 {
                     "ClosePackageDetailsButton_Click", "PackageEnabled_Click", "RemovePackageButton_Click",
                     "OpenSelectedPackageLocationButton_Click", "DeleteConfirmationCancelButton_Click",
                     "DeleteConfirmationConfirmButton_Click", "ToastCloseButton_Click"
                 })
            Assert.Contains(handler, xaml, StringComparison.Ordinal);
        Assert.Contains("Key.Escape", code, StringComparison.Ordinal);
        Assert.Contains("PackagesList.SelectedItem = null", code, StringComparison.Ordinal);
        Assert.Contains("CatalogList.SelectedItem = null", code, StringComparison.Ordinal);
        Assert.Contains("MaximumVisible = 3", Read("src", "Moonrise", "Services", "ToastController.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeSelectorAndLocalizedEmptyDropErrorStatesExistWithoutAccountUi()
    {
        var xaml = Read("src", "Moonrise", "MainWindow.xaml");
        var code = Read("src", "Moonrise", "MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"ThemePreviewList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CatalogErrorPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("T(\"Перетащите JAR-файлы сюда\", \"Drop JAR files here\")", code, StringComparison.Ordinal);
        Assert.Contains("T(\"Каталог недоступен\", \"Catalog unavailable\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("subscription", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", xaml, StringComparison.OrdinalIgnoreCase);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Moonrise.Tests", Guid.NewGuid().ToString("N"));
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
