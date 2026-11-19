using System.Diagnostics;
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
    private MainViewModel? _viewModel;
    private CameraBridgeLogger? _logger;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // No Window is created: the application lives only in the notification area.
        _trayIcon = new TrayIcon();
        _trayIcon.ExitRequested += OnExitRequested;

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
            _viewModel = _services.GetRequiredService<MainViewModel>();
            _logger.LogInfo("Tray application started. Bridge startup is not enabled in this step.");
        }
        catch (Exception ex)
        {
            // Keep Exit available even when configuration cannot be loaded.
            Trace.TraceError(ex.ToString());
            _logger?.LogError(ex.ToString());
            _trayIcon.ShowStartupFailure();
        }
    }

    private async void OnExitRequested(object? sender, EventArgs e)
    {
        if (_isExiting)
            return;

        _isExiting = true;
        _trayIcon?.DisableExit();
        int exitCode = 0;
        try
        {
            if (_viewModel is not null)
                await _viewModel.ShutdownAsync();
            if (_logger is not null)
            {
                _logger.LogInfo("Tray application stopped.");
                await _logger.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
            exitCode = 1;
        }
        finally
        {
            Shutdown(exitCode);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.ExitRequested -= OnExitRequested;
            _trayIcon.Dispose();
        }
        _services?.Dispose();
        (_configuration as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
