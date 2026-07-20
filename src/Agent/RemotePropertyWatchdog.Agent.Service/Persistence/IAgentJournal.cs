namespace RemotePropertyWatchdog.Agent.Service;

public interface IAgentJournal
{
    AgentJournalHealth Health { get; }

    Task<AgentJournalHealth> InitializeAsync(
        AgentJournalInitializationMode mode,
        CancellationToken cancellationToken);

    Task<AgentJournalHealth> VerifyIntegrityAsync(CancellationToken cancellationToken);

    Task RecordSessionAsync(AgentSessionRecord session, CancellationToken cancellationToken);

    Task AppendProbeSampleAsync(ProbeSampleWrite sample, CancellationToken cancellationToken);

    Task<MaterialEventCommit> AppendMaterialEventAsync(MaterialEventWrite materialEvent, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingOutboxItem>> ReadPendingOutboxAsync(string agentId, int maximumCount, CancellationToken cancellationToken);
}
