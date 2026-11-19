using DirectShowLib;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Cameras.Models;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Cameras;

public class CameraDiscovery(ICameraBridgeLogger logger) : ICameraDiscovery
{
    private readonly ICameraBridgeLogger _logger = logger;

    /// <inheritdoc />
    public IReadOnlyList<CameraDeviceInfo> FindCameras()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DirectShow camera discovery requires Windows.");

        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        try
        {
            return devices
                .Where(device => !string.IsNullOrWhiteSpace(device.DevicePath))
                .Select(device => new CameraDeviceInfo(
                    string.IsNullOrWhiteSpace(device.Name) ? "Unnamed camera" : device.Name,
                    device.DevicePath))
                .DistinctBy(device => device.DevicePath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(device => device.DevicePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            foreach (var device in devices)
                device.Dispose();
        }
    }

    /// <inheritdoc />
    public object CreateCameraFilter(int vid, int pid, string usbInterface)
        => CreateCameraFilter(device => MatchesDevice(device, vid, pid, usbInterface),
            $"VID=0x{vid:X4}, PID=0x{pid:X4}, Interface={usbInterface}");

    public object CreateCameraFilter(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        return CreateCameraFilter(device => string.Equals(device.DevicePath, devicePath,
            StringComparison.OrdinalIgnoreCase), devicePath);
    }

    private object CreateCameraFilter(Func<DsDevice, bool> matches, string identity)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DirectShow camera discovery requires Windows.");

        _logger.LogInfo("Detecting camera to control:");
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

        try
        {
            var device = devices.FirstOrDefault(matches)
                ?? throw new InvalidOperationException(
                    $"Camera not found. {identity}.");

            _logger.LogInfo("Camera found.");
            _logger.LogInfo($"Name:      {device.Name}");
            _logger.LogInfo($"Path:      {device.DevicePath}");
            _logger.LogInfo(string.Empty);

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
