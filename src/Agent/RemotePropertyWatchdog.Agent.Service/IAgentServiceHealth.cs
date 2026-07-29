namespace RemotePropertyWatchdog.Agent.Service;

public interface IAgentServiceHealth
{
    AgentJournalHealth Health { get; }

    bool IsReady { get; }
}
