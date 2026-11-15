namespace LogitechCameraBridgeConsole.Options;

public sealed class BridgeControllerDeviceOptions
{
    public const string SectionName = "BridgeControllerDevice";

    public string Vid { get; set; } = string.Empty;
    public string Pid { get; set; } = string.Empty;
    public string UsagePage { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
    public string ZoomInCode { get; set; } = string.Empty;
    public string ZoomOutCode { get; set; } = string.Empty;
}
