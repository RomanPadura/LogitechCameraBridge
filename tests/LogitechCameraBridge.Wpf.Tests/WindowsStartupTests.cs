using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.Tests;

public class WindowsStartupTests
{
    [Fact]
    public void RegistrationUsesQuotedStableUpdaterAndPersistsAcrossInstances()
    {
        string? value = null;
        string command = WindowsStartup.CreateCommand(@"C:\Users\Test User\AppData\Local\LogitechCameraBridge\Update.exe");
        Assert.Equal("\"C:\\Users\\Test User\\AppData\\Local\\LogitechCameraBridge\\Update.exe\" start", command);
        var startup = new WindowsStartup(() => value, text => value = text, () => command);
        Assert.False(startup.IsEnabled);
        Assert.True(startup.CanEnable);
        Assert.Null(value); // Reading settings never opts the user in.
        startup.SetEnabled(true);
        Assert.Equal(command, value);
        var restarted = new WindowsStartup(() => value, text => value = text, () => command);
        Assert.True(restarted.IsEnabled);
        restarted.SetEnabled(false);
        Assert.Null(value);
    }

    [Fact]
    public void UninstallCleanupDoesNotRequireAnInstalledLocatorAndIsIdempotent()
    {
        string? value = "old registration";
        var startup = new WindowsStartup(() => value, text => value = text,
            () => throw new InvalidOperationException("Locator unavailable during cleanup"));
        startup.SetEnabled(false);
        startup.SetEnabled(false);
        Assert.Null(value);
    }

    [Fact]
    public void MissingReleaseLayoutCannotBeRegistered()
    {
        string? value = null;
        var startup = new WindowsStartup(() => value, text => value = text, () => null);
        Assert.False(startup.CanEnable);
        Assert.Throws<InvalidOperationException>(() => startup.SetEnabled(true));
        Assert.Null(value);
    }

    [Fact]
    public void FailedWritePreservesPreviousRegistration()
    {
        string? value = null;
        var startup = new WindowsStartup(() => value, _ => throw new UnauthorizedAccessException(), () => "valid command");
        Assert.Throws<UnauthorizedAccessException>(() => startup.SetEnabled(true));
        Assert.False(startup.IsEnabled);
    }

    [Theory]
    [InlineData("relative/Update.exe")]
    [InlineData("C:\\Bad\"Path\\Update.exe")]
    public void InvalidLaunchPathsAreRejected(string path) =>
        Assert.Throws<ArgumentException>(() => WindowsStartup.CreateCommand(path));
}
