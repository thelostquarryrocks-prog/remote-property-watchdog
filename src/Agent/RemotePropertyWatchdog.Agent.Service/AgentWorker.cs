using System.Data.Common;
using Microsoft.Extensions.Options;

namespace RemotePropertyWatchdog.Agent.Service;

public sealed partial class AgentWorker(
    IAgentLifecycleCoordinator lifecycleCoordinator,
    IAgentJournal agentJournal,
    IAgentReadinessTransitionGate readinessTransitionGate,
    IOptions<AgentLifecycleOptions> options,
    ILogger<AgentWorker> logger) : BackgroundService, IAgentServiceHealth
{
    private int executionFaulted;
    private int shutdownStarted;

    public AgentJournalHealth Health => agentJournal.Health;

    public bool IsReady =>
        Volatile.Read(ref executionFaulted) == 0 &&
        Volatile.Read(ref shutdownStarted) == 0 &&
        lifecycleCoordinator.CurrentState?.IsReady == true &&
        Health.State == AgentJournalHealthState.Healthy;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            AgentLifecycleState started = await lifecycleCoordinator.StartAsync(stoppingToken);
            LogAgentHostStarted(logger, started.SessionId, started.StartedAtUtc, started.PreviousSessionId, started.PreviousSessionEndedCleanly);

            AgentJournalInitializationMode initializationMode;
            if (started.HasJournalOwnership)
            {
                initializationMode = AgentJournalInitializationMode.RequireExistingForPriorLifecycle;
            }
            else
            {
                await lifecycleCoordinator.ClaimJournalOwnershipAsync(stoppingToken);
                initializationMode = AgentJournalInitializationMode.CreateIfMissingForFreshLifecycle;
            }
            AgentJournalHealth journalHealth = await agentJournal.InitializeAsync(initializationMode, stoppingToken);
            if (journalHealth.State != AgentJournalHealthState.Healthy)
            {
                await TransitionToJournalDegradedAsync(journalHealth, stoppingToken);
                await WaitForShutdownAsync(stoppingToken);
                return;
            }

            if (!await TryPersistSessionAsync(ToJournalSession(started), stoppingToken))
            {
                await WaitForShutdownAsync(stoppingToken);
                return;
            }

            AgentLifecycleState ready = await lifecycleCoordinator.PrepareReadyAsync(stoppingToken);
            if (!await TryPersistSessionAsync(ToJournalSession(ready), stoppingToken))
            {
                await WaitForShutdownAsync(stoppingToken);
                return;
            }

            AgentLifecycleState? publishedReady = await readinessTransitionGate.ExecuteAsync(
                AgentReadinessTransitionKind.ReadinessPublication,
                async () =>
                {
                    if (agentJournal.Health.State != AgentJournalHealthState.Healthy)
                    {
                        return null;
                    }

                    return await lifecycleCoordinator.PublishReadyAsync(ready, stoppingToken);
                },
                stoppingToken);
            if (publishedReady is null)
            {
                await TransitionToJournalDegradedAsync(agentJournal.Health, stoppingToken);
                await WaitForShutdownAsync(stoppingToken);
                return;
            }

            ready = publishedReady;
            LogAgentHostReady(logger, ready.SessionId, ready.ReadyAtUtc!.Value, ready.ReadinessElapsed!.Value.TotalMilliseconds, options.Value.ReadinessTimeout.TotalSeconds);

            await WaitForShutdownAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogAgentHostCancellation(logger, lifecycleCoordinator.CurrentState?.SessionId);
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref executionFaulted, 1);
            LogAgentHostExecutionFaulted(logger, lifecycleCoordinator.CurrentState?.SessionId);
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref shutdownStarted, 1);
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

        AgentLifecycleState stopped;
        using CancellationTokenSource writeTimeout = new(options.Value.CleanShutdownWriteTimeout);
        try
        {
            stopped = await lifecycleCoordinator.StopAsync(writeTimeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogCleanShutdownMarkerFailed(logger);
            return;
        }

        if (agentJournal.Health.State == AgentJournalHealthState.Healthy)
        {
            try
            {
                if (!await TryPersistSessionAsync(ToJournalSession(stopped), writeTimeout.Token))
                {
                    LogJournalShutdownPersistenceFailed(logger, agentJournal.Health.ReasonCode);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException or DbException)
            {
                string reasonCode = ex is OperationCanceledException
                    ? "session_persistence_timeout"
                    : "session_persistence_failed";
                LogJournalShutdownPersistenceFailed(logger, reasonCode);
            }
        }

        LogAgentHostStopped(logger, stopped.SessionId, stopped.StoppedAtUtc!.Value);
    }

    private async Task<bool> TryPersistSessionAsync(
        AgentSessionRecord session,
        CancellationToken cancellationToken)
    {
        try
        {
            await agentJournal.RecordSessionAsync(session, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            AgentJournalHealth journalHealth = agentJournal.Health;
            if (journalHealth.State != AgentJournalHealthState.Degraded)
            {
                throw;
            }

            await TransitionToJournalDegradedAsync(journalHealth, cancellationToken);
            return false;
        }
    }

    private Task TransitionToJournalDegradedAsync(
        AgentJournalHealth journalHealth,
        CancellationToken _)
    {
        LogAgentJournalDegraded(
            logger,
            journalHealth.ReasonCode,
            journalHealth.SchemaVersion,
            journalHealth.IntegrityResult);
        return Task.CompletedTask;
    }

    private static Task WaitForShutdownAsync(CancellationToken cancellationToken) =>
        Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

    private static AgentSessionRecord ToJournalSession(AgentLifecycleState state) =>
        new(
            state.SessionId.ToString("D"),
            state.StartedAtUtc,
            state.ReadyAtUtc,
            state.StoppedAtUtc,
            state.PlannedShutdown);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host started. SessionId={SessionId} StartedAtUtc={StartedAtUtc} PreviousSessionId={PreviousSessionId} PreviousSessionEndedCleanly={PreviousSessionEndedCleanly}")]
    private static partial void LogAgentHostStarted(ILogger logger, Guid sessionId, DateTimeOffset startedAtUtc, Guid? previousSessionId, bool previousSessionEndedCleanly);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host monitoring-ready. SessionId={SessionId} ReadyAtUtc={ReadyAtUtc} ReadinessElapsedMilliseconds={ReadinessElapsedMilliseconds} ReadinessTimeoutSeconds={ReadinessTimeoutSeconds}")]
    private static partial void LogAgentHostReady(ILogger logger, Guid sessionId, DateTimeOffset readyAtUtc, double readinessElapsedMilliseconds, double readinessTimeoutSeconds);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Remote Property Watchdog agent host planned shutdown recorded. SessionId={SessionId} StoppedAtUtc={StoppedAtUtc}")]
    private static partial void LogAgentHostStopped(ILogger logger, Guid sessionId, DateTimeOffset stoppedAtUtc);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Remote Property Watchdog agent host could not record planned shutdown; previous session will remain unclean on next start.")]
    private static partial void LogCleanShutdownMarkerFailed(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Remote Property Watchdog agent host execution faulted. SessionId={SessionId}")]
    private static partial void LogAgentHostExecutionFaulted(ILogger logger, Guid? sessionId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Remote Property Watchdog agent host session remains unclean because worker execution faulted. SessionId={SessionId}")]
    private static partial void LogAgentHostFaultedSessionLeftUnclean(ILogger logger, Guid sessionId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Remote Property Watchdog agent host execution canceled by host. SessionId={SessionId}")]
    private static partial void LogAgentHostCancellation(ILogger logger, Guid? sessionId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "Agent journal is degraded; monitoring-ready is blocked. ReasonCode={ReasonCode} SchemaVersion={SchemaVersion} IntegrityResult={IntegrityResult}")]
    private static partial void LogAgentJournalDegraded(ILogger logger, string reasonCode, int? schemaVersion, string integrityResult);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Agent journal could not persist the planned shutdown session milestone. ReasonCode={ReasonCode}")]
    private static partial void LogJournalShutdownPersistenceFailed(ILogger logger, string reasonCode);
}
