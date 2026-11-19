using LogitechCameraBridge.Wpf.Models;
using LogitechCameraBridge.Common.Options;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class MonitoredBridgeSession(
    BridgeSession inner,
    BridgeActivity activity,
    IOptions<CameraToControlOptions> cameraOptions) : IBridgeSession
{
    private int _running;

    public async Task RunAsync(IProgress<BridgeNotification> progress, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("The bridge is already running.");
        string? path = cameraOptions.Value.DevicePath;
        activity.Starting(path);
        try
        {
            await inner.RunAsync(new MonitoredProgress(progress, activity, path), cancellationToken);
            activity.Stopped(path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity.Stopped(path);
            throw;
        }
        catch (Exception ex)
        {
            activity.Stopped(path, ex.Message);
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private sealed class MonitoredProgress(IProgress<BridgeNotification> next, BridgeActivity activity, string? path)
        : IProgress<BridgeNotification>
    {
        public void Report(BridgeNotification notification)
        {
            if (notification.IsReady)
                activity.Ready(path);
            else if (notification.Error is { } error)
                activity.CameraError(path, error);
            next.Report(notification);
        }
    }
}
