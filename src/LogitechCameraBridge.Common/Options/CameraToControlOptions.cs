namespace LogitechCameraBridge.Common.Options;

public class CameraToControlOptions
{
    public const string SectionName = "CameraToControl";

    public string Vid { get; set; } = string.Empty;

    public string Pid { get; set; } = string.Empty;

    public string UsbInterface { get; set; } = string.Empty;

    public string UsbInterfacePrefix { get; set; } = string.Empty;

    public string Device { get; set; } = string.Empty;

    public string? DevicePath { get; set; }
}
