using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Connector.Interfaces;
using LogitechCameraBridge.Logger;
using LogitechCameraBridge.Wpf.Services;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.Tests;

public class BridgeSessionTests
{
    [Fact]
    public async Task CancellationWaitsForCleanupOnTheSameMtaWorkerAndRejectsOverlap()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCleanup = new ManualResetEventSlim();
        int startThread = 0, cleanupThread = 0;
        ApartmentState apartment = ApartmentState.Unknown;
        var manager = new StubManager((token, ready) =>
        {
            startThread = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
            try
            {
                ready();
                entered.SetResult();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                releaseCleanup.Wait(TimeSpan.FromSeconds(10));
                cleanupThread = Environment.CurrentManagedThreadId;
            }
        });
        var session = CreateSession(manager);
        var notifications = new List<BridgeNotification>();
        var run = session.RunAsync(new InlineProgress(notifications.Add), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(new InlineProgress(_ => { }), default));
            cancellation.Cancel();
            Assert.False(run.IsCompleted);
        }
        finally
        {
            cancellation.Cancel();
            releaseCleanup.Set();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(startThread, cleanupThread);
        Assert.Equal(ApartmentState.MTA, apartment);
        Assert.Single(notifications, notification => notification.IsReady);
    }

    [Fact]
    public async Task FailureIsObservedAndSessionCanRestart()
    {
        int attempts = 0;
        var session = CreateSession(new StubManager((_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("Camera unavailable");
        }));
        var progress = new InlineProgress(_ => { });
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(progress, default));
        Assert.Equal("Camera unavailable", failure.Message);
        await session.RunAsync(progress, default).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task PrecancelledRunDoesNotTouchDevices()
    {
        bool called = false;
        var session = CreateSession(new StubManager((_, _) => called = true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync(
            new InlineProgress(_ => { }), new CancellationToken(true)));
        Assert.False(called);
    }

    private static BridgeSession CreateSession(ICameraBridgeManager manager) => new(manager,
        new UiCameraBridgeLogger(new CameraBridgeLogger(Options.Create(new LoggerOptions
        {
            LogToConsole = false,
            MaxTextFileLines = 0
        }))));

    private sealed class InlineProgress(Action<BridgeNotification> report) : IProgress<BridgeNotification>
    {
        public void Report(BridgeNotification value) => report(value);
    }

    private sealed class StubManager(Action<CancellationToken, Action> run) : ICameraBridgeManager
    {
        public void Start(CancellationToken cancellationToken = default) => Start(cancellationToken, () => { });
        public void Start(CancellationToken cancellationToken, Action onReady) => run(cancellationToken, onReady);
    }
}
