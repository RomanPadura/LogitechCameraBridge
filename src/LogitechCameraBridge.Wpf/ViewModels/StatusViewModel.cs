using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.ViewModels;

public sealed record DeviceStatus(string Text, bool IsError);

public partial class StatusViewModel(
    IControllerDiscovery controllers,
    ICameraDiscovery cameras,
    ICameraSelectionStore selection,
    IOptions<BridgeControllerDeviceOptions> controllerOptions,
    BridgeActivity activity,
    ICameraBridgeLogger logger) : ObservableObject
{
    [ObservableProperty]
    private DeviceStatus _controller = new("checking...", false);

    [ObservableProperty]
    private DeviceStatus _camera = new("checking...", false);

    [ObservableProperty]
    private bool _active;

    private Task _refresh = Task.CompletedTask;
    private bool _closing;

    public Task RefreshAsync()
    {
        if (_closing || !_refresh.IsCompleted)
            return _refresh;
        _refresh = RefreshCoreAsync();
        return _refresh;
    }

    private async Task RefreshCoreAsync()
    {
        Active = false;
        Controller = new("checking...", false);
        Camera = new("checking...", false);
        bool controllerFound = false;
        try
        {
            await Task.Run(() =>
            {
                var options = controllerOptions.Value;
                controllers.FindController(ReadHex(options.Vid), ReadHex(options.Pid),
                    ((uint)ReadHex(options.UsagePage) << 16) | ReadHex(options.Usage));
            });
            Controller = new(controllers is ControllerDiscoveryStub ? "found: Stub" : "found: Logitech", false);
            controllerFound = true;
        }
        catch (ControllerNotFoundException ex)
        {
            Controller = new("not found", true);
            logger.LogError(ex.Message);
        }
        catch (Exception ex)
        {
            Controller = new("Error...check logs", true);
            logger.LogError(ex.ToString());
        }

        try
        {
            var selected = await selection.LoadAsync();
            if (selected is null)
            {
                Camera = new("not selected", true);
                return;
            }
            var available = await Task.Run(cameras.FindCameras);
            var found = available.FirstOrDefault(camera => string.Equals(camera.DevicePath, selected.DevicePath,
                StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                Camera = new("not found", true);
                return;
            }
            var state = activity.Snapshot;
            bool sameCamera = string.Equals(state.DevicePath, selected.DevicePath, StringComparison.OrdinalIgnoreCase);
            if (sameCamera && state.Error is not null)
            {
                Camera = new("Error...check logs", true);
                return;
            }
            Camera = new($"found: {found.Name}", false);
            Active = controllerFound && sameCamera && state.IsReady;
        }
        catch (Exception ex)
        {
            Camera = new("Error...check logs", true);
            logger.LogError(ex.ToString());
        }
    }

    public async Task ShutdownAsync()
    {
        _closing = true;
        await _refresh;
        Active = false;
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
}
