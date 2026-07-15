using System.Text.Json.Serialization;

namespace RemotePropertyWatchdog.Contracts;

public sealed record GatewaySummary([property: JsonRequired] ProbeState State, int? LatencyMs = null) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RejectUndefinedEnum(errors, "gateway.state", State);
        if (LatencyMs is < 0 or > 60000) errors.Add(new("gateway.latency_ms", "out_of_range", "Latency must be between 0 and 60000 ms."));
        return errors;
    }
}

public sealed record InternetSummary([property: JsonRequired] ProbeState State, [property: JsonRequired] ProbeState DnsState, [property: JsonRequired] int HttpsOk) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RejectUndefinedEnum(errors, "internet.state", State);
        ContractValidation.RejectUndefinedEnum(errors, "internet.dns_state", DnsState);
        if (HttpsOk is < 0 or > 10) errors.Add(new("internet.https_ok", "out_of_range", "HTTPS success count must be between 0 and 10."));
        return errors;
    }
}

public sealed record CriticalTargetSummary([property: JsonRequired] int Healthy, [property: JsonRequired] int Degraded, [property: JsonRequired] int Offline, [property: JsonRequired] int Unknown) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RequireNonNegative(errors, "critical.healthy", Healthy);
        ContractValidation.RequireNonNegative(errors, "critical.degraded", Degraded);
        ContractValidation.RequireNonNegative(errors, "critical.offline", Offline);
        ContractValidation.RequireNonNegative(errors, "critical.unknown", Unknown);
        return errors;
    }
}

public sealed record HeartbeatDto(
    [property: JsonRequired, JsonPropertyOrder(0)] string SchemaVersion,
    [property: JsonRequired, JsonPropertyOrder(1)] string HeartbeatId,
    [property: JsonRequired, JsonPropertyOrder(2)] string AgentId,
    [property: JsonRequired, JsonPropertyOrder(3)] string PropertyId,
    [property: JsonRequired, JsonPropertyOrder(4)] long Sequence,
    [property: JsonRequired, JsonPropertyOrder(5)] string SessionId,
    [property: JsonRequired, JsonPropertyOrder(6)] DateTimeOffset SentAt,
    [property: JsonRequired, JsonPropertyOrder(7)] long AgentUptimeSeconds,
    [property: JsonRequired, JsonPropertyOrder(8)] AgentMode Mode,
    [property: JsonRequired, JsonPropertyOrder(9)] GatewaySummary Gateway,
    [property: JsonRequired, JsonPropertyOrder(10)] InternetSummary Internet,
    [property: JsonRequired, JsonPropertyOrder(11)] CriticalTargetSummary Critical,
    [property: JsonRequired, JsonPropertyOrder(12)] long LastEventSequence,
    [property: JsonRequired, JsonPropertyOrder(13)] int QueuedEvents,
    [property: JsonRequired, JsonPropertyOrder(14)] string AgentVersion) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RequireSchemaVersion(errors, SchemaVersion);
        ContractValidation.RequireIdentifier(errors, "heartbeat_id", HeartbeatId);
        ContractValidation.RequireIdentifier(errors, "agent_id", AgentId);
        ContractValidation.RequireIdentifier(errors, "property_id", PropertyId);
        ContractValidation.RequirePositive(errors, "sequence", Sequence);
        ContractValidation.RequireIdentifier(errors, "session_id", SessionId);
        ContractValidation.RequireUtc(errors, "sent_at", SentAt);
        ContractValidation.RequireNonNegative(errors, "agent_uptime_seconds", AgentUptimeSeconds);
        ContractValidation.RejectUndefinedEnum(errors, "mode", Mode);
        if (ContractValidation.RequireNotNull(errors, "gateway", Gateway)) errors.AddRange(Gateway.Validate());
        if (ContractValidation.RequireNotNull(errors, "internet", Internet)) errors.AddRange(Internet.Validate());
        if (ContractValidation.RequireNotNull(errors, "critical", Critical)) errors.AddRange(Critical.Validate());
        ContractValidation.RequireNonNegative(errors, "last_event_sequence", LastEventSequence);
        ContractValidation.RequireNonNegative(errors, "queued_events", QueuedEvents);
        if (string.IsNullOrWhiteSpace(AgentVersion) || AgentVersion.Length > 64) errors.Add(new("agent_version", "invalid_length", "Agent version is required and must be no more than 64 characters."));
        ContractValidation.RequireMaxSerializedSize(errors, "heartbeat", this, ContractValidation.MaximumHeartbeatUtf8Bytes);
        return errors;
    }
}
