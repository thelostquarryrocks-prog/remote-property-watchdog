namespace RemotePropertyWatchdog.Agent.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        using IHost host = CreateHostBuilder(args).Build();
        await host.RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options => options.ServiceName = "Remote Property Watchdog Agent")
            .ConfigureServices(services =>
            {
                services.AddHostedService<AgentWorker>();
            });
}
