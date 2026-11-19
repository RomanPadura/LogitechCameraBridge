using System.Runtime.InteropServices;
using LogitechCameraBridge.Wpf.Models;
using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.Tests;

public class NotificationAreaIconTests
{
    [Fact]
    public void NotificationStructureMatchesWindowsAbi() =>
        Assert.Equal(IntPtr.Size == 8 ? 976 : 956, Marshal.SizeOf<NotificationAreaIcon.NotifyIconData>());

    [Fact]
    public Task ExplorerRestartRestoresLatestIconAndRenegotiatesVersion() => MainViewModelTests.OnDispatcherAsync(() =>
    {
        var shell = new RecordingShell();
        using var first = TrayStatusIcon.Create(TrayIconState.Initializing, 16);
        using var latest = TrayStatusIcon.Create(TrayIconState.Healthy, 16);
        using var tray = new NotificationAreaIcon(shell.Notify);
        Assert.False(IsWindowVisible(tray.WindowHandle));
        tray.Update(first, "Starting");
        tray.Update(latest, "Active");
        SendMessage(tray.WindowHandle, tray.TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero);
        Assert.Equal(new uint[] { 0, 4, 1, 0, 4 }, shell.Calls.Select(call => call.Command));
        var restored = shell.Calls[^2].Data;
        Assert.Equal("Active", restored.Tip);
        Assert.Equal(latest.DangerousGetHandle(), restored.Icon);
        Assert.Equal(4u, shell.Calls[^1].Data.VersionOrTimeout);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedShellRegistrationCanRecoverAndDisposalDeletesOnlyOnce() => MainViewModelTests.OnDispatcherAsync(() =>
    {
        var shell = new RecordingShell { AcceptAdd = false };
        using var icon = TrayStatusIcon.Create(TrayIconState.Initializing, 16);
        var tray = new NotificationAreaIcon(shell.Notify);
        IntPtr window = tray.WindowHandle;
        tray.Update(icon, "Starting");
        Assert.Equal(0u, Assert.Single(shell.Calls).Command);
        shell.AcceptAdd = true;
        SendMessage(window, tray.TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero);
        Assert.Equal(new uint[] { 0, 0, 4 }, shell.Calls.Select(call => call.Command));
        tray.Dispose();
        tray.Dispose();
        Assert.Equal(2u, shell.Calls[^1].Command);
        Assert.Single(shell.Calls, call => call.Command == 2);
        Assert.False(IsWindow(window));
        Assert.False(icon.IsClosed); // The menu owns icon resources, not the shell registration.
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task MouseAndKeyboardCallbacksRespectNegotiatedVersion(bool version4) => MainViewModelTests.OnDispatcherAsync(() =>
    {
        var shell = new RecordingShell { AcceptVersion4 = version4 };
        using var icon = TrayStatusIcon.Create(TrayIconState.Initializing, 16);
        using var tray = new NotificationAreaIcon(shell.Notify);
        tray.Update(icon, "Starting");
        int requested = 0;
        tray.MenuRequested += _ => requested++;
        int packedId = version4 ? 1 << 16 : 0;
        SendMessage(tray.WindowHandle, NotificationAreaIcon.CallbackMessage, (IntPtr)1, (IntPtr)(packedId | 0x007B));
        SendMessage(tray.WindowHandle, NotificationAreaIcon.CallbackMessage, (IntPtr)1, (IntPtr)(packedId | 0x0401));
        SendMessage(tray.WindowHandle, NotificationAreaIcon.CallbackMessage, (IntPtr)1, (IntPtr)(packedId | 0x0205));
        Assert.Equal(version4 ? 2 : 1, requested);
        // A left-click sequence opens once: NIN_SELECT in v4, WM_LBUTTONUP in legacy mode.
        SendMessage(tray.WindowHandle, NotificationAreaIcon.CallbackMessage, (IntPtr)1, (IntPtr)(packedId | 0x0201));
        SendMessage(tray.WindowHandle, NotificationAreaIcon.CallbackMessage, (IntPtr)1, (IntPtr)(packedId | 0x0202));
        SendMessage(tray.WindowHandle, NotificationAreaIcon.CallbackMessage, (IntPtr)1, (IntPtr)(packedId | 0x0400));
        Assert.Equal(version4 ? 3 : 2, requested);
        tray.ReturnFocus();
        Assert.Equal(3u, shell.Calls[^1].Command);
        return Task.CompletedTask;
    });

    [Fact]
    public Task NotificationTextRespectsNativeBufferSizes() => MainViewModelTests.OnDispatcherAsync(() =>
    {
        var shell = new RecordingShell();
        using var icon = TrayStatusIcon.Create(TrayIconState.Error, 16);
        using var tray = new NotificationAreaIcon(shell.Notify);
        tray.Update(icon, new string('T', 200));
        tray.ShowMessage(new string('H', 100), new string('M', 500), error: true);
        var message = shell.Calls[^1].Data;
        Assert.Equal(127, message.Tip.Length);
        Assert.Equal(63, message.InfoTitle.Length);
        Assert.Equal(255, message.Info.Length);
        Assert.Equal(3u, message.InfoFlags);
        Assert.Equal(0x10u, message.Flags);
        return Task.CompletedTask;
    });

    private sealed class RecordingShell
    {
        public bool AcceptAdd { get; set; } = true;
        public bool AcceptVersion4 { get; init; } = true;
        public List<(uint Command, NotificationAreaIcon.NotifyIconData Data)> Calls { get; } = [];
        public bool Notify(uint command, ref NotificationAreaIcon.NotifyIconData data)
        {
            Calls.Add((command, data));
            return command switch { 0 => AcceptAdd, 4 => AcceptVersion4, _ => true };
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
}
