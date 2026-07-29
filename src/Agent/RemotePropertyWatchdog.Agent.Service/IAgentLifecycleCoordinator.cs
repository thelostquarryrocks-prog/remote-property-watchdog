namespace RemotePropertyWatchdog.Agent.Service;

public interface IAgentLifecycleCoordinator
{
    AgentLifecycleState? CurrentState { get; }

    Task<AgentLifecycleState> StartAsync(CancellationToken cancellationToken);

    Task<AgentLifecycleState> ClaimJournalOwnershipAsync(CancellationToken cancellationToken);

    Task<AgentLifecycleState> PrepareReadyAsync(CancellationToken cancellationToken);

    Task<AgentLifecycleState> PublishReadyAsync(
        AgentLifecycleState readyState,
        CancellationToken cancellationToken);

    Task<AgentLifecycleState> MarkNotReadyAsync(CancellationToken cancellationToken);

    Task<AgentLifecycleState> StopAsync(CancellationToken cancellationToken);
}
