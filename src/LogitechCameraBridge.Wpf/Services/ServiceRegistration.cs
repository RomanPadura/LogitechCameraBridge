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
        services.Configure<CameraToControlOptions>(configuration.GetRequiredSection(CameraToControlOptions.SectionName));
        services.AddSingleton<CameraBridgeLogger>();
        services.AddSingleton<UiCameraBridgeLogger>();
        services.AddSingleton<ICameraBridgeLogger>(provider => provider.GetRequiredService<UiCameraBridgeLogger>());
        services.AddSingleton<IControllerDiscovery, ControllerDiscovery>();
        services.AddSingleton<ICameraDiscovery, CameraDiscovery>();
        services.AddSingleton<ICameraBridgeManager, CameraBridgeManager>();
        services.AddSingleton<IBridgeSession, BridgeSession>();
        services.AddSingleton<MainViewModel>();
        return services;
    }
}
