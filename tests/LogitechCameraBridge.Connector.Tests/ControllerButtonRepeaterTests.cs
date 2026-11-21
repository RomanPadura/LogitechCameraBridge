using DirectShowLib;
using LogitechCameraBridge.Common.Options;

namespace LogitechCameraBridge.Connector.Tests;

public sealed class ControllerButtonRepeaterTests
{
    [Fact]
    public void ConfiguredDelayAndIntervalControlRepeatDeadlinesWithoutChangingTapOrRelease()
    {
        var timing = new ButtonRepeatOptions();
        timing.SetTimings("75", "250");
        int moves = 0;
        var buttons = new ControllerButtonRepeater(Mappings, (_, _) => { moves++; return true; }, timing: timing);
        buttons.Handle(0x10, 1000);
        buttons.Handle(0x00, 1015);
        Assert.Equal(1, moves);
        buttons.Tick(1249);
        Assert.Equal(1, moves);
        buttons.Tick(1250);
        Assert.Equal(2, moves);
        buttons.Tick(1324);
        Assert.Equal(2, moves);
        buttons.Tick(1325);
        Assert.Equal(3, moves);
        buttons.Handle(0xFF, 1400);
        buttons.Tick(1400);
        buttons.Tick(2000);
        Assert.Equal(3, moves);
    }

    private static readonly Dictionary<int, (CameraControlProperty Property, int Direction)> Mappings = new()
    {
        [0x10] = (CameraControlProperty.Zoom, 1),
        [0x20] = (CameraControlProperty.Zoom, -1),
        [0x08] = (CameraControlProperty.Pan, -1),
        [0x04] = (CameraControlProperty.Pan, 1),
        [0x01] = (CameraControlProperty.Tilt, 1),
        [0x02] = (CameraControlProperty.Tilt, -1)
    };

    [Theory]
    [InlineData(0x10, CameraControlProperty.Zoom, 1)]
    [InlineData(0x20, CameraControlProperty.Zoom, -1)]
    [InlineData(0x08, CameraControlProperty.Pan, -1)]
    [InlineData(0x04, CameraControlProperty.Pan, 1)]
    [InlineData(0x01, CameraControlProperty.Tilt, 1)]
    [InlineData(0x02, CameraControlProperty.Tilt, -1)]
    public void HoldRepeatsAfterDelayAndReleaseStops(byte code, CameraControlProperty axis, int direction)
    {
        var moves = new List<(CameraControlProperty, int)>();
        var buttons = new ControllerButtonRepeater(Mappings, (p, d) => { moves.Add((p, d)); return true; });
        buttons.Handle(code, 0);
        buttons.Handle(0x00, 15); // Neutral report while the physical button is still down.
        Assert.Equal((axis, direction), Assert.Single(moves));
        buttons.Tick(399);
        Assert.Single(moves);
        buttons.Tick(400);
        buttons.Tick(499);
        Assert.Equal(2, moves.Count);
        buttons.Tick(500);
        Assert.Equal(3, moves.Count);
        buttons.Handle(0xFF, 600);
        buttons.Handle(0x00, 600);
        buttons.Tick(600);
        buttons.Tick(10000);
        Assert.Equal(3, moves.Count);
    }

    [Fact]
    public void DuplicatePressDoesNotAccelerateOrPostponeRepeatAndZeroIsIgnored()
    {
        int moves = 0;
        var buttons = new ControllerButtonRepeater(Mappings, (_, _) => { moves++; return true; });
        buttons.Handle(0x10, 0);
        buttons.Handle(0x10, 200);
        buttons.Handle(0x00, 300);
        buttons.Tick(400);
        Assert.Equal(2, moves);
        buttons.Tick(10000);
        Assert.Equal(3, moves); // No catch-up burst.
    }

    [Fact]
    public void OppositeDirectionStartsNewHold()
    {
        var moves = new List<int>();
        var buttons = new ControllerButtonRepeater(Mappings, (_, d) => { moves.Add(d); return true; });
        buttons.Handle(0x10, 0);
        buttons.Handle(0x20, 200);
        buttons.Tick(400);
        Assert.Equal(new[] { 1, -1 }, moves);
        buttons.Tick(600);
        Assert.Equal(new[] { 1, -1, -1 }, moves);
    }

    [Fact]
    public void LimitOrDriverFailureStopsRepeatingUntilANewPress()
    {
        int moves = 0;
        var buttons = new ControllerButtonRepeater(Mappings, (_, _) => { moves++; return moves == 1; });
        buttons.Handle(0x10, 0);
        buttons.Tick(400);
        buttons.Tick(500);
        buttons.Handle(0x10, 600);
        buttons.Tick(1000);
        Assert.Equal(2, moves);
        buttons.Handle(0xFF, 1100);
        buttons.Handle(0x10, 1200);
        Assert.Equal(3, moves);
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0x40)]
    public void UnmappedReportStopsHeldMovementWithoutMovingCamera(byte code)
    {
        int moves = 0;
        var buttons = new ControllerButtonRepeater(Mappings, (_, _) => { moves++; return true; });
        buttons.Handle(0x10, 0);
        buttons.Handle(code, 200);
        buttons.Tick(400);
        Assert.Equal(1, moves);
    }
}
