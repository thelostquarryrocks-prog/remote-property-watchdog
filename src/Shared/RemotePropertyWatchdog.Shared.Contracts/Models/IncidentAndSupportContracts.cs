using System.Text.Json.Serialization;

namespace RemotePropertyWatchdog.Contracts;

public sealed record IncidentSummaryDto(
    [property: JsonPropertyOrder(0)] string SchemaVersion,
    [property: JsonPropertyOrder(1)] string IncidentId,
    [property: JsonPropertyOrder(2)] string PropertyId,
    [property: JsonPropertyOrder(3)] IncidentClass IncidentClass,
    [property: JsonPropertyOrder(4)] IncidentSeverity Severity,
    [property: JsonPropertyOrder(5)] IncidentState State,
    [property: JsonPropertyOrder(6)] string ConfirmedObservation,
    [property: JsonPropertyOrder(7)] string? Interpretation,
    [property: JsonPropertyOrder(8)] string Limitations,
    [property: JsonPropertyOrder(9)] string? RecommendedAction,
    [property: JsonPropertyOrder(10)] Confidence Confidence,
    [property: JsonPropertyOrder(11)] DateTimeOffset StartedAt,
    [property: JsonPropertyOrder(12)] DateTimeOffset? ResolvedAt) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RequireSchemaVersion(errors, SchemaVersion);
        ContractValidation.RequireIdentifier(errors, "incident_id", IncidentId);
        ContractValidation.RequireIdentifier(errors, "property_id", PropertyId);
        ContractValidation.RejectUndefinedEnum(errors, "incident_class", IncidentClass);
        ContractValidation.RejectUndefinedEnum(errors, "severity", Severity);
        ContractValidation.RejectUndefinedEnum(errors, "state", State);
        RequireText(errors, "confirmed_observation", ConfirmedObservation, 500, true);
        RequireText(errors, "interpretation", Interpretation, 1000, false);
        RequireText(errors, "limitations", Limitations, 1000, true);
        RequireText(errors, "recommended_action", RecommendedAction, 500, false);
        ContractValidation.RejectUndefinedEnum(errors, "confidence", Confidence);
        ContractValidation.RequireUtc(errors, "started_at", StartedAt);
        if (ResolvedAt is { } resolvedAt) ContractValidation.RequireUtc(errors, "resolved_at", resolvedAt);
        return errors;
    }

    private static void RequireText(List<ContractValidationError> errors, string field, string? value, int maxLength, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) errors.Add(new(field, "required", "Text is required."));
            return;
        }

        if (value.Length > maxLength) errors.Add(new(field, "invalid_length", $"Text must be no more than {maxLength} characters."));
    }
}

public sealed record ErrorEnvelopeDto(
    [property: JsonPropertyOrder(0)] string SchemaVersion,
    [property: JsonPropertyOrder(1)] string ErrorCode,
    [property: JsonPropertyOrder(2)] string Message,
    [property: JsonPropertyOrder(3)] string? CorrelationId = null,
    [property: JsonPropertyOrder(4)] IReadOnlyList<ContractValidationError>? ValidationErrors = null);

public sealed record AgentConfigurationDto(
    [property: JsonPropertyOrder(0)] string SchemaVersion,
    [property: JsonPropertyOrder(1)] string ConfigurationId,
    [property: JsonPropertyOrder(2)] string PropertyId,
    [property: JsonPropertyOrder(3)] AgentMode Mode,
    [property: JsonPropertyOrder(4)] int HeartbeatIntervalSeconds,
    [property: JsonPropertyOrder(5)] IReadOnlyList<ConfiguredTargetDto> CriticalTargets) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RequireSchemaVersion(errors, SchemaVersion);
        ContractValidation.RequireIdentifier(errors, "configuration_id", ConfigurationId);
        ContractValidation.RequireIdentifier(errors, "property_id", PropertyId);
        ContractValidation.RejectUndefinedEnum(errors, "mode", Mode);
        if (HeartbeatIntervalSeconds is < 5 or > 3600) errors.Add(new("heartbeat_interval_seconds", "out_of_range", "Heartbeat interval must be between 5 and 3600 seconds."));
        foreach (ConfiguredTargetDto target in CriticalTargets) errors.AddRange(target.Validate());
        return errors;
    }
}

public sealed record ConfiguredTargetDto(string TargetId, string Label) : IContractValidatable
{
    public IReadOnlyList<ContractValidationError> Validate()
    {
        List<ContractValidationError> errors = [];
        ContractValidation.RequireIdentifier(errors, "target_id", TargetId);
        if (string.IsNullOrWhiteSpace(Label) || Label.Length > 160) errors.Add(new("label", "invalid_length", "Target label is required and must be no more than 160 characters."));
        return errors;
    }
}
