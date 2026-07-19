namespace RemotePropertyWatchdog.Agent.Service;

public interface IAgentJournalFaultInjector
{
    Task BeforeMaterialEventCommitAsync(CancellationToken cancellationToken);

    Task AfterMaterialEventCommitAsync(CancellationToken cancellationToken);
}

public sealed class NoOpAgentJournalFaultInjector : IAgentJournalFaultInjector
{
    public static NoOpAgentJournalFaultInjector Instance { get; } = new();

    private NoOpAgentJournalFaultInjector()
    {
    }

    public Task BeforeMaterialEventCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterMaterialEventCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
