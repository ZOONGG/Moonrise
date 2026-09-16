using System.Collections.ObjectModel;
using System.Windows.Threading;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class ToastController
{
    public const int MaximumVisible = 3;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, CancellationTokenSource> _dismissals = [];

    public ToastController(Dispatcher dispatcher) => _dispatcher = dispatcher;
    public ObservableCollection<ToastMessage> Visible { get; } = [];

    public ToastMessage Show(ToastKind kind, string title, string message = "", TimeSpan? lifetime = null)
    {
        while (Visible.Count >= MaximumVisible)
            Remove(Visible[0]);
        var toast = new ToastMessage { Kind = kind, Title = title, Message = message };
        Visible.Add(toast);
        if (lifetime is { } duration)
        {
            var cancellation = new CancellationTokenSource();
            _dismissals[toast.Id] = cancellation;
            _ = DismissLaterAsync(toast, duration, cancellation.Token);
        }
        return toast;
    }

    public void Remove(ToastMessage toast)
    {
        if (_dismissals.Remove(toast.Id, out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        Visible.Remove(toast);
    }

    public void Dismiss(ToastMessage toast) => _ = DismissAnimatedAsync(toast, CancellationToken.None);

    public void Clear()
    {
        foreach (var toast in Visible.ToArray())
            Remove(toast);
    }

    private async Task DismissLaterAsync(ToastMessage toast, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(duration, cancellationToken);
            await DismissAnimatedAsync(toast, cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    private async Task DismissAnimatedAsync(ToastMessage toast, CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(() => toast.IsClosing = true);
        await Task.Delay(MotionController.ToastMilliseconds, cancellationToken);
        await _dispatcher.InvokeAsync(() => Remove(toast));
    }
}
