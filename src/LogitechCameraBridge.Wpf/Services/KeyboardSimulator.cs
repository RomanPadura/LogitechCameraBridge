using System.Globalization;
using System.IO;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Controllers;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Wpf.Services;

public enum SimulatorButton { ZoomIn, ZoomOut, Left, Right, Up, Down }

/// <summary>Submits input only to the bridge already initialized at application startup.</summary>
public sealed class KeyboardSimulator(
    ControllerDiscoveryStub stub,
    IOptions<BridgeControllerDeviceOptions> controllerOptions,
    BridgeActivity activity)
{
    public Task PressAsync(SimulatorButton button)
    {
        var state = activity.Snapshot;
        if (!state.IsReady || state.Error is not null)
            return Task.CompletedTask;
        var options = controllerOptions.Value;
        string code = button switch
        {
            SimulatorButton.ZoomIn => options.ZoomInCode,
            SimulatorButton.ZoomOut => options.ZoomOutCode,
            SimulatorButton.Left => options.PanLeftCode,
            SimulatorButton.Right => options.PanRightCode,
            SimulatorButton.Up => options.TiltUpCode,
            SimulatorButton.Down => options.TiltDownCode,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };
        if (!stub.TryPressButton(ParseByte(options.ReportId), ParseByte(code)))
            throw new InvalidOperationException("Controller input is unavailable. Restart the application.");
        return Task.CompletedTask;
    }

    private static byte ParseByte(string value)
    {
        var hex = value.AsSpan().Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        if (!byte.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte result))
            throw new InvalidDataException("Simulator button codes and report ID must be hexadecimal bytes.");
        return result;
    }
}
