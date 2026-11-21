using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Connector;

internal static class ControllerReportLoop
{
    public static void Run(Func<byte[]> read, int reportId, ControllerButtonRepeater buttons,
        ICameraBridgeLogger logger, CancellationToken cancellationToken, Func<long>? clock = null)
    {
        clock ??= () => Environment.TickCount64;
        bool loggedIgnoredReport = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? report = null;
            try { report = read(); }
            catch (TimeoutException) { }

            if (cancellationToken.IsCancellationRequested)
                break;
            long now = clock();
            if (report is { Length: >= 2 } && report[0] == reportId)
            {
                logger.LogInfo($"HID Report: {Convert.ToHexString(report)} [t={now}ms]");
                // Process release before any repeat due at the same moment.
                buttons.Handle(report[1], now);
            }
            else if (report is not null && !loggedIgnoredReport)
            {
                logger.LogInfo($"Ignoring unrelated/malformed HID report: {Convert.ToHexString(report)}. Repeat ticks remain active.");
                loggedIgnoredReport = true;
            }

            // Silence AND unrelated reports must allow the held button to repeat.
            if (!cancellationToken.IsCancellationRequested)
                buttons.Tick(clock());
        }
    }
}
