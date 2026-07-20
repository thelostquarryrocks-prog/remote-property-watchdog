using Microsoft.Extensions.Options;

namespace RemotePropertyWatchdog.Agent.Service;

public sealed partial class AgentWorker(
    IAgentLifecycleCoordinator lifecycleCoordinator,
    IAgentJournal agentJournal,
    IOptions<AgentLifecycleOptions> options,
    ILogger<AgentWorker> logger) : BackgroundService
{
    private int executionFaulted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            AgentLifecycleState started = await lifecycleCoordinator.StartAsync(stoppingToken);
            LogAgentHostStarted(logger, started.SessionId, started.StartedAtUtc, started.PreviousSessionId, started.PreviousSessionEndedCleanly);

            AgentJournalHealth journalHealth = await agentJournal.InitializeAsync(stoppingToken);
            if (journalHealth.State != AgentJournalHealthState.Healthy)
            {
                LogAgentJournalDegraded(logger, journalHealth.ReasonCode, journalHealth.SchemaVersion, journalHealth.IntegrityResult);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }

            AgentLifecycleState ready = await lifecycleCoordinator.MarkReadyAsync(stoppingToken);
            LogAgentHostReady(logger, ready.SessionId, ready.ReadyAtUtc!.Value, ready.ReadinessElapsed!.Value.TotalMilliseconds, options.Value.ReadinessTimeout.TotalSeconds);

            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogAgentHostCancellation(logger, lifecycleCoordinator.CurrentState?.SessionId);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref executionFaulted, 1);
            LogAgentHostExecutionFaulted(logger, lifecycleCoordinator.CurrentState?.SessionId, ex);
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (lifecycleCoordinator.CurrentState is null)
        {
            return;
        }

        if (Volatile.Read(ref executionFaulted) != 0)
        {
            LogAgentHostFaultedSessionLeftUnclean(logger, lifecycleCoordinator.CurrentState.SessionId);
            return;
        }

        try
        {
            using CancellationTokenSource writeTimeout = new(options.Value.CleanShutdownWriteTimeout);
            AgentLifecycleState stopped = await lifecycleCoordinator.StopAsync(writeTimeout.Token);
            LogAgentHostStopped(logger, stopped.SessionId, stopped.StoppedAtUtc!.Value);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogCleanShutdownMarkerFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host started. SessionId={SessionId} StartedAtUtc={StartedAtUtc} PreviousSessionId={PreviousSessionId} PreviousSessionEndedCleanly={PreviousSessionEndedCleanly}")]
    private static partial void LogAgentHostStarted(ILogger logger, Guid sessionId, DateTimeOffset startedAtUtc, Guid? previousSessionId, bool previousSessionEndedCleanly);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host monitoring-ready. SessionId={SessionId} ReadyAtUtc={ReadyAtUtc} ReadinessElapsedMilliseconds={ReadinessElapsedMilliseconds} ReadinessTimeoutSeconds={ReadinessTimeoutSeconds}")]
    private static partial void LogAgentHostReady(ILogger logger, Guid sessionId, DateTimeOffset readyAtUtc, double readinessElapsedMilliseconds, double readinessTimeoutSeconds);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host planned shutdown recorded. SessionId={SessionId} StoppedAtUtc={StoppedAtUtc}")]
    private static partial void LogAgentHostStopped(ILogger logger, Guid sessionId, DateTimeOffset stoppedAtUtc);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Remote Property Watchdog agent host could not record planned shutdown; previous session will remain unclean on next start.")]
    private static partial void LogCleanShutdownMarkerFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Remote Property Watchdog agent host execution faulted. SessionId={SessionId}")]
    private static partial void LogAgentHostExecutionFaulted(ILogger logger, Guid? sessionId, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Remote Property Watchdog agent host session remains unclean because worker execution faulted. SessionId={SessionId}")]
    private static partial void LogAgentHostFaultedSessionLeftUnclean(ILogger logger, Guid sessionId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Remote Property Watchdog agent host execution canceled by host. SessionId={SessionId}")]
    private static partial void LogAgentHostCancellation(ILogger logger, Guid? sessionId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "Agent journal is degraded; monitoring-ready is blocked. ReasonCode={ReasonCode} SchemaVersion={SchemaVersion} IntegrityResult={IntegrityResult}")]
    private static partial void LogAgentJournalDegraded(ILogger logger, string reasonCode, int? schemaVersion, string integrityResult);
}
