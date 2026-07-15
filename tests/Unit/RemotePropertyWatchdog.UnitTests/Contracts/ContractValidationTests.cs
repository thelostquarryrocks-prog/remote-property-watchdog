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
}
