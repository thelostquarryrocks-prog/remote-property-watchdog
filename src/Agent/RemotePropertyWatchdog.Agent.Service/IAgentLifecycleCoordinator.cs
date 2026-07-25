namespace RemotePropertyWatchdog.Agent.Service;

public interface IAgentLifecycleCoordinator
{
    AgentLifecycleState? CurrentState { get; }

    Task<AgentLifecycleState> StartAsync(CancellationToken cancellationToken);

    Task<AgentLifecycleState> MarkReadyAsync(CancellationToken cancellationToken);

    Task<AgentLifecycleState> MarkNotReadyAsync(CancellationToken cancellationToken);

    Task<AgentLifecycleState> StopAsync(CancellationToken cancellationToken);
}
