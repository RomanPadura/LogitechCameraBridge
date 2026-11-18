using System.ComponentModel;
using HidSharp;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Controllers.Interop;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Controllers;

public class ControllerDiscovery(ICameraBridgeLogger logger) : IControllerDiscovery
{
    private readonly ICameraBridgeLogger _logger = logger;

    /// <summary>
    /// Finds a HID collection by VID, PID, and combined usage page/usage.
    /// The caller owns any HID stream opened for the returned device.
    /// </summary>
    public HidDevice FindController(
        int vid,
        int pid,
        uint fullUsage,
        bool LogToConsole = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("HID collection discovery requires Windows.");

        _logger.LogInfo("Detecting Bridge HID controller:", LogToConsole);

        foreach (var device in DeviceList.Local.GetHidDevices(vid, pid))
        {
            try
            {
                uint deviceUsage = WindowsHidCapabilities.GetFullUsage(device.DevicePath, _logger, LogToConsole);
                _logger.LogInfo($"HID candidate: Usage=0x{deviceUsage:X8}, Path={device.DevicePath}", LogToConsole);

                if (deviceUsage != fullUsage)
                    continue;

                _logger.LogInfo("HID controller found.", LogToConsole);
                _logger.LogInfo($"VID:       0x{vid:X4}", LogToConsole);
                _logger.LogInfo($"PID:       0x{pid:X4}", LogToConsole);
                _logger.LogInfo($"UsagePage: 0x{fullUsage >> 16:X4}", LogToConsole);
                _logger.LogInfo($"Usage:     0x{fullUsage & 0xFFFF:X4}", LogToConsole);
                _logger.LogInfo($"Path:      {device.DevicePath}", LogToConsole);
                _logger.LogInfo(string.Empty, LogToConsole);

                return device;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                _logger.LogError($"Cannot inspect HID collection '{device.DevicePath}': {ex.Message}",
                    LogToConsole);
            }
        }

        throw new InvalidOperationException(
            $"HID controller not found. VID=0x{vid:X4}, PID=0x{pid:X4}, Usage=0x{fullUsage:X8}.");
    }
}
