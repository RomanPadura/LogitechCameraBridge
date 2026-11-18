using System.Runtime.InteropServices;
using DirectShowLib;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Connector;

internal sealed class CameraControls(IAMCameraControl camera, ICameraBridgeLogger logger)
{
    private readonly Dictionary<CameraControlProperty, ControlRange> _ranges = [];

    public void Initialize()
    {
        _ranges.Clear();
        ReadRange(CameraControlProperty.Zoom, required: true);
        ReadRange(CameraControlProperty.Pan, required: false);
        ReadRange(CameraControlProperty.Tilt, required: false);
    }

    public void Move(CameraControlProperty property, int direction)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));

        if (!_ranges.TryGetValue(property, out var range))
        {
            logger.LogInfo($"{property} is unavailable; ignoring button.");
            return;
        }

        try
        {
            CheckResult(camera.Get(property, out int current, out _), property, "Get");

            int next = (int)Math.Clamp((long)current + direction * (long)range.Step,
                range.Min, range.Max);
            if (next == current)
            {
                logger.LogInfo($"{property} limit reached: {current}");
                return;
            }

            CheckResult(camera.Set(property, next, CameraControlFlags.Manual), property, "Set");
            logger.LogInfo($"{property}: {current} -> {next}");
        }
        catch (COMException ex)
        {
            // A driver error on one axis must not stop processing other controls.
            logger.LogError(ex.ToString());
        }
    }

    private void ReadRange(CameraControlProperty property, bool required)
    {
        try
        {
            CheckResult(camera.GetRange(property, out int min, out int max,
                out int step, out int defaultValue, out var capabilities), property, "GetRange");

            if ((capabilities & CameraControlFlags.Manual) == 0)
                throw new NotSupportedException($"The camera does not support manual {property}.");

            if (min > max || step <= 0)
                throw new InvalidOperationException(
                    $"Invalid {property} range: Min={min}, Max={max}, Step={step}.");

            CheckResult(camera.Get(property, out int current, out _), property, "Get");
            _ranges[property] = new ControlRange(min, max, step);

            logger.LogInfo($"Camera {property} capabilities:");
            logger.LogInfo($"Min:          {min}");
            logger.LogInfo($"Max:          {max}");
            logger.LogInfo($"Step:         {step}");
            logger.LogInfo($"Default:      {defaultValue}");
            logger.LogInfo($"Current:      {current}");
            logger.LogInfo($"Capabilities: {capabilities}");
            logger.LogInfo(string.Empty);
        }
        catch (Exception ex) when (!required &&
            (ex is COMException or NotSupportedException or InvalidOperationException))
        {
            logger.LogError($"{property} is unavailable. {ex.Message}");
        }
    }

    private static void CheckResult(int result, CameraControlProperty property, string operation)
    {
        if (result < 0)
            throw new COMException($"{property} {operation} failed (HRESULT=0x{result:X8}).", result);
    }

    private sealed record ControlRange(int Min, int Max, int Step);
}
