using LogitechCameraBridge.Connector.Interfaces;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class BridgeSession(ICameraBridgeManager manager, UiCameraBridgeLogger logger) : IBridgeSession
{
    private int _isRunning;

    public Task RunAsync(IProgress<BridgeNotification> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            throw new InvalidOperationException("The bridge is already running.");

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            Exception? failure = null;
            void OnError(string message) => progress.Report(new(false, message));
            logger.ErrorLogged += OnError;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Discovery, camera calls, HID disposal and COM release stay on this thread.
                manager.Start(cancellationToken, () => progress.Report(new(true)),
                    capabilities => progress.Report(new(false, Capabilities: capabilities)));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                logger.ErrorLogged -= OnError;
                Interlocked.Exchange(ref _isRunning, 0);
            }

            if (failure is OperationCanceledException && cancellationToken.IsCancellationRequested)
                completion.SetCanceled(cancellationToken);
            else if (failure is not null)
                completion.SetException(failure);
            else
                completion.SetResult();
        }) { Name = "Camera bridge worker", IsBackground = false };

        try
        {
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }
        catch
        {
            Interlocked.Exchange(ref _isRunning, 0);
            throw;
        }

        return completion.Task;
    }
}
