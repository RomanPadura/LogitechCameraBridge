using LogitechCameraBridge.Wpf.Models;

namespace LogitechCameraBridge.Wpf.Services;

/// <summary>Tracks only simulator keys and conflicting modifiers; never records typed text.</summary>
public sealed class SimulatorKeyChords
{
    private readonly HashSet<uint> _down = [];
    private readonly HashSet<uint> _consumed = [];

    public void ReleaseInactivePrefixes(bool zoomHeld, bool panTiltHeld)
    {
        // Recover from releases missed while the input desktop was unavailable.
        if (!zoomHeld)
            _down.Remove(0x5A);
        if (!panTiltHeld)
            _down.Remove(0x50);
    }

    public SimulatorKeyResult Process(uint key, bool isDown)
    {
        if (!IsRelevant(key))
            return default;
        if (!isDown)
        {
            _down.Remove(key);
            return new(_consumed.Remove(key));
        }
        if (!_down.Add(key))
            return new(_consumed.Contains(key)); // Ignore auto-repeat.
        if (_down.Any(IsConflictingModifier))
            return default;
        bool zoom = _down.Contains(0x5A); // Z.
        bool panTilt = _down.Contains(0x50); // P.
        if (zoom == panTilt)
            return default; // Neither or both held: no movement.
        SimulatorButton? button = zoom ? key switch
        {
            0xBB or 0x6B => SimulatorButton.ZoomIn,
            0xBD or 0x6D => SimulatorButton.ZoomOut,
            _ => null
        } : key switch
        {
            0x25 => SimulatorButton.Left,
            0x27 => SimulatorButton.Right,
            0x26 => SimulatorButton.Up,
            0x28 => SimulatorButton.Down,
            _ => null
        };
        if (button is null)
            return default;
        _consumed.Add(key);
        return new(true, button);
    }

    private static bool IsRelevant(uint key) => key is 0x5A or 0x50 or 0xBB or 0xBD or 0x6B or 0x6D
        or >= 0x25 and <= 0x28 || IsConflictingModifier(key);
    private static bool IsConflictingModifier(uint key) => key is 0x11 or 0x12 or 0x5B or 0x5C
        or >= 0xA2 and <= 0xA5; // Ctrl, Alt, Windows. Shift is permitted for main +.
}
