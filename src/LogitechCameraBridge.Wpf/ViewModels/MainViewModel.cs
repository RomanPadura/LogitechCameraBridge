using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.ViewModels;

public partial class MainViewModel(IBridgeSession session, ICameraBridgeLogger logger) : ObservableObject
{
    private CancellationTokenSource? _cancellation;
    private Task _runTask = Task.CompletedTask;
    private bool _isClosing;

    [ObservableProperty]
    private string _status = "Stopped";

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isActive;

    private bool CanStart() => !IsActive && !_isClosing;
    private bool CanStop() => IsActive && _cancellation is { IsCancellationRequested: false };

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync()
    {
        if (!CanStart())
            return Task.CompletedTask;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Error = string.Empty;
        Status = "Starting: discovering devices and initializing camera...";
        IsActive = true;

        // Progress captures the WPF synchronization context. Ignore queued updates from old runs.
        var progress = new Progress<BridgeNotification>(notification =>
        {
            if (_cancellation != cancellation)
                return;
            if (notification.Error is { } error)
                Error = error;
            if (notification.IsReady && !cancellation.IsCancellationRequested)
                Status = "Running: camera initialized; listening for controller buttons";
        });
        _runTask = RunAsync(progress, cancellation);
        return _runTask;
    }

    private async Task RunAsync(IProgress<BridgeNotification> progress, CancellationTokenSource cancellation)
    {
        try
        {
            await session.RunAsync(progress, cancellation.Token);
            Status = "Stopped";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = "Stopped";
        }
        catch (Exception ex)
        {
            Status = "Failed: bridge stopped";
            Error = ex.Message;
            logger.LogError(ex.ToString());
        }
        finally
        {
            _cancellation = null;
            cancellation.Dispose();
            IsActive = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        RequestStop();
        await _runTask;
    }

    private void RequestStop()
    {
        if (!CanStop())
            return;
        Status = "Stopping: waiting for device resources to be released...";
        _cancellation!.Cancel();
        StopCommand.NotifyCanExecuteChanged();
    }

    public async Task ShutdownAsync()
    {
        _isClosing = true;
        StartCommand.NotifyCanExecuteChanged();
        RequestStop();
        await _runTask;
    }
}
