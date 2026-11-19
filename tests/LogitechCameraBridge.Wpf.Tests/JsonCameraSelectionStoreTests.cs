using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LogitechCameraBridge.Cameras.Models;
using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.Tests;

public sealed class JsonCameraSelectionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CameraBridgeSelectionTests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "userSettings.json");

    [Fact]
    public async Task MissingSettingsHaveNoSelection()
    {
        using var store = new JsonCameraSelectionStore(SettingsPath);
        Assert.Null(await store.LoadAsync());
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task SelectionSurvivesNewStoreInstanceAndPreservesUnrelatedSettings()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{\"OtherSetting\":{\"Enabled\":true}}");
        var camera = new CameraDeviceInfo("Camera & virtual input", "@device:pnp:\\?\\usb#camera-instance-2");
        using (var store = new JsonCameraSelectionStore(SettingsPath))
            await store.SaveAsync(camera);

        using var restartedStore = new JsonCameraSelectionStore(SettingsPath);
        Assert.Equal(camera, await restartedStore.LoadAsync());
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!;
        Assert.True(settings["OtherSetting"]!["Enabled"]!.GetValue<bool>());
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SameNameCamerasArePersistedByDistinctDevicePath()
    {
        using var store = new JsonCameraSelectionStore(SettingsPath);
        await store.SaveAsync(new("Same camera name", "device-1"));
        var second = new CameraDeviceInfo("Same camera name", "device-2");
        await store.SaveAsync(second);
        Assert.Equal(second, await store.LoadAsync());
    }

    [Fact]
    public async Task MalformedSettingsAreReportedAndNeverOverwritten()
    {
        Directory.CreateDirectory(_directory);
        const string original = "{ broken settings";
        await File.WriteAllTextAsync(SettingsPath, original);
        using var store = new JsonCameraSelectionStore(SettingsPath);
        await Assert.ThrowsAnyAsync<JsonException>(() => store.LoadAsync());
        await Assert.ThrowsAnyAsync<JsonException>(() => store.SaveAsync(new("Camera", "device-1")));
        Assert.Equal(original, await File.ReadAllTextAsync(SettingsPath));
    }

    [Fact]
    public async Task FailedReplacementPreservesPreviousSelectionAndCleansTemporaryFile()
    {
        using var store = new JsonCameraSelectionStore(SettingsPath);
        var previous = new CameraDeviceInfo("Camera", "device-1");
        await store.SaveAsync(previous);
        using (var lockedFile = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Record.ExceptionAsync(() => store.SaveAsync(new("Camera", "device-2")));
            Assert.True(failure is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(previous, await store.LoadAsync());
        Assert.Single(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
            return;
        foreach (string file in Directory.GetFiles(_directory))
            File.Delete(file);
        Directory.Delete(_directory);
    }
}
