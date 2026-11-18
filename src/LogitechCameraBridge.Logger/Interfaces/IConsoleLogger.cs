namespace LogitechCameraBridge.Logger.Interfaces;

public interface ICameraBridgeLogger
{
    void LogInfo(string message, bool LogToConsole = false);

    void LogError(string message, bool LogToConsole = false);
}
