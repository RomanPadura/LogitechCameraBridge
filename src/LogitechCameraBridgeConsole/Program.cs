using LogitechCameraBridgeConsole.Managers;
using LogitechCameraBridgeConsole.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appSettings.json", optional: false, reloadOnChange: false)
    .Build();
using var configurationLifetime = configuration as IDisposable;

var services = new ServiceCollection();
services.Configure<BridgeControllerDeviceOptions>(
    configuration.GetRequiredSection(BridgeControllerDeviceOptions.SectionName));
services.Configure<CameraToControlOptions>(
    configuration.GetRequiredSection(CameraToControlOptions.SectionName));
services.AddSingleton<CameraBridgeManager>();

using var serviceProvider = services.BuildServiceProvider();
serviceProvider.GetRequiredService<CameraBridgeManager>().InitializeDevices();
