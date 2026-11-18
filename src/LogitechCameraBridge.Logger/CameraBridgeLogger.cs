using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Logger;

public class ConsoleLogger : ICameraBridgeLogger
{
    public void LogInfo(string message, bool LogToConsole = false)
    {
        if (!LogToConsole)
            return;

        Console.WriteLine(message);
    }

    public void LogError(string message, bool LogToConsole = false)
    {
        if (!LogToConsole)
            return;

        Console.Error.WriteLine(message);
    }
}