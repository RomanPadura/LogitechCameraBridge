using LogitechCameraBridge.Controllers;
using LogitechCameraBridge.Controllers.Interfaces;
using LogitechCameraBridge.Wpf.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LogitechCameraBridge.Wpf.Tests;

public class ServiceRegistrationTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void ControllerRegistrationUsesConfiguredImplementation(string? setting, bool expectStub)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Logger:LogToConsole"] = "false",
            ["Logger:MaxTextFileLines"] = "0",
            ["BridgeControllerDevice:Vid"] = "0x046D",
            ["CameraToControl:Vid"] = "0x046D"
        };
        if (setting is not null)
            settings["UseControllerStub"] = setting;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var configurationLifetime = configuration as IDisposable;
        using var services = new ServiceCollection().AddCameraBridge(configuration).BuildServiceProvider();
        var discovery = Assert.Single(services.GetServices<IControllerDiscovery>());
        if (expectStub)
            Assert.IsType<ControllerDiscoveryStub>(discovery);
        else
            Assert.IsType<ControllerDiscovery>(discovery);
        Assert.Same(discovery, services.GetRequiredService<IControllerDiscovery>());
    }
}
