using DirectShowLib;

namespace LogitechCameraBridge.Connector;

/// <summary>Runs on the camera worker; no timer or camera calls on another thread.</summary>
internal sealed class ControllerButtonRepeater(
    IReadOnlyDictionary<int, (CameraControlProperty Property, int Direction)> mappings,
    Func<CameraControlProperty, int, bool> move,
    Action<string>? diagnostic = null)
{
    private int? _held;
    private bool _canRepeat;
    private long _nextRepeat;
    private long _pressedAt;
    private int _repeatCount;

    public void Handle(byte code, long now)
    {
        if (code == 0xFF)
            return;
        if (code == 0x00)
        {
            if (_held is { } held)
                diagnostic?.Invoke($"Button 0x{held:X2} released after {now - _pressedAt}ms; repeats={_repeatCount}.");
            _held = null;
            _canRepeat = false;
            return;
        }
        if (_held == code)
            return; // Duplicate reports must not accelerate the repeat rate.
        _held = code;
        _canRepeat = false;
        _pressedAt = now;
        _repeatCount = 0;
        if (mappings.TryGetValue(code, out var command))
        {
            _canRepeat = move(command.Property, command.Direction);
            _nextRepeat = now + 400;
            diagnostic?.Invoke($"Button 0x{code:X2} pressed; repeat {(_canRepeat ? "armed, initial delay=400ms" : "stopped: camera reported no change, a limit or a control error")}.");
        }
    }

    public void Tick(long now)
    {
        if (!_canRepeat || _held is not { } code || now < _nextRepeat)
            return;
        var command = mappings[code];
        _repeatCount++;
        diagnostic?.Invoke($"Repeat 0x{code:X2} #{_repeatCount} after {now - _pressedAt}ms.");
        _canRepeat = move(command.Property, command.Direction);
        if (!_canRepeat)
            diagnostic?.Invoke($"Repeat 0x{code:X2} stopped: camera reported no change, a limit or a control error.");
        _nextRepeat = now + 100; // No catch-up burst after a slow camera call.
    }
}
