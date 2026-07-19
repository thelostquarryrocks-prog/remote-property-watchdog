namespace RemotePropertyWatchdog.Agent.Service;

public sealed class AgentJournalOptions
{
    public const string SectionName = "AgentJournal";

    public string? DatabasePath { get; set; }

    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
