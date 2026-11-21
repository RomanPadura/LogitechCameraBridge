using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.Tests;

public class WindowsStartupTests
{
    [Fact]
    public void ExistingUpdaterRegistrationMigratesOnceToFriendlyLauncher()
    {
        const string legacy = "\"C:\\Apps\\LogitechCameraBridge\\Update.exe\" start";
        string friendly = WindowsStartup.CreateCommand(@"C:\Apps\LogitechCameraBridge\Logitech Camera Bridge.exe");
        string? value = legacy;
        int writes = 0;
        var startup = new WindowsStartup(() => value, text => { value = text; writes++; }, () => friendly, () => legacy);
        startup.MigrateExistingRegistration();
        Assert.Equal(friendly, value);
        Assert.True(startup.IsEnabled);
        startup.MigrateExistingRegistration();
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"C:\\OtherInstallation\\Update.exe\" start")]
    public void MigrationDoesNotEnableStartupOrOverwriteUnrecognizedRegistration(string? existing)
    {
        string? value = existing;
        int writes = 0;
        var startup = new WindowsStartup(() => value, text => { value = text; writes++; },
            () => "\"C:\\Apps\\Logitech Camera Bridge.exe\"", () => "\"C:\\Apps\\Update.exe\" start");
        startup.MigrateExistingRegistration();
        Assert.Equal(existing, value);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void MigrationKeepsWorkingUpdaterRegistrationWhenFriendlyLauncherIsMissing()
    {
        const string legacy = "\"C:\\Apps\\Update.exe\" start";
        string? value = legacy;
        var startup = new WindowsStartup(() => value, text => value = text, () => null, () => legacy);
        startup.MigrateExistingRegistration();
        Assert.Equal(legacy, value);
    }

    [Fact]
    public void RegistrationUsesQuotedFriendlyLauncherAndPersistsAcrossInstances()
    {
        string? value = null;
        string command = WindowsStartup.CreateCommand(@"C:\Users\Test User\AppData\Local\LogitechCameraBridge\Logitech Camera Bridge.exe");
        Assert.Equal("\"C:\\Users\\Test User\\AppData\\Local\\LogitechCameraBridge\\Logitech Camera Bridge.exe\"", command);
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
