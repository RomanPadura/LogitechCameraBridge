using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LogitechCameraBridge.Wpf.Services;

/// <summary>Owns a hidden top-level HWND and the Shell notification-area registration.</summary>
internal sealed class NotificationAreaIcon : INotificationAreaIcon
{
    internal const int CallbackMessage = 0x8001; // WM_APP + 1
    private readonly HwndSource _source;
    private readonly DispatcherTimer _retry;
    private readonly NotifyCommand _notify;
    private SafeIconHandle? _icon;
    private string _text = string.Empty;
    private bool _added;
    private bool _version4;
    private bool _disposed;

    internal delegate bool NotifyCommand(uint command, ref NotifyIconData data);

    public NotificationAreaIcon() : this(Shell_NotifyIcon) { }

    internal NotificationAreaIcon(NotifyCommand notify)
    {
        _notify = notify;
        TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        if (TaskbarCreatedMessage == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        // A message-only HWND cannot receive the TaskbarCreated broadcast after Explorer restarts.
        _source = new HwndSource(new HwndSourceParameters("LogitechCameraBridge tray")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, without WS_VISIBLE
            ExtendedWindowStyle = 0x80, // WS_EX_TOOLWINDOW: no taskbar/Alt+Tab entry
            Width = 0,
            Height = 0
        });
        _source.AddHook(WindowProcedure);
        _retry = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
            (_, _) => EnsureAdded(), _source.Dispatcher);
        _retry.Stop();
    }

    public event Action<System.Windows.Point?>? MenuRequested;
    internal IntPtr WindowHandle => _source.Handle;
    internal uint TaskbarCreatedMessage { get; }

    public void Update(SafeIconHandle icon, string text)
    {
        if (_disposed)
            return;
        if (_added && ReferenceEquals(_icon, icon) && _text == text)
            return;
        _icon = icon;
        _text = text;
        if (!_added)
        {
            EnsureAdded();
            return;
        }
        var data = CreateData();
        data.Flags = 0x02 | 0x04 | 0x80; // NIF_ICON | NIF_TIP | NIF_SHOWTIP
        if (!_notify(1, ref data))
        {
            _added = false;
            EnsureAdded();
        }
    }

    private void EnsureAdded()
    {
        if (_disposed || _added || _icon is null)
            return;
        var data = CreateData();
        data.Flags = 0x01 | 0x02 | 0x04 | 0x80; // message, icon, tooltip, show tooltip
        _added = _notify(0, ref data);
        if (!_added)
        {
            _retry.Start(); // Explorer can still be starting when its broadcast arrives.
            return;
        }
        _retry.Stop();
        data.VersionOrTimeout = 4;
        _version4 = _notify(4, ref data); // NIM_SETVERSION after every successful add.
    }

    public void ShowMessage(string title, string message, bool error)
    {
        if (_disposed)
            return;
        EnsureAdded();
        if (!_added)
            return;
        var data = CreateData();
        data.Flags = 0x10; // NIF_INFO
        data.InfoTitle = Limit(title, 63);
        data.Info = Limit(message, 255);
        data.InfoFlags = error ? 3u : 1u; // NIIF_ERROR / NIIF_INFO
        _notify(1, ref data);
    }

    public void Activate()
    {
        if (!_disposed)
            SetForegroundWindow(_source.Handle);
    }

    public void ReturnFocus()
    {
        if (_disposed)
            return;
        var data = CreateData();
        _notify(3, ref data); // NIM_SETFOCUS after the menu is dismissed.
        PostMessage(_source.Handle, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed)
            return IntPtr.Zero;
        try
        {
            if ((uint)message == TaskbarCreatedMessage)
            {
                _added = false;
                EnsureAdded();
            }
            else if (message == CallbackMessage)
            {
                int notification = _version4 ? (int)(lParam.ToInt64() & 0xFFFF) : lParam.ToInt32();
                // Version 4 sends WM_CONTEXTMENU for right click and NIN_KEYSELECT for keyboard activation.
                if ((_version4 && notification is 0x007B or 0x0401) || (!_version4 && notification == 0x0205))
                    MenuRequested?.Invoke(GetMenuPosition());
                handled = true;
            }
        }
        catch (Exception ex)
        {
            // Managed exceptions must never cross a native window-procedure boundary.
            Trace.TraceError(ex.ToString());
        }
        return IntPtr.Zero;
    }

    private NotifyIconData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _source.Handle,
        Id = 1,
        Callback = CallbackMessage,
        Icon = _icon?.DangerousGetHandle() ?? IntPtr.Zero,
        Tip = Limit(_text, 127),
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private System.Windows.Point? GetMenuPosition()
    {
        var identifier = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = _source.Handle, Id = 1
        };
        if (Shell_NotifyIconGetRect(ref identifier, out var rectangle) < 0)
            return null; // Fall back to the mouse when the icon is not currently on-screen.
        // Anchor to the icon instead of the cursor so keyboard activation opens beside the tray.
        var physical = new System.Windows.Point(rectangle.Left, rectangle.Top);
        return _source.CompositionTarget.TransformFromDevice.Transform(physical);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _retry.Stop();
        var data = CreateData();
        _notify(2, ref data); // Delete before destroying the owner HWND or icon handles.
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        _icon = null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint VersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid Guid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect rectangle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint command, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
