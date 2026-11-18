using LogitechCameraBridge.Connector.Managers;
using LogitechCameraBridge.Connector.Interfaces;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Cameras;
using LogitechCameraBridge.Logger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Cameras.Interfaces;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appSettings.json", optional: false, reloadOnChange: false)
    .Build();
using var configurationLifetime = configuration as IDisposable;

var services = new ServiceCollection();
services.Configure<LoggerOptions>(
    configuration.GetRequiredSection(LoggerOptions.SectionName));
services.Configure<BridgeControllerDeviceOptions>(
    configuration.GetRequiredSection(BridgeControllerDeviceOptions.SectionName));
services.Configure<CameraToControlOptions>(
    configuration.GetRequiredSection(CameraToControlOptions.SectionName));
services.AddSingleton<ICameraBridgeLogger, CameraBridgeLogger>();
services.AddSingleton<IControllerDiscovery, ControllerDiscovery>();
services.AddSingleton<ICameraDiscovery, CameraDiscovery>();
services.AddSingleton<ICameraBridgeManager, CameraBridgeManager>();

using var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += OnCancelKeyPress;

using var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ICameraBridgeLogger>();
logger.LogInfo("Starting app...");
logger.LogInfo("Services are built successfully...");
logger.LogInfo(string.Empty);

try
{
    var cameraManager = serviceProvider.GetRequiredService<ICameraBridgeManager>();
    cameraManager.Start(cancellationTokenSource.Token);
}
catch (OperationCanceledException)
    when (cancellationTokenSource.IsCancellationRequested)
{
    logger.LogInfo("Operation cancelled.");
}
catch (Exception ex)
{
    logger.LogError(ex.ToString());
    Environment.ExitCode = 1;
}
finally
{
    Console.CancelKeyPress -= OnCancelKeyPress;
}

Console.ReadLine();

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
}
