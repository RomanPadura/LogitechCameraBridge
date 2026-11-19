using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LogitechCameraBridge.Wpf.Services;

namespace LogitechCameraBridge.Wpf.Tests;

public class TrayStatusIconTests
{
    [Fact]
    public Task EmbeddedCameraRendersValidTransparentIconsAtTraySizes() => MainViewModelTests.OnDispatcherAsync(() =>
    {
        var states = new[] { TrayIconState.Healthy, TrayIconState.Error, TrayIconState.CameraNotSelected, TrayIconState.Initializing };
        var labels = new[] { "Active", "Error", "Not selected", "Starting" };
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 512, 156));
            for (int index = 0; index < states.Length; index++)
            {
                foreach (int size in new[] { 16, 32 })
                {
                    using var icon = TrayStatusIcon.Create(states[index], size);
                    Assert.Equal(size, icon.Width);
                    Assert.NotEqual(IntPtr.Zero, icon.Handle);
                    var bitmap = TrayStatusIcon.Render(states[index], size);
                    byte[] pixels = new byte[size * size * 4];
                    bitmap.CopyPixels(pixels, size * 4, 0);
                    Assert.Equal(0, pixels[3]); // Transparent outside the ring.
                    drawing.DrawImage(bitmap, new Rect(index * 128 + (size == 16 ? 36 : 60), 112, size, size));
                }
                drawing.DrawImage(TrayStatusIcon.Render(states[index], 64), new Rect(index * 128 + 32, 12, 64, 64));
                var label = new FormattedText(labels[index], CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.Black, 1);
                drawing.DrawText(label, new Point(index * 128 + (128 - label.Width) / 2, 82));
            }
        }
        var preview = new RenderTargetBitmap(512, 156, 96, 96, PixelFormats.Pbgra32);
        preview.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(preview));
        string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TestResults"));
        Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, "tray-icons-preview.png"));
        encoder.Save(file);
        return Task.CompletedTask;
    });
}
