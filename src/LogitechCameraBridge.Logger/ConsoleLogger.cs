namespace LogitechCameraBridge.Logger;

public static class ConsoleLogger
{
    public static void LogInfo(string message, bool LogToConsole = false, bool isError = false)
    {
        if (!LogToConsole)
            return;

        if (isError)
            Console.Error.WriteLine(message);
        else
            Console.WriteLine(message);
    }
}
