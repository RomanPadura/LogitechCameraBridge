using DirectShowLib;
using LogitechCameraBridge.Logger;

namespace LogitechCameraBridge.Cameras;

public class CameraDiscovery : ICameraDiscovery
{
    /// <inheritdoc />
    public object CreateCameraFilter(int vid, int pid, string usbInterface, bool LogToConsole = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DirectShow camera discovery requires Windows.");

        ConsoleLogger.LogInfo("Detecting camera to control:", LogToConsole);
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

        try
        {
            var device = devices.FirstOrDefault(d => MatchesDevice(d, vid, pid, usbInterface))
                ?? throw new InvalidOperationException(
                    $"Camera not found. VID=0x{vid:X4}, PID=0x{pid:X4}, Interface={usbInterface}.");

            ConsoleLogger.LogInfo("Camera found.", LogToConsole);
            ConsoleLogger.LogInfo($"Name:      {device.Name}", LogToConsole);
            ConsoleLogger.LogInfo($"VID:       0x{vid:X4}", LogToConsole);
            ConsoleLogger.LogInfo($"PID:       0x{pid:X4}", LogToConsole);
            ConsoleLogger.LogInfo($"Interface: {usbInterface}", LogToConsole);
            ConsoleLogger.LogInfo($"Path:      {device.DevicePath}", LogToConsole);
            ConsoleLogger.LogInfo(string.Empty, LogToConsole);

            var iid = typeof(IBaseFilter).GUID;
            device.Mon.BindToObject(null, null, ref iid, out object filter);
            return filter;
        }
        finally
        {
            // Release all enumerated COM monikers; the returned filter has its own reference.
            foreach (var device in devices)
                device.Dispose();
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
}
