using LogitechCameraBridge.Wpf.Models;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Connector.Interfaces;
using LogitechCameraBridge.Logger;
using LogitechCameraBridge.Wpf.Services;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.Tests;

public class MonitoredBridgeSessionTests
{
    [Theory]
    [InlineData("stop")]
    [InlineData("cancel")]
    [InlineData("failure")]
    public async Task ReadinessIsObservedAndClearedAfterCleanup(string outcome)
    {
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = new BridgeActivity();
        var logger = new UiCameraBridgeLogger(new CameraBridgeLogger(Options.Create(new LoggerOptions
        {
            LogToConsole = false, MaxTextFileLines = 0
        })));
        bool cleanedUp = false;
        var inner = new BridgeSession(new FakeManager((token, ready) =>
        {
            try
            {
                ready();
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Test worker was not released.");
                if (outcome == "failure")
                    throw new InvalidOperationException("Camera failed");
                token.ThrowIfCancellationRequested();
            }
            finally { cleanedUp = true; }
        }), logger);
        var session = new MonitoredBridgeSession(inner, activity,
            Options.Create(new CameraToControlOptions { DevicePath = "device-1" }));
        var run = session.RunAsync(new InlineProgress(), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new BridgeActivitySnapshot("device-1", true, null), activity.Snapshot);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(new InlineProgress(), default));
            Assert.True(activity.Snapshot.IsReady);
            logger.LogError("Camera Set failed");
            Assert.Equal("Camera Set failed", activity.Snapshot.Error);
            if (outcome == "cancel")
                cancellation.Cancel();
        }
        finally { release.Set(); }

        if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        else if (outcome == "failure")
            await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        else
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(cleanedUp);
        Assert.False(activity.Snapshot.IsReady);
        Assert.Equal(outcome == "failure" ? "Camera failed" : null, activity.Snapshot.Error);
    }

    private sealed class FakeManager(Action<CancellationToken, Action> run) : ICameraBridgeManager
    {
        public void Start(CancellationToken cancellationToken = default) => Start(cancellationToken, () => { });
        public void Start(CancellationToken cancellationToken, Action onReady) => run(cancellationToken, onReady);
    }

    private sealed class InlineProgress : IProgress<BridgeNotification>
    {
        public void Report(BridgeNotification value) { }
    }
}
