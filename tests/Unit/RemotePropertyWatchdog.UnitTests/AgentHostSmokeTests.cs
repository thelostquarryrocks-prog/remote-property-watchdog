using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RemotePropertyWatchdog.Agent.Service;

namespace RemotePropertyWatchdog.UnitTests;

public sealed class AgentHostSmokeTests
{
    [Fact]
    public void AgentHostBuilderRegistersHostedServiceAndLifecycleServices()
    {
        using IHost host = Program.CreateHostBuilder([]).Build();

        IEnumerable<IHostedService> hostedServices = host.Services.GetRequiredService<IEnumerable<IHostedService>>();

        Assert.Contains(hostedServices, service => service.GetType() == typeof(AgentWorker));
        Assert.IsType<AgentLifecycleCoordinator>(host.Services.GetRequiredService<IAgentLifecycleCoordinator>());
        Assert.IsType<FileAgentLifecycleStore>(host.Services.GetRequiredService<IAgentLifecycleStore>());
    }

    [Fact]
    public async Task AgentHostStartsAndStopsWithCleanLifecycleMarker()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        using IHost host = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:00"
        ]).Build();

        await host.StartAsync(CancellationToken.None);
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentLifecycleState? started = null;
        for (int i = 0; i < 20; i++)
        {
            started = coordinator.CurrentState;
            if (started?.IsReady == true)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        await host.StopAsync(CancellationToken.None);

        AgentLifecycleState? stopped = coordinator.CurrentState;

        Assert.NotNull(started);
        Assert.True(started.IsReady);
        Assert.NotNull(stopped);
        Assert.True(stopped.PlannedShutdown);
        Assert.True(File.Exists(markerPath));
    }
}
