using LogitechCameraBridge.Logger;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Wpf.Services;

/// <summary>Forwards all logging to the existing options-controlled logger.</summary>
public sealed class UiCameraBridgeLogger(CameraBridgeLogger logger) : ICameraBridgeLogger
{
    public event Action<string>? ErrorLogged;

    public void LogInfo(string message) => logger.LogInfo(message);

    public void LogError(string message)
    {
        logger.LogError(message);
        ErrorLogged?.Invoke(message);
    }
}
