using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class SafeIconHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeIconHandle(IntPtr handle, int size) : base(ownsHandle: true)
    {
        SetHandle(handle);
        Size = size;
    }

    public int Size { get; }

    protected override bool ReleaseHandle() => DestroyIcon(handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
