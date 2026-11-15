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

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
}