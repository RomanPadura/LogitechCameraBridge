using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LogitechCameraBridge.Connector.Models;
using LogitechCameraBridge.Wpf.Models;
using LogitechCameraBridge.Wpf.ViewModels;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly Dictionary<TrayIconState, SafeIconHandle> _icons = [];
    private TrayIconState _iconState = TrayIconState.Initializing;
    private readonly ContextMenu _menu = new();
    private readonly MenuItem _exitItem = Item("Exit");
    private readonly MenuItem _restartItem = Item("Restart");
    private readonly MenuItem _camerasItem = Item("Cameras");
    private readonly MenuItem _statusItem = Item("Status");
    private readonly MenuItem _controlsItem = Item("Camera controls");
    private readonly MenuItem _aboutItem = Item("About");
    private readonly MenuItem _updateItem = Item("Check for updates...");
    private readonly MenuItem _startupItem = Item("Starts with OS: false");
    private readonly MenuItem _activateStartupItem = Item("Activate");
    private readonly MenuItem _deactivateStartupItem = Item("Deactivate");
    private readonly IWindowsStartup _startup;
    private bool _startupEnabled;
    private bool _isShuttingDown;
    private readonly TextBlock _zoomValue = new() { Text = "Zoom: Not checked" };
    private readonly TextBlock _panValue = new() { Text = "Pan: Not checked" };
    private readonly TextBlock _tiltValue = new() { Text = "Tilt: Not checked" };
    private readonly TextBlock _controllerStatus = new() { Text = "Logitech Controller: Checking..." };
    private readonly TextBlock _cameraStatus = new() { Text = "Camera: Checking..." };
    private readonly TextBlock _activeStatus = new() { Text = "Active: false" };
    private readonly INotificationAreaIcon _notificationIcon;
    private CameraMenuViewModel? _cameraMenu;
    private StatusViewModel? _status;
    private bool _isDisposed;

    public event EventHandler? ExitRequested;
    public event EventHandler? RestartRequested;
    public event EventHandler? UpdateRequested;

    public TrayIcon(AboutViewModel about) : this(about, new NotificationAreaIcon()) { }

    internal TrayIcon(AboutViewModel about, INotificationAreaIcon notificationIcon, IWindowsStartup? startup = null)
    {
        _notificationIcon = notificationIcon;
        _startup = startup ?? new WindowsStartup();
        try
        {
            int size = Math.Max(16, GetSystemMetrics(49)); // SM_CXSMICON, in physical pixels.
            foreach (var state in Enum.GetValues<TrayIconState>())
                _icons.Add(state, TrayStatusIcon.Create(state, size));
            _exitItem.Click += OnExitClick;
            _restartItem.Click += OnRestartClick;
            _updateItem.Click += OnUpdateClick;
            _startupItem.Items.Add(_activateStartupItem);
            _startupItem.Items.Add(_deactivateStartupItem);
            _activateStartupItem.Click += OnActivateStartupClick;
            _deactivateStartupItem.Click += OnDeactivateStartupClick;
            foreach (var label in new[] { _controllerStatus, _cameraStatus, _activeStatus })
                _statusItem.Items.Add(Label(label));
            foreach (var label in new[] { _zoomValue, _panValue, _tiltValue })
                _controlsItem.Items.Add(Label(label));
            _camerasItem.Items.Add(Item("Cameras unavailable", enabled: false));
            _aboutItem.Items.Add(Label(new TextBlock { Text = $"Version: {about.Version}" }));
            _aboutItem.Items.Add(_updateItem);
            foreach (var item in new[] { _statusItem, _controlsItem, _camerasItem, _aboutItem, _startupItem, _restartItem, _exitItem })
                _menu.Items.Add(item);
            _menu.Opened += OnMenuOpened;
            _menu.Closed += OnMenuClosed;
            _notificationIcon.MenuRequested += OnMenuRequested;
            SetIconState(_iconState);
            RefreshStartup();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal ContextMenu Menu => _menu;

    // TextBlock avoids WPF AccessText interpreting underscores in device names as mnemonics.
    private static MenuItem Item(string text, bool enabled = true) => new()
    {
        Header = new TextBlock { Text = text },
        IsEnabled = enabled
    };

    private static MenuItem Label(TextBlock text)
    {
        text.Foreground = SystemColors.MenuTextBrush;
        return new MenuItem { Header = text, IsEnabled = false, Focusable = false };
    }

    private void OnMenuRequested(Point? position)
    {
        if (_isDisposed || _isShuttingDown)
            return;
        _notificationIcon.Activate();
        _menu.Placement = position.HasValue ? PlacementMode.AbsolutePoint : PlacementMode.MousePoint;
        _menu.HorizontalOffset = position?.X ?? 0;
        _menu.VerticalOffset = position?.Y ?? 0;
        _menu.IsOpen = true;
        Keyboard.Focus(_menu);
    }

    private void OnMenuClosed(object? sender, RoutedEventArgs e) => _notificationIcon.ReturnFocus();

    private void OnMenuOpened(object? sender, RoutedEventArgs e) => RefreshStartup();

    private void RefreshStartup()
    {
        if (_isDisposed || _isShuttingDown)
            return;
        _startupItem.Visibility = Visibility.Collapsed;
        try
        {
            if (!_startup.CanEnable)
                return;
            _startupItem.Visibility = Visibility.Visible;
            _startupEnabled = _startup.IsEnabled;
            _startupItem.IsEnabled = true;
            _startupItem.ToolTip = "Start when you sign in to Windows.";
            RenderStartup();
        }
        catch (Exception)
        {
            _startupItem.IsEnabled = false;
            ((TextBlock)_startupItem.Header).Text = "Starts with OS: unavailable";
            _startupItem.ToolTip = "Could not read Windows startup settings. Reopen the menu to retry.";
        }
    }

    private void RenderStartup()
    {
        _activateStartupItem.IsEnabled = !_startupEnabled;
        _deactivateStartupItem.IsEnabled = _startupEnabled;
        ((TextBlock)_startupItem.Header).Text = $"Starts with OS: {_startupEnabled.ToString().ToLowerInvariant()}";
    }

    private void OnActivateStartupClick(object sender, RoutedEventArgs e) => SetStartupEnabled(true);

    private void OnDeactivateStartupClick(object sender, RoutedEventArgs e) => SetStartupEnabled(false);

    private void SetStartupEnabled(bool enabled)
    {
        if (_isDisposed || _isShuttingDown)
            return;
        try
        {
            _startup.SetEnabled(enabled);
            RefreshStartup();
        }
        catch (Exception)
        {
            RenderStartup(); // Preserve the previous action states if Windows rejects the write.
            _notificationIcon.ShowMessage("Windows startup", "Could not change Windows startup settings. Please try again.", error: true);
        }
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
        _controllerStatus.Text = $"Logitech Controller: {_status.Controller.Text}";
        _controllerStatus.Foreground = _status.Controller.IsError ? Brushes.Red : SystemColors.MenuTextBrush;
        _cameraStatus.Text = $"Camera: {_status.Camera.Text}";
        _cameraStatus.Foreground = _status.Camera.IsError ? Brushes.Red : SystemColors.MenuTextBrush;
        _activeStatus.Text = _status.Active ? "Active: true" : "Active: false";
        _activeStatus.Foreground = _status.Active ? SystemColors.MenuTextBrush : Brushes.Red;
        SetIconState(_status.TrayState);
        var capabilities = _status.Capabilities;
        RenderControl(_zoomValue, "Zoom", capabilities.Zoom, capabilities.ZoomValue);
        RenderControl(_panValue, "Pan", capabilities.Pan, capabilities.PanValue);
        RenderControl(_tiltValue, "Tilt", capabilities.Tilt, capabilities.TiltValue);
    }

    private static void RenderControl(TextBlock label, string name, CameraControlSupport support, CameraControlValue? value)
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
        label.Foreground = support == CameraControlSupport.Error ? Brushes.Red
            : support == CameraControlSupport.NotSupported || support == CameraControlSupport.NotChecked
                ? SystemColors.GrayTextBrush : SystemColors.MenuTextBrush;
    }

    private void SetIconState(TrayIconState state)
    {
        _iconState = state;
        string text = state switch
        {
            TrayIconState.Healthy => "LogitechCameraBridge - active",
            TrayIconState.CameraNotSelected => "LogitechCameraBridge - camera not selected",
            TrayIconState.Error => "LogitechCameraBridge - error; check logs",
            _ => "LogitechCameraBridge - starting"
        };
        _notificationIcon.Update(_icons[state], text);
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
        _isShuttingDown = true;
        foreach (MenuItem item in _menu.Items)
            item.IsEnabled = false;
        _menu.IsOpen = false;
    }

    private void OnCameraMenuChanged(object? sender, PropertyChangedEventArgs e) => RenderCameras();

    private void RenderCameras()
    {
        if (_isDisposed || _cameraMenu is null)
            return;
        _camerasItem.Items.Clear();
        if (_cameraMenu.IsBusy)
        {
            _camerasItem.Items.Add(Item("Please wait...", enabled: false));
            return;
        }
        if (!string.IsNullOrEmpty(_cameraMenu.Error))
            _camerasItem.Items.Add(Item(_cameraMenu.Error, enabled: false));
        foreach (var group in _cameraMenu.Cameras.GroupBy(camera => camera.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            int index = 0;
            foreach (var camera in group)
            {
                string label = group.Count() > 1 ? $"{camera.Name} ({++index})" : camera.Name;
                var item = Item(label);
                item.IsChecked = string.Equals(camera.DevicePath, _cameraMenu.SelectedCamera?.DevicePath,
                    StringComparison.OrdinalIgnoreCase);
                item.Click += async (_, _) =>
                {
                    if (_isDisposed || !_cameraMenu.SelectCommand.CanExecute(camera))
                        return;
                    _menu.IsOpen = false;
                    await _cameraMenu.SelectCommand.ExecuteAsync(camera);
                    if (!_isDisposed && !string.IsNullOrEmpty(_cameraMenu.Error))
                        _notificationIcon.ShowMessage("Camera selection", _cameraMenu.Error, error: true);
                    else if (!_isDisposed)
                        RestartRequested?.Invoke(this, EventArgs.Empty);
                };
                _camerasItem.Items.Add(item);
            }
        }
        var selected = _cameraMenu.SelectedCamera;
        if (selected is not null && string.IsNullOrEmpty(_cameraMenu.Error) && !_cameraMenu.Cameras.Any(camera => string.Equals(
                camera.DevicePath, selected.DevicePath, StringComparison.OrdinalIgnoreCase)))
        {
            var unavailable = Item($"{selected.Name} (saved; unavailable)", enabled: false);
            unavailable.IsChecked = true;
            _camerasItem.Items.Add(unavailable);
        }
        if (_cameraMenu.Cameras.Count == 0 && string.IsNullOrEmpty(_cameraMenu.Error))
            _camerasItem.Items.Add(Item("No cameras found", enabled: false));
    }

    public void ShowStartupFailure()
    {
        SetIconState(TrayIconState.Error);
        _notificationIcon.Update(_icons[_iconState], "LogitechCameraBridge - startup failed");
        _controllerStatus.Text = "Logitech Controller: Error";
        _cameraStatus.Text = "Camera: Error";
        _activeStatus.Text = "Active: false";
        _controllerStatus.Foreground = _cameraStatus.Foreground = _activeStatus.Foreground = Brushes.Red;
    }

    public void ShowError(string message) => _notificationIcon.ShowMessage("Camera bridge", message, error: true);

    public void SetUpdating(bool updating)
    {
        _updateItem.IsEnabled = !updating;
        ((TextBlock)_updateItem.Header).Text = updating ? "Checking / downloading update..." : "Check for updates...";
    }

    public void ShowUpdateMessage(string message) =>
        _notificationIcon.ShowMessage("Application updates", message, error: false);

    private void OnUpdateClick(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke(this, EventArgs.Empty);
    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private void OnRestartClick(object sender, RoutedEventArgs e) => RestartRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _menu.Opened -= OnMenuOpened;
        _menu.Closed -= OnMenuClosed;
        _menu.IsOpen = false;
        _notificationIcon.MenuRequested -= OnMenuRequested;
        _notificationIcon.Dispose();
        if (_cameraMenu is not null)
            _cameraMenu.PropertyChanged -= OnCameraMenuChanged;
        if (_status is not null)
            _status.PropertyChanged -= OnStatusChanged;
        _exitItem.Click -= OnExitClick;
        _restartItem.Click -= OnRestartClick;
        _updateItem.Click -= OnUpdateClick;
        _activateStartupItem.Click -= OnActivateStartupClick;
        _deactivateStartupItem.Click -= OnDeactivateStartupClick;
        _menu.Items.Clear();
        foreach (var icon in _icons.Values)
            icon.Dispose();
        _icons.Clear();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
