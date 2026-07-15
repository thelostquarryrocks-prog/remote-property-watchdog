using Microsoft.Extensions.Options;

namespace RemotePropertyWatchdog.Agent.Service;

public sealed partial class AgentWorker(
    IAgentLifecycleCoordinator lifecycleCoordinator,
    IOptions<AgentLifecycleOptions> options,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AgentLifecycleState started = await lifecycleCoordinator.StartAsync(stoppingToken);
        LogAgentHostStarted(logger, started.SessionId, started.StartedAtUtc, started.PreviousSessionId, started.PreviousSessionEndedCleanly);

        AgentLifecycleState ready = await lifecycleCoordinator.MarkReadyAsync(stoppingToken);
        LogAgentHostReady(logger, ready.SessionId, ready.ReadyAtUtc!.Value, options.Value.ReadinessTimeout.TotalSeconds);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (lifecycleCoordinator.CurrentState is not null)
        {
            AgentLifecycleState stopped = await lifecycleCoordinator.StopAsync(cancellationToken);
            LogAgentHostStopped(logger, stopped.SessionId, stopped.StoppedAtUtc!.Value);
        }

        await base.StopAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host started. SessionId={SessionId} StartedAtUtc={StartedAtUtc} PreviousSessionId={PreviousSessionId} PreviousSessionEndedCleanly={PreviousSessionEndedCleanly}")]
    private static partial void LogAgentHostStarted(ILogger logger, Guid sessionId, DateTimeOffset startedAtUtc, Guid? previousSessionId, bool previousSessionEndedCleanly);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host monitoring-ready. SessionId={SessionId} ReadyAtUtc={ReadyAtUtc} ReadinessTimeoutSeconds={ReadinessTimeoutSeconds}")]
    private static partial void LogAgentHostReady(ILogger logger, Guid sessionId, DateTimeOffset readyAtUtc, double readinessTimeoutSeconds);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host planned shutdown recorded. SessionId={SessionId} StoppedAtUtc={StoppedAtUtc}")]
    private static partial void LogAgentHostStopped(ILogger logger, Guid sessionId, DateTimeOffset stoppedAtUtc);
}
