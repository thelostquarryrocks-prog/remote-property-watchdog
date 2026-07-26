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
        AgentLifecycleState firstReady = await MarkReadyAsync(firstCoordinator);
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
        AgentLifecycleState secondReady = await MarkReadyAsync(secondCoordinator);

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

        AgentLifecycleState ready = await MarkReadyAsync(coordinator);

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareReadyAsync(CancellationToken.None));

        Assert.Null(store.Marker?.ReadyAtUtc);
        Assert.Null(store.Marker?.ReadinessElapsedMilliseconds);
        Assert.False(store.Marker?.PlannedShutdown);
    }

    [Fact]
    public async Task BackwardAndEqualClockStepsProduceRestartableChronologicalMarkers()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        IOptions<AgentLifecycleOptions> options = Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath });
        DateTimeOffset initialTime = new(2026, 7, 15, 1, 2, 3, TimeSpan.Zero);
        TestTimeProvider clock = new(initialTime);

        using (AgentLifecycleCoordinator first = new(new FileAgentLifecycleStore(options), clock, options))
        {
            AgentLifecycleState started = await first.StartAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromMinutes(-5));
            AgentLifecycleState ready = await MarkReadyAsync(first);
            clock.Advance(TimeSpan.FromMinutes(-5));
            AgentLifecycleState stopped = await first.StopAsync(CancellationToken.None);

            Assert.Equal(initialTime, started.StartedAtUtc);
            Assert.Equal(started.StartedAtUtc, ready.ReadyAtUtc);
            Assert.Equal(TimeSpan.Zero, ready.ReadinessElapsed);
            Assert.Equal(ready.ReadyAtUtc, stopped.StoppedAtUtc);
        }

        AgentLifecycleMarker firstMarker = await ReadMarkerAsync(markerPath);
        Assert.Equal(firstMarker.StartedAtUtc, firstMarker.ReadyAtUtc);
        Assert.Equal(firstMarker.ReadyAtUtc, firstMarker.StoppedAtUtc);

        using AgentLifecycleCoordinator restarted = new(new FileAgentLifecycleStore(options), clock, options);
        AgentLifecycleState restartedState = await restarted.StartAsync(CancellationToken.None);
        AgentLifecycleState restartedReady = await MarkReadyAsync(restarted);
        AgentLifecycleState restartedStop = await restarted.StopAsync(CancellationToken.None);

        Assert.Equal(firstMarker.StoppedAtUtc, restartedState.StartedAtUtc);
        Assert.Equal(restartedState.StartedAtUtc, restartedReady.ReadyAtUtc);
        Assert.Equal(TimeSpan.Zero, restartedReady.ReadinessElapsed);
        Assert.Equal(restartedReady.ReadyAtUtc, restartedStop.StoppedAtUtc);

        AgentLifecycleMarker restartedMarker = await ReadMarkerAsync(markerPath);
        Assert.Equal(restartedMarker.StartedAtUtc, restartedMarker.ReadyAtUtc);
        Assert.Equal(restartedMarker.ReadyAtUtc, restartedMarker.StoppedAtUtc);
    }

    [Fact]
    public async Task MarkNotReadyAsyncClearsActiveReadinessFromStateAndMarker()
    {
        InMemoryLifecycleStore store = new();
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        AgentLifecycleCoordinator coordinator = new(
            store,
            clock,
            Options.Create(new AgentLifecycleOptions()));
        await coordinator.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await MarkReadyAsync(coordinator)).IsReady);

        AgentLifecycleState notReady = await coordinator.MarkNotReadyAsync(CancellationToken.None);

        Assert.False(notReady.IsReady);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Null(notReady.ReadyAtUtc);
        Assert.Null(notReady.ReadinessElapsed);
        Assert.Null(store.Marker?.ReadyAtUtc);
        Assert.Null(store.Marker?.ReadinessElapsedMilliseconds);
    }

    [Fact]
    public async Task JournalDegradationKeepsInMemoryStateNotReadyWhenMarkerWriteFails()
    {
        FailingLifecycleStore store = new();
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        AgentLifecycleCoordinator coordinator = new(
            store,
            clock,
            Options.Create(new AgentLifecycleOptions()));
        await coordinator.StartAsync(CancellationToken.None);
        await MarkReadyAsync(coordinator);
        AgentLifecycleMarker readyMarker = Assert.IsType<AgentLifecycleMarker>(store.Marker);
        store.FailWrites = true;

        bool persisted = await coordinator.TransitionToJournalDegradedAsync(CancellationToken.None);

        Assert.False(persisted);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Null(coordinator.CurrentState?.ReadyAtUtc);
        Assert.Null(coordinator.CurrentState?.ReadinessElapsed);
        Assert.Same(readyMarker, store.Marker);
        Assert.NotNull(store.Marker?.ReadyAtUtc);
    }

    [Fact]
    public async Task PreparedReadinessIsNotPublishedUntilExplicitPublication()
    {
        InMemoryLifecycleStore store = new();
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        AgentLifecycleCoordinator coordinator = new(
            store,
            clock,
            Options.Create(new AgentLifecycleOptions()));
        await coordinator.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));

        AgentLifecycleState candidate = await coordinator.PrepareReadyAsync(CancellationToken.None);

        Assert.True(candidate.IsReady);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Null(store.Marker?.ReadyAtUtc);
        Assert.Null(store.Marker?.ReadinessElapsedMilliseconds);

        AgentLifecycleState published = await coordinator.PublishReadyAsync(candidate, CancellationToken.None);

        Assert.True(coordinator.CurrentState?.IsReady);
        Assert.Equal(candidate.ReadyAtUtc, published.ReadyAtUtc);
        Assert.Equal(candidate.ReadinessElapsed, published.ReadinessElapsed);
        Assert.Equal(candidate.ReadyAtUtc, store.Marker?.ReadyAtUtc);
    }

    [Fact]
    public async Task InterruptedLegacyUpgradeCannotReauthorizeCreationAfterOwnershipClaim()
    {
        AgentLifecycleMarker legacy = AuthenticLegacyMarker();
        InMemoryLifecycleStore store = new(legacy);
        TestTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero));
        IOptions<AgentLifecycleOptions> options = Options.Create(new AgentLifecycleOptions());

        using (AgentLifecycleCoordinator beforeClaim = new(store, clock, options))
        {
            AgentLifecycleState started = await beforeClaim.StartAsync(CancellationToken.None);
            Assert.False(started.HasJournalOwnership);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        using (AgentLifecycleCoordinator retryBeforeClaim = new(store, clock, options))
        {
            AgentLifecycleState retried = await retryBeforeClaim.StartAsync(CancellationToken.None);
            Assert.False(retried.HasJournalOwnership);
            AgentLifecycleState claimed = await retryBeforeClaim.ClaimJournalOwnershipAsync(CancellationToken.None);
            Assert.True(claimed.HasJournalOwnership);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        using AgentLifecycleCoordinator afterClaim = new(store, clock, options);
        AgentLifecycleState restarted = await afterClaim.StartAsync(CancellationToken.None);

        Assert.True(restarted.HasJournalOwnership);
        Assert.Equal(AgentLifecycleMarker.CurrentJournalOwnershipVersion, store.Marker?.JournalOwnershipVersion);
    }

    [Fact]
    public async Task AmbiguousOwnershipEvidenceIsRejectedBeforeLifecycleStart()
    {
        AgentLifecycleMarker ambiguous = AuthenticLegacyMarker() with { JournalOwnershipVersion = 0 };
        InMemoryLifecycleStore store = new(ambiguous);
        AgentLifecycleCoordinator coordinator = new(
            store,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero)),
            Options.Create(new AgentLifecycleOptions()));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => coordinator.StartAsync(CancellationToken.None));

        Assert.Null(coordinator.CurrentState);
        Assert.Same(ambiguous, store.Marker);
    }

    [Fact]
    public async Task ContradictoryExternalChronologyRemainsRejected()
    {
        AgentLifecycleMarker contradictory = AuthenticLegacyMarker() with
        {
            ReadyAtUtc = new DateTimeOffset(2026, 7, 14, 22, 59, 59, TimeSpan.Zero)
        };
        InMemoryLifecycleStore store = new(contradictory);
        AgentLifecycleCoordinator coordinator = new(
            store,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero)),
            Options.Create(new AgentLifecycleOptions()));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => coordinator.StartAsync(CancellationToken.None));

        Assert.Null(coordinator.CurrentState);
        Assert.Same(contradictory, store.Marker);
    }

    private static AgentLifecycleMarker AuthenticLegacyMarker() =>
        new(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            new DateTimeOffset(2026, 7, 14, 23, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 14, 23, 0, 1, TimeSpan.Zero),
            1000,
            new DateTimeOffset(2026, 7, 14, 23, 0, 2, TimeSpan.Zero),
            PlannedShutdown: true,
            PreviousSessionId: null,
            PreviousSessionEndedCleanly: true);

    private static async Task<AgentLifecycleState> MarkReadyAsync(
        AgentLifecycleCoordinator coordinator)
    {
        AgentLifecycleState ready = await coordinator.PrepareReadyAsync(CancellationToken.None);
        return await coordinator.PublishReadyAsync(ready, CancellationToken.None);
    }

    private static async Task<AgentLifecycleMarker> ReadMarkerAsync(string markerPath)
    {
        FileAgentLifecycleStore store = new(Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }));
        AgentLifecycleMarker? marker = await store.ReadLastMarkerAsync(CancellationToken.None);
        Assert.NotNull(marker);
        return marker;
    }

    private sealed class InMemoryLifecycleStore(AgentLifecycleMarker? marker = null) : IAgentLifecycleStore
    {
        public AgentLifecycleMarker? Marker { get; private set; } = marker;

        public Task<AgentLifecycleMarker?> ReadLastMarkerAsync(CancellationToken cancellationToken) => Task.FromResult(Marker);

        public Task WriteMarkerAsync(AgentLifecycleMarker marker, CancellationToken cancellationToken)
        {
            Marker = marker;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingLifecycleStore : IAgentLifecycleStore
    {
        public AgentLifecycleMarker? Marker { get; private set; }

        public bool FailWrites { get; set; }

        public Task<AgentLifecycleMarker?> ReadLastMarkerAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Marker);

        public Task WriteMarkerAsync(AgentLifecycleMarker marker, CancellationToken cancellationToken)
        {
            if (FailWrites)
            {
                throw new IOException("synthetic lifecycle marker failure");
            }

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
