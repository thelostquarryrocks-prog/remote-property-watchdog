namespace RemotePropertyWatchdog.Agent.Service;

public enum AgentReadinessTransitionKind
{
    JournalOperation,
    ReadinessPublication
}

public interface IAgentReadinessTransitionGate
{
    Task<T> ExecuteAsync<T>(
        AgentReadinessTransitionKind kind,
        Func<Task<T>> operation,
        CancellationToken cancellationToken);
}

public sealed class AgentReadinessTransitionGate : IAgentReadinessTransitionGate, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<T> ExecuteAsync<T>(
        AgentReadinessTransitionKind kind,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentNullException.ThrowIfNull(operation);
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await operation();
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}
