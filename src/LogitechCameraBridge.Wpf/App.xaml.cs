using LogitechCameraBridge.Wpf.Models;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using LogitechCameraBridge.Logger;
using LogitechCameraBridge.Wpf.Services;
using LogitechCameraBridge.Wpf.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace LogitechCameraBridge.Wpf;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _updateCancellation = new();
    private bool _isUpdating;

    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ =>
            {
                try { new WindowsStartup().SetEnabled(false); }
                catch (Exception ex) { Trace.TraceError(ex.ToString()); }
            }).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private TrayIcon? _trayIcon;
    private IConfigurationRoot? _configuration;
    private ServiceProvider? _services;
    private CameraMenuViewModel? _cameraMenu;
    private CameraBridgeLogger? _logger;
    private bool _isExiting;
    private SimulatorHotkeys? _hotkeys;
    private KeyboardSimulator? _simulator;
    private StatusViewModel? _status;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A replacement instance must not initialize devices before the previous process exits.
        try { await AppRestart.WaitForParentAsync(e.Args); }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
            Shutdown(1);
            return;
        }

        // No Window is created: the application lives only in the notification area.
        _trayIcon = new TrayIcon(new AboutViewModel());
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.RestartRequested += OnRestartRequested;
        _trayIcon.UpdateRequested += OnUpdateRequested;

        try
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appSettings.json", optional: false, reloadOnChange: false)
                .Build();
            _services = new ServiceCollection()
                .AddCameraBridge(_configuration)
                .BuildServiceProvider();
            _logger = _services.GetRequiredService<CameraBridgeLogger>();
            _cameraMenu = _services.GetRequiredService<CameraMenuViewModel>();
            _trayIcon.ConfigureCameras(_cameraMenu);
            _status = _services.GetRequiredService<StatusViewModel>();
            _trayIcon.ConfigureStatus(_status);
            _cameraMenu.PropertyChanged += OnCameraMenuChanged;
            if (_configuration.GetValue<bool>("UseControllerStub"))
            {
                _simulator = _services.GetRequiredService<KeyboardSimulator>();
                _hotkeys = new SimulatorHotkeys();
                _hotkeys.Pressed += OnSimulatorKey;
            }
            _logger.LogInfo("Tray application started. Initializing bridge once.");
            await _status.InitializeAsync();
        }
        catch (Exception ex)
        {
            _status?.Fail(ex);
            // Keep Exit available even when configuration cannot be loaded.
            Trace.TraceError(ex.ToString());
            _logger?.LogError(ex.ToString());
            _trayIcon.ShowStartupFailure();
            _trayIcon.ShowError(ex.Message);
        }
    }

    private async void OnSimulatorKey(SimulatorButton button)
    {
        if (_isExiting || _simulator is null || _status?.Active != true)
            return;
        try
        {
            await _simulator.PressAsync(button);
        }
        catch (Exception ex)
        {
            _status?.Fail(ex);
            _logger?.LogError(ex.ToString());
            if (!_isExiting)
                _trayIcon?.ShowError(ex.Message);
        }
    }

    private void OnCameraMenuChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraMenuViewModel.Error) && !string.IsNullOrEmpty(_cameraMenu?.Error))
            _status?.Fail(new InvalidOperationException(_cameraMenu.Error));
    }

    private async void OnExitRequested(object? sender, EventArgs e) => await ShutdownApplicationAsync(restart: false);

    private async void OnRestartRequested(object? sender, EventArgs e) => await ShutdownApplicationAsync(restart: true);

    private async void OnUpdateRequested(object? sender, EventArgs e)
    {
        if (_isExiting || _isUpdating)
            return;
        _isUpdating = true;
        _trayIcon?.SetUpdating(true);
        try
        {
            var applyUpdate = await new AppUpdater().DownloadUpdateAsync(_updateCancellation.Token);
            if (_isExiting)
                return;
            if (applyUpdate is null)
                _trayIcon?.ShowUpdateMessage("You are running the latest version.");
            else
                await ShutdownApplicationAsync(restart: false, applyUpdate: applyUpdate);
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            // Authentication/network exceptions can contain request details; never log credentials.
            if (!_isExiting)
                _trayIcon?.ShowError("Update failed. Check your connection, release assets and repository access. " +
                    "For a private repository, configure LOGITECH_CAMERA_BRIDGE_GITHUB_TOKEN. " +
                    "Updates require installation using the release Setup.exe.");
        }
        finally
        {
            _isUpdating = false;
            if (!_isExiting)
                _trayIcon?.SetUpdating(false);
        }
    }

    private async Task ShutdownApplicationAsync(bool restart, Action? applyUpdate = null)
    {
        if (_isExiting)
            return;

        _isExiting = true;
        _updateCancellation.Cancel();
        _hotkeys?.Dispose();
        _hotkeys = null;
        _trayIcon?.DisableExit();
        int exitCode = 0;
        try
        {
            if (_status is not null)
                await _status.ShutdownAsync();
            if (_cameraMenu is not null)
                await _cameraMenu.ShutdownAsync();
            if (_logger is not null)
            {
                _logger.LogInfo(restart ? "Tray application restarting." : "Tray application stopped.");
                await _logger.FlushAsync();
            }
            // Cleanup and settings writes have completed. The child waits for this process to exit.
            if (restart)
                AppRestart.StartReplacement();
            applyUpdate?.Invoke();
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
            if (_logger is not null)
            {
                _logger.LogError(ex.ToString());
                await _logger.FlushAsync();
            }
            exitCode = 1;
        }
        finally
        {
            Shutdown(exitCode);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_cameraMenu is not null)
            _cameraMenu.PropertyChanged -= OnCameraMenuChanged;
        _hotkeys?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.ExitRequested -= OnExitRequested;
            _trayIcon.RestartRequested -= OnRestartRequested;
            _trayIcon.UpdateRequested -= OnUpdateRequested;
            _trayIcon.Dispose();
        }
        _services?.Dispose();
        (_configuration as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
