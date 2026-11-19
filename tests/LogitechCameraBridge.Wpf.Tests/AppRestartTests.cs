using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.Tests;

public class AppRestartTests
{
    [Fact]
    public void ApphostRestartPreservesArgumentsAndReplacesOldParentHandshake()
    {
        var start = AppRestart.CreateStartInfo(@"C:\Program Files\Camera Bridge\Bridge.exe", "",
            ["--example", "value with spaces", "--restart-parent", "12"], 42);
        Assert.Equal(@"C:\Program Files\Camera Bridge\Bridge.exe", start.FileName);
        Assert.Equal(["--example", "value with spaces", "--restart-parent", "42"], start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(42, AppRestart.GetParentProcessId(start.ArgumentList.ToArray()));
    }

    [Fact]
    public void DotnetHostedRestartIncludesApplicationDll()
    {
        var start = AppRestart.CreateStartInfo(@"C:\Program Files\dotnet\dotnet.exe",
            @"C:\Camera Bridge\Bridge.dll", [], 42);
        Assert.Equal([@"C:\Camera Bridge\Bridge.dll", "--restart-parent", "42"], start.ArgumentList);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    public void InvalidParentIsRejected(string parent)
    {
        Assert.Throws<ArgumentException>(() => AppRestart.GetParentProcessId(["--restart-parent", parent]));
    }

    [Fact]
    public async Task NormalLaunchDoesNotWaitForAnotherProcess()
    {
        Assert.Null(AppRestart.GetParentProcessId([]));
        await AppRestart.WaitForParentAsync([]);
        Assert.Throws<ArgumentException>(() => AppRestart.GetParentProcessId(["--restart-parent"]));
        Assert.Throws<ArgumentException>(() => AppRestart.GetParentProcessId(
            ["--restart-parent", Environment.ProcessId.ToString()]));
    }
}
