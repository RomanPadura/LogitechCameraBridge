using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Cameras.Models;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using LogitechCameraBridge.Wpf.ViewModels;

namespace LogitechCameraBridge.Wpf.Tests;

public class TrayIconTests
{
    [Fact]
    public Task AboutSubmenuFitsLongVersionTextAtDifferentFontSizes() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        using var tray = new TrayIcon(new AboutViewModel(), new FakeNotificationIcon());
        foreach (double fontSize in new[] { 12d, 18d, 24d })
        {
            tray.Menu.FontSize = fontSize;
            tray.Menu.IsOpen = true;
            var about = (MenuItem)tray.Menu.Items[3];
            about.IsSubmenuOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var version = (MenuItem)about.Items[0];
            var text = (TextBlock)version.Header;
            Assert.True(text.IsVisible);
            Assert.True(text.ActualWidth >= text.DesiredSize.Width - 1);
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(text));
            var popup = Assert.IsAssignableFrom<UIElement>(source.RootVisual);
            Rect bounds = text.TransformToAncestor(popup).TransformBounds(new Rect(text.RenderSize));
            Assert.True(bounds.Right <= popup.RenderSize.Width + 1);
            Assert.True(bounds.Left >= 0);
            if (fontSize == 18)
            {
                var preview = new RenderTargetBitmap((int)Math.Ceiling(popup.RenderSize.Width),
                    (int)Math.Ceiling(popup.RenderSize.Height), 96, 96, PixelFormats.Pbgra32);
                preview.Render(popup);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(preview));
                string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TestResults"));
                Directory.CreateDirectory(output);
                using var file = File.Create(Path.Combine(output, "wpf-about-menu-preview.png"));
                encoder.Save(file);
            }
            about.IsSubmenuOpen = false;
            tray.Menu.IsOpen = false;
        }
    });

    [Fact]
    public Task WpfMenuPreservesActionsNotificationsAndCleansUpIcons() => MainViewModelTests.OnDispatcherAsync(() =>
    {
        var native = new FakeNotificationIcon();
        var tray = new TrayIcon(new AboutViewModel(), native);
        Assert.Equal(new[] { "Status", "Camera controls", "Cameras", "About", "Restart", "Exit" },
            tray.Menu.Items.Cast<MenuItem>().Select(Text));
        var about = (MenuItem)tray.Menu.Items[3];
        var version = (MenuItem)about.Items[0];
        Assert.Equal($"Version: {new AboutViewModel().Version}", Text(version));
        Assert.False(version.IsEnabled);
        int updates = 0, restarts = 0, exits = 0;
        tray.UpdateRequested += (_, _) => updates++;
        tray.RestartRequested += (_, _) => restarts++;
        tray.ExitRequested += (_, _) => exits++;
        var update = (MenuItem)about.Items[1];
        update.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        ((MenuItem)tray.Menu.Items[4]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        ((MenuItem)tray.Menu.Items[5]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal((1, 1, 1), (updates, restarts, exits));
        tray.SetUpdating(true);
        Assert.False(update.IsEnabled);
        tray.SetUpdating(false);
        Assert.True(update.IsEnabled);
        Assert.Equal("Check for updates...", Text(update));
        tray.ShowError("Error details");
        Assert.Equal(("Camera bridge", "Error details", true), native.LastMessage);
        tray.ShowUpdateMessage("Current");
        Assert.Equal(("Application updates", "Current", false), native.LastMessage);
        tray.ShowStartupFailure();
        Assert.Equal("LogitechCameraBridge - startup failed", native.Tooltip);
        tray.DisableExit();
        Assert.All(tray.Menu.Items.Cast<MenuItem>(), item => Assert.False(item.IsEnabled));
        var lastIcon = native.Icon!;
        tray.Dispose();
        tray.Dispose();
        Assert.Equal(1, native.Disposals);
        Assert.True(lastIcon.IsClosed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CameraMenuPreservesLiteralNamesSelectionAndRestart() => MainViewModelTests.OnDispatcherAsync(async () =>
    {
        var camera = new CameraDeviceInfo("Camera_A & B", "device-1");
        var store = new SelectionStore();
        var model = new CameraMenuViewModel(new Discovery(camera), store, new SilentLogger());
        var native = new FakeNotificationIcon();
        using var tray = new TrayIcon(new AboutViewModel(), native);
        tray.ConfigureCameras(model);
        await model.RefreshCommand.ExecuteAsync(null);
        var cameras = (MenuItem)tray.Menu.Items[2];
        var cameraItem = Assert.IsType<MenuItem>(Assert.Single(cameras.Items.Cast<object>()));
        Assert.Equal(camera.Name, Text(cameraItem));
        int restarts = 0;
        tray.RestartRequested += (_, _) => restarts++;
        cameraItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(camera, store.Saved);
        Assert.Equal(1, restarts);
        Assert.True(((MenuItem)cameras.Items[0]).IsChecked);
        await model.ShutdownAsync();
    });

    private static string Text(MenuItem item) => ((TextBlock)item.Header).Text;

    private sealed class FakeNotificationIcon : INotificationAreaIcon
    {
        public event Action<Point?>? MenuRequested { add { } remove { } }
        public SafeIconHandle? Icon { get; private set; }
        public string? Tooltip { get; private set; }
        public (string, string, bool) LastMessage { get; private set; }
        public int Disposals { get; private set; }
        public void Update(SafeIconHandle icon, string text) { Icon = icon; Tooltip = text; }
        public void ShowMessage(string title, string message, bool error) => LastMessage = (title, message, error);
        public void Activate() { }
        public void ReturnFocus() { }
        public void Dispose() => Disposals++;
    }

    private sealed class Discovery(CameraDeviceInfo camera) : ICameraDiscovery
    {
        public IReadOnlyList<CameraDeviceInfo> FindCameras() => [camera];
        public object CreateCameraFilter(int vid, int pid, string usbInterface) => throw new NotSupportedException();
    }

    private sealed class SelectionStore : ICameraSelectionStore
    {
        public CameraDeviceInfo? Saved { get; private set; }
        public Task<CameraDeviceInfo?> LoadAsync() => Task.FromResult(Saved);
        public Task SaveAsync(CameraDeviceInfo camera) { Saved = camera; return Task.CompletedTask; }
    }

    private sealed class SilentLogger : ICameraBridgeLogger
    {
        public void LogInfo(string message) { }
        public void LogError(string message) { }
    }
}
