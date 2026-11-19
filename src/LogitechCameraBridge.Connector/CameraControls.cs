using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using LogitechCameraBridge.Connector.Models;
using DirectShowLib;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Connector;

internal sealed class CameraControls(IAMCameraControl camera, ICameraBridgeLogger logger)
{
    private readonly Dictionary<CameraControlProperty, ControlRange> _ranges = [];
    private readonly Dictionary<CameraControlProperty, CameraControlValue> _values = [];
    private CameraControlCapabilities _capabilities = CameraControlCapabilities.NotChecked;
    private Action<CameraControlCapabilities>? _onCapabilities;

    public void Initialize()
        => Initialize(null);

    public void Initialize(Action<CameraControlCapabilities>? onCapabilities)
    {
        _ranges.Clear();
        _values.Clear();
        _onCapabilities = onCapabilities;
        var zoomFailure = ReadRange(CameraControlProperty.Zoom, out var zoom);
        ReadRange(CameraControlProperty.Pan, out var pan);
        ReadRange(CameraControlProperty.Tilt, out var tilt);
        _capabilities = new(zoom, pan, tilt,
            _values.GetValueOrDefault(CameraControlProperty.Zoom),
            _values.GetValueOrDefault(CameraControlProperty.Pan),
            _values.GetValueOrDefault(CameraControlProperty.Tilt));
        _onCapabilities?.Invoke(_capabilities);
        // Report every axis even if required Zoom is unavailable, then preserve startup failure.
        if (zoomFailure is not null)
            ExceptionDispatchInfo.Capture(zoomFailure).Throw();
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
                PublishValue(property, new(current, range.Min, range.Max));
                logger.LogInfo($"{property} limit reached: {current}");
                return;
            }

            CheckResult(camera.Set(property, next, CameraControlFlags.Manual), property, "Set");
            CheckResult(camera.Get(property, out int actual, out _), property, "Get");
            PublishValue(property, new(actual, range.Min, range.Max));
            logger.LogInfo($"{property}: {current} -> {actual}");
        }
        catch (COMException ex)
        {
            PublishValue(property, null);
            // A driver error on one axis must not stop processing other controls.
            logger.LogError(ex.ToString());
        }
    }

    private void PublishValue(CameraControlProperty property, CameraControlValue? value)
    {
        var support = value is null ? CameraControlSupport.Error : CameraControlSupport.Supported;
        _capabilities = property switch
        {
            CameraControlProperty.Zoom => _capabilities with { Zoom = support, ZoomValue = value },
            CameraControlProperty.Pan => _capabilities with { Pan = support, PanValue = value },
            CameraControlProperty.Tilt => _capabilities with { Tilt = support, TiltValue = value },
            _ => _capabilities
        };
        _onCapabilities?.Invoke(_capabilities);
    }

    private Exception? ReadRange(CameraControlProperty property, out CameraControlSupport support)
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
            _values[property] = new(current, min, max);

            logger.LogInfo($"Camera {property} capabilities:");
            logger.LogInfo($"Min:          {min}");
            logger.LogInfo($"Max:          {max}");
            logger.LogInfo($"Step:         {step}");
            logger.LogInfo($"Default:      {defaultValue}");
            logger.LogInfo($"Current:      {current}");
            logger.LogInfo($"Capabilities: {capabilities}");
            logger.LogInfo(string.Empty);
            support = CameraControlSupport.Supported;
            return null;
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException or InvalidOperationException)
        {
            support = ex is NotSupportedException || ex is COMException com &&
                com.HResult is unchecked((int)0x80004001) or unchecked((int)0x80070490) or unchecked((int)0x80070492)
                    ? CameraControlSupport.NotSupported : CameraControlSupport.Error;
            logger.LogError($"{property} is unavailable. {ex.Message}");
            return ex;
        }
    }

    private static void CheckResult(int result, CameraControlProperty property, string operation)
    {
        if (result < 0)
            throw new COMException($"{property} {operation} failed (HRESULT=0x{result:X8}).", result);
    }
}
