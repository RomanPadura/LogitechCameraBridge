using System.Runtime.InteropServices;
using System.Globalization;
using LogitechCameraBridgeConsole.Options;
using Microsoft.Extensions.Options;
using DirectShowLib;
using HidSharp;

namespace LogitechCameraBridgeConsole.Managers;

public class CameraBridgeManager(IOptions<BridgeControllerDeviceOptions> deviceOptions)
{
    private readonly BridgeControllerDeviceOptions _deviceOptions = deviceOptions.Value;

    public void InitializeDevices()
    {
        int vid = ReadHexDeviceId(_deviceOptions.Vid, nameof(BridgeControllerDeviceOptions.Vid));
        int pid = ReadHexDeviceId(_deviceOptions.Pid, nameof(BridgeControllerDeviceOptions.Pid));
        uint usagePage = (uint)ReadHexDeviceId(
            _deviceOptions.UsagePage, nameof(BridgeControllerDeviceOptions.UsagePage));
        uint usage = (uint)ReadHexDeviceId(
            _deviceOptions.Usage, nameof(BridgeControllerDeviceOptions.Usage));
        uint fullUsage = (usagePage << 16) | usage;

        // Find the Logitech GROUP camera control HID collection by usage.
        var speakerphone = DeviceList.Local
            .GetHidDevices(vid, pid)
            .FirstOrDefault(hidDevice => hidDevice
                .GetReportDescriptor()
                .DeviceItems
                .Any(item => item.Usages.ContainsValue(fullUsage)))
            ?? throw new InvalidOperationException(
                "Logitech GROUP camera control device not found.");

        // Find Logitech C930e camera.
        var device = DsDevice
            .GetDevicesOfCat(FilterCategory.VideoInputDevice)
            .First(d => d.Name.Contains(
                "C930e", StringComparison.OrdinalIgnoreCase));

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
                    0x10 => 1,  // Zoom In
                    0x20 => -1, // Zoom Out
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

    private static int ReadHexDeviceId(string? value, string name)
    {
        var hex = value.AsSpan().Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out ushort id))
        {
            throw new InvalidDataException(
                $"BridgeControllerDevice.{name} must be a hexadecimal USB ID, such as 0x046D.");
        }

        return id;
    }
}
