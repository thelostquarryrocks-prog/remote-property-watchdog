namespace RemotePropertyWatchdog.Shared.Contracts;

public static class ContractSchema
{
    public const string CurrentVersion = "0.1";

    public static readonly string[] RequiredSchemaFiles =
    [
        "heartbeat.schema.json",
        "material-event.schema.json",
        "event-batch.schema.json",
        "incident-summary.schema.json"
    ];
}
