namespace SWCouponManager;

internal enum WindowCloseDisposition
{
    Exit,
    HideToTray
}

internal static class BackgroundAgentLifecycle
{
    internal static WindowCloseDisposition DecideClose(bool backgroundEnabled, bool explicitExit) =>
        backgroundEnabled && !explicitExit
            ? WindowCloseDisposition.HideToTray
            : WindowCloseDisposition.Exit;
}

internal sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owns;

    private SingleInstanceLease(Mutex mutex, bool owns)
    {
        _mutex = mutex;
        _owns = owns;
    }

    internal static SingleInstanceLease? TryAcquire(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("mutex 이름이 필요합니다.", nameof(name));
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew) return new SingleInstanceLease(mutex, owns: true);
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_owns)
        {
            _mutex.ReleaseMutex();
            _owns = false;
        }
        _mutex.Dispose();
    }
}
