namespace LogitechCameraBridge.Connector.Interfaces;

public interface ICameraBridgeManager
{
    /// <summary>
    /// Connects the controller to the camera and processes reports until cancellation.
    /// </summary>
    void Run(CancellationToken cancellationToken = default);
}
