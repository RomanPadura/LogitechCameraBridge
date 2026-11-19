using DirectShowLib;

namespace LogitechCameraBridge.Connector;

/// <summary>Runs on the camera worker; no timer or camera calls on another thread.</summary>
internal sealed class ControllerButtonRepeater(
    IReadOnlyDictionary<int, (CameraControlProperty Property, int Direction)> mappings,
    Func<CameraControlProperty, int, bool> move)
{
    private int? _held;
    private bool _canRepeat;
    private long _nextRepeat;

    public void Handle(byte code, long now)
    {
        if (code == 0xFF)
            return;
        if (code == 0x00)
        {
            _held = null;
            _canRepeat = false;
            return;
        }
        if (_held == code)
            return; // Duplicate reports must not accelerate the repeat rate.
        _held = code;
        _canRepeat = false;
        if (mappings.TryGetValue(code, out var command))
        {
            _canRepeat = move(command.Property, command.Direction);
            _nextRepeat = now + 400;
        }
    }

    public void Tick(long now)
    {
        if (!_canRepeat || _held is not { } code || now < _nextRepeat)
            return;
        var command = mappings[code];
        _canRepeat = move(command.Property, command.Direction);
        _nextRepeat = now + 100; // No catch-up burst after a slow camera call.
    }
}
