using System.Text.Json;
using System.Text.Json.Nodes;
using RemotePropertyWatchdog.Contracts;

namespace RemotePropertyWatchdog.UnitTests.Contracts;

public sealed class ContractValidationTests
{
    [Fact]
    public void HeartbeatExampleRoundTripsWithoutFieldLoss()
    {
        HeartbeatDto heartbeat = ContractExamples.Heartbeat();
        string json = ContractJson.Serialize(heartbeat);

        HeartbeatDto? roundTripped = ContractJson.Deserialize<HeartbeatDto>(json);

        Assert.Equal(heartbeat, roundTripped);
        Assert.Empty(heartbeat.Validate());
    }

    [Fact]
    public void InvalidIdsFailValidation()
    {
        HeartbeatDto heartbeat = ContractExamples.Heartbeat() with { AgentId = "short" };

        Assert.Contains(heartbeat.Validate(), error => error.Field == "agent_id" && error.Code == "invalid_length");
    }

    [Fact]
    public void MissingSequenceFailsValidation()
    {
        MaterialEventDto evt = ContractExamples.MaterialEvent() with { EventSequence = 0 };

        Assert.Contains(evt.Validate(), error => error.Field == "event_sequence" && error.Code == "required_positive");
    }

    [Fact]
    public void OversizedHeartbeatFailsValidation()
    {
        HeartbeatDto heartbeat = ContractExamples.Heartbeat() with { AgentVersion = new string('a', 11_000) };

        Assert.Contains(heartbeat.Validate(), error => error.Code is "invalid_length" or "payload_too_large");
    }

    [Fact]
    public void NonUtcTimeFailsValidation()
    {
        HeartbeatDto heartbeat = ContractExamples.Heartbeat() with { SentAt = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.FromHours(-4)) };

        Assert.Contains(heartbeat.Validate(), error => error.Field == "sent_at" && error.Code == "utc_required");
    }

    [Fact]
    public void UndefinedEnumFailsValidation()
    {
        HeartbeatDto heartbeat = ContractExamples.Heartbeat() with { Mode = (AgentMode)999 };

        Assert.Contains(heartbeat.Validate(), error => error.Field == "mode" && error.Code == "unsupported_value");
    }

    [Fact]
    public void IncidentLanguageFieldsStaySeparated()
    {
        IncidentSummaryDto incident = ContractExamples.IncidentSummary();

        Assert.Empty(incident.Validate());
        Assert.Contains("cause unknown", incident.Limitations, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("intruder", ContractJson.Serialize(incident), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"normal\"")]
    [InlineData("\"away\"")]
    public void DefinedStringEnumJsonValuesDeserialize(string modeJson)
    {
        string json = ContractJson.Serialize(ContractExamples.Heartbeat()).Replace("\"mode\":\"normal\"", $"\"mode\":{modeJson}", StringComparison.Ordinal);

        HeartbeatDto? heartbeat = ContractJson.Deserialize<HeartbeatDto>(json);

        Assert.NotNull(heartbeat);
        Assert.True(Enum.IsDefined(heartbeat.Mode));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("999")]
    public void NumericEnumJsonValuesAreRejectedDuringDeserialization(string modeJson)
    {
        string json = ContractJson.Serialize(ContractExamples.Heartbeat()).Replace("\"mode\":\"normal\"", $"\"mode\":{modeJson}", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => ContractJson.Deserialize<HeartbeatDto>(json));
    }

    [Theory]
    [InlineData("gateway")]
    [InlineData("internet")]
    [InlineData("critical")]
    public void MissingRequiredHeartbeatObjectsThrowControlledJsonException(string propertyName)
    {
        string json = RemoveProperty(ContractJson.Serialize(ContractExamples.Heartbeat()), propertyName);

        Assert.Throws<JsonException>(() => ContractJson.Deserialize<HeartbeatDto>(json));
    }

    [Theory]
    [InlineData("evidence", typeof(MaterialEventDto))]
    [InlineData("events", typeof(EventBatchDto))]
    public void MissingRequiredCollectionsThrowControlledJsonException(string propertyName, Type dtoType)
    {
        object payload = dtoType == typeof(MaterialEventDto) ? ContractExamples.MaterialEvent() : ContractExamples.EventBatch();
        string json = RemoveProperty(ContractJson.Serialize(payload), propertyName);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, dtoType, ContractJson.Options));
    }

    [Theory]
    [InlineData("gateway")]
    [InlineData("internet")]
    [InlineData("critical")]
    public void NullRequiredHeartbeatObjectsReturnStructuredValidationError(string propertyName)
    {
        string json = ReplacePropertyWithNull(ContractJson.Serialize(ContractExamples.Heartbeat()), propertyName);
        HeartbeatDto heartbeat = ContractJson.Deserialize<HeartbeatDto>(json)!;

        Assert.Contains(heartbeat.Validate(), error => error.Field == propertyName && error.Code == "required");
    }

    [Fact]
    public void EventBatchValidationRequiresMatchingIdentityAndContiguousOrderedSequences()
    {
        MaterialEventDto first = ContractExamples.MaterialEvent() with { EventSequence = 42 };
        MaterialEventDto second = ContractExamples.MaterialEvent() with { EventSequence = 44, AgentId = "agt_01J000000000000000000999" };
        EventBatchDto batch = ContractExamples.EventBatch() with { FirstSequence = 42, LastSequence = 44, Events = [first, second] };

        IReadOnlyList<ContractValidationError> errors = batch.Validate();

        Assert.Contains(errors, error => error.Field == "events[1].agent_id" && error.Code == "batch_mismatch");
        Assert.Contains(errors, error => error.Field == "events[1].event_sequence" && error.Code == "non_contiguous_sequence");
        Assert.Contains(errors, error => error.Field == "last_sequence" && error.Code == "batch_mismatch");
    }

    [Fact]
    public void EventBatchValidationAcceptsMatchingIdentityAndContiguousOrderedSequences()
    {
        MaterialEventDto first = ContractExamples.MaterialEvent() with { EventSequence = 42 };
        MaterialEventDto second = ContractExamples.MaterialEvent() with { EventSequence = 43 };
        EventBatchDto batch = ContractExamples.EventBatch() with { FirstSequence = 42, LastSequence = 43, Events = [first, second] };

        Assert.Empty(batch.Validate());
    }

    private static string RemoveProperty(string json, string propertyName)
    {
        JsonObject node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove(propertyName));
        return node.ToJsonString(ContractJson.Options);
    }

    private static string ReplacePropertyWithNull(string json, string propertyName)
    {
        JsonObject node = JsonNode.Parse(json)!.AsObject();
        node[propertyName] = null;
        return node.ToJsonString(ContractJson.Options);
    }

}
