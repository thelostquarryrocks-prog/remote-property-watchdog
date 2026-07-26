using RemotePropertyWatchdog.Agent.Service;

namespace RemotePropertyWatchdog.UnitTests;

public sealed class AgentReadinessTransitionGateTests
{
    [Fact]
    public async Task JournalOperationCannotOvertakeReadinessPublication()
    {
        using AgentReadinessTransitionGate gate = new();
        TaskCompletionSource readinessEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseReadiness =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool journalOperationEntered = false;

        Task<bool> readiness = gate.ExecuteAsync(
            AgentReadinessTransitionKind.ReadinessPublication,
            async () =>
            {
                readinessEntered.SetResult();
                await releaseReadiness.Task;
                return true;
            },
            CancellationToken.None);
        await readinessEntered.Task;

        Task<bool> journalOperation = gate.ExecuteAsync(
            AgentReadinessTransitionKind.JournalOperation,
            () =>
            {
                journalOperationEntered = true;
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.False(journalOperation.IsCompleted);
        Assert.False(journalOperationEntered);
        releaseReadiness.SetResult();

        Assert.True(await readiness);
        Assert.True(await journalOperation);
        Assert.True(journalOperationEntered);
    }

    [Fact]
    public async Task CancelledWaiterNeverEntersTransition()
    {
        using AgentReadinessTransitionGate gate = new();
        TaskCompletionSource readinessEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseReadiness =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cancelledOperationEntered = false;

        Task<bool> readiness = gate.ExecuteAsync(
            AgentReadinessTransitionKind.ReadinessPublication,
            async () =>
            {
                readinessEntered.SetResult();
                await releaseReadiness.Task;
                return true;
            },
            CancellationToken.None);
        await readinessEntered.Task;
        using CancellationTokenSource cancellation = new();
        Task<bool> cancelled = gate.ExecuteAsync(
            AgentReadinessTransitionKind.JournalOperation,
            () =>
            {
                cancelledOperationEntered = true;
                return Task.FromResult(true);
            },
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(cancelledOperationEntered);
        releaseReadiness.SetResult();
        Assert.True(await readiness);
    }
}
