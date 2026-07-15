namespace RemotePropertyWatchdog.Agent.Service;

public sealed record AgentLifecycleMarker(
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? StoppedAtUtc,
    bool PlannedShutdown);
