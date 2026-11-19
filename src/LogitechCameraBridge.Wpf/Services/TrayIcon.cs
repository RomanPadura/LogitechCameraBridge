using System.Drawing;
using System.Windows.Forms;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly Icon _icon = (Icon)SystemIcons.Application.Clone();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _exitItem = new("Exit");
    private readonly NotifyIcon _notifyIcon;
    private bool _isDisposed;

    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _exitItem.Click += OnExitClick;
        _menu.Items.Add(_exitItem);
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "LogitechCameraBridge",
            ContextMenuStrip = _menu,
            Visible = true
        };
    }

    public void DisableExit() => _exitItem.Enabled = false;

    public void ShowStartupFailure() => _notifyIcon.Text = "LogitechCameraBridge - startup failed";

    private void OnExitClick(object? sender, EventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _exitItem.Click -= OnExitClick;
        _menu.Dispose();
        _icon.Dispose();
    }
}
