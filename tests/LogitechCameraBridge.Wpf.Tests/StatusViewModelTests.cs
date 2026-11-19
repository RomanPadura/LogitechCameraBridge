using LogitechCameraBridge.Wpf.Models;
using HidSharp;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Cameras.Models;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Connector.Models;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using LogitechCameraBridge.Wpf.ViewModels;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.Tests;

public class StatusViewModelTests
{
    [Fact]
    public Task ControlValuesUpdateOnUiWithoutRescanningOrRestarting() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var f = new Fixture();
        await f.Model.InitializeAsync();
        var capabilities = new CameraControlCapabilities(CameraControlSupport.Supported,
            CameraControlSupport.NotSupported, CameraControlSupport.Error, new(150, 100, 400));
        f.Session.Progress!.Report(new(false, Capabilities: capabilities));
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(capabilities, f.Model.Capabilities);
        Assert.True(f.Model.Active);
        Assert.Equal(1, f.Session.Starts);
        Assert.Equal(1, f.Cameras.Scans);
        await f.Model.ShutdownAsync();
    });

    [Fact]
    public Task StartupRunsWithoutKeyboardInputAndDoesNotRetry() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var f = new Fixture();
        Assert.Equal(TrayIconState.Initializing, f.Model.TrayState);
        await f.Model.InitializeAsync();
        Assert.Equal(new DeviceStatus("Stub", false), f.Model.Controller);
        Assert.Equal(new DeviceStatus("Camera", false), f.Model.Camera);
        Assert.True(f.Model.Active);
        Assert.Equal(TrayIconState.Healthy, f.Model.TrayState);
        Assert.Equal("device-1", f.Options.Value.DevicePath);
        await f.Model.InitializeAsync();
        Assert.Equal(1, f.Session.Starts);
        Assert.Equal(1, f.Cameras.Scans);
        await f.Model.ShutdownAsync();
        Assert.False(f.Model.Active);
        Assert.True(f.Session.CleanedUp);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnrestorableSelectionIsNotSelected(bool missingDevice) => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var f = new Fixture();
        if (missingDevice) f.Cameras.Available = [];
        else f.Store.Camera = null;
        await f.Model.InitializeAsync();
        Assert.Equal(new DeviceStatus("Not selected", true), f.Model.Camera);
        Assert.Equal(TrayIconState.CameraNotSelected, f.Model.TrayState);
        Assert.False(f.Model.Active);
        Assert.Equal(0, f.Session.Starts);
        await f.Model.ShutdownAsync();
    });

    [Fact]
    public Task MissingControllerStillListsCameras() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var f = new Fixture(new MissingController());
        f.Store.Camera = null; // Missing controller takes priority over the orange no-selection state.
        await f.Model.InitializeAsync();
        Assert.Equal(new DeviceStatus("Not found", true), f.Model.Controller);
        Assert.Equal(TrayIconState.Error, f.Model.TrayState);
        Assert.Single(f.Menu.Cameras);
        Assert.False(f.Model.Active);
        Assert.Equal(0, f.Session.Starts);
        await f.Model.ShutdownAsync();
    });

    [Fact]
    public Task SavedChoiceOnlyAffectsNextStartup() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var f = new Fixture();
        var second = new CameraDeviceInfo("Other", "device-2");
        f.Cameras.Available = [new("Camera", "device-1"), second];
        await f.Model.InitializeAsync();
        await f.Menu.SelectCommand.ExecuteAsync(second);
        Assert.Equal(second, f.Store.Camera);
        Assert.Equal("device-1", f.Options.Value.DevicePath);
        Assert.Equal(1, f.Session.Starts);
        Assert.True(f.Model.Active);
        await f.Model.ShutdownAsync();
    });

    [Fact]
    public Task FailedStartupNeverRetries() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var f = new Fixture();
        f.Session.StartFailure = new InvalidOperationException("Camera failed");
        await f.Model.InitializeAsync();
        Assert.Equal(new DeviceStatus("Error", true), f.Model.Camera);
        Assert.Equal(TrayIconState.Error, f.Model.TrayState);
        Assert.False(f.Model.Active);
        await f.Model.InitializeAsync();
        Assert.Equal(1, f.Session.Starts);
        await f.Model.ShutdownAsync();
    });

    [Fact]
    public Task RuntimeFailureStopsWorkerAndRequiresRestart() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var f = new Fixture();
        await f.Model.InitializeAsync();
        f.Activity.CameraError("device-1", "Set failed");
        f.Session.Progress!.Report(new(false, "Set failed"));
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.False(f.Model.Active);
        Assert.Equal(new DeviceStatus("Error", true), f.Model.Camera);
        await f.Model.InitializeAsync();
        Assert.Equal(1, f.Session.Starts);
        await f.Model.ShutdownAsync();
        Assert.True(f.Session.CleanedUp);
    });

    [Fact]
    public Task ShutdownCanCancelStartupBeforeReady() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var f = new Fixture();
        f.Session.Ready = false;
        var startup = f.Model.InitializeAsync();
        await f.Session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(f.Model.Active);
        await f.Model.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await startup;
        Assert.True(f.Session.CleanedUp);
        Assert.False(f.Model.Active);
    });

    private sealed class Fixture
    {
        public FakeCameras Cameras { get; } = new();
        public FakeStore Store { get; } = new();
        public BridgeActivity Activity { get; } = new();
        public FakeSession Session { get; }
        public CameraMenuViewModel Menu { get; }
        public IOptions<CameraToControlOptions> Options { get; } = Microsoft.Extensions.Options.Options.Create(new CameraToControlOptions());
        public StatusViewModel Model { get; }
        public Fixture(IControllerDiscovery? controller = null)
        {
            var logger = new SilentLogger();
            Menu = new(Cameras, Store, logger);
            Session = new(Activity);
            Model = new(controller ?? new ControllerDiscoveryStub(logger), Menu,
                Microsoft.Extensions.Options.Options.Create(new BridgeControllerDeviceOptions
                { Vid = "046D", Pid = "0857", UsagePage = "FF00", Usage = "0001" }),
                Options, Session, Activity, logger);
        }
    }
    private sealed class FakeSession(BridgeActivity activity) : IBridgeSession
    {
        public int Starts { get; private set; }
        public bool Ready { get; set; } = true;
        public bool CleanedUp { get; private set; }
        public Exception? StartFailure { get; set; }
        public IProgress<BridgeNotification>? Progress { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(IProgress<BridgeNotification> progress, CancellationToken cancellationToken)
        {
            Starts++;
            Progress = progress;
            if (StartFailure is not null) throw StartFailure;
            try
            {
                if (Ready) { activity.Ready("device-1"); progress.Report(new(true)); }
                Entered.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally { activity.Stopped("device-1"); CleanedUp = true; }
        }
    }
    private sealed class FakeStore : ICameraSelectionStore
    {
        public CameraDeviceInfo? Camera { get; set; } = new("Camera", "device-1");
        public Task<CameraDeviceInfo?> LoadAsync() => Task.FromResult(Camera);
        public Task SaveAsync(CameraDeviceInfo camera) { Camera = camera; return Task.CompletedTask; }
    }
    private sealed class FakeCameras : ICameraDiscovery
    {
        public IReadOnlyList<CameraDeviceInfo> Available { get; set; } = [new("Camera", "device-1")];
        public int Scans { get; private set; }
        public IReadOnlyList<CameraDeviceInfo> FindCameras() { Scans++; return Available; }
        public object CreateCameraFilter(int vid, int pid, string usbInterface) => throw new NotSupportedException();
    }
    private sealed class MissingController : IControllerDiscovery
    {
        public HidDevice FindController(int vid, int pid, uint fullUsage) => throw new ControllerNotFoundException("Missing");
    }
    private sealed class SilentLogger : ICameraBridgeLogger
    {
        public void LogInfo(string message) { }
        public void LogError(string message) { }
    }
}
