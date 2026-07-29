using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RemotePropertyWatchdog.Agent.Service;
using RemotePropertyWatchdog.Contracts;

namespace RemotePropertyWatchdog.UnitTests;

public sealed class AgentHostSmokeTests
{
    [Fact]
    public void AgentHostBuilderRegistersHostedServiceAndLifecycleServices()
    {
        using IHost host = Program.CreateHostBuilder([]).Build();

        IEnumerable<IHostedService> hostedServices = host.Services.GetRequiredService<IEnumerable<IHostedService>>();

        Assert.Contains(hostedServices, service => service.GetType() == typeof(AgentWorker));
        IAgentLifecycleCoordinator coordinator = Assert.IsType<AgentLifecycleCoordinator>(
            host.Services.GetRequiredService<IAgentLifecycleCoordinator>());
        Assert.Same(
            coordinator,
            host.Services.GetRequiredService<IAgentJournalDegradationHandler>());
        Assert.IsType<FileAgentLifecycleStore>(host.Services.GetRequiredService<IAgentLifecycleStore>());
        Assert.IsType<SqliteAgentJournal>(host.Services.GetRequiredService<IAgentJournal>());
        Assert.IsType<NoOpAgentJournalFaultInjector>(host.Services.GetRequiredService<IAgentJournalFaultInjector>());
        Assert.IsType<AgentReadinessTransitionGate>(
            host.Services.GetRequiredService<IAgentReadinessTransitionGate>());
        AgentWorker worker = Assert.Single(hostedServices.OfType<AgentWorker>());
        Assert.Same(worker, host.Services.GetRequiredService<IAgentServiceHealth>());
    }

