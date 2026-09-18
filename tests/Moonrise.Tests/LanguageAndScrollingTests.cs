using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class LanguageAndScrollingTests
{
    [Fact]
    public void InvalidLanguageDoesNotHideOtherPacksAndReportsItsFileName()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "broken.moonrise-language.json"), "{broken");
        File.WriteAllText(Path.Combine(directory.Path, "valid.moonrise-language.json"),
            """{"code":"sr-Latn-RS","name":"Srpski","translations":{"Launch":"Pokreni"}}""");
        var errors = new List<string>();
        var packs = new AppearancePackService().LoadLanguages(directory.Path, errors.Add);
        Assert.Contains(packs, pack => pack.Code == "sr-Latn-RS");
        Assert.Contains(packs, pack => pack.Code == "ru");
        Assert.Contains("broken.moonrise-language.json", Assert.Single(errors));
    }

    [Theory]
    [InlineData("{\"code\":null,\"name\":\"Broken\"}")]
    [InlineData("{\"code\":\"xx\",\"name\":\"Broken\",\"translations\":null}")]
    [InlineData("{\"schemaVersion\":2,\"code\":\"xx\",\"name\":\"Broken\"}")]
    [InlineData("{\"code\":\"xx\",\"name\":\"Broken\",\"translations\":{\"{0} enabled\":\"{1} enabled\"}}")]
    [InlineData("{\"code\":\"xx\",\"name\":\"Broken\",\"translations\":{\"{0} enabled\":\"{broken\"}}")]
    public void InvalidMetadataAndPlaceholdersAreRejected(string json)
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "invalid.moonrise-language.json"), json);
        var errors = new List<string>();
        var packs = new AppearancePackService().LoadLanguages(directory.Path, errors.Add);
        Assert.Single(errors);
        Assert.DoesNotContain(packs, pack => pack.Code == "xx");
    }

    [Fact]
    public void CustomLanguageOverridesBundledPackAndReloadPicksUpEditsAndRemoval()
    {
        using var directory = new TestDirectory();
        var service = new AppearancePackService();
        var path = Path.Combine(directory.Path, "override.moonrise-language.json");
        File.WriteAllText(path, """{"code":"DE","name":"Custom","translations":{"Launch":"Los!"}}""");
        var custom = Assert.Single(service.LoadLanguages(directory.Path), pack => pack.Code.Equals("de", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Los!", service.Translate(custom, "Launch"));
        File.WriteAllText(path, """{"code":"DE","name":"Custom","translations":{"Launch":"Start!"}}""");
        Assert.Equal("Start!", service.Translate(service.LoadLanguages(directory.Path).Single(pack => pack.Code == "DE"), "Launch"));
        File.Delete(path);
        Assert.Equal("Starten", service.Translate(service.LoadLanguages(directory.Path).Single(pack => pack.Code == "de"), "Launch"));
    }

    [Fact]
    public void BundledPacksAndEditableTemplatesLoadWithoutOverwritingUserFiles()
    {
        using var directory = new TestDirectory();
        var service = new AppearancePackService();
        var errors = new List<string>();
        Assert.Equal(12, service.LoadLanguages(directory.Path, errors.Add).Count);
        Assert.Empty(errors);
        service.WriteLanguageExamples(directory.Path);
        var template = File.ReadAllText(Path.Combine(directory.Path, "en.template.json"));
        Assert.Contains("Open folder", template);
        File.WriteAllText(Path.Combine(directory.Path, "en.template.json"), "user work");
        service.WriteLanguageExamples(directory.Path);
        Assert.Equal("user work", File.ReadAllText(Path.Combine(directory.Path, "en.template.json")));
        Assert.Equal(12, service.LoadLanguages(directory.Path).Count);
    }

    [Theory]
    [InlineData(0, -120, 3, 66)]
    [InlineData(0, -30, 3, 16.5)]
    [InlineData(700, -120, 3, 700)]
    [InlineData(10, 120, 3, 0)]
    [InlineData(0, -120, -1, 300)]
    [InlineData(80, -120, 0, 80)]
    public void WheelUsesPixelsHonorsSystemSettingsAndClampsAtBounds(double start, int delta, int lines, double expected) =>
        Assert.Equal(expected, SmoothScroll.WheelTarget(start, 1000, 300, delta, lines));

    [Fact]
    public void WheelReachesTargetAndKeyboardCancelsPendingAnimation()
    {
        RunSta(() =>
        {
            var content = new Border { Width = 180, Height = 1600 };
            var viewer = new ScrollViewer { Content = content, Width = 200, Height = 220 };
            SmoothScroll.SetIsEnabled(viewer, true);
            var window = new Window { Content = viewer, Width = 240, Height = 260, ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow, Left = -10000, Top = -10000 };
            try
            {
                window.Show();
                Pump(50);
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                    { RoutedEvent = Mouse.PreviewMouseWheelEvent };
                content.RaiseEvent(wheel);
                if (SystemParameters.WheelScrollLines == 0) return;
                Assert.True(wheel.Handled);
                Pump(250);
                var expected = SmoothScroll.WheelTarget(0, viewer.ExtentHeight, viewer.ViewportHeight, -120, SystemParameters.WheelScrollLines);
                Assert.InRange(viewer.VerticalOffset, expected - 1, expected + 1);
                content.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                    { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                viewer.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Home)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                viewer.ScrollToTop();
                Pump(250);
                Assert.Equal(0, viewer.VerticalOffset);
            }
            finally { window.Close(); SmoothScroll.SetIsEnabled(viewer, false); }
        });
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF scroll test timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Moonrise.Tests", Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
