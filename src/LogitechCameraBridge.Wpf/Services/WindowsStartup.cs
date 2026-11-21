using System.IO;
using Microsoft.Win32;
using Velopack.Locators;

namespace LogitechCameraBridge.Wpf.Services;

internal sealed class WindowsStartup : IWindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LogitechCameraBridge";
    internal const string LauncherFileName = "Logitech Camera Bridge.exe"; // Velopack --packTitle execution stub.
    private readonly Func<string?> _read;
    private readonly Action<string?> _write;
    private readonly Func<string?> _command;
    private readonly Func<string?> _legacyCommand;

    public WindowsStartup() : this(ReadRegistration, WriteRegistration, GetInstalledCommand, GetLegacyInstalledCommand) { }

    internal WindowsStartup(Func<string?> read, Action<string?> write, Func<string?> command,
        Func<string?>? legacyCommand = null)
    {
        _read = read;
        _write = write;
        _command = command;
        _legacyCommand = legacyCommand ?? (() => null);
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_read());
    public bool CanEnable => _command() is not null;

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            _write(null);
            return;
        }
        string command = _command() ?? throw new InvalidOperationException("Install the release build before enabling Windows startup.");
        _write(command);
    }

    public void MigrateExistingRegistration()
    {
        string? existing = _read();
        if (string.IsNullOrWhiteSpace(existing))
            return; // Never opt a user in during migration.
        string? legacy = _legacyCommand();
        if (legacy is not null && string.Equals(existing, legacy, StringComparison.OrdinalIgnoreCase)
            && _command() is { } command)
            _write(command); // Keep the Run value name, preserving Windows' separate disabled override.
    }

    internal static string CreateCommand(string launcherPath)
    {
        if (!Path.IsPathFullyQualified(launcherPath) || launcherPath.Contains('"') || launcherPath.Contains('\0'))
            throw new ArgumentException("The launcher path must be an absolute Windows executable path.");
        // The named execution stub lives outside current/ and launches the latest installed version.
        string command = $"\"{launcherPath}\"";
        if (command.Length > 260)
            throw new InvalidOperationException("The installation path is too long for Windows startup registration.");
        return command;
    }

    private static string? GetInstalledCommand()
    {
        var locator = VelopackLocator.Current;
        if (locator.IsPortable || locator.CurrentlyInstalledVersion is null || string.IsNullOrWhiteSpace(locator.RootAppDir))
            return null;
        string launcher = Path.Combine(locator.RootAppDir, LauncherFileName);
        return File.Exists(launcher) ? CreateCommand(launcher) : null;
    }

    private static string? GetLegacyInstalledCommand()
    {
        var locator = VelopackLocator.Current;
        return !locator.IsPortable && locator.CurrentlyInstalledVersion is not null
            && !string.IsNullOrWhiteSpace(locator.UpdateExePath) ? $"\"{locator.UpdateExePath}\" start" : null;
    }

    private static string? ReadRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    private static void WriteRegistration(string? command)
    {
        if (command is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }
}