    [Fact]
    public async Task AgentHostStartsAndStopsWithCleanLifecycleMarker()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        using IHost host = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:00",
            "--AgentJournal:DatabasePath", databasePath
        ]).Build();

        await host.StartAsync(CancellationToken.None);
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentLifecycleState? started = null;
        for (int i = 0; i < 20; i++)
        {
            started = coordinator.CurrentState;
            if (started?.IsReady == true)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        Assert.NotNull(started);
        Assert.Null(started.PreviousSessionId);
        Assert.True(started.HasJournalOwnership);
        await WaitUntilJournalSessionReadyAsync(databasePath, started.SessionId);
        await host.StopAsync(CancellationToken.None);

        AgentLifecycleState? stopped = coordinator.CurrentState;
        AgentSessionRecord? persisted = await ReadJournalSessionAsync(databasePath, started.SessionId);

        Assert.True(started.IsReady);
        Assert.NotNull(stopped);
        Assert.True(stopped.PlannedShutdown);
        Assert.NotNull(persisted);
        Assert.Equal(started.SessionId.ToString("D"), persisted.SessionId);
        Assert.Equal(started.StartedAtUtc, persisted.StartedAtUtc);
        Assert.Equal(started.ReadyAtUtc, persisted.ReadyAtUtc);
        Assert.Equal(stopped.StoppedAtUtc, persisted.EndedAtUtc);
        Assert.True(persisted.PlannedShutdown);
        Assert.True(File.Exists(markerPath));
        Assert.True(File.Exists(databasePath));
        AgentJournalHealth health = host.Services.GetRequiredService<IAgentJournal>().Health;
        Assert.Equal(AgentJournalHealthState.Healthy, health.State);
        Assert.Equal(SqliteJournalSchema.CurrentVersion, health.SchemaVersion);
    }

    [Fact]
    public async Task DuplicateProbeRetryPreservesRealHostReadinessMarkerAndLaterJournalWork()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        ListLogger<SqliteAgentJournal> journalLogger = new();
        using IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
                services.AddSingleton<ILogger<SqliteAgentJournal>>(journalLogger))
            .Build();

        await host.StartAsync(CancellationToken.None);
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        await WaitUntilReadyAsync(coordinator);
        AgentLifecycleState readyState = Assert.IsType<AgentLifecycleState>(coordinator.CurrentState);
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        AgentWorker worker = GetAgentWorker(host);
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
        FileAgentLifecycleStore lifecycleStore = new(
            Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }));
        AgentLifecycleMarker readyMarker = Assert.IsType<AgentLifecycleMarker>(
            await lifecycleStore.ReadLastMarkerAsync(CancellationToken.None));
        ProbeSampleWrite original = RuntimeProbe(readyState.SessionId, readyState.ReadyAtUtc!.Value) with
        {
            SampleId = "sample-ready-duplicate"
        };

        await journal.AppendProbeSampleAsync(original, CancellationToken.None);
        await journal.AppendProbeSampleAsync(
            original with
            {
                ObservedAtUtc = original.ObservedAtUtc.AddSeconds(1),
                ProbeState = "unreachable",
                TargetId = "target-alternate",
                Evidence = ["probe_timeout"]
            },
            CancellationToken.None);
        await journal.AppendProbeSampleAsync(
            original with { SampleId = "sample-ready-after-duplicate" },
            CancellationToken.None);
        MaterialEventCommit commit = await journal.AppendMaterialEventAsync(
            new MaterialEventWrite(
                "event-ready-after-probe-duplicate",
                "agent-synthetic",
                "property-synthetic",
                readyState.SessionId.ToString("D"),
                original.ObservedAtUtc,
                MaterialEventType.TargetStateChanged,
                "target-synthetic",
                "Synthetic target",
                "up",
                "down",
                ["probe_timeout"],
                "up",
                "up"),
            CancellationToken.None);

        AgentLifecycleMarker markerAfterWrites = Assert.IsType<AgentLifecycleMarker>(
            await lifecycleStore.ReadLastMarkerAsync(CancellationToken.None));
        Assert.Equal(2L, await ReadProbeSampleCountAsync(databasePath));
        Assert.Equal(1, commit.AgentSequence);
        Assert.Single(await journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        Assert.Equal(AgentJournalHealthState.Healthy, worker.Health.State);
        Assert.True(worker.IsReady);
        Assert.Same(worker, serviceHealth);
        Assert.True(serviceHealth.IsReady);
        Assert.Equal(readyState, coordinator.CurrentState);
        Assert.Equal(readyMarker, markerAfterWrites);
        Assert.DoesNotContain(journalLogger.Entries, entry => entry.EventId is 101 or 104);
        Assert.DoesNotContain(
            journalLogger.Entries,
            entry => entry.Message.Contains("runtime_write_failed", StringComparison.Ordinal));

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public Task CredentialUrlValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInput() =>
        AssertCredentialValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInputAsync(
            "https://camera.local/live?token=synthetic-value",
            "credential-url");

    [Fact]
    public Task EmbeddedCredentialUrlValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInput() =>
        AssertCredentialValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInputAsync(
            "camera failed while contacting https://synthetic-user:synthetic-password@camera.invalid/live",
            "embedded-credential-url");

    [Fact]
    public Task ClosingPunctuationCredentialUrlValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInput() =>
        AssertCredentialValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInputAsync(
            "url=https://u:p@camera.invalid, status=down",
            "closing-punctuation-credential-url");

    [Fact]
    public Task BareCredentialAssignmentValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInput() =>
        AssertCredentialValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInputAsync(
            "api_key=synthetic-value",
            "bare-credential-assignment");

    [Fact]
    public Task ColonCredentialAssignmentValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInput() =>
        AssertCredentialValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInputAsync(
            "\"api_key\": \"synthetic-value\"",
            "colon-credential-assignment");

    [Fact]
    public Task EscapedAuthorizationAssignmentValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInput() =>
        AssertCredentialValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInputAsync(
            "{\\\"authorization\\\":\\\"Basic synthetic-value\\\"}",
            "escaped-authorization-assignment");

    private static async Task AssertCredentialValidationPreservesRealHostReadinessAndLaterJournalWorkWithoutLoggingInputAsync(
        string prohibitedValue,
        string eventName)
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        ListLogger<SqliteAgentJournal> journalLogger = new();
        ListLogger<AgentWorker> workerLogger = new();
        using IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILogger<SqliteAgentJournal>>(journalLogger);
                services.AddSingleton<ILogger<AgentWorker>>(workerLogger);
            })
            .Build();

        await host.StartAsync(CancellationToken.None);
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        await WaitUntilReadyAsync(coordinator);
        AgentLifecycleState readyState = Assert.IsType<AgentLifecycleState>(coordinator.CurrentState);
        DateTimeOffset readyAtUtc = readyState.ReadyAtUtc ??
            throw new InvalidOperationException("The host must publish a readiness timestamp.");
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        AgentWorker worker = GetAgentWorker(host);
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
        FileAgentLifecycleStore lifecycleStore = new(
            Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }));
        AgentLifecycleMarker readyMarker = Assert.IsType<AgentLifecycleMarker>(
            await lifecycleStore.ReadLastMarkerAsync(CancellationToken.None));
        int journalLogCount = journalLogger.Entries.Count;
        int workerLogCount = workerLogger.Entries.Count;

        ArgumentException rejection = await Assert.ThrowsAsync<ArgumentException>(
            () => journal.AppendMaterialEventAsync(
                new MaterialEventWrite(
                    $"event-rejected-{eventName}",
                    "agent-synthetic",
                    "property-synthetic",
                    readyState.SessionId.ToString("D"),
                    readyAtUtc,
                    MaterialEventType.TargetStateChanged,
                    "target-synthetic",
                    prohibitedValue,
                    "up",
                    "down",
                    ["probe_timeout"],
                    "up",
                    "up"),
                CancellationToken.None));

        Assert.Equal("values", rejection.ParamName);
        Assert.Contains(
            "Persistence input contains a prohibited secret-bearing value.",
            rejection.Message,
            StringComparison.Ordinal);
        await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);
        Assert.Equal(journalLogCount, journalLogger.Entries.Count);
        Assert.Equal(workerLogCount, workerLogger.Entries.Count);
        Assert.DoesNotContain(journalLogger.Entries, entry => entry.Message.Contains(prohibitedValue, StringComparison.Ordinal));
        Assert.DoesNotContain(workerLogger.Entries, entry => entry.Message.Contains(prohibitedValue, StringComparison.Ordinal));
        Assert.DoesNotContain(journalLogger.Entries, entry => entry.Message.Contains("synthetic-value", StringComparison.Ordinal));
        Assert.DoesNotContain(workerLogger.Entries, entry => entry.Message.Contains("synthetic-value", StringComparison.Ordinal));
        Assert.DoesNotContain(journalLogger.Entries, entry => entry.Exception is not null);
        Assert.DoesNotContain(workerLogger.Entries, entry => entry.Exception is not null);

        await journal.AppendProbeSampleAsync(
            RuntimeProbe(readyState.SessionId, readyAtUtc) with
            {
                SampleId = $"sample-after-{eventName}-rejection"
            },
            CancellationToken.None);
        MaterialEventCommit commit = await journal.AppendMaterialEventAsync(
            new MaterialEventWrite(
                $"event-after-{eventName}-rejection",
                "agent-synthetic",
                "property-synthetic",
                readyState.SessionId.ToString("D"),
                readyAtUtc,
                MaterialEventType.TargetStateChanged,
                "target-synthetic",
                "Synthetic target",
                "up",
                "down",
                ["probe_timeout"],
                "up",
                "up"),
            CancellationToken.None);

        AgentLifecycleMarker markerAfterWrites = Assert.IsType<AgentLifecycleMarker>(
            await lifecycleStore.ReadLastMarkerAsync(CancellationToken.None));
        Assert.Equal(1L, await ReadProbeSampleCountAsync(databasePath));
        Assert.Equal(1, commit.AgentSequence);
        Assert.Single(await journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        Assert.Equal(AgentJournalHealthState.Healthy, worker.Health.State);
        Assert.True(worker.IsReady);
        Assert.Same(worker, serviceHealth);
        Assert.True(serviceHealth.IsReady);
        Assert.Equal(readyState, coordinator.CurrentState);
        Assert.Equal(readyMarker, markerAfterWrites);
        Assert.DoesNotContain(journalLogger.Entries, entry => entry.EventId is 101 or 104);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AuthenticR0003UpgradeCreatesJournalThenRequiresAndReusesIt()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        Guid legacySessionId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        await WriteAuthenticR0003MarkerAsync(markerPath, legacySessionId);

        Guid upgradedSessionId;
        using (IHost upgradeHost = CreateHost(markerPath, databasePath))
        {
            await upgradeHost.StartAsync(CancellationToken.None);
            IAgentLifecycleCoordinator coordinator = upgradeHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
            await WaitUntilReadyAsync(coordinator);
            upgradedSessionId = coordinator.CurrentState!.SessionId;

            Assert.Equal(legacySessionId, coordinator.CurrentState.PreviousSessionId);
            Assert.True(coordinator.CurrentState.HasJournalOwnership);
            Assert.NotNull(await ReadJournalSessionAsync(databasePath, upgradedSessionId));
            AgentLifecycleMarker marker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(
                        Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.Equal(AgentLifecycleMarker.CurrentJournalOwnershipVersion, marker.JournalOwnershipVersion);

            await upgradeHost.StopAsync(CancellationToken.None);
        }

        Guid restartedSessionId;
        using (IHost restartedHost = CreateHost(markerPath, databasePath))
        {
            await restartedHost.StartAsync(CancellationToken.None);
            IAgentLifecycleCoordinator coordinator = restartedHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
            await WaitUntilReadyAsync(coordinator);
            restartedSessionId = coordinator.CurrentState!.SessionId;

            Assert.Equal(upgradedSessionId, coordinator.CurrentState.PreviousSessionId);
            Assert.True(coordinator.CurrentState.HasJournalOwnership);
            Assert.NotNull(await ReadJournalSessionAsync(databasePath, upgradedSessionId));
            Assert.NotNull(await ReadJournalSessionAsync(databasePath, restartedSessionId));

            await restartedHost.StopAsync(CancellationToken.None);
        }

        File.Delete(databasePath);
        File.Delete($"{databasePath}-wal");
        File.Delete($"{databasePath}-shm");
        AssertJournalFilesAbsent(databasePath);

        using IHost missingHost = CreateHost(markerPath, databasePath);
        await missingHost.StartAsync(CancellationToken.None);
        IAgentJournal journal = missingHost.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator missingCoordinator = missingHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        await WaitUntilJournalStateAsync(journal, AgentJournalHealthState.Degraded);

        Assert.Equal("database_missing", journal.Health.ReasonCode);
        Assert.Equal(restartedSessionId, missingCoordinator.CurrentState?.PreviousSessionId);
        Assert.False(missingCoordinator.CurrentState?.IsReady);
        AssertJournalFilesAbsent(databasePath);

        await missingHost.StopAsync(CancellationToken.None);
        AssertJournalFilesAbsent(databasePath);
    }

    [Fact]
    public async Task CrashAfterOwnershipClaimBeforeJournalCreationFailsClosedOnRestart()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        await WriteAuthenticR0003MarkerAsync(
            markerPath,
            Guid.Parse("22222222-2222-4222-8222-222222222222"));
        IOptions<AgentLifecycleOptions> options = Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath });
        using (AgentLifecycleCoordinator interrupted = new(
                   new FileAgentLifecycleStore(options),
                   TimeProvider.System,
                   options))
        {
            await interrupted.StartAsync(CancellationToken.None);
            AgentLifecycleState claimed = await interrupted.ClaimJournalOwnershipAsync(CancellationToken.None);
            Assert.True(claimed.HasJournalOwnership);
        }

        AssertJournalFilesAbsent(databasePath);

        using IHost restartedHost = CreateHost(markerPath, databasePath);
        await restartedHost.StartAsync(CancellationToken.None);
        IAgentJournal journal = restartedHost.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = restartedHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        await WaitUntilJournalStateAsync(journal, AgentJournalHealthState.Degraded);

        Assert.Equal("database_missing", journal.Health.ReasonCode);
        Assert.True(coordinator.CurrentState?.HasJournalOwnership);
        Assert.False(coordinator.CurrentState?.IsReady);
        AssertJournalFilesAbsent(databasePath);

        await restartedHost.StopAsync(CancellationToken.None);
        AssertJournalFilesAbsent(databasePath);
    }

    [Fact]
    public async Task MalformedLegacyMarkerCannotAuthorizeJournalCreation()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        await File.WriteAllTextAsync(markerPath, "{}", CancellationToken.None);
        ListLogger<AgentWorker> logger = new();
        using IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
                services.AddSingleton<ILogger<AgentWorker>>(logger))
            .Build();

        await host.StartAsync(CancellationToken.None);
        await WaitUntilLoggedAsync(logger, eventId: 5);
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();

        Assert.Equal(AgentJournalHealthState.NotInitialized, journal.Health.State);
        Assert.Null(coordinator.CurrentState);
        AssertJournalFilesAbsent(databasePath);

        await host.StopAsync(CancellationToken.None);
        AssertJournalFilesAbsent(databasePath);
    }


    [Fact]
    public async Task InitialSessionStorageFailureDegradesRealWorkerBeforeReadinessAndStopsFurtherJournalWork()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        SessionFaultInjector faultInjector = new(
            failOnCall: 1,
            new IOException("synthetic storage failure"));
        using IHost host = CreateHostWithFaultInjector(markerPath, databasePath, faultInjector);

        await host.StartAsync(CancellationToken.None);
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentWorker worker = GetAgentWorker(host);
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
        await WaitUntilJournalStateAsync(journal, AgentJournalHealthState.Degraded);

        Assert.Equal("runtime_write_failed", worker.Health.ReasonCode);
        Assert.Equal(worker.Health, journal.Health);
        Assert.Equal(worker.Health, serviceHealth.Health);
        Assert.False(worker.IsReady);
        Assert.False(serviceHealth.IsReady);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Equal(1, faultInjector.Calls);
        Assert.Null(await ReadJournalSessionAsync(databasePath, coordinator.CurrentState!.SessionId));
        await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);

        await host.StopAsync(CancellationToken.None);

        Assert.True(coordinator.CurrentState?.PlannedShutdown);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Equal(1, faultInjector.Calls);
        Assert.Equal("runtime_write_failed", journal.Health.ReasonCode);
    }

    [Fact]
    public async Task ReadySessionStorageFailureClearsEveryReadinessSurfaceAndStopsFurtherJournalWork()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        SessionFaultInjector faultInjector = new(
            failOnCall: 2,
            new IOException("synthetic storage failure"));
        using IHost host = CreateHostWithFaultInjector(markerPath, databasePath, faultInjector);

        await host.StartAsync(CancellationToken.None);
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentWorker worker = GetAgentWorker(host);
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
        await WaitUntilJournalStateAsync(journal, AgentJournalHealthState.Degraded);
        await WaitUntilNotReadyAsync(coordinator);

        Assert.Equal(AgentJournalHealthState.Degraded, worker.Health.State);
        Assert.Equal("runtime_write_failed", worker.Health.ReasonCode);
        Assert.Equal(worker.Health, journal.Health);
        Assert.Equal(worker.Health, serviceHealth.Health);
        Assert.False(worker.IsReady);
        Assert.False(serviceHealth.IsReady);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Null(coordinator.CurrentState?.ReadyAtUtc);
        Assert.Equal(2, faultInjector.Calls);
        AgentLifecycleMarker marker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(
                    Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                .ReadLastMarkerAsync(CancellationToken.None));
        Assert.Null(marker.ReadyAtUtc);
        Assert.Null(marker.ReadinessElapsedMilliseconds);

        AgentSessionRecord persisted = Assert.IsType<AgentSessionRecord>(
            await ReadJournalSessionAsync(databasePath, coordinator.CurrentState!.SessionId));
        Assert.Null(persisted.ReadyAtUtc);
        Assert.Null(persisted.EndedAtUtc);
        Assert.False(persisted.PlannedShutdown);
        await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);

        AgentJournalDegradedException blocked = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.RecordSessionAsync(persisted, CancellationToken.None));
        Assert.Equal("runtime_write_failed", blocked.Health.ReasonCode);
        Assert.Equal(2, faultInjector.Calls);

        await host.StopAsync(CancellationToken.None);

        Assert.True(coordinator.CurrentState?.PlannedShutdown);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.False(serviceHealth.IsReady);
        Assert.Equal(2, faultInjector.Calls);
        Assert.Equal("runtime_write_failed", serviceHealth.Health.ReasonCode);
    }

    [Fact]
    public async Task ReadyCommitRemainsUnpublishedWhileBlockedAndPublishesOneTimestampAfterCommit()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        BlockingReadySessionFaultInjector faultInjector = new();
        using IHost host = CreateHostWithFaultInjector(markerPath, databasePath, faultInjector);

        await host.StartAsync(CancellationToken.None);
        await faultInjector.ReadyCommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentWorker worker = GetAgentWorker(host);
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
        Guid sessionId = coordinator.CurrentState!.SessionId;
        AgentLifecycleMarker blockedMarker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(
                    Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                .ReadLastMarkerAsync(CancellationToken.None));
        AgentSessionRecord blockedSession = Assert.IsType<AgentSessionRecord>(
            await ReadJournalSessionAsync(databasePath, sessionId));

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        Assert.False(coordinator.CurrentState.IsReady);
        Assert.Null(coordinator.CurrentState.ReadyAtUtc);
        Assert.Null(blockedMarker.ReadyAtUtc);
        Assert.Null(blockedMarker.ReadinessElapsedMilliseconds);
        Assert.Null(blockedSession.ReadyAtUtc);
        Assert.False(worker.IsReady);
        Assert.False(serviceHealth.IsReady);
        await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);

        faultInjector.ReleaseReadyCommit();
        await WaitUntilReadyAsync(coordinator);
        await WaitUntilJournalSessionReadyAsync(databasePath, sessionId);
        AgentLifecycleMarker publishedMarker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(
                    Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                .ReadLastMarkerAsync(CancellationToken.None));
        AgentSessionRecord publishedSession = Assert.IsType<AgentSessionRecord>(
            await ReadJournalSessionAsync(databasePath, sessionId));

        Assert.True(worker.IsReady);
        Assert.True(serviceHealth.IsReady);
        Assert.True(coordinator.CurrentState.IsReady);
        Assert.Equal(coordinator.CurrentState.ReadyAtUtc, publishedMarker.ReadyAtUtc);
        Assert.Equal(coordinator.CurrentState.ReadyAtUtc, publishedSession.ReadyAtUtc);
        Assert.Equal(
            Convert.ToInt64(coordinator.CurrentState.ReadinessElapsed!.Value.TotalMilliseconds),
            publishedMarker.ReadinessElapsedMilliseconds);

        await host.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(ReadinessPausePoint.BeforeGate)]
    [InlineData(ReadinessPausePoint.AfterGate)]
    [InlineData(ReadinessPausePoint.AfterPublication)]
    public async Task ConcurrentReadinessAndDegradationAlwaysFinishNotReady(
        ReadinessPausePoint pausePoint)
    {
        for (int iteration = 0; iteration < 3; iteration++)
        {
            string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
            string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
            CoordinatedReadinessTransitionGate transitionGate = new(pausePoint);
            MaterialEventStorageFaultInjector faultInjector = new();
            MutableTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
            ListLogger<AgentWorker> workerLogger = new();
            using IHost host = Program.CreateHostBuilder([
                    "--AgentLifecycle:MarkerPath", markerPath,
                    "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                    "--AgentJournal:DatabasePath", databasePath
                ])
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IAgentReadinessTransitionGate>(transitionGate);
                    services.AddSingleton<IAgentJournalFaultInjector>(faultInjector);
                    services.AddSingleton<TimeProvider>(clock);
                    services.AddSingleton<ILogger<AgentWorker>>(workerLogger);
                })
                .Build();

            await host.StartAsync(CancellationToken.None);
            await transitionGate.ReadinessPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
            IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
            IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
            AgentWorker worker = GetAgentWorker(host);
            IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
            Guid sessionId = coordinator.CurrentState!.SessionId;
            AgentSessionRecord committedReady = Assert.IsType<AgentSessionRecord>(
                await ReadJournalSessionAsync(databasePath, sessionId));
            Assert.NotNull(committedReady.ReadyAtUtc);

            AgentLifecycleMarker pausedMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(
                        Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                    .ReadLastMarkerAsync(CancellationToken.None));
            if (pausePoint == ReadinessPausePoint.AfterPublication)
            {
                Assert.True(coordinator.CurrentState.IsReady);
                Assert.True(worker.IsReady);
                Assert.True(serviceHealth.IsReady);
                Assert.NotNull(pausedMarker.ReadyAtUtc);
            }
            else
            {
                Assert.False(coordinator.CurrentState.IsReady);
                Assert.False(worker.IsReady);
                Assert.False(serviceHealth.IsReady);
                Assert.Null(pausedMarker.ReadyAtUtc);
            }

            clock.Advance(TimeSpan.FromMinutes(-5));
            Task<MaterialEventCommit> degradation = journal.AppendMaterialEventAsync(
                RuntimeEvent(sessionId, clock.GetUtcNow(), iteration),
                CancellationToken.None);
            if (pausePoint == ReadinessPausePoint.BeforeGate)
            {
                AgentJournalDegradedException failure =
                    await Assert.ThrowsAsync<AgentJournalDegradedException>(() => degradation);
                Assert.Equal("runtime_write_failed", failure.Health.ReasonCode);
            }
            else
            {
                await transitionGate.JournalOperationAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            transitionGate.ReleaseReadiness();
            if (pausePoint != ReadinessPausePoint.BeforeGate)
            {
                AgentJournalDegradedException failure =
                    await Assert.ThrowsAsync<AgentJournalDegradedException>(() => degradation);
                Assert.Equal("runtime_write_failed", failure.Health.ReasonCode);
            }
            else
            {
                await WaitUntilLoggedAsync(workerLogger, eventId: 8);
            }

            Assert.Equal(AgentJournalHealthState.Degraded, journal.Health.State);
            Assert.Equal("runtime_write_failed", journal.Health.ReasonCode);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.False(worker.IsReady);
            Assert.False(serviceHealth.IsReady);
            AgentLifecycleMarker degradedMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(
                        Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.Null(degradedMarker.ReadyAtUtc);
            Assert.Null(degradedMarker.ReadinessElapsedMilliseconds);
            Assert.Equal(1, faultInjector.MaterialEventCalls);
            await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);
            await AssertBlockedAfterDegradationAsync(
                journal,
                new AgentSessionRecord(sessionId.ToString("D"), coordinator.CurrentState.StartedAtUtc),
                sessionId,
                clock.GetUtcNow(),
                "runtime_write_failed");

            await host.StopAsync(CancellationToken.None);

            Assert.True(coordinator.CurrentState.PlannedShutdown);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.Equal(coordinator.CurrentState.StartedAtUtc, coordinator.CurrentState.StoppedAtUtc);
            AgentLifecycleMarker stoppedMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(
                        Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.Equal(TimeSpan.Zero, stoppedMarker.StartedAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, stoppedMarker.StoppedAtUtc?.Offset);
            Assert.True(stoppedMarker.StoppedAtUtc >= stoppedMarker.StartedAtUtc);
            Assert.Null(stoppedMarker.ReadyAtUtc);
        }
    }

    [Fact]
    public async Task DegradationQueuedDuringReadyMarkerWriteCannotLeaveReadyMarker()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        IOptions<AgentLifecycleOptions> lifecycleOptions =
            Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath });
        BlockingReadyLifecycleStore lifecycleStore =
            new(new FileAgentLifecycleStore(lifecycleOptions));
        ObservingReadinessTransitionGate transitionGate = new();
        MaterialEventStorageFaultInjector faultInjector = new();
        ListLogger<AgentWorker> workerLogger = new();
        using IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
            {
                services.AddSingleton<IAgentLifecycleStore>(lifecycleStore);
                services.AddSingleton<IAgentReadinessTransitionGate>(transitionGate);
                services.AddSingleton<IAgentJournalFaultInjector>(faultInjector);
                services.AddSingleton<ILogger<AgentWorker>>(workerLogger);
            })
            .Build();

        await host.StartAsync(CancellationToken.None);
        await lifecycleStore.ReadyWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentWorker worker = GetAgentWorker(host);
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
        Guid sessionId = coordinator.CurrentState!.SessionId;
        AgentSessionRecord committedReady = Assert.IsType<AgentSessionRecord>(
            await ReadJournalSessionAsync(databasePath, sessionId));
        Assert.NotNull(committedReady.ReadyAtUtc);
        Assert.False(coordinator.CurrentState.IsReady);
        Assert.False(worker.IsReady);
        Assert.False(serviceHealth.IsReady);

        transitionGate.ObserveNextJournalOperation();
        Task<MaterialEventCommit> degradation = journal.AppendMaterialEventAsync(
            RuntimeEvent(sessionId, DateTimeOffset.UtcNow, 0),
            CancellationToken.None);
        await transitionGate.JournalOperationAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        lifecycleStore.ReleaseReadyWrite();
        await Assert.ThrowsAsync<AgentJournalDegradedException>(() => degradation);

        Assert.Equal("runtime_write_failed", journal.Health.ReasonCode);
        Assert.False(coordinator.CurrentState.IsReady);
        Assert.False(worker.IsReady);
        Assert.False(serviceHealth.IsReady);
        AgentLifecycleMarker marker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(lifecycleOptions)
                .ReadLastMarkerAsync(CancellationToken.None));
        Assert.Null(marker.ReadyAtUtc);
        Assert.Null(marker.ReadinessElapsedMilliseconds);
        await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CancelledReadinessWaitingForTransitionGateNeverPublishesPartialReadiness()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        CoordinatedReadinessTransitionGate transitionGate = new(ReadinessPausePoint.BeforeGate);
        using IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
                services.AddSingleton<IAgentReadinessTransitionGate>(transitionGate))
            .Build();

        await host.StartAsync(CancellationToken.None);
        await transitionGate.ReadinessPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
        Guid sessionId = coordinator.CurrentState!.SessionId;

        await host.StopAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        Assert.False(coordinator.CurrentState.IsReady);
        Assert.False(serviceHealth.IsReady);
        Assert.True(coordinator.CurrentState.PlannedShutdown);
        AgentSessionRecord committedReady = Assert.IsType<AgentSessionRecord>(
            await ReadJournalSessionAsync(databasePath, sessionId));
        Assert.NotNull(committedReady.ReadyAtUtc);
        AgentLifecycleMarker marker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(
                    Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                .ReadLastMarkerAsync(CancellationToken.None));
        Assert.Null(marker.ReadyAtUtc);
        Assert.Null(marker.ReadinessElapsedMilliseconds);
        Assert.True(marker.PlannedShutdown);
    }

    [Fact]
    public async Task DegradationMarkerFailureDuringReadinessRaceStaysFailClosedAndRestartsConservatively()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        CoordinatedReadinessTransitionGate transitionGate = new(ReadinessPausePoint.AfterPublication);
        MaterialEventStorageFaultInjector faultInjector = new();
        IOptions<AgentLifecycleOptions> lifecycleOptions =
            Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath });
        FailingAfterReadyLifecycleStore lifecycleStore =
            new(new FileAgentLifecycleStore(lifecycleOptions));
        ListLogger<SqliteAgentJournal> journalLogger = new();
        ListLogger<AgentWorker> workerLogger = new();
        Guid degradedSessionId;

        using (IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
            {
                services.AddSingleton<IAgentReadinessTransitionGate>(transitionGate);
                services.AddSingleton<IAgentJournalFaultInjector>(faultInjector);
                services.AddSingleton<IAgentLifecycleStore>(lifecycleStore);
                services.AddSingleton<ILogger<SqliteAgentJournal>>(journalLogger);
                services.AddSingleton<ILogger<AgentWorker>>(workerLogger);
            })
            .Build())
        {
            await host.StartAsync(CancellationToken.None);
            await transitionGate.ReadinessPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
            IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
            IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
            AgentWorker worker = GetAgentWorker(host);
            IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
            degradedSessionId = coordinator.CurrentState!.SessionId;
            AgentLifecycleMarker readyMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(lifecycleOptions)
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.NotNull(readyMarker.ReadyAtUtc);

            Task<MaterialEventCommit> degradation = journal.AppendMaterialEventAsync(
                RuntimeEvent(degradedSessionId, DateTimeOffset.UtcNow, 0),
                CancellationToken.None);
            await transitionGate.JournalOperationAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            transitionGate.ReleaseReadiness();
            await Assert.ThrowsAsync<AgentJournalDegradedException>(() => degradation);

            Assert.Equal("runtime_write_failed", journal.Health.ReasonCode);
            Assert.False(worker.IsReady);
            Assert.False(serviceHealth.IsReady);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.Contains(
                journalLogger.Entries,
                entry =>
                    entry.EventId == 104 &&
                    entry.LogLevel == LogLevel.Error &&
                    entry.Exception is null &&
                    entry.Message.Contains("lifecycle_marker_write_failed", StringComparison.Ordinal));
            AgentLifecycleMarker staleMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(lifecycleOptions)
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.Equal(readyMarker, staleMarker);
            await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);
            await AssertBlockedAfterDegradationAsync(
                journal,
                new AgentSessionRecord(degradedSessionId.ToString("D"), coordinator.CurrentState.StartedAtUtc),
                degradedSessionId,
                DateTimeOffset.UtcNow,
                "runtime_write_failed");

            await host.StopAsync(CancellationToken.None);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.False(coordinator.CurrentState.PlannedShutdown);
        }

        CoordinatedReadinessTransitionGate restartGate = new(ReadinessPausePoint.BeforeGate);
        using IHost restartedHost = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
                services.AddSingleton<IAgentReadinessTransitionGate>(restartGate))
            .Build();
        await restartedHost.StartAsync(CancellationToken.None);
        await restartGate.ReadinessPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        IAgentLifecycleCoordinator restartedCoordinator =
            restartedHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        IAgentServiceHealth restartedHealth =
            restartedHost.Services.GetRequiredService<IAgentServiceHealth>();
        Assert.Equal(degradedSessionId, restartedCoordinator.CurrentState?.PreviousSessionId);
        Assert.False(restartedCoordinator.CurrentState?.IsReady);
        Assert.False(restartedHealth.IsReady);
        AgentLifecycleMarker restartedMarker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(lifecycleOptions)
                .ReadLastMarkerAsync(CancellationToken.None));
        Assert.Null(restartedMarker.ReadyAtUtc);
        Assert.False(restartedMarker.PlannedShutdown);

        restartGate.ReleaseReadiness();
        await WaitUntilReadyAsync(restartedCoordinator);
        Assert.True(restartedHealth.IsReady);
        await restartedHost.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PostCommitPrePublicationFailureStaysNotReadyAndRecoversDeterministically()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        PostCommitReadySessionFaultInjector faultInjector = new();
        ListLogger<AgentWorker> logger = new();
        Guid failedSessionId;
        using (IHost failedHost = CreateHostWithFaultInjector(markerPath, databasePath, faultInjector, logger))
        {
            await failedHost.StartAsync(CancellationToken.None);
            await faultInjector.FaultInjected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilLoggedAsync(logger, eventId: 5);
            IAgentJournal journal = failedHost.Services.GetRequiredService<IAgentJournal>();
            IAgentLifecycleCoordinator coordinator = failedHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
            AgentWorker worker = GetAgentWorker(failedHost);
            IAgentServiceHealth serviceHealth = failedHost.Services.GetRequiredService<IAgentServiceHealth>();
            failedSessionId = coordinator.CurrentState!.SessionId;
            AgentSessionRecord persisted = Assert.IsType<AgentSessionRecord>(
                await ReadJournalSessionAsync(databasePath, failedSessionId));
            AgentLifecycleMarker marker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(
                        Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                    .ReadLastMarkerAsync(CancellationToken.None));

            Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
            Assert.NotNull(persisted.ReadyAtUtc);
            Assert.Null(marker.ReadyAtUtc);
            Assert.Null(marker.ReadinessElapsedMilliseconds);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.False(worker.IsReady);
            Assert.False(serviceHealth.IsReady);
            await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);

            await failedHost.StopAsync(CancellationToken.None);
            Assert.False(coordinator.CurrentState.PlannedShutdown);
        }

        using IHost recoveredHost = CreateHost(markerPath, databasePath);
        await recoveredHost.StartAsync(CancellationToken.None);
        IAgentLifecycleCoordinator recoveredCoordinator = recoveredHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        IAgentServiceHealth recoveredHealth = recoveredHost.Services.GetRequiredService<IAgentServiceHealth>();
        await WaitUntilReadyAsync(recoveredCoordinator);
        Guid recoveredSessionId = recoveredCoordinator.CurrentState!.SessionId;
        await WaitUntilJournalSessionReadyAsync(databasePath, recoveredSessionId);

        Assert.Equal(failedSessionId, recoveredCoordinator.CurrentState.PreviousSessionId);
        Assert.True(recoveredCoordinator.CurrentState.IsReady);
        Assert.True(recoveredHealth.IsReady);
        Assert.NotNull(await ReadJournalSessionAsync(databasePath, failedSessionId));
        Assert.NotNull(await ReadJournalSessionAsync(databasePath, recoveredSessionId));

        await recoveredHost.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PlannedShutdownSessionStorageFailureClearsReadinessAndPreservesJournalReason()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        SessionFaultInjector faultInjector = new(
            failOnCall: 3,
            new IOException("synthetic storage failure"));
        using IHost host = CreateHostWithFaultInjector(markerPath, databasePath, faultInjector);

        await host.StartAsync(CancellationToken.None);
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentWorker worker = GetAgentWorker(host);
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
        await WaitUntilReadyAsync(coordinator);
        Guid sessionId = coordinator.CurrentState!.SessionId;
        await WaitUntilJournalSessionReadyAsync(databasePath, sessionId);
        Assert.True(worker.IsReady);
        Assert.True(serviceHealth.IsReady);

        await host.StopAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Degraded, worker.Health.State);
        Assert.Equal("runtime_write_failed", worker.Health.ReasonCode);
        Assert.Equal(worker.Health, journal.Health);
        Assert.Equal(worker.Health, serviceHealth.Health);
        Assert.False(worker.IsReady);
        Assert.False(serviceHealth.IsReady);
        Assert.True(coordinator.CurrentState?.PlannedShutdown);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Equal(3, faultInjector.Calls);
        AgentSessionRecord persisted = Assert.IsType<AgentSessionRecord>(
            await ReadJournalSessionAsync(databasePath, sessionId));
        Assert.NotNull(persisted.ReadyAtUtc);
        Assert.Null(persisted.EndedAtUtc);
        Assert.False(persisted.PlannedShutdown);
        await AssertNoProbeEventOutboxOrSequenceWritesAsync(databasePath);

        AgentLifecycleState stopped = coordinator.CurrentState!;
        AgentJournalDegradedException blocked = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.RecordSessionAsync(
                new AgentSessionRecord(
                    stopped.SessionId.ToString("D"),
                    stopped.StartedAtUtc,
                    stopped.ReadyAtUtc,
                    stopped.StoppedAtUtc,
                    stopped.PlannedShutdown),
                CancellationToken.None));
        Assert.Equal("runtime_write_failed", blocked.Health.ReasonCode);
        Assert.Equal(3, faultInjector.Calls);
    }

    [Fact]
    public async Task RuntimeJournalLossClearsAllReadinessPersistsNotReadyAndSurvivesBackwardClockRestart()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        Guid degradedSessionId;
        using (IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(clock))
            .Build())
        {
            await host.StartAsync(CancellationToken.None);
            IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
            IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
            AgentWorker worker = GetAgentWorker(host);
            IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
            await WaitUntilReadyAsync(coordinator);
            degradedSessionId = coordinator.CurrentState!.SessionId;
            await WaitUntilJournalSessionReadyAsync(databasePath, degradedSessionId);
            AgentSessionRecord readySession = Assert.IsType<AgentSessionRecord>(
                await ReadJournalSessionAsync(databasePath, degradedSessionId));
            AgentLifecycleMarker readyMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(
                        Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.Equal(readySession.ReadyAtUtc, readyMarker.ReadyAtUtc);
            Assert.Equal(coordinator.CurrentState.ReadyAtUtc, readyMarker.ReadyAtUtc);
            clock.Advance(TimeSpan.FromMinutes(-5));
            DeleteJournalFiles(databasePath);

            AgentJournalDegradedException failure = await Assert.ThrowsAsync<AgentJournalDegradedException>(
                () => journal.AppendProbeSampleAsync(
                    RuntimeProbe(degradedSessionId, clock.GetUtcNow()),
                    CancellationToken.None));
            await WaitUntilNotReadyAsync(coordinator);

            Assert.Equal("database_missing", failure.Health.ReasonCode);
            Assert.Equal(failure.Health, journal.Health);
            Assert.Equal(failure.Health, worker.Health);
            Assert.Equal(failure.Health, serviceHealth.Health);
            Assert.False(worker.IsReady);
            Assert.False(serviceHealth.IsReady);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.Null(coordinator.CurrentState.ReadyAtUtc);
            AgentLifecycleMarker degradedMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(
                        Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.Null(degradedMarker.ReadyAtUtc);
            Assert.Null(degradedMarker.ReadinessElapsedMilliseconds);

            AgentSessionRecord blockedSession = new(
                degradedSessionId.ToString("D"),
                coordinator.CurrentState.StartedAtUtc);
            await AssertBlockedAfterDegradationAsync(
                journal,
                blockedSession,
                degradedSessionId,
                clock.GetUtcNow(),
                "database_missing");
            AssertJournalFilesAbsent(databasePath);

            await host.StopAsync(CancellationToken.None);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.True(coordinator.CurrentState.PlannedShutdown);
            Assert.Equal(coordinator.CurrentState.StartedAtUtc, coordinator.CurrentState.StoppedAtUtc);
        }

        using IHost restartedHost = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(clock))
            .Build();
        await restartedHost.StartAsync(CancellationToken.None);
        IAgentJournal restartedJournal = restartedHost.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator restartedCoordinator = restartedHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        IAgentServiceHealth restartedHealth = restartedHost.Services.GetRequiredService<IAgentServiceHealth>();
        await WaitUntilJournalStateAsync(restartedJournal, AgentJournalHealthState.Degraded);

        Assert.Equal("database_missing", restartedJournal.Health.ReasonCode);
        Assert.Equal(degradedSessionId, restartedCoordinator.CurrentState?.PreviousSessionId);
        Assert.False(restartedCoordinator.CurrentState?.IsReady);
        Assert.False(restartedHealth.IsReady);
        AssertJournalFilesAbsent(databasePath);
        AgentLifecycleMarker restartedMarker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(
                    Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                .ReadLastMarkerAsync(CancellationToken.None));
        Assert.Null(restartedMarker.ReadyAtUtc);
        Assert.False(restartedMarker.PlannedShutdown);

        await restartedHost.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RuntimeDegradationMarkerFailureStaysFailClosedAndLogsBoundedResult()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        IOptions<AgentLifecycleOptions> lifecycleOptions =
            Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath });
        FailingAfterReadyLifecycleStore lifecycleStore =
            new(new FileAgentLifecycleStore(lifecycleOptions));
        ListLogger<SqliteAgentJournal> journalLogger = new();
        Guid degradedSessionId;
        using (IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
            {
                services.AddSingleton<IAgentLifecycleStore>(lifecycleStore);
                services.AddSingleton<ILogger<SqliteAgentJournal>>(journalLogger);
            })
            .Build())
        {
            await host.StartAsync(CancellationToken.None);
            IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
            IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
            AgentWorker worker = GetAgentWorker(host);
            IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();
            await WaitUntilReadyAsync(coordinator);
            degradedSessionId = coordinator.CurrentState!.SessionId;
            await WaitUntilJournalSessionReadyAsync(databasePath, degradedSessionId);
            AgentLifecycleMarker durableReadyMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(lifecycleOptions)
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.NotNull(durableReadyMarker.ReadyAtUtc);
            DeleteJournalFiles(databasePath);

            AgentJournalDegradedException failure = await Assert.ThrowsAsync<AgentJournalDegradedException>(
                () => journal.AppendProbeSampleAsync(
                    RuntimeProbe(degradedSessionId, DateTimeOffset.UtcNow),
                    CancellationToken.None));
            await WaitUntilNotReadyAsync(coordinator);

            Assert.Equal("database_missing", failure.Health.ReasonCode);
            Assert.False(worker.IsReady);
            Assert.False(serviceHealth.IsReady);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.Contains(
                journalLogger.Entries,
                entry =>
                    entry.EventId == 104 &&
                    entry.LogLevel == LogLevel.Error &&
                    entry.Exception is null &&
                    entry.Message.Contains("lifecycle_marker_write_failed", StringComparison.Ordinal));
            AgentLifecycleMarker staleMarker = Assert.IsType<AgentLifecycleMarker>(
                await new FileAgentLifecycleStore(lifecycleOptions)
                    .ReadLastMarkerAsync(CancellationToken.None));
            Assert.Equal(durableReadyMarker, staleMarker);
            Assert.NotNull(staleMarker.ReadyAtUtc);
            await AssertBlockedAfterDegradationAsync(
                journal,
                new AgentSessionRecord(
                    degradedSessionId.ToString("D"),
                    coordinator.CurrentState.StartedAtUtc),
                degradedSessionId,
                DateTimeOffset.UtcNow,
                "database_missing");
            AssertJournalFilesAbsent(databasePath);

            await host.StopAsync(CancellationToken.None);
            Assert.False(coordinator.CurrentState.IsReady);
            Assert.False(coordinator.CurrentState.PlannedShutdown);
        }

        using IHost restartedHost = CreateHost(markerPath, databasePath);
        await restartedHost.StartAsync(CancellationToken.None);
        IAgentJournal restartedJournal = restartedHost.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator restartedCoordinator = restartedHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        await WaitUntilJournalStateAsync(restartedJournal, AgentJournalHealthState.Degraded);

        Assert.Equal("database_missing", restartedJournal.Health.ReasonCode);
        Assert.Equal(degradedSessionId, restartedCoordinator.CurrentState?.PreviousSessionId);
        Assert.False(restartedCoordinator.CurrentState?.IsReady);
        AgentLifecycleMarker restartedMarker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(lifecycleOptions)
                .ReadLastMarkerAsync(CancellationToken.None));
        Assert.Null(restartedMarker.ReadyAtUtc);
        AssertJournalFilesAbsent(databasePath);

        await restartedHost.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MissingJournalOnLaterRealHostStartDegradesWithoutRecreatingArtifacts()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        Guid firstSessionId;
        using (IHost firstHost = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:00",
            "--AgentJournal:DatabasePath", databasePath
        ]).Build())
        {
            await firstHost.StartAsync(CancellationToken.None);
            IAgentLifecycleCoordinator firstCoordinator = firstHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
            await WaitUntilReadyAsync(firstCoordinator);
            firstSessionId = firstCoordinator.CurrentState!.SessionId;
            await firstHost.StopAsync(CancellationToken.None);
        }

        Assert.True(File.Exists(markerPath));
        Assert.True(File.Exists(databasePath));
        File.Delete(databasePath);
        File.Delete($"{databasePath}-wal");
        File.Delete($"{databasePath}-shm");
        AssertJournalFilesAbsent(databasePath);

        using IHost restartedHost = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:00",
            "--AgentJournal:DatabasePath", databasePath
        ]).Build();
        await restartedHost.StartAsync(CancellationToken.None);
        IAgentJournal restartedJournal = restartedHost.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator restartedCoordinator = restartedHost.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        await WaitUntilJournalStateAsync(restartedJournal, AgentJournalHealthState.Degraded);

        Assert.Equal("database_missing", restartedJournal.Health.ReasonCode);
        Assert.Equal("not_run", restartedJournal.Health.IntegrityResult);
        Assert.Equal(firstSessionId, restartedCoordinator.CurrentState?.PreviousSessionId);
        Assert.False(restartedCoordinator.CurrentState?.IsReady);
        AssertJournalFilesAbsent(databasePath);

        await restartedHost.StopAsync(CancellationToken.None);

        Assert.False(restartedCoordinator.CurrentState?.IsReady);
        AssertJournalFilesAbsent(databasePath);
    }

    [Fact]
    public async Task OwnedJournalForeignKeyViolationBlocksEveryReadinessSurfaceWithoutRepairOrStartupWrites()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        SignalingLogger<AgentWorker> readyLogger = new(eventId: 2);
        using (IHost setupHost = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services => services.AddSingleton<ILogger<AgentWorker>>(readyLogger))
            .Build())
        {
            await setupHost.StartAsync(CancellationToken.None);
            await readyLogger.WaitAsync();
            await setupHost.StopAsync(CancellationToken.None);
        }

        AgentLifecycleMarker ownedMarker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(
                    Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                .ReadLastMarkerAsync(CancellationToken.None));
        Assert.Equal(AgentLifecycleMarker.CurrentJournalOwnershipVersion, ownedMarker.JournalOwnershipVersion);
        await InsertOutboxForeignKeyViolationAsync(databasePath);
        string before = await ReadJournalContentSignatureAsync(databasePath);

        SignalingLogger<AgentWorker> degradedLogger = new(eventId: 8);
        ListLogger<SqliteAgentJournal> journalLogger = new();
        using IHost host = Program.CreateHostBuilder([
                "--AgentLifecycle:MarkerPath", markerPath,
                "--AgentLifecycle:ReadinessTimeout", "00:01:00",
                "--AgentJournal:DatabasePath", databasePath
            ])
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILogger<AgentWorker>>(degradedLogger);
                services.AddSingleton<ILogger<SqliteAgentJournal>>(journalLogger);
            })
            .Build();

        await host.StartAsync(CancellationToken.None);
        LogEntry degradedLog = await degradedLogger.WaitAsync();
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentWorker worker = GetAgentWorker(host);
        IAgentServiceHealth serviceHealth = host.Services.GetRequiredService<IAgentServiceHealth>();

        Assert.Equal(AgentJournalHealthState.Degraded, journal.Health.State);
        Assert.Equal("integrity_check_failed", journal.Health.ReasonCode);
        Assert.Equal("failed", journal.Health.IntegrityResult);
        Assert.Equal(journal.Health, worker.Health);
        Assert.Equal(journal.Health, serviceHealth.Health);
        Assert.False(worker.IsReady);
        Assert.False(serviceHealth.IsReady);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Null(coordinator.CurrentState?.ReadyAtUtc);
        AgentLifecycleMarker rejectedMarker = Assert.IsType<AgentLifecycleMarker>(
            await new FileAgentLifecycleStore(
                    Options.Create(new AgentLifecycleOptions { MarkerPath = markerPath }))
                .ReadLastMarkerAsync(CancellationToken.None));
        Assert.Null(rejectedMarker.ReadyAtUtc);
        Assert.Null(rejectedMarker.ReadinessElapsedMilliseconds);

        AgentLifecycleState rejectedState = Assert.IsType<AgentLifecycleState>(coordinator.CurrentState);
        await AssertBlockedAfterDegradationAsync(
            journal,
            new AgentSessionRecord(rejectedState.SessionId.ToString("D"), rejectedState.StartedAtUtc),
            rejectedState.SessionId,
            rejectedState.StartedAtUtc,
            "integrity_check_failed");
        Assert.Equal(before, await ReadJournalContentSignatureAsync(databasePath));

        Assert.Null(degradedLog.Exception);
        Assert.DoesNotContain(databasePath, degradedLog.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outbox", degradedLog.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("event-missing", degradedLog.Message, StringComparison.Ordinal);
        LogEntry journalDegradedLog = Assert.Single(
            journalLogger.Entries,
            entry => entry.EventId == 101);
        Assert.Null(journalDegradedLog.Exception);
        Assert.DoesNotContain(databasePath, journalDegradedLog.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outbox", journalDegradedLog.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("event-missing", journalDegradedLog.Message, StringComparison.Ordinal);

        await host.StopAsync(CancellationToken.None);

        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Equal(before, await ReadJournalContentSignatureAsync(databasePath));
    }

    [Fact]
    public async Task CorruptJournalRemainsDegradedAndPreventsRealHostReadinessWithoutReplacingFile()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        byte[] corruptBytes = "not-a-sqlite-database"u8.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await File.WriteAllBytesAsync(databasePath, corruptBytes);
        using IHost host = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:00",
            "--AgentJournal:DatabasePath", databasePath
        ]).Build();

        await host.StartAsync(CancellationToken.None);
        IAgentJournal journal = host.Services.GetRequiredService<IAgentJournal>();
        await WaitUntilJournalStateAsync(journal, AgentJournalHealthState.Degraded);
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();

        Assert.Equal(AgentJournalHealthState.Degraded, journal.Health.State);
        Assert.False(coordinator.CurrentState?.IsReady);
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(databasePath));

        await host.StopAsync(CancellationToken.None);

        Assert.True(coordinator.CurrentState?.PlannedShutdown);
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(databasePath));
    }

    [Fact]
    public async Task AgentHostRejectsReadinessTimeoutOverSixtySecondsOnStartup()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        using IHost host = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:01",
            "--AgentJournal:DatabasePath", databasePath
        ]).Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AgentHostRejectsCleanShutdownWriteTimeoutOverThirtySecondsOnStartup()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        string databasePath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.db");
        using IHost host = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:CleanShutdownWriteTimeout", "00:00:31",
            "--AgentJournal:DatabasePath", databasePath
        ]).Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(CancellationToken.None));
    }

    [Fact]
    public void CanonicalServiceNameDoesNotDriftFromWindowsLifecycleScript()
    {
        string script = File.ReadAllText(FindRepositoryFile("scripts", "windows-service-lifecycle.ps1"));

        Assert.Contains(AgentServiceIdentity.ServiceName, script, StringComparison.Ordinal);
        Assert.Contains(AgentServiceIdentity.DisplayName, script, StringComparison.Ordinal);
    }


    [Fact]
    public void PowerShellLiveMarkerReaderUsesDeleteCompatibleFileStreamWithoutGetContent()
    {
        string script = File.ReadAllText(FindRepositoryFile("scripts", "windows-service-lifecycle.ps1"));
        int start = script.IndexOf("function Read-LifecycleMarker", StringComparison.Ordinal);
        int end = script.IndexOf("function Test-LifecycleMarker", StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        string readerFunction = script[start..end];

        Assert.DoesNotContain("Get-Content", readerFunction, StringComparison.Ordinal);
        Assert.Contains("[System.IO.FileStream]::new", readerFunction, StringComparison.Ordinal);
        Assert.Contains("[System.IO.FileShare]::Delete", readerFunction, StringComparison.Ordinal);
        Assert.Contains("[System.IO.FileAccess]::Read", readerFunction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentWorkerRecordsCleanShutdownOnlyAfterBaseStopCompletes()
    {
        RecordingLifecycleCoordinator coordinator = new();
        AgentWorker worker = CreateWorker(coordinator);
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilStartedAsync(coordinator);

        Assert.False(coordinator.StopCalled);

        await worker.StopAsync(CancellationToken.None);

        Assert.True(coordinator.StopCalled);
    }

    [Fact]
    public async Task AgentWorkerInitializesHealthyJournalAfterLifecycleStartAndBeforeReadiness()
    {
        List<string> operations = [];
        RecordingLifecycleCoordinator coordinator = new(operations);
        RecordingJournal journal = new(HealthyJournal(), operations);
        AgentWorker worker = CreateWorker(coordinator, journal: journal);

        await worker.StartAsync(CancellationToken.None);
        await journal.Initialized.Task;
        await WaitUntilMarkReadyCalledAsync(coordinator);

        Assert.Equal(
            AgentJournalInitializationMode.CreateIfMissingForFreshLifecycle,
            journal.InitializationMode);
        Assert.Equal(
            [
                "lifecycle_start",
                "lifecycle_journal_owned",
                "journal_initialize",
                "journal_session_started",
                "lifecycle_ready_prepared",
                "journal_session_ready",
                "lifecycle_ready_published"
            ],
            operations);

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(
            [
                "lifecycle_start",
                "lifecycle_journal_owned",
                "journal_initialize",
                "journal_session_started",
                "lifecycle_ready_prepared",
                "journal_session_ready",
                "lifecycle_ready_published",
                "lifecycle_stop",
                "journal_session_stopped"
            ],
            operations);
        AgentSessionRecord stopped = Assert.Single(journal.Sessions, session => session.PlannedShutdown);
        Assert.NotNull(stopped.EndedAtUtc);
    }

    [Fact]
    public async Task AgentWorkerKeepsDegradedJournalObservableAndNeverMarksReady()
    {
        RecordingLifecycleCoordinator coordinator = new();
        AgentJournalHealth degraded = new(AgentJournalHealthState.Degraded, 1, "integrity_check_failed", "failed");
        RecordingJournal journal = new(degraded);
        ListLogger<AgentWorker> logger = new();
        AgentWorker worker = CreateWorker(coordinator, journal: journal, logger: logger);

        await worker.StartAsync(CancellationToken.None);
        await journal.Initialized.Task;
        await WaitUntilLoggedAsync(logger, eventId: 8);

        Assert.Equal(degraded, journal.Health);
        Assert.False(coordinator.MarkReadyCalled);
        Assert.Empty(journal.Sessions);
        LogEntry entry = Assert.Single(logger.Entries, candidate => candidate.EventId == 8);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(".db", entry.Message, StringComparison.OrdinalIgnoreCase);

        await worker.StopAsync(CancellationToken.None);

        Assert.True(coordinator.StopCalled);
        Assert.True(coordinator.CleanShutdownRecorded);
    }


    [Fact]
    public async Task AgentWorkerMarkReadyFailureMarksFaultedAndRemainsObservable()
    {
        InvalidOperationException expected = new("readiness failed");
        RecordingLifecycleCoordinator coordinator = new() { MarkReadyException = expected };
        ListLogger<AgentWorker> logger = new();
        AgentWorker worker = CreateWorker(coordinator, logger: logger);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilLoggedAsync(logger, eventId: 5);
        await worker.StopAsync(CancellationToken.None);
        Assert.False(coordinator.StopCalled);
        Assert.Contains(logger.Entries, entry => entry.EventId == 5 && entry.LogLevel == LogLevel.Error && entry.Exception is null);
        Assert.Contains(logger.Entries, entry => entry.EventId == 6 && entry.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task AgentWorkerFaultDrivenStopDoesNotRecordPlannedShutdown()
    {
        RecordingLifecycleCoordinator coordinator = new() { MarkReadyException = new InvalidOperationException("readiness failed") };
        RecordingJournal journal = new(HealthyJournal());
        AgentWorker worker = CreateWorker(coordinator, journal: journal);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilFaultedAsync(coordinator);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(coordinator.StopCalled);
        Assert.False(coordinator.CleanShutdownRecorded);
        AgentSessionRecord session = Assert.Single(journal.Sessions);
        Assert.False(session.PlannedShutdown);
        Assert.Null(session.EndedAtUtc);
    }

    [Fact]
    public async Task AgentWorkerLeavesMarkerUncleanAndContinuesShutdownWhenFinalLifecycleWriteTimesOut()
    {
        RecordingLifecycleCoordinator coordinator = new() { DelayStopUntilCancelled = true };
        AgentWorker worker = CreateWorker(coordinator, TimeSpan.FromMilliseconds(1));
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilStartedAsync(coordinator);

        await worker.StopAsync(CancellationToken.None);

        Assert.True(coordinator.StopCalled);
        Assert.False(coordinator.CleanShutdownRecorded);
    }

    private static IHost CreateHost(string markerPath, string databasePath) =>
        Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:00",
            "--AgentJournal:DatabasePath", databasePath
        ]).Build();

    private static async Task WriteAuthenticR0003MarkerAsync(string markerPath, Guid sessionId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        string json = $$"""
            {
              "session_id": "{{sessionId:D}}",
              "started_at_utc": "2026-07-14T23:00:00+00:00",
              "ready_at_utc": "2026-07-14T23:00:01+00:00",
              "readiness_elapsed_milliseconds": 1000,
              "stopped_at_utc": "2026-07-14T23:00:02+00:00",
              "planned_shutdown": true,
              "previous_session_id": null,
              "previous_session_ended_cleanly": true
            }
            """;
        await File.WriteAllTextAsync(markerPath, json, CancellationToken.None);
    }

    private static async Task WaitUntilLoggedAsync(ListLogger<AgentWorker> logger, int eventId)
    {
        for (int i = 0; i < 20; i++)
        {
            if (logger.Entries.Any(entry => entry.EventId == eventId))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Expected log entry was not written within the test timeout.");
    }

    private static async Task WaitUntilFaultedAsync(RecordingLifecycleCoordinator coordinator)
    {
        await WaitUntilMarkReadyCalledAsync(coordinator);
    }

    private static async Task WaitUntilMarkReadyCalledAsync(RecordingLifecycleCoordinator coordinator)
    {
        for (int i = 0; i < 20; i++)
        {
            if (coordinator.MarkReadyCalled)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Worker did not reach MarkReadyAsync within the test timeout.");
    }

    private static async Task WaitUntilJournalStateAsync(IAgentJournal journal, AgentJournalHealthState expected)
    {
        for (int i = 0; i < 40; i++)
        {
            if (journal.Health.State == expected)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException($"Journal did not enter {expected} within the test timeout.");
    }

    private static async Task WaitUntilStartedAsync(RecordingLifecycleCoordinator coordinator)
    {
        for (int i = 0; i < 20; i++)
        {
            if (coordinator.CurrentState is not null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Worker did not start within the test timeout.");
    }

    private static async Task WaitUntilReadyAsync(IAgentLifecycleCoordinator coordinator)
    {
        for (int i = 0; i < 40; i++)
        {
            if (coordinator.CurrentState?.IsReady == true)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Agent did not become monitoring-ready within the test timeout.");
    }

    private static async Task WaitUntilNotReadyAsync(IAgentLifecycleCoordinator coordinator)
    {
        for (int i = 0; i < 40; i++)
        {
            if (coordinator.CurrentState is { IsReady: false })
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Agent did not clear monitoring readiness within the test timeout.");
    }

    private static async Task WaitUntilJournalSessionReadyAsync(string databasePath, Guid sessionId)
    {
        for (int i = 0; i < 40; i++)
        {
            AgentSessionRecord? session = await ReadJournalSessionAsync(databasePath, sessionId);
            if (session?.ReadyAtUtc is not null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Journal did not persist the ready lifecycle session within the test timeout.");
    }

    private static async Task<AgentSessionRecord?> ReadJournalSessionAsync(string databasePath, Guid sessionId)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, started_at_utc, ready_at_utc, ended_at_utc, planned_shutdown
            FROM agent_sessions
            WHERE session_id = $session_id;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new AgentSessionRecord(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetInt64(4) != 0);
    }

    private static IHost CreateHostWithFaultInjector(
        string markerPath,
        string databasePath,
        IAgentJournalFaultInjector faultInjector,
        ListLogger<AgentWorker>? logger = null)
    {
        IHostBuilder builder = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:00",
            "--AgentJournal:DatabasePath", databasePath
        ]);
        return builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAgentJournalFaultInjector>(faultInjector);
                if (logger is not null)
                {
                    services.AddSingleton<ILogger<AgentWorker>>(logger);
                }
            })
            .Build();
    }

    private static AgentWorker GetAgentWorker(IHost host) =>
        Assert.IsType<AgentWorker>(
            host.Services.GetServices<IHostedService>().Single(service => service is AgentWorker));

    private static async Task AssertNoProbeEventOutboxOrSequenceWritesAsync(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        foreach (string tableName in new[] { "probe_samples", "material_events", "outbox", "local_state" })
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
            Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
    }

    private static async Task<long> ReadProbeSampleCountAsync(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM probe_samples;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task InsertOutboxForeignKeyViolationAsync(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA wal_checkpoint(TRUNCATE);
            PRAGMA journal_mode = DELETE;
            PRAGMA foreign_keys = OFF;
            INSERT INTO outbox(
                outbox_id, event_id, agent_id, agent_sequence, payload_json, created_at_utc)
            VALUES (
                'outbox-orphan', 'event-missing', 'agent-synthetic', 1, '{}',
                '2026-07-26T12:00:00.0000000+00:00');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadJournalContentSignatureAsync(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        List<string> parts = [];
        foreach (string table in SqliteJournalSchema.Tables)
        {
            await using SqliteCommand count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {table};";
            parts.Add($"{table}={Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture)}");
        }

        await using (SqliteCommand integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            parts.Add($"integrity={Convert.ToString(await integrity.ExecuteScalarAsync(), CultureInfo.InvariantCulture)}");
        }

        await using (SqliteCommand foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check;";
            parts.Add($"foreign_key_violations={Convert.ToInt64(await foreignKeys.ExecuteScalarAsync(), CultureInfo.InvariantCulture)}");
        }

        return string.Join("\u001f", parts);
    }

    private static async Task AssertBlockedAfterDegradationAsync(
        IAgentJournal journal,
        AgentSessionRecord session,
        Guid sessionId,
        DateTimeOffset observedAtUtc,
        string reasonCode)
    {
        AgentJournalDegradedException sessionFailure = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.RecordSessionAsync(session, CancellationToken.None));
        AgentJournalDegradedException probeFailure = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendProbeSampleAsync(
                RuntimeProbe(sessionId, observedAtUtc),
                CancellationToken.None));
        AgentJournalDegradedException eventFailure = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendMaterialEventAsync(
                new MaterialEventWrite(
                    "event-runtime-blocked",
                    "agent-synthetic",
                    "property-synthetic",
                    sessionId.ToString("D"),
                    observedAtUtc,
                    MaterialEventType.TargetStateChanged,
                    "target-synthetic",
                    "Synthetic target",
                    "up",
                    "down",
                    ["probe_timeout"],
                    "up",
                    "up"),
                CancellationToken.None));
        AgentJournalDegradedException outboxFailure = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));

        Assert.All(
            new[] { sessionFailure, probeFailure, eventFailure, outboxFailure },
            failure => Assert.Equal(reasonCode, failure.Health.ReasonCode));
    }

    private static ProbeSampleWrite RuntimeProbe(Guid sessionId, DateTimeOffset observedAtUtc) =>
        new(
            "sample-runtime-failure",
            "agent-synthetic",
            sessionId.ToString("D"),
            observedAtUtc,
            "tcp",
            "reachable",
            "target-synthetic",
            ["probe_succeeded"]);

    private static MaterialEventWrite RuntimeEvent(
        Guid sessionId,
        DateTimeOffset observedAtUtc,
        int iteration) =>
        new(
            $"event-readiness-race-{iteration}-{Guid.NewGuid():N}",
            "agent-synthetic",
            "property-synthetic",
            sessionId.ToString("D"),
            observedAtUtc,
            MaterialEventType.TargetStateChanged,
            "target-synthetic",
            "Synthetic target",
            "up",
            "down",
            ["probe_timeout"],
            "up",
            "up");

    private static void DeleteJournalFiles(string databasePath)
    {
        File.Delete(databasePath);
        File.Delete($"{databasePath}-wal");
        File.Delete($"{databasePath}-shm");
    }

    private sealed class BlockingReadySessionFaultInjector : IAgentJournalFaultInjector
    {
        private readonly TaskCompletionSource releaseReadyCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;

        public TaskCompletionSource ReadyCommitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task BeforeSessionCommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls++;
            if (calls == 2)
            {
                ReadyCommitEntered.TrySetResult();
                await releaseReadyCommit.Task.WaitAsync(cancellationToken);
            }
        }

        public Task AfterSessionCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BeforeMaterialEventCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterMaterialEventCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void ReleaseReadyCommit() => releaseReadyCommit.TrySetResult();
    }

    private sealed class PostCommitReadySessionFaultInjector : IAgentJournalFaultInjector
    {
        private int calls;

        public TaskCompletionSource FaultInjected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task BeforeSessionCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterSessionCommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls++;
            if (calls != 2)
            {
                return Task.CompletedTask;
            }

            FaultInjected.TrySetResult();
            return Task.FromException(new SyntheticSessionPostCommitException());
        }

        public Task BeforeMaterialEventCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterMaterialEventCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SyntheticSessionPostCommitException : Exception;

    private sealed class SessionFaultInjector(int failOnCall, Exception exception) : IAgentJournalFaultInjector
    {
        public int Calls { get; private set; }

        public Task BeforeSessionCommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Calls == failOnCall
                ? Task.FromException(exception)
                : Task.CompletedTask;
        }

        public Task AfterSessionCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BeforeMaterialEventCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterMaterialEventCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MaterialEventStorageFaultInjector : IAgentJournalFaultInjector
    {
        public int MaterialEventCalls { get; private set; }

        public Task BeforeSessionCommitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AfterSessionCommitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task BeforeMaterialEventCommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MaterialEventCalls++;
            return Task.FromException(new IOException("synthetic storage failure"));
        }

        public Task AfterMaterialEventCommitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    public enum ReadinessPausePoint
    {
        BeforeGate,
        AfterGate,
        AfterPublication
    }

    private sealed class CoordinatedReadinessTransitionGate(
        ReadinessPausePoint pausePoint) : IAgentReadinessTransitionGate, IDisposable
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly TaskCompletionSource releaseReadiness =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int readinessIsPaused;

        public TaskCompletionSource ReadinessPaused { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource JournalOperationAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<T> ExecuteAsync<T>(
            AgentReadinessTransitionKind kind,
            Func<Task<T>> operation,
            CancellationToken cancellationToken)
        {
            if (kind == AgentReadinessTransitionKind.ReadinessPublication &&
                pausePoint == ReadinessPausePoint.BeforeGate)
            {
                await PauseReadinessAsync(cancellationToken);
            }

            if (kind == AgentReadinessTransitionKind.JournalOperation &&
                Volatile.Read(ref readinessIsPaused) != 0)
            {
                JournalOperationAttempted.TrySetResult();
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                if (kind == AgentReadinessTransitionKind.ReadinessPublication &&
                    pausePoint == ReadinessPausePoint.AfterGate)
                {
                    await PauseReadinessAsync(cancellationToken);
                }

                T result = await operation();
                if (kind == AgentReadinessTransitionKind.ReadinessPublication &&
                    pausePoint == ReadinessPausePoint.AfterPublication)
                {
                    await PauseReadinessAsync(cancellationToken);
                }

                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public void ReleaseReadiness() => releaseReadiness.TrySetResult();

        public void Dispose() => gate.Dispose();

        private async Task PauseReadinessAsync(CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref readinessIsPaused, 1);
            ReadinessPaused.TrySetResult();
            try
            {
                await releaseReadiness.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Exchange(ref readinessIsPaused, 0);
            }
        }
    }

    private sealed class ObservingReadinessTransitionGate : IAgentReadinessTransitionGate, IDisposable
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private int observeNextJournalOperation;

        public TaskCompletionSource JournalOperationAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<T> ExecuteAsync<T>(
            AgentReadinessTransitionKind kind,
            Func<Task<T>> operation,
            CancellationToken cancellationToken)
        {
            if (kind == AgentReadinessTransitionKind.JournalOperation &&
                Interlocked.Exchange(ref observeNextJournalOperation, 0) != 0)
            {
                JournalOperationAttempted.TrySetResult();
            }

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

        public void ObserveNextJournalOperation() =>
            Interlocked.Exchange(ref observeNextJournalOperation, 1);

        public void Dispose() => gate.Dispose();
    }

    private static AgentWorker CreateWorker(
        RecordingLifecycleCoordinator coordinator,
        TimeSpan? cleanShutdownWriteTimeout = null,
        IAgentJournal? journal = null,
        ILogger<AgentWorker>? logger = null) =>
        new(
            coordinator,
            journal ?? new RecordingJournal(HealthyJournal()),
            ImmediateReadinessTransitionGate.Instance,
            Options.Create(new AgentLifecycleOptions { CleanShutdownWriteTimeout = cleanShutdownWriteTimeout ?? TimeSpan.FromSeconds(1) }),
            logger ?? NullLogger<AgentWorker>.Instance);

    private static AgentJournalHealth HealthyJournal() =>
        new(AgentJournalHealthState.Healthy, SqliteJournalSchema.CurrentVersion, "healthy", "ok");

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(relativeParts));
    }

    private sealed class RecordingLifecycleCoordinator(
        List<string>? operations = null,
        Guid? previousSessionId = null,
        bool journalOwned = false) : IAgentLifecycleCoordinator
    {
        private readonly AgentLifecycleState state = new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ReadyAtUtc: null,
            ReadinessElapsed: null,
            StoppedAtUtc: null,
            PlannedShutdown: false,
            PreviousSessionId: previousSessionId,
            PreviousSessionEndedCleanly: true,
            JournalOwnershipVersion: journalOwned ? AgentLifecycleMarker.CurrentJournalOwnershipVersion : null);

        public bool StopCalled { get; private set; }

        public bool CleanShutdownRecorded { get; private set; }

        public bool DelayStopUntilCancelled { get; init; }

        public Exception? MarkReadyException { get; init; }

        public bool MarkReadyCalled { get; private set; }

        public AgentLifecycleState? CurrentState { get; private set; }

        public Task<AgentLifecycleState> StartAsync(CancellationToken cancellationToken)
        {
            operations?.Add("lifecycle_start");
            CurrentState = state;
            return Task.FromResult(state);
        }

        public Task<AgentLifecycleState> ClaimJournalOwnershipAsync(CancellationToken cancellationToken)
        {
            operations?.Add("lifecycle_journal_owned");
            CurrentState = CurrentState! with { JournalOwnershipVersion = AgentLifecycleMarker.CurrentJournalOwnershipVersion };
            return Task.FromResult(CurrentState);
        }

        public Task<AgentLifecycleState> PrepareReadyAsync(CancellationToken cancellationToken)
        {
            operations?.Add("lifecycle_ready_prepared");
            MarkReadyCalled = true;
            if (MarkReadyException is not null)
            {
                return Task.FromException<AgentLifecycleState>(MarkReadyException);
            }

            return Task.FromResult(CurrentState! with { ReadyAtUtc = state.StartedAtUtc, ReadinessElapsed = TimeSpan.Zero });
        }

        public Task<AgentLifecycleState> PublishReadyAsync(AgentLifecycleState readyState, CancellationToken cancellationToken)
        {
            operations?.Add("lifecycle_ready_published");
            CurrentState = readyState;
            return Task.FromResult(readyState);
        }

        public Task<AgentLifecycleState> MarkNotReadyAsync(CancellationToken cancellationToken)
        {
            AgentLifecycleState state = CurrentState ?? throw new InvalidOperationException("Lifecycle must be started before readiness can be cleared.");
            CurrentState = state with { ReadyAtUtc = null, ReadinessElapsed = null };
            return Task.FromResult(CurrentState);
        }

        public async Task<AgentLifecycleState> StopAsync(CancellationToken cancellationToken)
        {
            operations?.Add("lifecycle_stop");
            StopCalled = true;
            if (DelayStopUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            CleanShutdownRecorded = true;
            CurrentState = CurrentState! with { PlannedShutdown = true, StoppedAtUtc = DateTimeOffset.UtcNow };
            return CurrentState;
        }
    }

    private sealed class ImmediateReadinessTransitionGate : IAgentReadinessTransitionGate
    {
        public static ImmediateReadinessTransitionGate Instance { get; } = new();

        private ImmediateReadinessTransitionGate()
        {
        }

        public Task<T> ExecuteAsync<T>(
            AgentReadinessTransitionKind kind,
            Func<Task<T>> operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return operation();
        }
    }

    private sealed class RecordingJournal(AgentJournalHealth health, List<string>? operations = null) : IAgentJournal
    {
        public TaskCompletionSource Initialized { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<AgentSessionRecord> Sessions { get; } = [];

        public AgentJournalHealth Health { get; private set; } = AgentJournalHealth.NotInitialized;

        public AgentJournalInitializationMode? InitializationMode { get; private set; }

        public Task<AgentJournalHealth> InitializeAsync(
            AgentJournalInitializationMode mode,
            CancellationToken cancellationToken)
        {
            operations?.Add("journal_initialize");
            InitializationMode = mode;
            Health = health;
            Initialized.SetResult();
            return Task.FromResult(health);
        }

        public Task<AgentJournalHealth> VerifyIntegrityAsync(CancellationToken cancellationToken) => Task.FromResult(Health);

        public Task RecordSessionAsync(AgentSessionRecord session, CancellationToken cancellationToken)
        {
            Sessions.Add(session);
            operations?.Add(
                session.PlannedShutdown
                    ? "journal_session_stopped"
                    : session.ReadyAtUtc is not null
                        ? "journal_session_ready"
                        : "journal_session_started");
            return Task.CompletedTask;
        }

        public Task AppendProbeSampleAsync(ProbeSampleWrite sample, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MaterialEventCommit> AppendMaterialEventAsync(MaterialEventWrite materialEvent, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PendingOutboxItem>> ReadPendingOutboxAsync(string agentId, int maximumCount, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingAfterReadyLifecycleStore(IAgentLifecycleStore inner) : IAgentLifecycleStore
    {
        private int writeCount;

        public Task<AgentLifecycleMarker?> ReadLastMarkerAsync(CancellationToken cancellationToken) =>
            inner.ReadLastMarkerAsync(cancellationToken);

        public Task WriteMarkerAsync(AgentLifecycleMarker marker, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref writeCount) > 3)
            {
                throw new IOException("synthetic lifecycle marker failure");
            }

            return inner.WriteMarkerAsync(marker, cancellationToken);
        }
    }

    private sealed class BlockingReadyLifecycleStore(
        IAgentLifecycleStore inner) : IAgentLifecycleStore
    {
        private readonly TaskCompletionSource releaseReadyWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadyWriteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentLifecycleMarker?> ReadLastMarkerAsync(CancellationToken cancellationToken) =>
            inner.ReadLastMarkerAsync(cancellationToken);

        public async Task WriteMarkerAsync(
            AgentLifecycleMarker marker,
            CancellationToken cancellationToken)
        {
            if (marker.ReadyAtUtc is not null && !marker.PlannedShutdown)
            {
                ReadyWriteEntered.TrySetResult();
                await releaseReadyWrite.Task.WaitAsync(cancellationToken);
            }

            await inner.WriteMarkerAsync(marker, cancellationToken);
        }

        public void ReleaseReadyWrite() => releaseReadyWrite.TrySetResult();
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan value) => utcNow = utcNow.Add(value);
    }

    private static void AssertJournalFilesAbsent(string databasePath)
    {
        Assert.False(File.Exists(databasePath));
        Assert.False(File.Exists($"{databasePath}-wal"));
        Assert.False(File.Exists($"{databasePath}-shm"));
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId.Id, exception, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class SignalingLogger<T>(int eventId) : ILogger<T>
    {
        private readonly TaskCompletionSource<LogEntry> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId loggedEventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (loggedEventId.Id == eventId)
            {
                completion.TrySetResult(
                    new LogEntry(logLevel, loggedEventId.Id, exception, formatter(state, exception)));
            }
        }

        public Task<LogEntry> WaitAsync() =>
            completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, int EventId, Exception? Exception, string Message);
}
