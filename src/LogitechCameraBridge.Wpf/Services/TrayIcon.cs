using LogitechCameraBridge.Wpf.Models;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using LogitechCameraBridge.Wpf.ViewModels;
using LogitechCameraBridge.Connector.Models;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly Dictionary<TrayIconState, Icon> _icons = Enum.GetValues<TrayIconState>()
        .ToDictionary(state => state, state => TrayStatusIcon.Create(state, SystemInformation.SmallIconSize.Width));
    private TrayIconState _iconState = TrayIconState.Initializing;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _exitItem = new("Exit");
    private readonly ToolStripMenuItem _restartItem = new("Restart");
    private readonly ToolStripMenuItem _camerasItem = new("Cameras");
    private readonly ToolStripMenuItem _statusItem = new("Status");
    private readonly ToolStripMenuItem _controlsItem = new("Camera controls");
    private readonly ToolStripMenuItem _aboutItem = new("About");
    private readonly ToolStripMenuItem _updateItem = new("Check for updates...");
    private readonly TrayMenuLabel _zoomValue = new("Zoom: Not checked");
    private readonly TrayMenuLabel _panValue = new("Pan: Not checked");
    private readonly TrayMenuLabel _tiltValue = new("Tilt: Not checked");
    private readonly TrayMenuLabel _controllerStatus = new("Logitech Controller: Checking...");
    private readonly TrayMenuLabel _cameraStatus = new("Camera: Checking...");
    private readonly TrayMenuLabel _activeStatus = new("Active: false");
    private readonly NotifyIcon _notifyIcon;
    private CameraMenuViewModel? _cameraMenu;
    private StatusViewModel? _status;
    private bool _isDisposed;

    public event EventHandler? ExitRequested;
    public event EventHandler? RestartRequested;
    public event EventHandler? UpdateRequested;

    public TrayIcon(AboutViewModel about)
    {
        _exitItem.Click += OnExitClick;
        _restartItem.Click += OnRestartClick;
        _statusItem.DropDownItems.AddRange([_controllerStatus, _cameraStatus, _activeStatus]);
        _menu.Items.Add(_statusItem);
        _controlsItem.DropDownItems.AddRange([_zoomValue, _panValue, _tiltValue]);
        _menu.Items.Add(_controlsItem);
        _camerasItem.DropDownItems.Add(new ToolStripMenuItem("Cameras unavailable") { Enabled = false });
        _menu.Items.Add(_camerasItem);
        _aboutItem.DropDownItems.Add(new TrayMenuLabel($"Version: {about.Version}"));
        _updateItem.Click += OnUpdateClick;
        _aboutItem.DropDownItems.Add(_updateItem);
        _menu.Items.Add(_aboutItem);
        _menu.Items.Add(_restartItem);
        _menu.Items.Add(_exitItem);
        _notifyIcon = new NotifyIcon
        {
            Icon = _icons[_iconState],
            Text = "LogitechCameraBridge - starting",
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
        _controllerStatus.Text = $"Logitech Controller: {_status.Controller.Text.Replace("&", "&&")}";
        _controllerStatus.ForeColor = _status.Controller.IsError ? Color.Red : SystemColors.MenuText;
        _cameraStatus.Text = $"Camera: {_status.Camera.Text.Replace("&", "&&")}";
        _cameraStatus.ForeColor = _status.Camera.IsError ? Color.Red : SystemColors.MenuText;
        _activeStatus.Text = _status.Active ? "Active: true" : "Active: false";
        _activeStatus.ForeColor = _status.Active ? SystemColors.MenuText : Color.Red;
        SetIconState(_status.TrayState);
        var capabilities = _status.Capabilities;
        RenderControl(_zoomValue, "Zoom", capabilities.Zoom, capabilities.ZoomValue);
        RenderControl(_panValue, "Pan", capabilities.Pan, capabilities.PanValue);
        RenderControl(_tiltValue, "Tilt", capabilities.Tilt, capabilities.TiltValue);
    }

    private static void RenderControl(TrayMenuLabel label, string name, CameraControlSupport support, CameraControlValue? value)
    {
        string text = support switch
        {
            CameraControlSupport.Supported when value is not null => $"{value.Current} (min: {value.Min}, max: {value.Max})",
            CameraControlSupport.Supported => "Supported",
            CameraControlSupport.NotSupported => "Not supported",
            CameraControlSupport.Error => "Error",
            _ => "Not checked"
        };
        label.Text = $"{name}: {text}";
        label.ForeColor = support == CameraControlSupport.Error ? Color.Red
            : support == CameraControlSupport.NotSupported || support == CameraControlSupport.NotChecked
                ? SystemColors.GrayText : SystemColors.MenuText;
    }

    private void SetIconState(TrayIconState state)
    {
        if (_iconState != state)
        {
            _iconState = state;
            _notifyIcon.Icon = _icons[state];
        }
        _notifyIcon.Text = state switch
        {
            TrayIconState.Healthy => "LogitechCameraBridge - active",
            TrayIconState.CameraNotSelected => "LogitechCameraBridge - camera not selected",
            TrayIconState.Error => "LogitechCameraBridge - error; check logs",
            _ => "LogitechCameraBridge - starting"
        };
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
        _restartItem.Enabled = false;
        _camerasItem.Enabled = false;
        _statusItem.Enabled = false;
        _controlsItem.Enabled = false;
        _aboutItem.Enabled = false;
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
                    if (_isDisposed || !_cameraMenu.SelectCommand.CanExecute(camera))
                        return;
                    await _cameraMenu.SelectCommand.ExecuteAsync(camera);
                    // The menu closes on click; make a failed save visible immediately.
                    if (!_isDisposed && !string.IsNullOrEmpty(_cameraMenu.Error))
                        _notifyIcon.ShowBalloonTip(5000, "Camera selection", _cameraMenu.Error, ToolTipIcon.Error);
                    else if (!_isDisposed)
                        RestartRequested?.Invoke(this, EventArgs.Empty);
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
        SetIconState(TrayIconState.Error);
        _notifyIcon.Text = "LogitechCameraBridge - startup failed";
        _controllerStatus.Text = "Logitech Controller: Error";
        _cameraStatus.Text = "Camera: Error";
        _activeStatus.Text = "Active: false";
        _controllerStatus.ForeColor = _cameraStatus.ForeColor = _activeStatus.ForeColor = Color.Red;
    }

    public void ShowError(string message) => _notifyIcon.ShowBalloonTip(5000, "Camera bridge", message, ToolTipIcon.Error);

    public void SetUpdating(bool updating)
    {
        _updateItem.Enabled = !updating;
        _updateItem.Text = updating ? "Checking / downloading update..." : "Check for updates...";
    }

    public void ShowUpdateMessage(string message) =>
        _notifyIcon.ShowBalloonTip(5000, "Application updates", message, ToolTipIcon.Info);

    private void OnUpdateClick(object? sender, EventArgs e) => UpdateRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClick(object? sender, EventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private void OnRestartClick(object? sender, EventArgs e) => RestartRequested?.Invoke(this, EventArgs.Empty);

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
        _restartItem.Click -= OnRestartClick;
        _updateItem.Click -= OnUpdateClick;
        _menu.Dispose();
        foreach (var icon in _icons.Values)
            icon.Dispose();
    }
}
