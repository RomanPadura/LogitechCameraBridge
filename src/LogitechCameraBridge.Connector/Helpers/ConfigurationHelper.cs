using System.Globalization;
using LogitechCameraBridge.Common.Options;

namespace LogitechCameraBridge.Connector.Helpers;

internal static class ConfigurationHelper
{
    public static void ValidateButtonCodes(params int[] codes)
    {
        if (codes.Distinct().Count() != codes.Length)
            throw new InvalidDataException("All button codes must be different.");

        if (codes.Any(code => code is 0x00 or 0xFF))
            throw new InvalidDataException("Button codes 0x00 and 0xFF are reserved.");
    }

    public static void ValidateUsbInterface(string usbInterface)
    {
        if (usbInterface.Length != 5 ||
            !usbInterface.StartsWith(
                "MI_",
                StringComparison.OrdinalIgnoreCase) ||
            !byte.TryParse(
                usbInterface.AsSpan(3),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out _))
        {
            throw new InvalidDataException(
                $"{CameraToControlOptions.SectionName}.UsbInterface " +
                "must be a USB interface such as MI_00.");
        }
    }

    public static int ReadHexValue(
        string? value,
        string name,
        int maxValue = ushort.MaxValue,
        string sectionName = BridgeControllerDeviceOptions.SectionName)
    {
        var hex = value.AsSpan().Trim();

        if (hex.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        if (!ushort.TryParse(
                hex,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ushort id)
            || id > maxValue)
        {
            throw new InvalidDataException(
                $"{sectionName}.{name} must be a hexadecimal " +
                $"value between 0x00 and 0x{maxValue:X}.");
        }

        return id;
    }
}
