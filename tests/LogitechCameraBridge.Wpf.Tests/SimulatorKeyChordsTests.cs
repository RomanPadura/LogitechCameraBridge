using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.Tests;

public class SimulatorKeyChordsTests
{
    [Fact]
    public void MissedPrefixReleaseCannotLeaveSimulatorArmed()
    {
        var chords = new SimulatorKeyChords();
        chords.Process(0x5A, true);
        chords.ReleaseInactivePrefixes(false, false);
        Assert.Equal(default, chords.Process(0xBB, true));
    }
    [Theory]
    [InlineData(0x5A, 0xBB, SimulatorButton.ZoomIn)]
    [InlineData(0x5A, 0x6B, SimulatorButton.ZoomIn)]
    [InlineData(0x5A, 0xBD, SimulatorButton.ZoomOut)]
    [InlineData(0x5A, 0x6D, SimulatorButton.ZoomOut)]
    [InlineData(0x50, 0x25, SimulatorButton.Left)]
    [InlineData(0x50, 0x27, SimulatorButton.Right)]
    [InlineData(0x50, 0x26, SimulatorButton.Up)]
    [InlineData(0x50, 0x28, SimulatorButton.Down)]
    public void ChordEmitsOneCommandUntilActionKeyIsReleased(uint held, uint key, SimulatorButton expected)
    {
        var chords = new SimulatorKeyChords();
        Assert.False(chords.Process(held, true).Suppress);
        Assert.Equal(new SimulatorKeyResult(true, expected), chords.Process(key, true));
        Assert.Equal(new SimulatorKeyResult(true), chords.Process(key, true));
        Assert.True(chords.Process(key, false).Suppress);
        Assert.Equal(expected, chords.Process(key, true).Button);
    }

    [Fact]
    public void UnarmedAndUnrelatedKeysPassThrough()
    {
        var chords = new SimulatorKeyChords();
        Assert.Equal(default, chords.Process(0xBB, true));
        chords.Process(0xBB, false);
        chords.Process(0x5A, true);
        Assert.Equal(default, chords.Process(0x25, true));
        Assert.Equal(default, chords.Process(0x41, true));
        chords.Process(0x5A, false);
        Assert.Equal(default, chords.Process(0xBD, true));
    }

    [Fact]
    public void ReleasingPrefixFirstStillConsumesActionKeyRepeatAndRelease()
    {
        var chords = new SimulatorKeyChords();
        chords.Process(0x50, true);
        chords.Process(0x25, true);
        Assert.False(chords.Process(0x50, false).Suppress);
        Assert.Equal(new SimulatorKeyResult(true), chords.Process(0x25, true));
        Assert.True(chords.Process(0x25, false).Suppress);
        Assert.Equal(default, chords.Process(0x25, true));
    }

    [Theory]
    [InlineData(0xA2)]
    [InlineData(0xA4)]
    [InlineData(0x5B)]
    [InlineData(0x50)]
    public void ConflictingModifiersOrBothPrefixesPreventMovement(uint conflicting)
    {
        var chords = new SimulatorKeyChords();
        chords.Process(0x5A, true);
        chords.Process(conflicting, true);
        Assert.Equal(default, chords.Process(0xBB, true));
    }

    [Fact]
    public void HoldingActionKeyBeforePrefixDoesNotTriggerOnRepeat()
    {
        var chords = new SimulatorKeyChords();
        chords.Process(0xBB, true);
        chords.Process(0x5A, true);
        Assert.Equal(default, chords.Process(0xBB, true));
        chords.Process(0xBB, false);
        Assert.Equal(SimulatorButton.ZoomIn, chords.Process(0xBB, true).Button);
    }
}
