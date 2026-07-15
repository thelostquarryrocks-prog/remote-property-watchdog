namespace RemotePropertyWatchdog.Agent.Service;

public sealed record AgentLifecycleState(
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? StoppedAtUtc,
    bool PlannedShutdown,
    Guid? PreviousSessionId,
    bool PreviousSessionEndedCleanly)
{
    public bool IsReady => ReadyAtUtc is not null;
}
