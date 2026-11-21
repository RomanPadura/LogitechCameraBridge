using DirectShowLib;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Connector.Tests;

public class ControllerReportLoopTests
{
    [Theory]
    [InlineData("silence")]
    [InlineData("other-report-id")]
    [InlineData("short-report")]
    public void HeldButtonRepeatsWithoutFurtherPressReportsAndStopsOnUserReleaseSequence(string input)
    {
        var reports = new Queue<(long Time, byte[]? Data)>();
        reports.Enqueue((0, Report(0x10)));
        for (int time = 50; time < 1000; time += 50)
            reports.Enqueue((time, input switch
            {
                "other-report-id" => [0x0B, 0x00],
                "short-report" => [0x0A],
                _ => null
            }));
        reports.Enqueue((1000, Report(0x00)));
        reports.Enqueue((1001, Report(0xFF)));
        reports.Enqueue((1002, Report(0x00)));
        reports.Enqueue((2000, null));
        using var stop = new CancellationTokenSource();
        long now = 0;
        int moves = 0;
        var logger = new RecordingLogger();
        var buttons = new ControllerButtonRepeater(
            new Dictionary<int, (CameraControlProperty, int)> { [0x10] = (CameraControlProperty.Zoom, 1) },
            (_, _) => { moves++; return true; }, logger.LogInfo);
        byte[] Read()
        {
            if (!reports.TryDequeue(out var next)) { stop.Cancel(); return []; }
            now = next.Time;
            return next.Data ?? throw new TimeoutException();
        }
        ControllerReportLoop.Run(Read, 0x0A, buttons, logger, stop.Token, () => now);
        Assert.Equal(7, moves); // Initial press, then 400/500/600/700/800/900 ms.
        Assert.Contains(logger.Messages, message => message.Contains("released after 1000ms; repeats=6"));
        Assert.Equal(4, logger.Messages.Count(message => message.StartsWith("HID Report:")));
    }

    [Fact]
    public void ShortPressDoesNotRepeatAndCancellationBeforeDeadlinePreventsMovement()
    {
        using var stop = new CancellationTokenSource();
        long now = 0;
        int reads = 0, moves = 0;
        var buttons = new ControllerButtonRepeater(
            new Dictionary<int, (CameraControlProperty, int)> { [0x10] = (CameraControlProperty.Zoom, 1) },
            (_, _) => { moves++; return true; });
        byte[] Read()
        {
            reads++;
            if (reads == 1) return Report(0x10);
            if (reads == 2) { now = 100; return Report(0x00); }
            now = 500;
            stop.Cancel();
            return Report(0x10);
        }
        ControllerReportLoop.Run(Read, 0x0A, buttons, new RecordingLogger(), stop.Token, () => now);
        Assert.Equal(1, moves);
    }

    [Fact]
    public void DisconnectPropagatesWithoutIssuingAnotherMovement()
    {
        int reads = 0, moves = 0;
        var buttons = new ControllerButtonRepeater(
            new Dictionary<int, (CameraControlProperty, int)> { [0x10] = (CameraControlProperty.Zoom, 1) },
            (_, _) => { moves++; return true; });
        byte[] Read() => ++reads == 1 ? Report(0x10) : throw new IOException("Disconnected");
        Assert.Throws<IOException>(() => ControllerReportLoop.Run(Read, 0x0A, buttons,
            new RecordingLogger(), default, () => reads == 1 ? 0 : 500));
        Assert.Equal(1, moves);
    }

    private static byte[] Report(byte code)
    {
        var report = new byte[13];
        report[0] = 0x0A;
        report[1] = code;
        return report;
    }

    private sealed class RecordingLogger : ICameraBridgeLogger
    {
        public List<string> Messages { get; } = [];
        public void LogInfo(string message) => Messages.Add(message);
        public void LogError(string message) => Messages.Add(message);
    }
}
