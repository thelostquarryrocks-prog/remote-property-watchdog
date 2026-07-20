using RemotePropertyWatchdog.Contracts;

namespace RemotePropertyWatchdog.Agent.Service;

public enum AgentJournalHealthState
{
    NotInitialized,
    Healthy,
    Degraded
}

public sealed record AgentJournalHealth(
    AgentJournalHealthState State,
    int? SchemaVersion,
    string ReasonCode,
    string IntegrityResult)
{
    public static AgentJournalHealth NotInitialized { get; } =
        new(AgentJournalHealthState.NotInitialized, null, "not_initialized", "not_run");
}

public sealed record AgentSessionRecord(
    string SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ReadyAtUtc = null,
    DateTimeOffset? EndedAtUtc = null,
    bool PlannedShutdown = false);

public sealed record ProbeSampleWrite(
    string SampleId,
    string AgentId,
    string SessionId,
    DateTimeOffset ObservedAtUtc,
    string ProbeKind,
    string ProbeState,
    string? TargetId,
    IReadOnlyList<string> Evidence);

public sealed record MaterialEventWrite(
    string EventId,
    string AgentId,
    string PropertyId,
    string SessionId,
    DateTimeOffset ObservedAtUtc,
    MaterialEventType EventType,
    string? TargetId,
    string? TargetLabel,
    string? FromState,
    string? ToState,
    IReadOnlyList<string> Evidence,
    string? GatewayState,
    string? InternetState);

public sealed record MaterialEventCommit(string EventId, string OutboxId, long AgentSequence);

public sealed record PendingOutboxItem(
    string OutboxId,
    string EventId,
    string AgentId,
    long AgentSequence,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc);

public sealed class DuplicateEventIdException(string eventId)
    : InvalidOperationException($"Material event ID '{eventId}' is already journaled.")
{
    public string EventId { get; } = eventId;
}

public sealed class AgentJournalDegradedException(AgentJournalHealth health)
    : InvalidOperationException($"Agent journal is degraded ({health.ReasonCode}).")
{
    public AgentJournalHealth Health { get; } = health;
}

public sealed class AgentSessionConflictException(string reasonCode)
    : InvalidOperationException($"Agent session evidence conflicts with stored history ({reasonCode}).")
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class UnsupportedAgentJournalSchemaException(int actualVersion, int supportedVersion)
    : InvalidOperationException($"Agent journal schema version {actualVersion} is newer than supported version {supportedVersion}.")
{
    public int ActualVersion { get; } = actualVersion;

    public int SupportedVersion { get; } = supportedVersion;
}
