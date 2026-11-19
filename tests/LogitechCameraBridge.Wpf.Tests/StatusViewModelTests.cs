using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Cameras.Models;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using LogitechCameraBridge.Wpf.ViewModels;
using HidSharp;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.Tests;

public class StatusViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FoundDevicesAreNotActiveUntilReadiness(bool ready)
    {
        var fixture = new Fixture();
        if (ready)
            fixture.Activity.Ready("device-1");
        await fixture.Model.RefreshAsync();
        Assert.Equal(new DeviceStatus("found: Stub", false), fixture.Model.Controller);
        Assert.Equal(new DeviceStatus("found: Camera", false), fixture.Model.Camera);
        Assert.Equal(ready, fixture.Model.Active);
        Assert.Equal(0, fixture.Cameras.FiltersOpened);
    }

    [Theory]
    [InlineData("not selected")]
    [InlineData("not found")]
    public async Task MissingCameraSelectionOrDeviceIsRedAndInactive(string expected)
    {
        var fixture = new Fixture();
        fixture.Activity.Ready("device-1");
        if (expected == "not selected")
            fixture.Store.Camera = null;
        else
            fixture.Cameras.Available = [];
        await fixture.Model.RefreshAsync();
        Assert.Equal(new DeviceStatus(expected, true), fixture.Model.Camera);
        Assert.False(fixture.Model.Active);
    }

    [Fact]
    public async Task DifferentSelectedDeviceDoesNotInheritActiveOrErrorsFromPreviousSession()
    {
        var fixture = new Fixture();
        fixture.Activity.Ready("different-device");
        fixture.Activity.CameraError("different-device", "Old error");
        await fixture.Model.RefreshAsync();
        Assert.Equal(new DeviceStatus("found: Camera", false), fixture.Model.Camera);
        Assert.False(fixture.Model.Active);
    }

    [Fact]
    public async Task RuntimeCameraErrorIsRedAndRemainsUntilNewSession()
    {
        var fixture = new Fixture();
        fixture.Activity.Ready("device-1");
        fixture.Activity.CameraError("device-1", "Camera Set failed");
        await fixture.Model.RefreshAsync();
        Assert.Equal(new DeviceStatus("Error...check logs", true), fixture.Model.Camera);
        Assert.False(fixture.Model.Active);
        fixture.Activity.Starting("device-1");
        fixture.Activity.Ready("device-1");
        await fixture.Model.RefreshAsync();
        Assert.True(fixture.Model.Active);
    }

    [Fact]
    public async Task MissingPhysicalControllerIsRedEvenWithReadyCamera()
    {
        var fixture = new Fixture(new PhysicalDiscovery { Missing = true });
        fixture.Activity.Ready("device-1");
        await fixture.Model.RefreshAsync();
        Assert.Equal(new DeviceStatus("not found", true), fixture.Model.Controller);
        Assert.Equal(new DeviceStatus("found: Camera", false), fixture.Model.Camera);
        Assert.False(fixture.Model.Active);
        Assert.NotEmpty(fixture.Logger.Errors);
    }

    [Fact]
    public async Task PhysicalDiscoveryUsesConfiguredIdentityAndShowsLogitech()
    {
        var discovery = new PhysicalDiscovery();
        var fixture = new Fixture(discovery);
        await fixture.Model.RefreshAsync();
        Assert.Equal(new DeviceStatus("found: Logitech", false), fixture.Model.Controller);
        Assert.Equal((0x046D, 0x0857, 0xFF000001u), discovery.Identity);
    }

    [Fact]
    public async Task EnumerationErrorIsLoggedAndRedAndRefreshCanRecover()
    {
        var fixture = new Fixture();
        fixture.Cameras.Failure = new InvalidOperationException("Enumeration failed");
        await fixture.Model.RefreshAsync();
        Assert.Equal(new DeviceStatus("Error...check logs", true), fixture.Model.Camera);
        Assert.False(fixture.Model.Active);
        Assert.Contains(fixture.Logger.Errors, error => error.Contains("Enumeration failed"));
        fixture.Cameras.Failure = null;
        await fixture.Model.RefreshAsync();
        Assert.False(fixture.Model.Camera.IsError);
    }

    [Fact]
    public async Task ShutdownWaitsForRefreshAndPreventsNewScans()
    {
        var fixture = new Fixture();
        var load = new TaskCompletionSource<CameraDeviceInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.PendingLoad = load.Task;
        var refresh = fixture.Model.RefreshAsync();
        // Controller discovery finishes asynchronously before the blocked store read.
        var closing = fixture.Model.ShutdownAsync();
        Assert.False(closing.IsCompleted);
        load.SetResult(fixture.Store.Camera);
        await Task.WhenAll(refresh, closing).WaitAsync(TimeSpan.FromSeconds(10));
        int scans = fixture.Cameras.Scans;
        await fixture.Model.RefreshAsync();
        Assert.Equal(scans, fixture.Cameras.Scans);
        Assert.False(fixture.Model.Active);
    }

    private sealed class Fixture
    {
        public FakeCameras Cameras { get; } = new();
        public FakeStore Store { get; } = new();
        public RecordingLogger Logger { get; } = new();
        public BridgeActivity Activity { get; } = new();
        public StatusViewModel Model { get; }
        public Fixture(IControllerDiscovery? discovery = null)
        {
            Model = new StatusViewModel(discovery ?? new ControllerDiscoveryStub(Logger), Cameras, Store,
                Options.Create(new BridgeControllerDeviceOptions
                {
                    Vid = "0x046D", Pid = "0x0857", UsagePage = "0xFF00", Usage = "0x0001"
                }), Activity, Logger);
        }
    }

    private sealed class FakeStore : ICameraSelectionStore
    {
        public CameraDeviceInfo? Camera { get; set; } = new("Camera", "device-1");
        public Task<CameraDeviceInfo?>? PendingLoad { get; set; }
        public Task<CameraDeviceInfo?> LoadAsync() => PendingLoad ?? Task.FromResult(Camera);
        public Task SaveAsync(CameraDeviceInfo camera) => throw new NotSupportedException();
    }

    private sealed class FakeCameras : ICameraDiscovery
    {
        public IReadOnlyList<CameraDeviceInfo> Available { get; set; } = [new("Camera", "device-1")];
        public Exception? Failure { get; set; }
        public int FiltersOpened { get; private set; }
        public int Scans { get; private set; }
        public IReadOnlyList<CameraDeviceInfo> FindCameras()
        {
            Scans++;
            if (Failure is not null)
                throw Failure;
            return Available;
        }
        public object CreateCameraFilter(int vid, int pid, string usbInterface)
        {
            FiltersOpened++;
            throw new InvalidOperationException("Status must not open filters.");
        }
    }

    private sealed class PhysicalDiscovery : IControllerDiscovery
    {
        public bool Missing { get; set; }
        public (int, int, uint) Identity { get; private set; }
        public HidDevice FindController(int vid, int pid, uint fullUsage)
        {
            Identity = (vid, pid, fullUsage);
            if (Missing)
                throw new ControllerNotFoundException("Controller missing");
            return new ControllerDiscoveryStub(new RecordingLogger()).FindController(vid, pid, fullUsage);
        }
    }

    private sealed class RecordingLogger : ICameraBridgeLogger
    {
        public List<string> Errors { get; } = [];
        public void LogInfo(string message) { }
        public void LogError(string message) => Errors.Add(message);
    }
}
