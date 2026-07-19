namespace RemotePropertyWatchdog.Agent.Service;

public interface IAgentLifecycleStore
{
    Task<AgentLifecycleMarker?> ReadLastMarkerAsync(CancellationToken cancellationToken);

    Task WriteMarkerAsync(AgentLifecycleMarker marker, CancellationToken cancellationToken);
}
