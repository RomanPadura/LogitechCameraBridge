using System.Globalization;
using System.Runtime.InteropServices;
using LogitechCameraBridge.Common.Options;
using Microsoft.Extensions.Options;
using DirectShowLib;
using HidSharp;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Cameras.Interfaces;

namespace LogitechCameraBridgeConsole.Managers;

public class CameraBridgeManager(
    IOptions<BridgeControllerDeviceOptions> deviceOptions,
    IOptions<CameraToControlOptions> cameraOptions,
    IControllerDiscovery controllerDiscovery,
    ICameraDiscovery cameraDiscovery)
{
    private readonly IControllerDiscovery _controllerDiscovery = controllerDiscovery;
    private readonly ICameraDiscovery _cameraDiscovery = cameraDiscovery;

    private readonly BridgeControllerDeviceOptions _deviceOptions =
        deviceOptions.Value;

    private readonly CameraToControlOptions _cameraOptions =
        cameraOptions.Value;

    public void Run(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The camera bridge requires Windows.");

        // Read controller configuration.
        int vid = ReadHexValue(
            _deviceOptions.Vid,
            nameof(BridgeControllerDeviceOptions.Vid));

        int pid = ReadHexValue(
            _deviceOptions.Pid,
            nameof(BridgeControllerDeviceOptions.Pid));

        uint usagePage = (uint)ReadHexValue(
            _deviceOptions.UsagePage,
            nameof(BridgeControllerDeviceOptions.UsagePage));

        uint usage = (uint)ReadHexValue(
            _deviceOptions.Usage,
            nameof(BridgeControllerDeviceOptions.Usage));

        uint fullUsage = (usagePage << 16) | usage;

        int reportId = ReadHexValue(
            _deviceOptions.ReportId,
            nameof(BridgeControllerDeviceOptions.ReportId),
            byte.MaxValue);

        int zoomInCode = ReadHexValue(
            _deviceOptions.ZoomInCode,
            nameof(BridgeControllerDeviceOptions.ZoomInCode),
            byte.MaxValue);

        int zoomOutCode = ReadHexValue(
            _deviceOptions.ZoomOutCode,
            nameof(BridgeControllerDeviceOptions.ZoomOutCode),
            byte.MaxValue);

        ValidateButtonCodes(zoomInCode, zoomOutCode);

        // Read camera configuration.
        int cameraVid = ReadHexValue(
            _cameraOptions.Vid,
            nameof(CameraToControlOptions.Vid),
            sectionName: CameraToControlOptions.SectionName);

        int cameraPid = ReadHexValue(
            _cameraOptions.Pid,
            nameof(CameraToControlOptions.Pid),
            sectionName: CameraToControlOptions.SectionName);

        string usbInterface =
            _cameraOptions.UsbInterface?.Trim() ?? string.Empty;

        ValidateUsbInterface(usbInterface);

        cancellationToken.ThrowIfCancellationRequested();

        // Find the HID controller.
        var speakerphone = _controllerDiscovery.FindController(
            vid, pid, fullUsage, LogToConsole: true);

        // Find and initialize the camera filter.
        var filter = _cameraDiscovery.CreateCameraFilter(
            cameraVid,
            cameraPid,
            usbInterface,
            LogToConsole: true);

        try
        {
            if (filter is not IAMCameraControl camera)
            {
                throw new NotSupportedException(
                    "The selected camera does not support IAMCameraControl.");
            }

            ProcessReports(
                speakerphone,
                camera,
                reportId,
                zoomInCode,
                zoomOutCode,
                cancellationToken);
        }
        finally
        {
            Marshal.ReleaseComObject(filter);
        }

        Console.WriteLine("Camera bridge stopped.");
    }

    private static void ProcessReports(
        HidDevice speakerphone,
        IAMCameraControl camera,
        int reportId,
        int zoomInCode,
        int zoomOutCode,
        CancellationToken cancellationToken)
    {
        const CameraControlProperty property =
            CameraControlProperty.Zoom;

        // Read camera capabilities.
        Marshal.ThrowExceptionForHR(camera.GetRange(
            property,
            out int min,
            out int max,
            out int step,
            out int defaultValue,
            out CameraControlFlags capabilities));

        if ((capabilities & CameraControlFlags.Manual) == 0)
        {
            throw new NotSupportedException(
                "The selected camera does not support manual zoom.");
        }

        if (step <= 0 || min > max)
        {
            throw new InvalidOperationException(
                $"Invalid camera zoom range. " +
                $"Min={min}, Max={max}, Step={step}.");
        }

        Marshal.ThrowExceptionForHR(camera.Get(
            property,
            out int currentZoom,
            out _));

        Console.WriteLine("Camera zoom capabilities:");
        Console.WriteLine($"Min:          {min}");
        Console.WriteLine($"Max:          {max}");
        Console.WriteLine($"Step:         {step}");
        Console.WriteLine($"Default:      {defaultValue}");
        Console.WriteLine($"Current:      {currentZoom}");
        Console.WriteLine($"Capabilities: {capabilities}");
        Console.WriteLine();

        // Open the controller HID stream.
        using var stream = speakerphone.Open();

        stream.ReadTimeout = 1000;

        Console.WriteLine("Listening for controller buttons...");
        Console.WriteLine($"Report ID: 0x{reportId:X2}");
        Console.WriteLine($"Zoom In:   0x{zoomInCode:X2}");
        Console.WriteLine($"Zoom Out:  0x{zoomOutCode:X2}");
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] report;

            try
            {
                report = stream.Read();
            }
            catch (TimeoutException)
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            // Validate the HID report.
            if (report.Length < 2 || report[0] != reportId)
                continue;

            Console.WriteLine(
                $"HID Report: {Convert.ToHexString(report)}");

            byte buttonCode = report[1];

            // Ignore button release and unknown reports.
            if (buttonCode is 0x00 or 0xFF)
                continue;

            int direction = buttonCode switch
            {
                var code when code == zoomInCode => 1,
                var code when code == zoomOutCode => -1,
                _ => 0
            };

            // Ignore unsupported buttons.
            if (direction == 0)
                continue;

            Marshal.ThrowExceptionForHR(camera.Get(
                property,
                out int current,
                out _));

            int next = (int)Math.Clamp(
                (long)current + direction * (long)step,
                min,
                max);

            // Avoid unnecessary camera commands.
            if (next == current)
            {
                Console.WriteLine(
                    $"Zoom limit reached: {current}");

                continue;
            }

            Marshal.ThrowExceptionForHR(camera.Set(
                property,
                next,
                CameraControlFlags.Manual));

            Console.WriteLine($"Zoom: {current} -> {next}");
        }
    }

    private static void ValidateButtonCodes(
        int zoomInCode,
        int zoomOutCode)
    {
        if (zoomInCode == zoomOutCode)
        {
            throw new InvalidDataException(
                "ZoomInCode and ZoomOutCode must be different.");
        }

        if (zoomInCode is 0x00 or 0xFF ||
            zoomOutCode is 0x00 or 0xFF)
        {
            throw new InvalidDataException(
                "Button codes 0x00 and 0xFF are reserved.");
        }
    }

    private static void ValidateUsbInterface(string usbInterface)
    {
        if (usbInterface.Length != 5 ||
            !usbInterface.StartsWith(
                "MI_",
                StringComparison.OrdinalIgnoreCase) ||
            !byte.TryParse(
                usbInterface.AsSpan(3),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out _))
        {
            throw new InvalidDataException(
                $"{CameraToControlOptions.SectionName}.UsbInterface " +
                "must be a USB interface such as MI_00.");
        }
    }

    private static int ReadHexValue(
        string? value,
        string name,
        int maxValue = ushort.MaxValue,
        string sectionName = BridgeControllerDeviceOptions.SectionName)
    {
        var hex = value.AsSpan().Trim();

        if (hex.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        if (!ushort.TryParse(
                hex,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ushort id)
            || id > maxValue)
        {
            throw new InvalidDataException(
                $"{sectionName}.{name} must be a hexadecimal " +
                $"value between 0x00 and 0x{maxValue:X}.");
        }

        return id;
    }
}
