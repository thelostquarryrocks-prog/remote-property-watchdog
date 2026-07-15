namespace RemotePropertyWatchdog.Agent.Service;

public sealed class AgentLifecycleCoordinator(
    IAgentLifecycleStore store,
    TimeProvider timeProvider) : IAgentLifecycleCoordinator, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private AgentLifecycleState? currentState;

    public AgentLifecycleState? CurrentState => currentState;

    public async Task<AgentLifecycleState> StartAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (currentState is not null)
            {
                return currentState;
            }

            AgentLifecycleMarker? previous = await store.ReadLastMarkerAsync(cancellationToken);
            DateTimeOffset now = timeProvider.GetUtcNow();
            AgentLifecycleState state = new(
                Guid.NewGuid(),
                now,
                ReadyAtUtc: null,
                StoppedAtUtc: null,
                PlannedShutdown: false,
                previous?.SessionId,
                previous is null || previous.PlannedShutdown);

            await store.WriteMarkerAsync(ToMarker(state), cancellationToken);
            currentState = state;
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AgentLifecycleState> MarkReadyAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            AgentLifecycleState state = currentState ?? throw new InvalidOperationException("Lifecycle must be started before readiness can be recorded.");
            if (state.ReadyAtUtc is not null)
            {
                return state;
            }

            state = state with { ReadyAtUtc = timeProvider.GetUtcNow() };
            await store.WriteMarkerAsync(ToMarker(state), cancellationToken);
            currentState = state;
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AgentLifecycleState> StopAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            AgentLifecycleState state = currentState ?? throw new InvalidOperationException("Lifecycle must be started before shutdown can be recorded.");
            if (state.PlannedShutdown)
            {
                return state;
            }

            state = state with { StoppedAtUtc = timeProvider.GetUtcNow(), PlannedShutdown = true };
            await store.WriteMarkerAsync(ToMarker(state), cancellationToken);
            currentState = state;
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private static AgentLifecycleMarker ToMarker(AgentLifecycleState state) =>
        new(state.SessionId, state.StartedAtUtc, state.ReadyAtUtc, state.StoppedAtUtc, state.PlannedShutdown);
}
