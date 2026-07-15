namespace RemotePropertyWatchdog.Contracts;

public static class ContractExamples
{
    private static readonly DateTimeOffset ExampleTime = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    public static HeartbeatDto Heartbeat() => new(
        ContractSchema.CurrentVersion,
        "hb_01J000000000000000000001",
        "agt_01J000000000000000000001",
        "prp_01J000000000000000000001",
        42,
        "ses_01J000000000000000000001",
        ExampleTime,
        3600,
        AgentMode.Normal,
        new GatewaySummary(ProbeState.Up, 4),
        new InternetSummary(ProbeState.Up, ProbeState.Up, 3),
        new CriticalTargetSummary(2, 0, 0, 0),
        41,
        0,
        "0.1.0-test");

    public static MaterialEventDto MaterialEvent() => new(
        ContractSchema.CurrentVersion,
        "msg_01J000000000000000000001",
        "agt_01J000000000000000000001",
        "prp_01J000000000000000000001",
        42,
        "ses_01J000000000000000000001",
        ExampleTime,
        MaterialEventType.TargetStateChanged,
        "tgt_camera_front_door_001",
        "Front door camera",
        "up",
        "down",
        ["tcp_connect_timeout", "selected_critical_target"],
        "up",
        "up");

    public static EventBatchDto EventBatch() => new(
        ContractSchema.CurrentVersion,
        "bat_01J000000000000000000001",
        "agt_01J000000000000000000001",
        "prp_01J000000000000000000001",
        42,
        42,
        [MaterialEvent()]);

    public static IncidentSummaryDto IncidentSummary() => new(
        ContractSchema.CurrentVersion,
        "inc_01J000000000000000000001",
        "prp_01J000000000000000000001",
        IncidentClass.CriticalDeviceOffline,
        IncidentSeverity.High,
        IncidentState.Open,
        "The selected front door camera did not respond to local health checks.",
        "The camera may be powered off, disconnected, or unreachable through the local network.",
        "The system cannot determine the physical cause unknown until additional evidence is available.",
        "Inspect the device and local network when safe to do so.",
        Confidence.Medium,
        ExampleTime,
        null);
}
