using LogitechCameraBridge.Common.Options;

namespace LogitechCameraBridge.Connector.Tests;

public class ButtonRepeatOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("true")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("60001")]
    [InlineData("99999999999999999999999")]
    public void InvalidValuesFallBackIndependently(string? invalid)
    {
        var options = new ButtonRepeatOptions();
        options.SetTimings(invalid, "250");
        Assert.Equal(100, options.RepeatIntervalMs);
        Assert.Equal(250, options.InitialDelayMs);
        options.SetTimings("75", invalid);
        Assert.Equal(75, options.RepeatIntervalMs);
        Assert.Equal(400, options.InitialDelayMs);
        options.SetTimings(invalid, invalid);
        Assert.Equal(100, options.RepeatIntervalMs);
        Assert.Equal(400, options.InitialDelayMs);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("60000", 60000)]
    [InlineData(" 200 ", 200)]
    public void ValidIntegerMillisecondsAreUsed(string value, int expected)
    {
        var options = new ButtonRepeatOptions();
        options.SetTimings(value, value);
        Assert.Equal(expected, options.RepeatIntervalMs);
        Assert.Equal(expected, options.InitialDelayMs);
    }
}
