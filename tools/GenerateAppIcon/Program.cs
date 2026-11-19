using System.IO;
using System.Windows.Media.Imaging;
using LogitechCameraBridge.Wpf.Models;
using LogitechCameraBridge.Wpf.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Pass the output .ico path.");

        int[] sizes = [16, 20, 24, 32, 48, 64, 128, 256];
        var images = sizes.Select(size =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(TrayStatusIcon.Render(TrayIconState.Healthy, size)));
            using var png = new MemoryStream();
            encoder.Save(png);
            return png.ToArray();
        }).ToArray();

        string path = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0); // ICONDIR reserved
        output.Write((ushort)1); // Icon, not cursor
        output.Write((ushort)sizes.Length);
        uint offset = (uint)(6 + 16 * sizes.Length);
        for (int index = 0; index < sizes.Length; index++)
        {
            byte dimension = sizes[index] == 256 ? (byte)0 : (byte)sizes[index];
            output.Write(dimension);
            output.Write(dimension);
            output.Write((byte)0); // True color
            output.Write((byte)0);
            output.Write((ushort)1); // Planes
            output.Write((ushort)32); // Bits per pixel
            output.Write((uint)images[index].Length);
            output.Write(offset);
            offset += (uint)images[index].Length;
        }
        foreach (byte[] image in images)
            output.Write(image);
        Console.WriteLine($"Created {path} ({sizes.Length} sizes, green camera icon).");
    }
}
