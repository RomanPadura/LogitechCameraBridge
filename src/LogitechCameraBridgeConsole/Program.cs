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
// USB selection is a console-only default. The tray host uses its saved camera path.
services.Configure<CameraToControlOptions>(options =>
{
    options.Vid = "0x046D";
    options.Pid = "0x0843";
    options.UsbInterface = "MI_00";
    options.UsbInterfacePrefix = "MI_";
});
services.AddSingleton<ICameraBridgeLogger, CameraBridgeLogger>();
if (configuration.GetValue<bool>("UseControllerStub"))
    services.AddSingleton<IControllerDiscovery, ControllerDiscoveryStub>();
else
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
