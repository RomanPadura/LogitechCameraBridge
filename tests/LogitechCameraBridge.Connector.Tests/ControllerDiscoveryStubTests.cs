using System.Diagnostics;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Connector.Tests;

public class ControllerDiscoveryStubTests
{
    [Fact]
    public void TapQueuesPressAndReleaseAndCannotPartiallyFillQueue()
    {
        var stub = new ControllerDiscoveryStub(new SilentLogger());
        using var stream = stub.FindController(1, 2, 3).Open();
        stream.ReadTimeout = 1;
        Assert.True(stub.TryTapButton(0x0A, 0x10));
        Assert.Equal(0x10, stream.Read()[1]);
        Assert.Equal(0x00, stream.Read()[1]);
        for (int i = 0; i < 31; i++)
            Assert.True(stub.TryPressButton(0x0A, 0xFF));
        Assert.False(stub.TryTapButton(0x0A, 0x20));
        for (int i = 0; i < 31; i++)
            Assert.Equal(0xFF, stream.Read()[1]);
        Assert.Throws<TimeoutException>(() => stream.Read());
    }

    [Fact]
    public void SubmittedButtonUsesRealReportLayoutAndDoesNotSurviveReopen()
    {
        var stub = new ControllerDiscoveryStub(new SilentLogger());
        Assert.False(stub.TryPressButton(0x0A, 0x10));
        var device = stub.FindController(1, 2, 3);
        using (var stream = device.Open())
        {
            Assert.True(stub.TryPressButton(0x0A, 0x10));
            byte[] report = stream.Read();
            Assert.Equal(13, report.Length);
            Assert.Equal(0x0A, report[0]);
            Assert.Equal(0x10, report[1]);
            Assert.All(report.Skip(2), value => Assert.Equal(0, value));
            Assert.True(stub.TryPressButton(0x0A, 0x20));
        }
        Assert.False(stub.TryPressButton(0x0A, 0x10));
        using var reopened = device.Open();
        reopened.ReadTimeout = 1;
        Assert.Throws<TimeoutException>(() => reopened.Read());
    }

    [Fact]
    public void DiscoveryReturnsSimulatedDeviceWithRequestedIdentity()
    {
        var discovery = new ControllerDiscoveryStub(new SilentLogger());
        var device = discovery.FindController(0x046D, 0x0857, 0xFF000001);
        Assert.Equal(0x046D, device.VendorID);
        Assert.Equal(0x0857, device.ProductID);
        Assert.StartsWith("stub://", device.DevicePath);
        Assert.Contains("FF000001", device.DevicePath);
        Assert.Contains("Simulated", device.GetFriendlyName());
        Assert.Equal(13, device.GetMaxInputReportLength());
    }

    [Fact]
    public void IdleStreamWaitsForTimeoutWithoutGeneratingButtonReports()
    {
        var device = new ControllerDiscoveryStub(new SilentLogger()).FindController(1, 2, 3);
        using var stream = device.Open();
        Assert.Equal(3000, stream.ReadTimeout);
        stream.ReadTimeout = 40;
        var elapsed = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => stream.Read());
        Assert.True(elapsed.ElapsedMilliseconds >= 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.ReadTimeout = 0);
    }

    [Fact]
    public void DisposedStreamCannotReadAndDeviceCanBeOpenedAgain()
    {
        var device = new ControllerDiscoveryStub(new SilentLogger()).FindController(1, 2, 3);
        var previous = device.Open();
        previous.Dispose();
        Assert.Throws<ObjectDisposedException>(() => previous.Read());
        using var next = device.Open();
        next.ReadTimeout = 1;
        Assert.Throws<TimeoutException>(() => next.Read());
    }

    [Fact]
    public async Task DisposalUnblocksAnInfiniteRead()
    {
        var device = new ControllerDiscoveryStub(new SilentLogger()).FindController(1, 2, 3);
        using var stream = device.Open();
        stream.ReadTimeout = Timeout.Infinite;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = Task.Run(() =>
        {
            entered.SetResult();
            return stream.Read();
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => read.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private sealed class SilentLogger : ICameraBridgeLogger
    {
        public void LogInfo(string message) { }
        public void LogError(string message) { }
    }
}
