namespace Moonrise.Services;

internal static class LaunchSingleFlight
{
    internal static bool TryEnter(ref int gate) =>
        Interlocked.CompareExchange(ref gate, 1, 0) == 0;

    internal static bool TrySend(ref int sent) =>
        Interlocked.CompareExchange(ref sent, 1, 0) == 0;

    internal static bool Exit(ref int gate) =>
        Interlocked.Exchange(ref gate, 0) != 0;
}
