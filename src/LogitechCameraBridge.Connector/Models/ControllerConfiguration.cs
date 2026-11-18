using DirectShowLib;

namespace LogitechCameraBridge.Connector.Models;

internal sealed record ControllerConfiguration(
    int Vid,
    int Pid,
    uint FullUsage,
    int ReportId,
    IReadOnlyDictionary<int, (CameraControlProperty Property, int Direction)> ButtonMappings);
