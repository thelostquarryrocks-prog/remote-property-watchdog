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
            .UseWindowsService(options => options.ServiceName = AgentServiceIdentity.ServiceName)
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<AgentLifecycleOptions>()
                    .Bind(context.Configuration.GetSection(AgentLifecycleOptions.SectionName))
                    .Validate(options => options.ReadinessTimeout > TimeSpan.Zero, "Readiness timeout must be positive.")
                    .Validate(options => options.ReadinessTimeout <= TimeSpan.FromSeconds(60), "Readiness timeout must not exceed 60 seconds.")
                    .Validate(options => options.CleanShutdownWriteTimeout > TimeSpan.Zero, "Clean shutdown write timeout must be positive.")
                    .ValidateOnStart();

                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<IAgentLifecycleStore, FileAgentLifecycleStore>();
                services.AddSingleton<IAgentLifecycleCoordinator, AgentLifecycleCoordinator>();
                services.AddHostedService<AgentWorker>();
            });
}
