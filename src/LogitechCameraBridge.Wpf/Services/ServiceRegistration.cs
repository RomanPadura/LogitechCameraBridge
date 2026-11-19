using System.IO;
using LogitechCameraBridge.Cameras;
using LogitechCameraBridge.Cameras.Interfaces;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Connector.Interfaces;
using LogitechCameraBridge.Connector.Managers;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Logger;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Wpf.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LogitechCameraBridge.Wpf.Services;

public static class ServiceRegistration
{
    public static IServiceCollection AddCameraBridge(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LoggerOptions>(configuration.GetRequiredSection(LoggerOptions.SectionName));
        services.Configure<BridgeControllerDeviceOptions>(configuration.GetRequiredSection(BridgeControllerDeviceOptions.SectionName));
        // Populated from the camera selected in the tray; no USB camera configuration is needed.
        services.AddOptions<CameraToControlOptions>();
        services.AddSingleton<CameraBridgeLogger>();
        services.AddSingleton<UiCameraBridgeLogger>();
        services.AddSingleton<ICameraBridgeLogger>(provider => provider.GetRequiredService<UiCameraBridgeLogger>());
        if (configuration.GetValue<bool>("UseControllerStub"))
        {
            services.AddSingleton<ControllerDiscoveryStub>();
            services.AddSingleton<IControllerDiscovery>(provider => provider.GetRequiredService<ControllerDiscoveryStub>());
            services.AddSingleton<KeyboardSimulator>();
        }
        else
            services.AddSingleton<IControllerDiscovery, ControllerDiscovery>();
        services.AddSingleton<ICameraDiscovery, CameraDiscovery>();
        services.AddSingleton<ICameraBridgeManager, CameraBridgeManager>();
        services.AddSingleton<BridgeActivity>();
        services.AddSingleton<BridgeSession>();
        services.AddSingleton<IBridgeSession, MonitoredBridgeSession>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ICameraSelectionStore>(_ => new JsonCameraSelectionStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogitechCameraBridgeData", "userSettings.json")));
        services.AddSingleton<CameraMenuViewModel>();
        services.AddSingleton<StatusViewModel>();
        return services;
    }
}
