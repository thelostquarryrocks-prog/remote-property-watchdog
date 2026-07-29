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
                    .Validate(options => options.CleanShutdownWriteTimeout <= TimeSpan.FromSeconds(30), "Clean shutdown write timeout must not exceed 30 seconds.")
                    .ValidateOnStart();

                services.AddSingleton(TimeProvider.System);
                services.AddOptions<AgentJournalOptions>()
                    .Bind(context.Configuration.GetSection(AgentJournalOptions.SectionName))
                    .Validate(options => string.IsNullOrWhiteSpace(options.DatabasePath) || Path.IsPathFullyQualified(options.DatabasePath), "Journal database path must be absolute when configured.")
                    .Validate(options => options.BusyTimeout > TimeSpan.Zero, "Journal busy timeout must be positive.")
                    .Validate(options => options.BusyTimeout <= TimeSpan.FromMinutes(1), "Journal busy timeout must not exceed one minute.")
                    .ValidateOnStart();
                services.AddSingleton<IAgentLifecycleFileSystem, DefaultAgentLifecycleFileSystem>();
                services.AddSingleton<IAgentLifecycleStore, FileAgentLifecycleStore>();
                services.AddSingleton<AgentLifecycleCoordinator>();
                services.AddSingleton<IAgentLifecycleCoordinator>(
                    serviceProvider => serviceProvider.GetRequiredService<AgentLifecycleCoordinator>());
                services.AddSingleton<IAgentJournalDegradationHandler>(
                    serviceProvider => serviceProvider.GetRequiredService<AgentLifecycleCoordinator>());
                services.AddSingleton<IAgentReadinessTransitionGate, AgentReadinessTransitionGate>();
                services.AddSingleton<IAgentJournalFaultInjector>(NoOpAgentJournalFaultInjector.Instance);
                services.AddSingleton<IAgentJournal, SqliteAgentJournal>();
                services.AddSingleton<AgentWorker>();
                services.AddSingleton<IAgentServiceHealth>(
                    serviceProvider => serviceProvider.GetRequiredService<AgentWorker>());
                services.AddHostedService<AgentWorker>(
                    serviceProvider => serviceProvider.GetRequiredService<AgentWorker>());
            });
}
