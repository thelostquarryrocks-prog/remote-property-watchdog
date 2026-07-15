namespace RemotePropertyWatchdog.Agent.Service;

public sealed record AgentLifecycleMarker(
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    long? ReadinessElapsedMilliseconds,
    DateTimeOffset? StoppedAtUtc,
    bool PlannedShutdown,
    Guid? PreviousSessionId,
    bool PreviousSessionEndedCleanly);
