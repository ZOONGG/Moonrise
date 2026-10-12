using System.Runtime.ExceptionServices;
using Xunit;
using Forms = System.Windows.Forms;

namespace Moonrise.Tests;

public sealed class TrayIconLifecycleTests
{
    [Fact]
    public void FinalExitRemovesIconAndDisposesMenuRepeatedly()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    var menu = new Forms.ContextMenuStrip();
                    Forms.NotifyIcon? icon = new()
                    {
                        Icon = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone(),
                        ContextMenuStrip = menu,
                        Text = "Moonrise lifecycle test",
                        Visible = true
                    };
                    var original = icon;
                    MainWindow.DisposeTrayIcon(ref icon);
                    Assert.Null(icon);
                    Assert.False(original.Visible);
                    Assert.True(menu.IsDisposed);
                    MainWindow.DisposeTrayIcon(ref icon);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Tray lifecycle test timed out");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
