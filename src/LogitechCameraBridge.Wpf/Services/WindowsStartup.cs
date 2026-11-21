using System.IO;
using Microsoft.Win32;
using Velopack.Locators;

namespace LogitechCameraBridge.Wpf.Services;

internal sealed class WindowsStartup : IWindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LogitechCameraBridge";
    private readonly Func<string?> _read;
    private readonly Action<string?> _write;
    private readonly Func<string?> _command;

    public WindowsStartup() : this(ReadRegistration, WriteRegistration, GetInstalledCommand) { }

    internal WindowsStartup(Func<string?> read, Action<string?> write, Func<string?> command)
    {
        _read = read;
        _write = write;
        _command = command;
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

    internal static string CreateCommand(string updaterPath)
    {
        if (!Path.IsPathFullyQualified(updaterPath) || updaterPath.Contains('"') || updaterPath.Contains('\0'))
            throw new ArgumentException("The updater path must be an absolute Windows executable path.");
        // Update.exe lives outside current/, so this keeps launching the latest installed version.
        string command = $"\"{updaterPath}\" start";
        if (command.Length > 260)
            throw new InvalidOperationException("The installation path is too long for Windows startup registration.");
        return command;
    }

    private static string? GetInstalledCommand()
    {
        var locator = VelopackLocator.Current;
        return !locator.IsPortable && locator.CurrentlyInstalledVersion is not null && !string.IsNullOrWhiteSpace(locator.UpdateExePath)
            && File.Exists(locator.UpdateExePath) ? CreateCommand(locator.UpdateExePath) : null;
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
