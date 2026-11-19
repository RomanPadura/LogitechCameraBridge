using LogitechCameraBridge.Cameras.Models;

namespace LogitechCameraBridge.Wpf.Services;

public interface ICameraSelectionStore
{
    Task<CameraDeviceInfo?> LoadAsync();
    Task SaveAsync(CameraDeviceInfo camera);
}
