using System.Globalization;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Logger;
using Microsoft.Extensions.Options;

namespace LogitechCameraBridge.Connector.Tests;

public class CameraBridgeLoggerTests : IDisposable
{
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "logger.txt");
    private readonly byte[]? _original;

    public CameraBridgeLoggerTests()
    {
        _original = File.Exists(_path) ? File.ReadAllBytes(_path) : null;
        File.Delete(_path);
    }

    [Fact]
    public async Task KeepsNewestEntriesFirstAcrossLoggerInstances()
    {
        File.WriteAllLines(_path, ["old entry", "older entry"]);
        CreateLogger(3).LogInfo("first");
        var logger = CreateLogger(3);
        logger.LogError("second");
        await logger.FlushAsync();

        var lines = File.ReadAllLines(_path);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith("|Error|second", lines[0]);
        Assert.EndsWith("|Info|first", lines[1]);
        Assert.Equal("old entry", lines[2]);
        Assert.True(DateTimeOffset.TryParseExact(lines[0].Split('|')[0], "O",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
    }

    [Fact]
    public async Task MultilineMessagesStayOnOneLine()
    {
        var logger = CreateLogger(1);
        logger.LogInfo("discarded");
        logger.LogError("first\r\nsecond\nthird\rfourth");
        await logger.FlushAsync();

        Assert.EndsWith("|Error|first\\nsecond\\nthird\\nfourth",
            Assert.Single(File.ReadAllLines(_path)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveLimitDisablesFileLogging(int limit)
    {
        var logger = CreateLogger(limit);
        logger.LogInfo("info");
        logger.LogError("error");

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task ConcurrentWritesKeepCompleteEntriesWithinLimit()
    {
        var logger = CreateLogger(10);
        Parallel.For(0, 30, i => logger.LogInfo($"entry {i}"));
        await logger.FlushAsync();

        var lines = File.ReadAllLines(_path);
        Assert.Equal(10, lines.Length);
        Assert.Equal(10, lines.Distinct().Count());
        Assert.All(lines, line => Assert.Contains("|Info|entry ", line));
    }

    private static CameraBridgeLogger CreateLogger(int limit) =>
        new(Options.Create(new LoggerOptions { LogToConsole = false, MaxTextFileLines = limit }));

    [Fact]
    public async Task RapidLoggingPreservesCallOrder()
    {
        var logger = CreateLogger(10);
        for (var i = 0; i < 30; i++)
            logger.LogInfo($"entry {i}");

        await logger.FlushAsync();

        Assert.Equal(Enumerable.Range(20, 10).Reverse().Select(i => $"entry {i}"),
            File.ReadAllLines(_path).Select(line => line.Split('|')[2]));
    }

    [Fact]
    public async Task FileAccessFailureDoesNotStopLaterWrites()
    {
        var logger = CreateLogger(10);
        using (var heldFile = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            logger.LogInfo("cannot write");
            await logger.FlushAsync();
        }

        logger.LogInfo("recovered");
        await logger.FlushAsync();

        Assert.EndsWith("|Info|recovered", Assert.Single(File.ReadAllLines(_path)));
    }

    public void Dispose()
    {
        if (_original is not null)
            File.WriteAllBytes(_path, _original);
        else
            File.Delete(_path);
    }
}
