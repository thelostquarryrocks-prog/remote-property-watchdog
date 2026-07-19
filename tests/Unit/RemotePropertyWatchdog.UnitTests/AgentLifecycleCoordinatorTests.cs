using Microsoft.Extensions.Options;
using RemotePropertyWatchdog.Agent.Service;

namespace RemotePropertyWatchdog.UnitTests;

public sealed class AgentLifecycleCoordinatorTests
{
    [Fact]
    public async Task FileBackedLifecycleMarkerRoundTripsCleanAndUncleanRestarts()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        IOptions<AgentLifecycleOptions> options = Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath });
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));

        AgentLifecycleCoordinator firstCoordinator = new(new FileAgentLifecycleStore(options), clock, options);
        AgentLifecycleState firstStart = await firstCoordinator.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        AgentLifecycleState firstReady = await firstCoordinator.MarkReadyAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        AgentLifecycleState firstStop = await firstCoordinator.StopAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, firstStart.SessionId);
        Assert.True(firstReady.IsReady);
        Assert.True(firstStop.PlannedShutdown);

        AgentLifecycleMarker cleanMarker = await ReadMarkerAsync(markerPath);
        Assert.Equal(firstStart.SessionId, cleanMarker.SessionId);
        Assert.True(cleanMarker.PlannedShutdown);
        Assert.Null(cleanMarker.PreviousSessionId);
        Assert.True(cleanMarker.PreviousSessionEndedCleanly);

        clock.Advance(TimeSpan.FromSeconds(1));
        AgentLifecycleCoordinator secondCoordinator = new(new FileAgentLifecycleStore(options), clock, options);
        AgentLifecycleState secondStart = await secondCoordinator.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        AgentLifecycleState secondReady = await secondCoordinator.MarkReadyAsync(CancellationToken.None);

        Assert.NotEqual(firstStart.SessionId, secondStart.SessionId);
        Assert.Equal(firstStart.SessionId, secondStart.PreviousSessionId);
        Assert.True(secondStart.PreviousSessionEndedCleanly);
        Assert.True(secondReady.IsReady);

        AgentLifecycleMarker runningMarker = await ReadMarkerAsync(markerPath);
        Assert.Equal(secondStart.SessionId, runningMarker.SessionId);
        Assert.False(runningMarker.PlannedShutdown);
        Assert.Equal(firstStart.SessionId, runningMarker.PreviousSessionId);
        Assert.True(runningMarker.PreviousSessionEndedCleanly);
        Assert.NotNull(runningMarker.ReadyAtUtc);
        Assert.InRange(runningMarker.ReadinessElapsedMilliseconds.GetValueOrDefault(), 0, 60_000);

        clock.Advance(TimeSpan.FromSeconds(1));
        AgentLifecycleCoordinator thirdCoordinator = new(new FileAgentLifecycleStore(options), clock, options);
        AgentLifecycleState thirdStart = await thirdCoordinator.StartAsync(CancellationToken.None);

        Assert.NotEqual(secondStart.SessionId, thirdStart.SessionId);
        Assert.Equal(secondStart.SessionId, thirdStart.PreviousSessionId);
        Assert.False(thirdStart.PreviousSessionEndedCleanly);

        AgentLifecycleMarker uncleanMarker = await ReadMarkerAsync(markerPath);
        Assert.Equal(thirdStart.SessionId, uncleanMarker.SessionId);
        Assert.False(uncleanMarker.PlannedShutdown);
        Assert.Equal(secondStart.SessionId, uncleanMarker.PreviousSessionId);
        Assert.False(uncleanMarker.PreviousSessionEndedCleanly);
    }

    [Fact]
    public async Task MarkReadyAsyncRecordsElapsedTimeWithinTimeout()
    {
        InMemoryLifecycleStore store = new();
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        AgentLifecycleCoordinator coordinator = new(store, clock, Options.Create(new AgentLifecycleOptions { ReadinessTimeout = TimeSpan.FromSeconds(2) }));
        await coordinator.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(1500));

        AgentLifecycleState ready = await coordinator.MarkReadyAsync(CancellationToken.None);

        Assert.True(ready.IsReady);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), ready.ReadinessElapsed);
        Assert.Equal(1500, store.Marker?.ReadinessElapsedMilliseconds);
        Assert.Equal(ready.ReadyAtUtc, store.Marker?.ReadyAtUtc);
    }

    [Fact]
    public async Task MarkReadyAsyncRejectsElapsedTimeBeyondTimeoutAndLeavesMarkerNotReady()
    {
        InMemoryLifecycleStore store = new();
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        AgentLifecycleCoordinator coordinator = new(store, clock, Options.Create(new AgentLifecycleOptions { ReadinessTimeout = TimeSpan.FromSeconds(1) }));
        await coordinator.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.MarkReadyAsync(CancellationToken.None));

        Assert.Null(store.Marker?.ReadyAtUtc);
        Assert.Null(store.Marker?.ReadinessElapsedMilliseconds);
        Assert.False(store.Marker?.PlannedShutdown);
    }

    private static async Task<AgentLifecycleMarker> ReadMarkerAsync(string markerPath)
    {
        FileAgentLifecycleStore store = new(Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }));
        AgentLifecycleMarker? marker = await store.ReadLastMarkerAsync(CancellationToken.None);
        Assert.NotNull(marker);
        return marker;
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
