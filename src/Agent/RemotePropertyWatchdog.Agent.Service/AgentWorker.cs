namespace RemotePropertyWatchdog.Agent.Service;

public sealed partial class AgentWorker(ILogger<AgentWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogAgentHostStarted(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host started.")]
    private static partial void LogAgentHostStarted(ILogger logger);
}
