namespace LogitechCameraBridge.Logger.Interfaces;

public interface ICameraBridgeLogger
{
    void LogInfo(string message);

    void LogError(string message);
}
