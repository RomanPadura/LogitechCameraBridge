using LogitechCameraBridgeConsole.Managers;
using LogitechCameraBridge.Common.Options;
using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Cameras;
using LogitechCameraBridge.Logger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LogitechCameraBridge.Logger.Interfaces;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Cameras.Interfaces;

Console.WriteLine("Starting app...");

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
services.AddSingleton<ICameraBridgeLogger, ConsoleLogger>();
services.AddSingleton<IControllerDiscovery, ControllerDiscovery>();
services.AddSingleton<ICameraDiscovery, CameraDiscovery>();
services.AddSingleton<CameraBridgeManager>();
Console.WriteLine("Services are build successfully...");
Console.WriteLine();

using var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += OnCancelKeyPress;

using var serviceProvider = services.BuildServiceProvider();

try
{
    var cameraManager = serviceProvider.GetRequiredService<CameraBridgeManager>();
    cameraManager.Run(cancellationTokenSource.Token);
}
catch (OperationCanceledException)
    when (cancellationTokenSource.IsCancellationRequested)
{
    Console.WriteLine("Operation cancelled.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
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
