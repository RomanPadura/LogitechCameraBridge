using System.Windows.Threading;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using LogitechCameraBridge.Wpf.ViewModels;

namespace LogitechCameraBridge.Wpf.Tests;

public class MainViewModelTests
{
    [Fact]
    public Task ReadinessAndErrorsAreReportedOnUiAndStopWaitsForCleanup() => OnDispatcherAsync(async () =>
    {
        var session = new FakeSession();
        var model = new MainViewModel(session, new SilentLogger());
        var run = model.StartCommand.ExecuteAsync(null);
        Assert.StartsWith("Starting", model.Status);
        Assert.False(model.StartCommand.CanExecute(null));
        session.Progress!.Report(new(true));
        session.Progress.Report(new(false, "Pan is unavailable"));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.StartsWith("Running", model.Status);
        Assert.Equal("Pan is unavailable", model.Error);

        var stop = model.StopCommand.ExecuteAsync(null);
        Assert.True(session.Token.IsCancellationRequested);
        Assert.False(stop.IsCompleted);
        Assert.True(model.IsActive);
        Assert.StartsWith("Stopping", model.Status);
        session.Progress.Report(new(true));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.StartsWith("Stopping", model.Status);

        session.Completion.SetResult();
        await Task.WhenAll(run, stop);
        Assert.Equal("Stopped", model.Status);
        Assert.False(model.IsActive);
        Assert.True(model.StartCommand.CanExecute(null));

        session.Progress.Report(new(true));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal("Stopped", model.Status);
    });

    [Fact]
    public Task ShutdownWaitsAndPermanentlyDisablesStart() => OnDispatcherAsync(async () =>
    {
        var session = new FakeSession();
        var model = new MainViewModel(session, new SilentLogger());
        var run = model.StartCommand.ExecuteAsync(null);
        var closing = model.ShutdownAsync();
        Assert.True(session.Token.IsCancellationRequested);
        Assert.False(closing.IsCompleted);
        session.Completion.SetCanceled(session.Token);
        await Task.WhenAll(run, closing);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.False(model.StopCommand.CanExecute(null));
    });

    [Fact]
    public Task StartupFailureIsVisibleAndAllowsRetry() => OnDispatcherAsync(async () =>
    {
        var session = new FakeSession();
        var model = new MainViewModel(session, new SilentLogger());
        var run = model.StartCommand.ExecuteAsync(null);
        session.Completion.SetException(new InvalidOperationException("Controller not found"));
        await run;
        Assert.StartsWith("Failed", model.Status);
        Assert.Equal("Controller not found", model.Error);
        Assert.True(model.StartCommand.CanExecute(null));
    });

    private static Task OnDispatcherAsync(Func<Task> body)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await body();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private sealed class FakeSession : IBridgeSession
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<BridgeNotification>? Progress { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task RunAsync(IProgress<BridgeNotification> progress, CancellationToken cancellationToken)
        {
            Progress = progress;
            Token = cancellationToken;
            return Completion.Task;
        }
    }

    private sealed class SilentLogger : ICameraBridgeLogger
    {
        public void LogInfo(string message) { }
        public void LogError(string message) { }
    }
}
