namespace RemotePropertyWatchdog.Agent.Service;

public sealed class AgentWorker(ILogger<AgentWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Remote Property Watchdog agent host started.");
        return Task.CompletedTask;
    }
}
