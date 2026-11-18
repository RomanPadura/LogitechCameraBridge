using HidSharp;

namespace LogitechCameraBridge.Controllers;

public interface IControllerDiscovery
{
    /// <summary>
    /// Finds a HID collection by VID, PID, and combined usage page/usage.
    /// The caller owns any HID stream opened for the returned device.
    /// </summary>
    HidDevice FindController(int vid, int pid, uint fullUsage, bool LogToConsole = false);
}
