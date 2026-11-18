using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Common.Options;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Logger;

public class CameraBridgeLogger(IOptions<LoggerOptions> options) : ICameraBridgeLogger
{
    private static readonly SemaphoreSlim FileSemaphore = new(1, 1);
    private static readonly object QueueLock = new();
    private static Task _pendingWrites = Task.CompletedTask;
    private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "logger.txt");
    private readonly LoggerOptions _options = options.Value;

    public void LogInfo(string message)
    {
        if (_options.LogToConsole)
            Console.WriteLine(message);

        LogToFile("Info", message);
    }

    public void LogError(string message)
    {
        if (_options.LogToConsole)
            Console.Error.WriteLine(message);

        LogToFile("Error", message);
    }

    private void LogToFile(string severity, string message)
    {
        if (_options.MaxTextFileLines <= 0)
            return;

        // Capture the timestamp at the call site and queue in arrival order.
        var text = message.ReplaceLineEndings("\\n");
        var entry = $"{DateTimeOffset.Now:O}|{severity}|{text}";
        var maxLines = _options.MaxTextFileLines;

        lock (QueueLock)
        {
            var previousWrite = _pendingWrites;
            _pendingWrites = Task.Run(async () =>
            {
                await previousWrite.ConfigureAwait(false);
                await WriteToFileAsync(entry, maxLines).ConfigureAwait(false);
            });
        }
    }

    /// <summary>Waits for file entries queued so far, when explicitly needed.</summary>
    public Task FlushAsync()
    {
        lock (QueueLock)
            return _pendingWrites;
    }

    private static async Task WriteToFileAsync(string entry, int maxLines)
    {
        await FileSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var previousEntries = new List<string>();
            if (maxLines > 1 && File.Exists(LogFilePath))
            {
                using var reader = new StreamReader(LogFilePath);
                while (previousEntries.Count < maxLines - 1 &&
                    await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    previousEntries.Add(line);
                }
            }

            await File.WriteAllLinesAsync(LogFilePath, new[] { entry }.Concat(previousEntries))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Observe fire-and-forget failures without stopping later log entries.
            Console.Error.WriteLine($"Failed to write logger.txt: {ex.Message}");
        }
        finally
        {
            FileSemaphore.Release();
        }
    }
}
