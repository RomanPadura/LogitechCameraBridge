using HidSharp;
using LogitechCameraBridge.Cameras.Models;
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
    public async Task ButtonsReachTheBridgeAsConfiguredReports(SimulatorButton button, byte expected)
    {
        var fixture = new Fixture();
        try
        {
            await fixture.Simulator.PressAsync(button);
            Assert.Equal("selected-device", fixture.CameraOptions.Value.DevicePath);
            byte[] report = fixture.Session.Stream!.Read();
            Assert.Equal(0x0A, report[0]);
            Assert.Equal(expected, report[1]);
        }
        finally { await fixture.Simulator.ShutdownAsync(); }
        Assert.True(fixture.Session.CleanedUp);
    }

    [Fact]
    public async Task NoSelectionNeverStartsCamera()
    {
        var fixture = new Fixture();
        fixture.Store.Camera = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Simulator.PressAsync(SimulatorButton.ZoomIn));
        Assert.Equal(0, fixture.Session.Starts);
        await fixture.Simulator.ShutdownAsync();
    }

    [Fact]
    public async Task NewSelectionStopsOldSessionBeforeStartingNewOne()
    {
        var fixture = new Fixture();
        try
        {
            await fixture.Simulator.PressAsync(SimulatorButton.ZoomIn);
            var previous = fixture.Session.Stream!;
            fixture.Store.Camera = new("Other camera", "other-device");
            await fixture.Simulator.PressAsync(SimulatorButton.Left);
            Assert.Throws<ObjectDisposedException>(() => previous.Read());
            Assert.Equal(2, fixture.Session.Starts);
            Assert.Equal("other-device", fixture.CameraOptions.Value.DevicePath);
            Assert.Equal(0x08, fixture.Session.Stream!.Read()[1]);
        }
        finally { await fixture.Simulator.ShutdownAsync(); }
    }

    [Fact]
    public async Task InputWaitsForReadinessAndShutdownCanCancelStartup()
    {
        var fixture = new Fixture();
        fixture.Session.Ready = false;
        var press = fixture.Simulator.PressAsync(SimulatorButton.ZoomIn);
        Assert.False(press.IsCompleted);
        fixture.Session.Stream!.ReadTimeout = 1;
        Assert.Throws<TimeoutException>(() => fixture.Session.Stream.Read());
        var closing = fixture.Simulator.ShutdownAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => press.WaitAsync(TimeSpan.FromSeconds(10)));
        await closing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(fixture.Session.CleanedUp);
        await fixture.Simulator.PressAsync(SimulatorButton.ZoomIn);
        Assert.Equal(1, fixture.Session.Starts);
    }

    private sealed class Fixture
    {
        public Store Store { get; } = new();
        public FakeSession Session { get; }
        public IOptions<CameraToControlOptions> CameraOptions { get; } = Options.Create(new CameraToControlOptions());
        public KeyboardSimulator Simulator { get; }

        public Fixture()
        {
            var logger = new SilentLogger();
            var stub = new ControllerDiscoveryStub(logger);
            Session = new FakeSession(stub);
            Simulator = new KeyboardSimulator(Session, stub, Store, CameraOptions,
                Options.Create(new BridgeControllerDeviceOptions
                {
                    ReportId = "0x0A", ZoomInCode = "0x10", ZoomOutCode = "0x20", PanLeftCode = "0x08",
                    PanRightCode = "0x04", TiltUpCode = "0x01", TiltDownCode = "0x02"
                }), logger);
        }
    }

    private sealed class FakeSession(ControllerDiscoveryStub stub) : IBridgeSession
    {
        public HidStream? Stream { get; private set; }
        public int Starts { get; private set; }
        public bool Ready { get; set; } = true;
        public bool CleanedUp { get; private set; }

        public async Task RunAsync(IProgress<BridgeNotification> progress, CancellationToken cancellationToken)
        {
            Starts++;
            CleanedUp = false;
            Stream = stub.FindController(1, 2, 3).Open();
            try
            {
                if (Ready)
                    progress.Report(new(true));
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                Stream.Dispose();
                CleanedUp = true;
            }
        }
    }

    private sealed class Store : ICameraSelectionStore
    {
        public CameraDeviceInfo? Camera { get; set; } = new("Selected camera", "selected-device");
        public Task<CameraDeviceInfo?> LoadAsync() => Task.FromResult(Camera);
        public Task SaveAsync(CameraDeviceInfo camera) { Camera = camera; return Task.CompletedTask; }
    }

    private sealed class SilentLogger : ICameraBridgeLogger
    {
        public void LogInfo(string message) { }
        public void LogError(string message) { }
    }
}
