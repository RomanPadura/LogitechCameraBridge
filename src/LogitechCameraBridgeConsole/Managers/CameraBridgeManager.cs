using System.Runtime.InteropServices;
using System.Globalization;
using LogitechCameraBridgeConsole.Options;
using Microsoft.Extensions.Options;
using DirectShowLib;
using HidSharp;

namespace LogitechCameraBridgeConsole.Managers;

public class CameraBridgeManager(
    IOptions<BridgeControllerDeviceOptions> deviceOptions,
    IOptions<CameraToControlOptions> cameraOptions)
{
    private readonly BridgeControllerDeviceOptions _deviceOptions = deviceOptions.Value;
    private readonly CameraToControlOptions _cameraOptions = cameraOptions.Value;

    public void InitializeDevices()
    {
        int vid = ReadHexValue(_deviceOptions.Vid, nameof(BridgeControllerDeviceOptions.Vid));
        int pid = ReadHexValue(_deviceOptions.Pid, nameof(BridgeControllerDeviceOptions.Pid));
        uint usagePage = (uint)ReadHexValue(
            _deviceOptions.UsagePage, nameof(BridgeControllerDeviceOptions.UsagePage));
        uint usage = (uint)ReadHexValue(
            _deviceOptions.Usage, nameof(BridgeControllerDeviceOptions.Usage));
        uint fullUsage = (usagePage << 16) | usage;
        int zoomInCode = ReadHexValue(_deviceOptions.ZoomInCode,
            nameof(BridgeControllerDeviceOptions.ZoomInCode), byte.MaxValue);
        int zoomOutCode = ReadHexValue(_deviceOptions.ZoomOutCode,
            nameof(BridgeControllerDeviceOptions.ZoomOutCode), byte.MaxValue);
        if (zoomInCode == zoomOutCode)
            throw new InvalidDataException("ZoomInCode and ZoomOutCode must be different.");

        int cameraVid = ReadHexValue(_cameraOptions.Vid,
            nameof(CameraToControlOptions.Vid), sectionName: CameraToControlOptions.SectionName);
        int cameraPid = ReadHexValue(_cameraOptions.Pid,
            nameof(CameraToControlOptions.Pid), sectionName: CameraToControlOptions.SectionName);
        string usbInterface = _cameraOptions.UsbInterface.Trim();
        if (usbInterface.Length != 5 ||
            !usbInterface.StartsWith("MI_", StringComparison.OrdinalIgnoreCase) ||
            !byte.TryParse(usbInterface.AsSpan(3), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidDataException(
                "CameraToControl.UsbInterface must be a USB interface such as MI_00.");
        }

        // Find the Logitech GROUP camera control HID collection by usage.
        var speakerphone = DeviceList.Local
            .GetHidDevices(vid, pid)
            .FirstOrDefault(hidDevice => hidDevice
                .GetReportDescriptor()
                .DeviceItems
                .Any(item => item.Usages.ContainsValue(fullUsage)))
            ?? throw new InvalidOperationException(
                "Logitech GROUP camera control device not found.");

        // Match the camera by USB identity; the configured name is for diagnostics.
        var device = DsDevice
            .GetDevicesOfCat(FilterCategory.VideoInputDevice)
            .FirstOrDefault(d => MatchesDevice(d, cameraVid, cameraPid, usbInterface))
            ?? throw new InvalidOperationException(
                $"Camera '{_cameraOptions.Device}' VID={cameraVid:X4}, PID={cameraPid:X4}, interface={usbInterface} not found.");

        var iid = typeof(IBaseFilter).GUID;

        device.Mon.BindToObject(
            null, null, ref iid, out object filter);

        try
        {
            var camera = (IAMCameraControl)filter;
            const CameraControlProperty property =
                CameraControlProperty.Zoom;

            Marshal.ThrowExceptionForHR(camera.GetRange(
                property,
                out int min,
                out int max,
                out int step,
                out int defaultValue,
                out _));

            Console.WriteLine(
                $"Zoom: Min={min}, Max={max}, Step={step}");

            using var stream = speakerphone.Open();
            stream.ReadTimeout = 1000;

            Console.WriteLine("Listening for GROUP buttons...");

            while (true)
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

                if (report.Length < 2 || report[0] != 0x0A)
                    continue;

                Console.WriteLine(Convert.ToHexString(report));

                int direction = report[1] switch
                {
                    var code when code == zoomInCode => 1,
                    var code when code == zoomOutCode => -1,
                    _ => 0
                };

                if (direction == 0)
                    continue;

                Marshal.ThrowExceptionForHR(camera.Get(
                    property, out int current, out _));

                int next = Math.Clamp(
                    current + direction * Math.Max(1, step),
                    min, max);

                Marshal.ThrowExceptionForHR(camera.Set(
                    property, next, CameraControlFlags.Manual));

                Console.WriteLine($"Zoom: {current} -> {next}");
            }
        }
        finally
        {
            Marshal.ReleaseComObject(filter);
        }
    }

    private static bool MatchesDevice(DsDevice device, int vid, int pid, string usbInterface)
    {
        string? devicePath = device.DevicePath;
        if (string.IsNullOrEmpty(devicePath))
            return false;

        return devicePath.Contains($"vid_{vid:X4}&pid_{pid:X4}", StringComparison.OrdinalIgnoreCase)
            && devicePath.Split('&', '#').Contains(usbInterface, StringComparer.OrdinalIgnoreCase);
    }

    private static int ReadHexValue(string? value, string name, int maxValue = ushort.MaxValue,
        string sectionName = BridgeControllerDeviceOptions.SectionName)
    {
        var hex = value.AsSpan().Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out ushort id) || id > maxValue)
        {
            throw new InvalidDataException(
                $"{sectionName}.{name} must be a hexadecimal value between 0x00 and 0x{maxValue:X}.");
        }

        return id;
    }
}
