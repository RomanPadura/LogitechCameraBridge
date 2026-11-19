using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Cameras.Models;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Connector.Managers;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Logger.Interfaces;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Connector.Tests;

public class CameraSelectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectedIdentityUsesExactPathAndConsoleRetainsUsbSelection(bool usePath)
    {
        var logger = new SilentLogger();
        var discovery = new RecordingDiscovery();
        var manager = new CameraBridgeManager(Options.Create(new BridgeControllerDeviceOptions
        {
            Vid = "046D", Pid = "0857", UsagePage = "FF00", Usage = "0001", ReportId = "0A",
            ZoomInCode = "10", ZoomOutCode = "20", PanLeftCode = "08", PanRightCode = "04",
            TiltUpCode = "01", TiltDownCode = "02"
        }), Options.Create(new CameraToControlOptions
        {
            DevicePath = usePath ? "selected-device-instance" : null,
            Vid = usePath ? "not USB" : "046D", Pid = "0843", UsbInterface = "MI_00", UsbInterfacePrefix = "MI_"
        }), new ControllerDiscoveryStub(logger), discovery, logger);

        // Stop at filter creation, before any hardware or COM interaction.
        Assert.Throws<InvalidOperationException>(() => manager.Start());
        Assert.Equal(usePath ? "selected-device-instance" : "046D:0843:MI_00", discovery.Requested);
    }

    private sealed class RecordingDiscovery : ICameraDiscovery
    {
        public string? Requested { get; private set; }
        public IReadOnlyList<CameraDeviceInfo> FindCameras() => [];
        public object CreateCameraFilter(string path)
        {
            Requested = path;
            throw new InvalidOperationException("Simulated missing device");
        }
        public object CreateCameraFilter(int vid, int pid, string usbInterface)
        {
            Requested = $"{vid:X4}:{pid:X4}:{usbInterface}";
            throw new InvalidOperationException("Simulated missing device");
        }
    }

    private sealed class SilentLogger : ICameraBridgeLogger
    {
        public void LogInfo(string message) { }
        public void LogError(string message) { }
    }
}
