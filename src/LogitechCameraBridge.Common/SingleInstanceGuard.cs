using System.Runtime.Versioning;
using System.Security.Principal;

namespace LogitechCameraBridge.Common;

/// <summary>Owns the app mutex. Acquire and dispose on the same thread.</summary>
[SupportedOSPlatform("windows")]
public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    public static SingleInstanceGuard? TryAcquire()
    {
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException("Cannot identify the current Windows user.");
        // Shared by both hosts, all versions and all install paths in this Windows session.
        return TryAcquire($@"Local\LogitechCameraBridge.{sid}");
    }

    internal static SingleInstanceGuard? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; } // Previous process crashed; ownership is ours now.
            if (acquired)
                return new SingleInstanceGuard(mutex);
            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_mutex is not { } mutex)
            return;
        mutex.ReleaseMutex();
        mutex.Dispose();
        _mutex = null;
    }
}
