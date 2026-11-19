using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LogitechCameraBridge.Cameras.Models;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class JsonCameraSelectionStore(string filePath) : ICameraSelectionStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task<CameraDeviceInfo?> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await ReadSettingsAsync().ConfigureAwait(false);
            var camera = settings["SelectedCamera"]?.Deserialize<CameraDeviceInfo>();
            if (camera is not null)
                ValidateCamera(camera);
            return camera;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CameraDeviceInfo camera)
    {
        ValidateCamera(camera);
        await _gate.WaitAsync().ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            // Preserve other user settings and never replace malformed JSON silently.
            var settings = await ReadSettingsAsync().ConfigureAwait(false);
            settings["SelectedCamera"] = JsonSerializer.SerializeToNode(camera);
            string fullPath = Path.GetFullPath(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, settings.ToJsonString(JsonOptions)).ConfigureAwait(false);
            // Replace only once the complete new document has been written on the same volume.
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (temporaryPath is not null && File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task<JsonObject> ReadSettingsAsync()
    {
        try
        {
            string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            return JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("User settings must contain a JSON object.");
        }
        catch (FileNotFoundException)
        {
            return new JsonObject();
        }
        catch (DirectoryNotFoundException)
        {
            return new JsonObject();
        }
    }

    private static void ValidateCamera(CameraDeviceInfo camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (string.IsNullOrWhiteSpace(camera.Name) || string.IsNullOrWhiteSpace(camera.DevicePath))
            throw new InvalidDataException("The saved camera requires a name and device path.");
    }
}
