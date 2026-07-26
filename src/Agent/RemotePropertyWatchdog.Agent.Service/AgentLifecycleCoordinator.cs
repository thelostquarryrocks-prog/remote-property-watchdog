using Microsoft.Extensions.Options;

namespace RemotePropertyWatchdog.Agent.Service;

public sealed class AgentLifecycleCoordinator(
    IAgentLifecycleStore store,
    TimeProvider timeProvider,
    IOptions<AgentLifecycleOptions> options) :
    IAgentLifecycleCoordinator,
    IAgentJournalDegradationHandler,
    IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private AgentLifecycleState? currentState;

    public AgentLifecycleState? CurrentState => Volatile.Read(ref currentState);

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
            ValidatePreviousMarker(previous);
            DateTimeOffset now = NormalizeTransitionTimestamp(
                timeProvider.GetUtcNow(),
                previous?.StartedAtUtc,
                previous?.ReadyAtUtc,
                previous?.StoppedAtUtc);
            AgentLifecycleState state = new(
                Guid.NewGuid(),
                now,
                ReadyAtUtc: null,
                ReadinessElapsed: null,
                StoppedAtUtc: null,
                PlannedShutdown: false,
                previous?.SessionId,
                previous is null || previous.PlannedShutdown,
                previous?.JournalOwnershipVersion);

            await store.WriteMarkerAsync(ToMarker(state), cancellationToken);
            currentState = state;
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AgentLifecycleState> ClaimJournalOwnershipAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            AgentLifecycleState state = currentState ?? throw new InvalidOperationException("Lifecycle must be started before journal ownership can be claimed.");
            if (state.HasJournalOwnership)
            {
                return state;
            }

            if (state.JournalOwnershipVersion is not null)
            {
                throw new InvalidDataException("Lifecycle marker contains an unsupported journal ownership version.");
            }

            state = state with
            {
                JournalOwnershipVersion = AgentLifecycleMarker.CurrentJournalOwnershipVersion
            };
            await store.WriteMarkerAsync(ToMarker(state), cancellationToken);
            currentState = state;
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AgentLifecycleState> PrepareReadyAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            AgentLifecycleState state = currentState ?? throw new InvalidOperationException("Lifecycle must be started before readiness can be prepared.");
            if (state.ReadyAtUtc is not null)
            {
                return state;
            }

            DateTimeOffset readyAtUtc = NormalizeTransitionTimestamp(
                timeProvider.GetUtcNow(),
                state.StartedAtUtc);
            TimeSpan elapsed = readyAtUtc - state.StartedAtUtc;
            if (elapsed > options.Value.ReadinessTimeout)
            {
                throw new InvalidOperationException("Lifecycle readiness exceeded the configured timeout.");
            }

            return state with { ReadyAtUtc = readyAtUtc, ReadinessElapsed = elapsed };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AgentLifecycleState> PublishReadyAsync(
        AgentLifecycleState readyState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readyState);
        await gate.WaitAsync(cancellationToken);
        try
        {
            AgentLifecycleState state = currentState ?? throw new InvalidOperationException("Lifecycle must be started before readiness can be published.");
            if (state.IsReady)
            {
                return state;
            }

            if (!readyState.IsReady ||
                readyState.SessionId != state.SessionId ||
                readyState.StartedAtUtc != state.StartedAtUtc ||
                readyState.PreviousSessionId != state.PreviousSessionId ||
                readyState.PreviousSessionEndedCleanly != state.PreviousSessionEndedCleanly ||
                readyState.JournalOwnershipVersion != state.JournalOwnershipVersion ||
                state.StoppedAtUtc is not null ||
                state.PlannedShutdown)
            {
                throw new InvalidOperationException("Prepared readiness does not match the active lifecycle session.");
            }

            await store.WriteMarkerAsync(ToMarker(readyState), cancellationToken);
            currentState = readyState;
            return readyState;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AgentLifecycleState> MarkNotReadyAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            AgentLifecycleState state = currentState ?? throw new InvalidOperationException("Lifecycle must be started before readiness can be cleared.");
            if (!state.IsReady)
            {
                return state;
            }

            state = state with { ReadyAtUtc = null, ReadinessElapsed = null };
            await store.WriteMarkerAsync(ToMarker(state), cancellationToken);
            currentState = state;
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> TransitionToJournalDegradedAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            AgentLifecycleState? state = currentState;
            if (state?.IsReady != true)
            {
                return true;
            }

            state = state with { ReadyAtUtc = null, ReadinessElapsed = null };
            Volatile.Write(ref currentState, state);
            try
            {
                await store.WriteMarkerAsync(ToMarker(state), cancellationToken);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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

            DateTimeOffset stoppedAtUtc = NormalizeTransitionTimestamp(
                timeProvider.GetUtcNow(),
                state.StartedAtUtc,
                state.ReadyAtUtc);
            state = state with { StoppedAtUtc = stoppedAtUtc, PlannedShutdown = true };
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

    private static DateTimeOffset NormalizeTransitionTimestamp(
        DateTimeOffset candidate,
        params DateTimeOffset?[] lowerBounds)
    {
        DateTimeOffset normalized = candidate.ToUniversalTime();
        foreach (DateTimeOffset? lowerBound in lowerBounds)
        {
            if (lowerBound.HasValue && normalized < lowerBound.Value)
            {
                normalized = lowerBound.Value;
            }
        }

        return normalized;
    }

    private static AgentLifecycleMarker ToMarker(AgentLifecycleState state) =>
        new(
            state.SessionId,
            state.StartedAtUtc,
            state.ReadyAtUtc,
            state.ReadinessElapsed is null ? null : Convert.ToInt64(state.ReadinessElapsed.Value.TotalMilliseconds),
            state.StoppedAtUtc,
            state.PlannedShutdown,
            state.PreviousSessionId,
            state.PreviousSessionEndedCleanly,
            state.JournalOwnershipVersion);

    private static void ValidatePreviousMarker(AgentLifecycleMarker? marker)
    {
        if (marker is null)
        {
            return;
        }

        bool readinessIsConsistent =
            marker.ReadyAtUtc.HasValue == marker.ReadinessElapsedMilliseconds.HasValue &&
            (!marker.ReadinessElapsedMilliseconds.HasValue ||
             marker.ReadinessElapsedMilliseconds.Value is >= 0 and <= 60_000);
        bool shutdownIsConsistent = marker.PlannedShutdown == marker.StoppedAtUtc.HasValue;
        bool chronologyIsValid =
            (!marker.ReadyAtUtc.HasValue || marker.ReadyAtUtc.Value >= marker.StartedAtUtc) &&
            (!marker.StoppedAtUtc.HasValue || marker.StoppedAtUtc.Value >= marker.StartedAtUtc) &&
            (!marker.ReadyAtUtc.HasValue ||
             !marker.StoppedAtUtc.HasValue ||
             marker.ReadyAtUtc.Value <= marker.StoppedAtUtc.Value);
        bool timestampsUseUtc =
            marker.StartedAtUtc.Offset == TimeSpan.Zero &&
            (!marker.ReadyAtUtc.HasValue || marker.ReadyAtUtc.Value.Offset == TimeSpan.Zero) &&
            (!marker.StoppedAtUtc.HasValue || marker.StoppedAtUtc.Value.Offset == TimeSpan.Zero);
        bool previousSessionIsConsistent =
            marker.PreviousSessionId != marker.SessionId &&
            (marker.PreviousSessionId is not null || marker.PreviousSessionEndedCleanly);
        bool ownershipIsSupported =
            marker.JournalOwnershipVersion is null or AgentLifecycleMarker.CurrentJournalOwnershipVersion;

        if (marker.SessionId == Guid.Empty ||
            marker.StartedAtUtc == default ||
            !readinessIsConsistent ||
            !shutdownIsConsistent ||
            !chronologyIsValid ||
            !timestampsUseUtc ||
            !previousSessionIsConsistent ||
            !ownershipIsSupported)
        {
            throw new InvalidDataException("Lifecycle marker is malformed or unsupported.");
        }
    }
}
