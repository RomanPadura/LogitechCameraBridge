using LogitechCameraBridge.Connector.Models;

namespace LogitechCameraBridge.Wpf.Models;

public sealed record BridgeNotification(bool IsReady, string? Error = null, CameraControlCapabilities? Capabilities = null);
