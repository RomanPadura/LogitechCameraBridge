using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using LogitechCameraBridge.Wpf.ViewModels;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly Icon _icon = (Icon)SystemIcons.Application.Clone();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _exitItem = new("Exit");
    private readonly ToolStripMenuItem _camerasItem = new("Cameras");
    private readonly ToolStripMenuItem _statusItem = new("Status");
    private readonly ToolStripLabel _controllerStatus = new("Controller: checking...");
    private readonly ToolStripLabel _cameraStatus = new("Camera: checking...");
    private readonly ToolStripLabel _activeStatus = new("Active: false");
    private readonly NotifyIcon _notifyIcon;
    private CameraMenuViewModel? _cameraMenu;
    private StatusViewModel? _status;
    private bool _isDisposed;

    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _exitItem.Click += OnExitClick;
        _statusItem.DropDownItems.AddRange([_controllerStatus, _cameraStatus, _activeStatus]);
        _menu.Items.Add(_statusItem);
        _camerasItem.DropDownItems.Add(new ToolStripMenuItem("Cameras unavailable") { Enabled = false });
        _menu.Items.Add(_camerasItem);
        _menu.Items.Add(_exitItem);
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "LogitechCameraBridge",
            ContextMenuStrip = _menu,
            Visible = true
        };
    }

    public void ConfigureStatus(StatusViewModel status)
    {
        if (_status is not null)
            _status.PropertyChanged -= OnStatusChanged;
        _status = status;
        _status.PropertyChanged += OnStatusChanged;
        RenderStatus();
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e) => RenderStatus();

    private void RenderStatus()
    {
        if (_isDisposed || _status is null)
            return;
        _controllerStatus.Text = $"Controller: {_status.Controller.Text}";
        _controllerStatus.ForeColor = _status.Controller.IsError ? Color.Red : SystemColors.MenuText;
        _cameraStatus.Text = $"Camera: {_status.Camera.Text}";
        _cameraStatus.ForeColor = _status.Camera.IsError ? Color.Red : SystemColors.MenuText;
        _activeStatus.Text = _status.Active ? "Active: true" : "Active: false";
        _activeStatus.ForeColor = _status.Active ? SystemColors.MenuText : Color.Red;
    }

    public void ConfigureCameras(CameraMenuViewModel cameraMenu)
    {
        if (_cameraMenu is not null)
            _cameraMenu.PropertyChanged -= OnCameraMenuChanged;
        _cameraMenu = cameraMenu;
        _cameraMenu.PropertyChanged += OnCameraMenuChanged;
        RenderCameras();
    }

    public void DisableExit()
    {
        _exitItem.Enabled = false;
        _camerasItem.Enabled = false;
        _statusItem.Enabled = false;
    }

    private void OnCameraMenuChanged(object? sender, PropertyChangedEventArgs e) => RenderCameras();

    private void RenderCameras()
    {
        if (_isDisposed || _cameraMenu is null)
            return;

        foreach (ToolStripItem item in _camerasItem.DropDownItems.Cast<ToolStripItem>().ToArray())
        {
            _camerasItem.DropDownItems.Remove(item);
            item.Dispose();
        }

        if (_cameraMenu.IsBusy)
        {
            _camerasItem.DropDownItems.Add(new ToolStripMenuItem("Please wait...") { Enabled = false });
            return;
        }

        if (!string.IsNullOrEmpty(_cameraMenu.Error))
            _camerasItem.DropDownItems.Add(new ToolStripMenuItem(_cameraMenu.Error) { Enabled = false });

        foreach (var group in _cameraMenu.Cameras.GroupBy(camera => camera.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            int index = 0;
            foreach (var camera in group)
            {
                string label = group.Count() > 1 ? $"{camera.Name} ({++index})" : camera.Name;
                var item = new ToolStripMenuItem(label)
                {
                    // Device names may contain '&'; display them literally instead of as access keys.
                    Text = label.Replace("&", "&&"),
                    Checked = string.Equals(camera.DevicePath, _cameraMenu.SelectedCamera?.DevicePath,
                        StringComparison.OrdinalIgnoreCase)
                };
                item.Click += async (_, _) =>
                {
                    await _cameraMenu.SelectCommand.ExecuteAsync(camera);
                    // The menu closes on click; make a failed save visible immediately.
                    if (!_isDisposed && !string.IsNullOrEmpty(_cameraMenu.Error))
                        _notifyIcon.ShowBalloonTip(5000, "Camera selection", _cameraMenu.Error, ToolTipIcon.Error);
                    else if (!_isDisposed)
                        _notifyIcon.ShowBalloonTip(5000, "Camera selection",
                            "Camera saved. Restart the application to use this selection.", ToolTipIcon.Info);
                };
                _camerasItem.DropDownItems.Add(item);
            }
        }

        var selected = _cameraMenu.SelectedCamera;
        if (selected is not null && string.IsNullOrEmpty(_cameraMenu.Error) && !_cameraMenu.Cameras.Any(camera => string.Equals(
                camera.DevicePath, selected.DevicePath, StringComparison.OrdinalIgnoreCase)))
        {
            _camerasItem.DropDownItems.Add(new ToolStripMenuItem(
                $"{selected.Name.Replace("&", "&&")} (saved; unavailable)") { Enabled = false, Checked = true });
        }
        if (_cameraMenu.Cameras.Count == 0 && string.IsNullOrEmpty(_cameraMenu.Error))
            _camerasItem.DropDownItems.Add(new ToolStripMenuItem("No cameras found") { Enabled = false });
    }

    public void ShowStartupFailure()
    {
        _notifyIcon.Text = "LogitechCameraBridge - startup failed";
        _controllerStatus.Text = "Controller: Error...check logs";
        _cameraStatus.Text = "Camera: Error...check logs";
        _activeStatus.Text = "Active: false";
        _controllerStatus.ForeColor = _cameraStatus.ForeColor = _activeStatus.ForeColor = Color.Red;
    }

    public void ShowError(string message) => _notifyIcon.ShowBalloonTip(5000, "Camera bridge", message, ToolTipIcon.Error);

    private void OnExitClick(object? sender, EventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_cameraMenu is not null)
            _cameraMenu.PropertyChanged -= OnCameraMenuChanged;
        if (_status is not null)
            _status.PropertyChanged -= OnStatusChanged;
        _exitItem.Click -= OnExitClick;
        _menu.Dispose();
        _icon.Dispose();
    }
}
