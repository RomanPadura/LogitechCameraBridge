using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using LogitechCameraBridge.Logger.Interfaces;

namespace LogitechCameraBridge.Controllers.Interop;

[SupportedOSPlatform("windows")]
internal static class WindowsHidCapabilities
{
    private const int HidpStatusSuccess = 0x00110000;

    public static uint GetFullUsage(string devicePath, ICameraBridgeLogger logger)
    {
        logger.LogInfo(
            $"HIDP_CAPS structure size: {Marshal.SizeOf<HidpCaps>()} bytes.");

        // Query metadata without requesting read/write access to the collection.
        using var handle = CreateFileW(devicePath, 0, FileShare.ReadWrite,
            IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open HID collection for capability queries.");

        if (!HidD_GetPreparsedData(handle, out var preparsedData))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot obtain HID preparsed data.");

        try
        {
            int status = HidP_GetCaps(preparsedData, out var capabilities);
            if (status != HidpStatusSuccess)
                throw new IOException($"HidP_GetCaps failed with NTSTATUS 0x{status:X8}.");

            return ((uint)capabilities.UsagePage << 16) | capabilities.Usage;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, FileShare shareMode,
        IntPtr securityAttributes, FileMode creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", ExactSpelling = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
}
