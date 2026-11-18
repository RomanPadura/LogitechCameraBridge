using System.ComponentModel;
using HidSharp;
using LogitechCameraBridge.Controllers.Interop;
using LogitechCameraBridge.Logger;

namespace LogitechCameraBridge.Controllers;

public class ControllerDiscovery : IControllerDiscovery
{
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

        ConsoleLogger.LogInfo("Detecting Bridge HID controller:", LogToConsole);

        foreach (var device in DeviceList.Local.GetHidDevices(vid, pid))
        {
            try
            {
                uint deviceUsage = WindowsHidCapabilities.GetFullUsage(device.DevicePath, LogToConsole);
                ConsoleLogger.LogInfo($"HID candidate: Usage=0x{deviceUsage:X8}, Path={device.DevicePath}", LogToConsole);

                if (deviceUsage != fullUsage)
                    continue;

                ConsoleLogger.LogInfo("HID controller found.", LogToConsole);
                ConsoleLogger.LogInfo($"VID:       0x{vid:X4}", LogToConsole);
                ConsoleLogger.LogInfo($"PID:       0x{pid:X4}", LogToConsole);
                ConsoleLogger.LogInfo($"UsagePage: 0x{fullUsage >> 16:X4}", LogToConsole);
                ConsoleLogger.LogInfo($"Usage:     0x{fullUsage & 0xFFFF:X4}", LogToConsole);
                ConsoleLogger.LogInfo($"Path:      {device.DevicePath}", LogToConsole);
                ConsoleLogger.LogInfo(string.Empty, LogToConsole);

                return device;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                ConsoleLogger.LogInfo($"Cannot inspect HID collection '{device.DevicePath}': {ex.Message}",
                    LogToConsole, isError: true);
            }
        }

        throw new InvalidOperationException(
            $"HID controller not found. VID=0x{vid:X4}, PID=0x{pid:X4}, Usage=0x{fullUsage:X8}.");
    }
}
