using LogitechCameraBridge.Connector.Models;

namespace LogitechCameraBridge.Connector.Interfaces;

public interface ICameraBridgeManager
{
    /// <summary>
    /// Connects the controller to the camera and processes reports until cancellation.
    /// </summary>
    void Start(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs synchronously on the calling thread. Invokes <paramref name="onReady"/>
    /// after camera initialization and opening the HID stream, before reading reports.
    /// All device resources are released before this method returns or throws.
    /// </summary>
    void Start(CancellationToken cancellationToken, Action onReady);

    /// <summary>Reports driver capabilities on the worker, before readiness or a Zoom startup failure.</summary>
    void Start(CancellationToken cancellationToken, Action onReady, Action<CameraControlCapabilities> onCapabilities)
        => Start(cancellationToken, onReady);
}
