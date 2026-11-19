using LogitechCameraBridge.Wpf.Models;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.Tests;

public class KeyboardSimulatorTests
{
    [Theory]
    [InlineData(SimulatorButton.ZoomIn, 0x10)]
    [InlineData(SimulatorButton.ZoomOut, 0x20)]
    [InlineData(SimulatorButton.Left, 0x08)]
    [InlineData(SimulatorButton.Right, 0x04)]
    [InlineData(SimulatorButton.Up, 0x01)]
    [InlineData(SimulatorButton.Down, 0x02)]
    public async Task OnlyReadyStartupSessionReceivesConfiguredReports(SimulatorButton button, byte expected)
    {
        var stub = new ControllerDiscoveryStub(new SilentLogger());
        var activity = new BridgeActivity();
        var simulator = new KeyboardSimulator(stub, Options.Create(new BridgeControllerDeviceOptions
        {
            ReportId = "0A", ZoomInCode = "10", ZoomOutCode = "20", PanLeftCode = "08",
            PanRightCode = "04", TiltUpCode = "01", TiltDownCode = "02"
        }), activity);
        using var stream = stub.FindController(1, 2, 3).Open();
        stream.ReadTimeout = 1;
        await simulator.PressAsync(button);
        Assert.Throws<TimeoutException>(() => stream.Read());
        activity.Ready("device-1");
        await simulator.PressAsync(button);
        var report = stream.Read();
        Assert.Equal(0x0A, report[0]);
        Assert.Equal(expected, report[1]);
        activity.CameraError("device-1", "Set failed");
        await simulator.PressAsync(button);
        Assert.Throws<TimeoutException>(() => stream.Read());
        activity.Stopped("device-1");
        await simulator.PressAsync(button);
        Assert.Throws<TimeoutException>(() => stream.Read());
    }
    private sealed class SilentLogger : ICameraBridgeLogger
    {
        public void LogInfo(string message) { }
        public void LogError(string message) { }
    }
}
