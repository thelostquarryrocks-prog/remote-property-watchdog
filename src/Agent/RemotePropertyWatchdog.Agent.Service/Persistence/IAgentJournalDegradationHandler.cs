namespace RemotePropertyWatchdog.Agent.Service;

public interface IAgentJournalDegradationHandler
{
    Task<bool> TransitionToJournalDegradedAsync(CancellationToken cancellationToken);
}

public sealed class NoOpAgentJournalDegradationHandler : IAgentJournalDegradationHandler
{
    public static NoOpAgentJournalDegradationHandler Instance { get; } = new();

    private NoOpAgentJournalDegradationHandler()
    {
    }

    public Task<bool> TransitionToJournalDegradedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}
