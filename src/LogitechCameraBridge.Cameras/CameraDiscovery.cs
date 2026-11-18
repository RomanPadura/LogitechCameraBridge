using DirectShowLib;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Cameras;

public class CameraDiscovery(ICameraBridgeLogger logger) : ICameraDiscovery
{
    private readonly ICameraBridgeLogger _logger = logger;

    /// <inheritdoc />
    public object CreateCameraFilter(int vid, int pid, string usbInterface, bool LogToConsole = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DirectShow camera discovery requires Windows.");

        _logger.LogInfo("Detecting camera to control:", LogToConsole);
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

        try
        {
            var device = devices.FirstOrDefault(d => MatchesDevice(d, vid, pid, usbInterface))
                ?? throw new InvalidOperationException(
                    $"Camera not found. VID=0x{vid:X4}, PID=0x{pid:X4}, Interface={usbInterface}.");

            _logger.LogInfo("Camera found.", LogToConsole);
            _logger.LogInfo($"Name:      {device.Name}", LogToConsole);
            _logger.LogInfo($"VID:       0x{vid:X4}", LogToConsole);
            _logger.LogInfo($"PID:       0x{pid:X4}", LogToConsole);
            _logger.LogInfo($"Interface: {usbInterface}", LogToConsole);
            _logger.LogInfo($"Path:      {device.DevicePath}", LogToConsole);
            _logger.LogInfo(string.Empty, LogToConsole);

            var iid = typeof(IBaseFilter).GUID;
            Marshal.ThrowExceptionForHR(CreateBindCtx(0, out var bindContext));
            try
            {
                device.Mon.BindToObject(bindContext, null, ref iid, out object filter);
                return filter;
            }
            finally
            {
                Marshal.ReleaseComObject(bindContext);
            }
        }
        finally
        {
            // Release all enumerated COM monikers; the returned filter has its own reference.
            foreach (var device in devices)
                device.Dispose();
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx bindContext);

    private static bool MatchesDevice(DsDevice device, int vid, int pid, string usbInterface)
    {
        string? devicePath = device.DevicePath;
        if (string.IsNullOrEmpty(devicePath))
            return false;

        return devicePath.Contains($"vid_{vid:X4}&pid_{pid:X4}", StringComparison.OrdinalIgnoreCase)
            && devicePath.Split('&', '#').Contains(usbInterface, StringComparer.OrdinalIgnoreCase);
    }
}
