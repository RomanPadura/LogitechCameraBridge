namespace LogitechCameraBridge.Connector.Models;

public enum CameraControlSupport { NotChecked, Supported, NotSupported, Error }

public sealed record CameraControlValue(int Current, int Min, int Max);

/// <summary>Manual-control support reported by the driver, without issuing movement commands.</summary>
public sealed record CameraControlCapabilities(
    CameraControlSupport Zoom, CameraControlSupport Pan, CameraControlSupport Tilt,
    CameraControlValue? ZoomValue = null, CameraControlValue? PanValue = null, CameraControlValue? TiltValue = null)
{
    public static CameraControlCapabilities NotChecked { get; } = new(
        CameraControlSupport.NotChecked, CameraControlSupport.NotChecked, CameraControlSupport.NotChecked);
    public static CameraControlCapabilities NotSupported { get; } = new(
        CameraControlSupport.NotSupported, CameraControlSupport.NotSupported, CameraControlSupport.NotSupported);
}
