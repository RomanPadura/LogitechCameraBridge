using LogitechCameraBridge.Wpf.Models;

namespace LogitechCameraBridge.Wpf.Services;

/// <summary>A thread-safe snapshot of observed bridge readiness, not a requested run mode.</summary>
public sealed class BridgeActivity
{
    private BridgeActivitySnapshot _snapshot = new(null, false, null);
    public BridgeActivitySnapshot Snapshot => Volatile.Read(ref _snapshot);
    public void Starting(string? path) => Volatile.Write(ref _snapshot, new(path, false, null));
    public void Ready(string? path) => Volatile.Write(ref _snapshot, new(path, true, null));
    public void Stopped(string? path, string? error = null) => Volatile.Write(ref _snapshot, new(path, false, error));
    public void CameraError(string? path, string error)
    {
        var previous = Snapshot;
        if (previous.IsReady && string.Equals(previous.DevicePath, path, StringComparison.OrdinalIgnoreCase))
            Volatile.Write(ref _snapshot, previous with { Error = error });
    }
}
