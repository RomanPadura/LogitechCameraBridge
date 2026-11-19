using LogitechCameraBridge.Wpf.Models;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Media = System.Windows.Media;

namespace LogitechCameraBridge.Wpf.Services;

/// <summary>Renders the licensed camera glyph inside a high-contrast status ring.</summary>
public static class TrayStatusIcon
{
    private static readonly Media.Geometry Camera = LoadCamera();

    public static Icon Create(TrayIconState state, int size)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(state, size)));
        using var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;
        using var bitmap = new Bitmap(png);
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    public static BitmapSource Render(TrayIconState state, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        string color = state switch
        {
            TrayIconState.Healthy => "#16A34A",
            TrayIconState.Error => "#DC2626",
            TrayIconState.CameraNotSelected => "#F59E0B",
            _ => "#64748B"
        };
        var ring = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(color));
        var visual = new Media.DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new Media.ScaleTransform(size / 32.0, size / 32.0));
            drawing.DrawEllipse(Media.Brushes.WhiteSmoke, new Media.Pen(ring, 3),
                new System.Windows.Point(16, 16), 14, 14);
            drawing.PushTransform(new Media.TranslateTransform(4.6, 4.6));
            drawing.PushTransform(new Media.ScaleTransform(0.95, 0.95));
            drawing.DrawGeometry(Media.Brushes.Black, null, Camera);
            drawing.Pop();
            drawing.Pop();
            drawing.Pop();
        }
        var rendered = new RenderTargetBitmap(size, size, 96, 96, Media.PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static Media.Geometry LoadCamera()
    {
        using var source = typeof(TrayStatusIcon).Assembly.GetManifestResourceStream(
            "LogitechCameraBridge.Wpf.Assets.FluentCamera.svg")
            ?? throw new InvalidOperationException("Embedded camera icon is missing.");
        var svg = XDocument.Load(source);
        var path = svg.Descendants().Single(element => element.Name.LocalName == "path");
        var geometry = Media.PathGeometry.CreateFromGeometry(Media.Geometry.Parse(path.Attribute("d")!.Value));
        geometry.FillRule = Media.FillRule.Nonzero;
        geometry.Freeze();
        return geometry;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
