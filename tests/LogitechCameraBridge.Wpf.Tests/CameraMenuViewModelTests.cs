using System.IO;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Cameras.Models;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using LogitechCameraBridge.Wpf.ViewModels;

namespace LogitechCameraBridge.Wpf.Tests;

public class CameraMenuViewModelTests
{
    [Fact]
    public Task RefreshScansOffUiThreadAndRetainsUnavailableSavedSelection() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        int uiThread = Environment.CurrentManagedThreadId;
        int scanThread = 0;
        ApartmentState apartment = ApartmentState.Unknown;
        var available = new CameraDeviceInfo("Available", "device-1");
        var saved = new CameraDeviceInfo("Disconnected", "device-2");
        var store = new FakeStore { Selected = saved };
        var discovery = new FakeDiscovery(() =>
        {
            scanThread = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
            return [available];
        });
        var model = new CameraMenuViewModel(discovery, store, new SilentLogger());
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
        Assert.NotEqual(uiThread, scanThread);
        Assert.Equal(ApartmentState.MTA, apartment);
        Assert.Equal(saved, model.SelectedCamera);
        Assert.Equal(available, Assert.Single(model.Cameras));
        Assert.Empty(model.Error);
        Assert.False(model.IsBusy);
    });

    [Fact]
    public Task SelectionChangesOnlyAfterSuccessfulSaveAndShutdownWaits() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var first = new CameraDeviceInfo("Same name", "device-1");
        var second = new CameraDeviceInfo("Same name", "device-2");
        var pendingSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeStore { Selected = first, PendingSave = pendingSave.Task };
        var model = new CameraMenuViewModel(new FakeDiscovery(() => [first, second]), store, new SilentLogger());
        await model.RefreshCommand.ExecuteAsync(null);
        var select = model.SelectCommand.ExecuteAsync(second);
        Assert.Equal(first, model.SelectedCamera);
        Assert.True(model.IsBusy);
        Assert.False(model.RefreshCommand.CanExecute(null));
        Assert.False(model.SelectCommand.CanExecute(first));
        var closing = model.ShutdownAsync();
        Assert.False(closing.IsCompleted);
        pendingSave.SetResult();
        await Task.WhenAll(select, closing);
        Assert.Equal(second, model.SelectedCamera);
        Assert.Equal(second, store.Selected);
        Assert.False(model.RefreshCommand.CanExecute(null));
        Assert.False(model.SelectCommand.CanExecute(first));
    });

    [Fact]
    public Task SaveFailureKeepsPreviousSelectionAndReportsError() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var first = new CameraDeviceInfo("Camera", "device-1");
        var second = new CameraDeviceInfo("Camera", "device-2");
        var store = new FakeStore { Selected = first, PendingSave = Task.FromException(new IOException("Disk is full")) };
        var model = new CameraMenuViewModel(new FakeDiscovery(() => [first, second]), store, new SilentLogger());
        await model.RefreshCommand.ExecuteAsync(null);
        await model.SelectCommand.ExecuteAsync(second);
        Assert.Equal(first, model.SelectedCamera);
        Assert.Equal(first, store.Selected);
        Assert.Equal("Could not save camera selection", model.Error);
        Assert.False(model.SelectCommand.CanExecute(second));
    });

    [Fact]
    public Task CameraListIsScannedOnlyOnce() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        int scans = 0;
        var model = new CameraMenuViewModel(new FakeDiscovery(() =>
            ++scans == 1 ? [new("Camera", "device-1")] : []), new FakeStore(), new SilentLogger());
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Single(model.Cameras);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Single(model.Cameras);
        Assert.Empty(model.Error);
        Assert.Equal(1, scans);
    });

    [Fact]
    public Task ScanFailureIsRetainedUntilRestart() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        int scans = 0;
        var model = new CameraMenuViewModel(new FakeDiscovery(() => ++scans == 1
            ? throw new InvalidOperationException("Enumeration failed") : []), new FakeStore(), new SilentLogger());
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.NotEmpty(model.Error);
        Assert.Empty(model.Cameras);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.NotEmpty(model.Error);
        Assert.Equal(1, scans);
    });

    private sealed class FakeDiscovery(Func<IReadOnlyList<CameraDeviceInfo>> scan) : ICameraDiscovery
    {
        public IReadOnlyList<CameraDeviceInfo> FindCameras() => scan();
        public object CreateCameraFilter(int vid, int pid, string usbInterface)
            => throw new InvalidOperationException("Listing cameras must not open camera filters.");
    }

    private sealed class FakeStore : ICameraSelectionStore
    {
        public CameraDeviceInfo? Selected { get; set; }
        public Task PendingSave { get; set; } = Task.CompletedTask;
        public Task<CameraDeviceInfo?> LoadAsync() => Task.FromResult(Selected);
        public async Task SaveAsync(CameraDeviceInfo camera)
        {
            await PendingSave;
            Selected = camera;
        }
    }

    private sealed class SilentLogger : ICameraBridgeLogger
    {
        public void LogInfo(string message) { }
        public void LogError(string message) { }
    }
}
