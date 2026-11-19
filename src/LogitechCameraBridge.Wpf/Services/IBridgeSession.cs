using LogitechCameraBridge.Wpf.Models;

namespace LogitechCameraBridge.Wpf.Services;

public interface IBridgeSession
{
    /// <summary>Completes only after the worker has released its device resources.</summary>
    Task RunAsync(IProgress<BridgeNotification> progress, CancellationToken cancellationToken);
}
