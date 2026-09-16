namespace Moonrise.Services;

public interface ISessionCountProvider
{
    int ActiveSessionCount { get; }
}
public sealed class LocalSessionCountProvider(Func<int> count) : ISessionCountProvider
{
    public int ActiveSessionCount => count();
}
