namespace LogitechCameraBridge.Wpf.Models;

public sealed record BridgeActivitySnapshot(string? DevicePath, bool IsReady, string? Error);
