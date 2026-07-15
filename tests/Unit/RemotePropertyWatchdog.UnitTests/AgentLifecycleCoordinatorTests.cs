using RemotePropertyWatchdog.Agent.Service;

namespace RemotePropertyWatchdog.UnitTests;

public sealed class AgentLifecycleCoordinatorTests
{
    [Fact]
    public async Task StartAsyncCreatesSessionAndUncleanMarkerUntilStopped()
    {
        InMemoryLifecycleStore store = new();
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        AgentLifecycleCoordinator coordinator = new(store, clock);

        AgentLifecycleState state = await coordinator.StartAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, state.SessionId);
        Assert.False(state.PlannedShutdown);
        Assert.True(state.PreviousSessionEndedCleanly);
        Assert.Equal(state.SessionId, store.Marker?.SessionId);
        Assert.False(store.Marker?.PlannedShutdown);
    }

    [Fact]
    public async Task StopAsyncRecordsPlannedShutdownMarker()
    {
        InMemoryLifecycleStore store = new();
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        AgentLifecycleCoordinator coordinator = new(store, clock);
        await coordinator.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(5));

        AgentLifecycleState stopped = await coordinator.StopAsync(CancellationToken.None);

        Assert.True(stopped.PlannedShutdown);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 1, 2, 8, TimeSpan.Zero), stopped.StoppedAtUtc);
        Assert.True(store.Marker?.PlannedShutdown);
    }

    [Fact]
    public async Task RestartAfterUnplannedTerminationReportsPreviousSessionUnclean()
    {
        InMemoryLifecycleStore store = new();
        AgentLifecycleCoordinator firstCoordinator = new(store, new TestTimeProvider(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero)));
        AgentLifecycleState first = await firstCoordinator.StartAsync(CancellationToken.None);

        AgentLifecycleCoordinator secondCoordinator = new(store, new TestTimeProvider(new DateTimeOffset(2026, 7, 15, 1, 3, 3, TimeSpan.Zero)));
        AgentLifecycleState second = await secondCoordinator.StartAsync(CancellationToken.None);

        Assert.Equal(first.SessionId, second.PreviousSessionId);
        Assert.False(second.PreviousSessionEndedCleanly);
    }

    [Fact]
    public async Task MarkReadyAsyncRecordsReadinessTimestamp()
    {
        InMemoryLifecycleStore store = new();
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        AgentLifecycleCoordinator coordinator = new(store, clock);
        await coordinator.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));

        AgentLifecycleState ready = await coordinator.MarkReadyAsync(CancellationToken.None);

        Assert.True(ready.IsReady);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 1, 2, 4, TimeSpan.Zero), ready.ReadyAtUtc);
        Assert.Equal(ready.ReadyAtUtc, store.Marker?.ReadyAtUtc);
    }

    private sealed class InMemoryLifecycleStore : IAgentLifecycleStore
    {
        public AgentLifecycleMarker? Marker { get; private set; }

        public Task<AgentLifecycleMarker?> ReadLastMarkerAsync(CancellationToken cancellationToken) => Task.FromResult(Marker);

        public Task WriteMarkerAsync(AgentLifecycleMarker marker, CancellationToken cancellationToken)
        {
            Marker = marker;
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan value) => utcNow = utcNow.Add(value);
    }
}
