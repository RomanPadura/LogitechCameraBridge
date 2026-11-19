using LogitechCameraBridge.Wpf.Models;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using LogitechCameraBridge.Logger;
using LogitechCameraBridge.Wpf.Services;
using LogitechCameraBridge.Wpf.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LogitechCameraBridge.Wpf;

public partial class App : System.Windows.Application
{
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
        _trayIcon = new TrayIcon();
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.RestartRequested += OnRestartRequested;

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

    private async Task ShutdownApplicationAsync(bool restart)
    {
        if (_isExiting)
            return;

        _isExiting = true;
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
            _trayIcon.Dispose();
        }
        _services?.Dispose();
        (_configuration as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
