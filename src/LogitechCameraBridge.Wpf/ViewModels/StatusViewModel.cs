using LogitechCameraBridge.Wpf.Models;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Connector.Models;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.ViewModels;

/// <summary>One startup attempt; failures require restarting the application.</summary>
public partial class StatusViewModel(
    IControllerDiscovery controllers,
    CameraMenuViewModel cameraMenu,
    IOptions<BridgeControllerDeviceOptions> controllerOptions,
    IOptions<CameraToControlOptions> cameraOptions,
    IBridgeSession session,
    BridgeActivity activity,
    ICameraBridgeLogger logger) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayState))]
    private DeviceStatus _controller = new("Checking...", false);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayState))]
    private DeviceStatus _camera = new("Checking...", false);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayState))]
    private bool _active;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayState))]
    private bool _isInitializing = true;

    [ObservableProperty]
    private CameraControlCapabilities _capabilities = CameraControlCapabilities.NotChecked;

    private bool _cameraNotSelected;

    public TrayIconState TrayState => _failed || Controller.IsError || (Camera.IsError && !_cameraNotSelected)
        ? TrayIconState.Error
        : IsInitializing ? TrayIconState.Initializing
        : _cameraNotSelected ? TrayIconState.CameraNotSelected
        : Active ? TrayIconState.Healthy : TrayIconState.Error;

    private readonly CancellationTokenSource _stop = new();
    private Task _startup = Task.CompletedTask;
    private Task _run = Task.CompletedTask;
    private Task _monitor = Task.CompletedTask;
    private bool _initialized;
    private bool _closing;
    private bool _failed;

    public Task InitializeAsync()
    {
        if (_initialized || _closing)
            return _startup;
        _initialized = true;
        _startup = InitializeAndSettleAsync();
        return _startup;
    }

    private async Task InitializeAndSettleAsync()
    {
        try { await InitializeCoreAsync(); }
        finally { IsInitializing = false; }
    }

    private async Task InitializeCoreAsync()
    {
        bool controllerFound = false;
        try
        {
            string controllerName = await Task.Run(() =>
            {
                var options = controllerOptions.Value;
                var device = controllers.FindController(ReadHex(options.Vid), ReadHex(options.Pid),
                    ((uint)ReadHex(options.UsagePage) << 16) | ReadHex(options.Usage));
                if (controllers is ControllerDiscoveryStub)
                    return "Stub";
                string name = device.GetFriendlyName();
                return string.IsNullOrWhiteSpace(name) ? "Logitech" : name;
            });
            Controller = new(controllerName, false);
            controllerFound = true;
        }
        catch (ControllerNotFoundException ex)
        {
            Controller = new("Not found", true);
            logger.LogError(ex.Message);
        }
        catch (Exception ex)
        {
            Controller = new("Error", true);
            logger.LogError(ex.ToString());
        }
        if (_closing)
            return;

        await cameraMenu.RefreshCommand.ExecuteAsync(null);
        if (_closing)
            return;
        if (!string.IsNullOrEmpty(cameraMenu.Error))
        {
            Camera = new("Error", true);
            return;
        }
        var selected = cameraMenu.SelectedCamera;
        var found = cameraMenu.Cameras.FirstOrDefault(camera => selected is not null && string.Equals(
            camera.DevicePath, selected.DevicePath, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            _cameraNotSelected = true;
            Camera = new("Not selected", true);
            return;
        }
        Camera = new(found.Name, false);
        if (!controllerFound || _failed)
            return;

        cameraOptions.Value.DevicePath = found.DevicePath;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiProgress = new Progress<BridgeNotification>(notification =>
        {
            if (_closing)
                return;
            if (notification.Capabilities is { } capabilities)
                Capabilities = capabilities;
            // Optional-axis initialization warnings do not invalidate otherwise working Zoom.
            if (!_closing && activity.Snapshot.Error is { } error)
                Fail(new InvalidOperationException(error));
        });
        try
        {
            _run = session.RunAsync(new StartupProgress(ready, uiProgress), _stop.Token);
            if (await Task.WhenAny(ready.Task, _run) == _run)
            {
                await _run;
                if (!_closing)
                    Fail(new InvalidOperationException("Bridge stopped before readiness. Restart the application."));
                return;
            }
            if (_closing || _failed)
                return;
            if (activity.Snapshot.Error is { } error)
                Fail(new InvalidOperationException(error));
            else
                Active = !_run.IsCompleted;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            _monitor = MonitorRunAsync();
        }
    }

    private async Task MonitorRunAsync()
    {
        try
        {
            await _run;
            if (!_closing && !_failed)
                Fail(new InvalidOperationException("Bridge stopped. Restart the application."));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Fail(ex); }
        finally { Active = false; }
    }

    public void Fail(Exception exception)
    {
        if (_closing || _failed)
            return;
        _failed = true;
        Active = false;
        Camera = new("Error", true);
        activity.Stopped(cameraOptions.Value.DevicePath, exception.Message);
        logger.LogError(exception.ToString());
        _stop.Cancel();
    }

    public async Task ShutdownAsync()
    {
        _closing = true;
        _stop.Cancel();
        await _startup;
        await _monitor;
        Active = false;
        _stop.Dispose();
    }

    private static ushort ReadHex(string value)
    {
        var hex = value.AsSpan().Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var result))
            throw new InvalidDataException("Controller identity must contain hexadecimal values.");
        return result;
    }

    private sealed class StartupProgress(TaskCompletionSource ready, IProgress<BridgeNotification> next)
        : IProgress<BridgeNotification>
    {
        public void Report(BridgeNotification notification)
        {
            if (notification.IsReady)
                ready.TrySetResult();
            next.Report(notification);
        }
    }
}
