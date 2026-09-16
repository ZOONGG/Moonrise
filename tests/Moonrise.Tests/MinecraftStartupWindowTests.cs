using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class MinecraftStartupWindowTests
{
    [Fact]
    public void BlankLwjglWindow_IsHiddenUntilMinecraftTitleIsReady()
    {
        var now = DateTimeOffset.UtcNow;
        var windows = new TestWindows
        {
            ClassName = "LWJGL"
        };
        var service = new MinecraftStartupWindowService(windows, () => now);
        var process = JavaProcess(title: string.Empty, visible: true);

        service.Observe([process]);

        Assert.Equal([(nint)100], windows.Hidden);
        Assert.Empty(windows.Restored);

        service.Observe([process with
        {
            MainWindowTitle = "Lunar Client 1.8.9",
            IsMainWindowVisible = false
        }]);

        Assert.Equal([(nint)100], windows.Restored);
    }

    [Fact]
    public void NonLwjglWindow_IsNeverHidden()
    {
        var windows = new TestWindows { ClassName = "ConsoleWindowClass" };
        var service = new MinecraftStartupWindowService(windows, () => DateTimeOffset.UtcNow);

        service.Observe([JavaProcess(title: string.Empty, visible: true)]);

        Assert.Empty(windows.Hidden);
    }

    [Fact]
    public void HiddenStartupWindow_IsRestoredAfterBoundedTimeout()
    {
        var now = DateTimeOffset.UtcNow;
        var windows = new TestWindows { ClassName = "LWJGL" };
        var service = new MinecraftStartupWindowService(windows, () => now);
        var process = JavaProcess(title: string.Empty, visible: true);
        service.Observe([process]);

        now = now.AddSeconds(16);
        service.Observe([process with { IsMainWindowVisible = false }]);

        Assert.Equal([(nint)100], windows.Restored);
    }

    private static ProcessRecord JavaProcess(string title, bool visible) =>
        new(20, "javaw", @"C:\Java\javaw.exe", 100, title, visible, IsResponding: true);

    private sealed class TestWindows : IMinecraftStartupWindowOperations
    {
        public string ClassName { get; set; } = string.Empty;
        public List<nint> Hidden { get; } = [];
        public List<nint> Restored { get; } = [];

        public string GetClassName(nint handle) => ClassName;
        public bool Hide(nint handle)
        {
            Hidden.Add(handle);
            return true;
        }
        public void Restore(nint handle) => Restored.Add(handle);
    }
}
