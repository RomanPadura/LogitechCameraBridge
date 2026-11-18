using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Common.Options;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Logger;

public class ConsoleLogger(IOptions<LoggerOptions> options) : ICameraBridgeLogger
{
    private readonly LoggerOptions _options = options.Value;

    public void LogInfo(string message)
    {
        if (!_options.LogToConsole)
            return;

        Console.WriteLine(message);
    }

    public void LogError(string message)
    {
        if (!_options.LogToConsole)
            return;

        Console.Error.WriteLine(message);
    }
}
