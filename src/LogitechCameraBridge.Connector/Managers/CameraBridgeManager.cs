using LogitechCameraBridge.Connector.Helpers;
using System.Runtime.InteropServices;
using LogitechCameraBridge.Common.Options;
using Microsoft.Extensions.Options;
using DirectShowLib;
using HidSharp;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Connector.Interfaces;
using LogitechCameraBridge.Connector.Models;

namespace LogitechCameraBridge.Connector.Managers;

public class CameraBridgeManager(
    IOptions<BridgeControllerDeviceOptions> deviceOptions,
    IOptions<CameraToControlOptions> cameraOptions,
    IControllerDiscovery controllerDiscovery,
    ICameraDiscovery cameraDiscovery,
    ICameraBridgeLogger logger) : ICameraBridgeManager
{
    private readonly ICameraBridgeLogger _logger = logger;

    private readonly IControllerDiscovery _controllerDiscovery = controllerDiscovery;
    private readonly ICameraDiscovery _cameraDiscovery = cameraDiscovery;

    private readonly BridgeControllerDeviceOptions _deviceOptions =
        deviceOptions.Value;

    private readonly CameraToControlOptions _cameraOptions =
        cameraOptions.Value;

    public void Start(CancellationToken cancellationToken = default)
        => Start(cancellationToken, static () => { });

    public void Start(CancellationToken cancellationToken, Action onReady)
        => Start(cancellationToken, onReady, static _ => { });

    public void Start(CancellationToken cancellationToken, Action onReady, Action<CameraControlCapabilities> onCapabilities)
    {
        ArgumentNullException.ThrowIfNull(onReady);
        ArgumentNullException.ThrowIfNull(onCapabilities);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The camera bridge requires Windows.");

        var controller = ReadControllerConfiguration();

        cancellationToken.ThrowIfCancellationRequested();
        var speakerphone = _controllerDiscovery.FindController(
            controller.Vid, controller.Pid, controller.FullUsage);
        cancellationToken.ThrowIfCancellationRequested();
        var filter = CreateCameraFilter();
        try
        {
            if (filter is not IAMCameraControl camera)
            {
                onCapabilities(CameraControlCapabilities.NotSupported);
                throw new NotSupportedException("The selected camera does not support IAMCameraControl.");
            }
            ProcessReports(speakerphone, camera, controller.ReportId, controller.ButtonMappings,
                cancellationToken, onReady, onCapabilities);
        }
        finally
        {
            Marshal.ReleaseComObject(filter);
        }
        _logger.LogInfo("Camera bridge stopped.");
    }

    private object CreateCameraFilter()
    {
        if (!string.IsNullOrWhiteSpace(_cameraOptions.DevicePath))
            return _cameraDiscovery.CreateCameraFilter(_cameraOptions.DevicePath);

        // Preserve USB configuration selection for the console host.
        int cameraVid = ConfigurationHelper.ReadHexValue(
            _cameraOptions.Vid,
            nameof(CameraToControlOptions.Vid),
            sectionName: CameraToControlOptions.SectionName);

        int cameraPid = ConfigurationHelper.ReadHexValue(
            _cameraOptions.Pid,
            nameof(CameraToControlOptions.Pid),
            sectionName: CameraToControlOptions.SectionName);

        string usbInterface =
            _cameraOptions.UsbInterface?.Trim() ?? string.Empty;

        ConfigurationHelper.ValidateUsbInterface(usbInterface, _cameraOptions.UsbInterfacePrefix);

        return _cameraDiscovery.CreateCameraFilter(
            cameraVid,
            cameraPid,
            usbInterface);

    }

    private ControllerConfiguration ReadControllerConfiguration()
    {
        // Read controller configuration.
        int vid = ConfigurationHelper.ReadHexValue(
            _deviceOptions.Vid,
            nameof(BridgeControllerDeviceOptions.Vid));

        int pid = ConfigurationHelper.ReadHexValue(
            _deviceOptions.Pid,
            nameof(BridgeControllerDeviceOptions.Pid));

        uint usagePage = (uint)ConfigurationHelper.ReadHexValue(
            _deviceOptions.UsagePage,
            nameof(BridgeControllerDeviceOptions.UsagePage));

        uint usage = (uint)ConfigurationHelper.ReadHexValue(
            _deviceOptions.Usage,
            nameof(BridgeControllerDeviceOptions.Usage));

        uint fullUsage = (usagePage << 16) | usage;

        int reportId = ConfigurationHelper.ReadHexValue(
            _deviceOptions.ReportId,
            nameof(BridgeControllerDeviceOptions.ReportId),
            byte.MaxValue);

        int zoomInCode = ConfigurationHelper.ReadHexValue(_deviceOptions.ZoomInCode,
            nameof(BridgeControllerDeviceOptions.ZoomInCode), byte.MaxValue);

        int zoomOutCode = ConfigurationHelper.ReadHexValue(_deviceOptions.ZoomOutCode,
            nameof(BridgeControllerDeviceOptions.ZoomOutCode), byte.MaxValue);

        int panLeftCode = ConfigurationHelper.ReadHexValue(_deviceOptions.PanLeftCode,
            nameof(BridgeControllerDeviceOptions.PanLeftCode), byte.MaxValue);
        int panRightCode = ConfigurationHelper.ReadHexValue(_deviceOptions.PanRightCode,
            nameof(BridgeControllerDeviceOptions.PanRightCode), byte.MaxValue);
        int tiltUpCode = ConfigurationHelper.ReadHexValue(_deviceOptions.TiltUpCode,
            nameof(BridgeControllerDeviceOptions.TiltUpCode), byte.MaxValue);
        int tiltDownCode = ConfigurationHelper.ReadHexValue(_deviceOptions.TiltDownCode,
            nameof(BridgeControllerDeviceOptions.TiltDownCode), byte.MaxValue);

        ConfigurationHelper.ValidateButtonCodes(zoomInCode, zoomOutCode, panLeftCode, panRightCode, tiltUpCode, tiltDownCode);
        var buttonMappings = new Dictionary<int, (CameraControlProperty Property, int Direction)>
        {
            [zoomInCode] = (CameraControlProperty.Zoom, 1),
            [zoomOutCode] = (CameraControlProperty.Zoom, -1),
            [panLeftCode] = (CameraControlProperty.Pan, -1),
            [panRightCode] = (CameraControlProperty.Pan, 1),
            [tiltUpCode] = (CameraControlProperty.Tilt, 1),
            [tiltDownCode] = (CameraControlProperty.Tilt, -1)
        };

        return new ControllerConfiguration(vid, pid, fullUsage, reportId, buttonMappings);
    }

    private void ProcessReports(
        HidDevice speakerphone,
        IAMCameraControl camera,
        int reportId,
        IReadOnlyDictionary<int, (CameraControlProperty Property, int Direction)> buttonMappings,
        CancellationToken cancellationToken,
        Action onReady,
        Action<CameraControlCapabilities> onCapabilities)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var controls = new CameraControls(camera, _logger);
        controls.Initialize(onCapabilities);

        cancellationToken.ThrowIfCancellationRequested();

        // Open the controller HID stream.
        using var stream = speakerphone.Open();

        stream.ReadTimeout = 50;

        var buttons = new ControllerButtonRepeater(buttonMappings, controls.Move, _logger.LogInfo);

        _logger.LogInfo("Listening for controller buttons...");
        _logger.LogInfo("Hold-to-repeat enabled: initial delay=400ms, interval=100ms, HID read timeout=50ms.");
        _logger.LogInfo("GROUP release mapping: 0x00=neutral, 0xFF=release.");
        _logger.LogInfo($"Report ID: 0x{reportId:X2}");
        foreach (var (code, command) in buttonMappings)
            _logger.LogInfo($"Button 0x{code:X2}: {command.Property}, direction={command.Direction}");
        _logger.LogInfo(string.Empty);

        cancellationToken.ThrowIfCancellationRequested();
        onReady();

        ControllerReportLoop.Run(stream.Read, reportId, buttons, _logger, cancellationToken);
    }

}
