using System.Text.Json.Serialization;

namespace RemotePropertyWatchdog.Contracts;

public sealed record MaterialEventDto(
    [property: JsonRequired, JsonPropertyOrder(0)] string SchemaVersion,
    [property: JsonRequired, JsonPropertyOrder(1)] string MessageId,
    [property: JsonRequired, JsonPropertyOrder(2)] string AgentId,
    [property: JsonRequired, JsonPropertyOrder(3)] string PropertyId,
    [property: JsonRequired, JsonPropertyOrder(4)] long EventSequence,
    [property: JsonRequired, JsonPropertyOrder(5)] string SessionId,
    [property: JsonRequired, JsonPropertyOrder(6)] DateTimeOffset ObservedAt,
    [property: JsonRequired, JsonPropertyOrder(7)] MaterialEventType EventType,
    [property: JsonPropertyOrder(8)] string? TargetId,
    [property: JsonPropertyOrder(9)] string? TargetLabel,
    [property: JsonPropertyOrder(10)] string? FromState,
    [property: JsonPropertyOrder(11)] string? ToState,
    [property: JsonRequired, JsonPropertyOrder(12)] IReadOnlyList<string> Evidence,
    [property: JsonPropertyOrder(13)] string? GatewayState,
    [property: JsonPropertyOrder(14)] string? InternetState) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RequireSchemaVersion(errors, SchemaVersion);
        ContractValidation.RequireIdentifier(errors, "message_id", MessageId);
        ContractValidation.RequireIdentifier(errors, "agent_id", AgentId);
        ContractValidation.RequireIdentifier(errors, "property_id", PropertyId);
        ContractValidation.RequirePositive(errors, "event_sequence", EventSequence);
        ContractValidation.RequireIdentifier(errors, "session_id", SessionId);
        ContractValidation.RequireUtc(errors, "observed_at", ObservedAt);
        ContractValidation.RejectUndefinedEnum(errors, "event_type", EventType);
        if (TargetId?.Length > 128) errors.Add(new("target_id", "invalid_length", "Target ID must be no more than 128 characters."));
        if (TargetLabel?.Length > 160) errors.Add(new("target_label", "invalid_length", "Target label must be no more than 160 characters."));
        ValidateOptional(errors, "from_state", FromState, 64);
        ValidateOptional(errors, "to_state", ToState, 64);
        ValidateOptional(errors, "gateway_state", GatewayState, 32);
        ValidateOptional(errors, "internet_state", InternetState, 32);
        if (ContractValidation.RequireNotNull(errors, "evidence", Evidence))
        {
            if (Evidence.Count > 16) errors.Add(new("evidence", "too_many_items", "Evidence is limited to 16 bounded codes."));
            if (Evidence.Distinct(StringComparer.Ordinal).Count() != Evidence.Count) errors.Add(new("evidence", "duplicate_items", "Evidence codes must be unique."));
            for (int i = 0; i < Evidence.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(Evidence[i]) || Evidence[i].Length > 96) errors.Add(new($"evidence[{i}]", "invalid_length", "Evidence code must be 1 to 96 characters."));
            }
        }
        return errors;
    }

    private static void ValidateOptional(List<ContractValidationError> errors, string field, string? value, int maxLength)
    {
        if (value?.Length > maxLength) errors.Add(new(field, "invalid_length", $"Value must be no more than {maxLength} characters."));
    }
}

public sealed record EventBatchDto(
    [property: JsonRequired, JsonPropertyOrder(0)] string SchemaVersion,
    [property: JsonRequired, JsonPropertyOrder(1)] string BatchId,
    [property: JsonRequired, JsonPropertyOrder(2)] string AgentId,
    [property: JsonRequired, JsonPropertyOrder(3)] string PropertyId,
    [property: JsonRequired, JsonPropertyOrder(4)] long FirstSequence,
    [property: JsonRequired, JsonPropertyOrder(5)] long LastSequence,
    [property: JsonRequired, JsonPropertyOrder(6)] IReadOnlyList<MaterialEventDto> Events) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RequireSchemaVersion(errors, SchemaVersion);
        ContractValidation.RequireIdentifier(errors, "batch_id", BatchId);
        ContractValidation.RequireIdentifier(errors, "agent_id", AgentId);
        ContractValidation.RequireIdentifier(errors, "property_id", PropertyId);
        ContractValidation.RequirePositive(errors, "first_sequence", FirstSequence);
        ContractValidation.RequirePositive(errors, "last_sequence", LastSequence);
        if (LastSequence < FirstSequence) errors.Add(new("last_sequence", "invalid_range", "Last sequence must be greater than or equal to first sequence."));
        if (ContractValidation.RequireNotNull(errors, "events", Events))
        {
            if (Events.Count is < 1 or > 500) errors.Add(new("events", "invalid_count", "Event batches must contain 1 to 500 events."));
            long expectedSequence = FirstSequence;
            for (int i = 0; i < Events.Count; i++)
            {
                MaterialEventDto evt = Events[i];
                errors.AddRange(evt.Validate());
                if (!StringComparer.Ordinal.Equals(evt.AgentId, AgentId)) errors.Add(new($"events[{i}].agent_id", "batch_mismatch", "Event agent_id must match batch agent_id."));
                if (!StringComparer.Ordinal.Equals(evt.PropertyId, PropertyId)) errors.Add(new($"events[{i}].property_id", "batch_mismatch", "Event property_id must match batch property_id."));
                if (evt.EventSequence != expectedSequence) errors.Add(new($"events[{i}].event_sequence", "non_contiguous_sequence", "Event sequences must be ordered and contiguous from first_sequence through last_sequence."));
                expectedSequence++;
            }

            long expectedLastSequence = FirstSequence + Events.Count - 1;
            if (Events.Count > 0 && LastSequence != expectedLastSequence) errors.Add(new("last_sequence", "batch_mismatch", "Last sequence must equal first_sequence plus the contiguous event count minus one."));
        }
        return errors;
    }
}
