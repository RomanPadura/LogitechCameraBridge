namespace LogitechCameraBridge.Cameras.Models;

/// <summary>A camera identity snapshot that does not retain native resources.</summary>
public sealed record CameraDeviceInfo(string Name, string DevicePath);
