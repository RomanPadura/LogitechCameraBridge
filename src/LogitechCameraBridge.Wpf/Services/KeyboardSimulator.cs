using System.Globalization;
using System.IO;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Logger.Interfaces;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.Services;

public enum SimulatorButton { ZoomIn, ZoomOut, Left, Right, Up, Down }

/// <summary>Starts the selected camera on demand and submits reports only after bridge readiness.</summary>
public sealed class KeyboardSimulator(
    IBridgeSession session,
    ControllerDiscoveryStub stub,
    ICameraSelectionStore selection,
    IOptions<CameraToControlOptions> cameraOptions,
    IOptions<BridgeControllerDeviceOptions> controllerOptions,
    ICameraBridgeLogger logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task _run = Task.CompletedTask;
    private bool _closing;

    public async Task PressAsync(SimulatorButton button)
    {
        // Drop keys while connecting/switching, rather than replaying a backlog of movements.
        if (_closing || !await _gate.WaitAsync(0))
            return;
        try
        {
            if (_closing)
                return;
            var camera = await selection.LoadAsync()
                ?? throw new InvalidOperationException("Select a camera from Cameras before using the simulator.");
            if (_closing)
                return;
            if (_run.IsCompleted || !string.Equals(cameraOptions.Value.DevicePath, camera.DevicePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                await StopAsync();
                if (_closing)
                    return;
                cameraOptions.Value.DevicePath = camera.DevicePath;
                _cancellation = new CancellationTokenSource();
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _run = session.RunAsync(new ReadyProgress(ready), _cancellation.Token);
                // Observe failures even when no further key is pressed.
                _ = _run.ContinueWith(task => logger.LogError(task.Exception!.ToString()), CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                if (await Task.WhenAny(ready.Task, _run) == _run)
                {
                    await _run;
                    throw new InvalidOperationException("The bridge stopped before becoming ready.");
                }
            }

            if (_closing)
                return;

            var options = controllerOptions.Value;
            string code = button switch
            {
                SimulatorButton.ZoomIn => options.ZoomInCode,
                SimulatorButton.ZoomOut => options.ZoomOutCode,
                SimulatorButton.Left => options.PanLeftCode,
                SimulatorButton.Right => options.PanRightCode,
                SimulatorButton.Up => options.TiltUpCode,
                SimulatorButton.Down => options.TiltDownCode,
                _ => throw new ArgumentOutOfRangeException(nameof(button))
            };
            if (_run.IsCompleted || !stub.TryPressButton(ParseByte(options.ReportId), ParseByte(code)))
                throw new InvalidOperationException("The simulated controller is not listening. Press again to reconnect.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ShutdownAsync()
    {
        _closing = true;
        // Cancel even when PressAsync is waiting for startup readiness.
        _cancellation?.Cancel();
        await _gate.WaitAsync();
        try { await StopAsync(); }
        finally { _gate.Release(); }
    }

    private async Task StopAsync()
    {
        _cancellation?.Cancel();
        try { await _run; }
        catch (Exception) { /* The caller reports run errors; cleanup must still allow a retry. */ }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private static byte ParseByte(string value)
    {
        var hex = value.AsSpan().Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        if (!byte.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte result))
            throw new InvalidDataException("Simulator button codes and report ID must be hexadecimal bytes.");
        return result;
    }

    private sealed class ReadyProgress(TaskCompletionSource ready) : IProgress<BridgeNotification>
    {
        public void Report(BridgeNotification notification)
        {
            if (notification.IsReady)
                ready.TrySetResult();
        }
    }
}
