using LogitechCameraBridge.Wpf.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LogitechCameraBridge.Wpf.Services;

/// <summary>Recognizes simulator chords globally; camera work is dispatched outside the hook.</summary>
public sealed class SimulatorHotkeys : IDisposable
{
    private readonly HookProcedure _procedure;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SimulatorKeyChords _chords = new();
    private IntPtr _hook;
    private bool _disposed;
    public event Action<SimulatorButton>? Pressed;

    public SimulatorHotkeys()
    {
        _procedure = OnKeyboard;
        _hook = SetWindowsHookEx(13, _procedure, GetModuleHandle(null), 0); // WH_KEYBOARD_LL.
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot install simulator keyboard hook.");
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !_disposed)
        {
            try
            {
                var input = Marshal.PtrToStructure<KeyboardInput>(data);
                // Ignore synthetic input and leave it available to the foreground application.
                if ((input.Flags & 0x10) == 0)
                {
                    int kind = message.ToInt32();
                    if (kind is 0x100 or 0x104 or 0x101 or 0x105)
                    {
                        if (input.Key is 0xBB or 0xBD or 0x6B or 0x6D or >= 0x25 and <= 0x28)
                            _chords.ReleaseInactivePrefixes(
                                (GetAsyncKeyState(0x5A) & 0x8000) != 0,
                                (GetAsyncKeyState(0x50) & 0x8000) != 0);
                        var result = _chords.Process(input.Key, kind is 0x100 or 0x104);
                        if (result.Button is { } button)
                            _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                            {
                                if (!_disposed)
                                    Pressed?.Invoke(button);
                            }));
                        if (result.Suppress)
                            return new IntPtr(1);
                    }
                }
            }
            catch
            {
                // Never let managed exceptions or slow error handling escape a native hook.
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public uint Key;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookType, HookProcedure procedure, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
