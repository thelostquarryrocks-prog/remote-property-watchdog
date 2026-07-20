using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RemotePropertyWatchdog.Agent.Service;
using RemotePropertyWatchdog.Contracts;

namespace RemotePropertyWatchdog.IntegrationTests;

public sealed class SqliteAgentJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreshDatabaseMigratesToExpectedSchemaAndCrashSafetySettings()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);

        AgentJournalHealth health = await InitializeFreshAsync(journal);

        Assert.Equal(AgentJournalHealthState.Healthy, health.State);
        Assert.Equal(SqliteJournalSchema.CurrentVersion, health.SchemaVersion);
        Assert.Equal("ok", health.IntegrityResult);
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(connection, "PRAGMA user_version;"));
        Assert.Equal("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(2L, await ScalarInt64Async(connection, "PRAGMA synchronous;"));
        Assert.Equal(SqliteJournalSchema.Tables, await NamesAsync(connection, "table"));
        Assert.Equal(SqliteJournalSchema.Indexes, await NamesAsync(connection, "index"));
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM schema_migrations WHERE version = 1;"));
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM pragma_foreign_key_list('outbox') WHERE \"table\" = 'material_events';"));
    }

    [Fact]
    public async Task InitializationIsIdempotentAcrossInstances()
    {
        using TestDatabase database = new();
        using (SqliteAgentJournal first = CreateJournal(database.Path))
        {
            AgentJournalHealth once = await InitializeFreshAsync(first);
            AgentJournalHealth twice = await InitializeFreshAsync(first);
            Assert.Equal(once, twice);
        }

        using (SqliteAgentJournal restarted = CreateJournal(database.Path))
        {
            Assert.Equal(AgentJournalHealthState.Healthy, (await InitializeExistingAsync(restarted)).State);
        }

        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM schema_migrations;"));
    }

    [Fact]
    public async Task ConcurrentFreshInitializationAppliesMigrationExactlyOnce()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal first = CreateJournal(database.Path);
        using SqliteAgentJournal second = CreateJournal(database.Path);
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AgentJournalHealth>[] initializations = new[] { first, second }
            .Select(journal => Task.Run(async () =>
            {
                await start.Task;
                return await InitializeFreshAsync(journal);
            }))
            .ToArray();
        start.SetResult();

        AgentJournalHealth[] results = await Task.WhenAll(initializations);

        Assert.All(results, result =>
        {
            Assert.Equal(AgentJournalHealthState.Healthy, result.State);
            Assert.Equal(SqliteJournalSchema.CurrentVersion, result.SchemaVersion);
        });
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(connection, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM schema_migrations WHERE version = 1;"));
    }

    [Fact]
    public async Task VersionZeroDatabaseUpgradesAndFutureVersionIsNotModified()
    {
        using TestDatabase versionZero = new();
        await using (SqliteConnection connection = await versionZero.OpenAsync())
        {
            Assert.Equal(0L, await ScalarInt64Async(connection, "PRAGMA user_version;"));
        }

        using (SqliteAgentJournal journal = CreateJournal(versionZero.Path))
        {
            Assert.Equal(1, (await InitializeExistingAsync(journal)).SchemaVersion);
        }

        using TestDatabase future = new();
        await using (SqliteConnection connection = await future.OpenAsync())
        {
            await ExecuteAsync(connection, "PRAGMA user_version = 2;");
        }

        using (SqliteAgentJournal journal = CreateJournal(future.Path))
        {
            AgentJournalHealth health = await InitializeExistingAsync(journal);
            Assert.Equal(AgentJournalHealthState.Degraded, health.State);
            Assert.Equal("schema_too_new", health.ReasonCode);
        }

        await using SqliteConnection futureConnection = await future.OpenAsync();
        Assert.Equal(2L, await ScalarInt64Async(futureConnection, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarInt64Async(futureConnection, "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'material_events';"));
    }

    [Fact]
    public async Task MaterialEventAndOutboxCommitTogether()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);

        MaterialEventCommit commit = await journal.AppendMaterialEventAsync(Event("event-atomic"), CancellationToken.None);

        Assert.Equal(1, commit.AgentSequence);
        await AssertCountsAsync(database, materialEvents: 1, outbox: 1);
        PendingOutboxItem item = Assert.Single(await journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));
        MaterialEventDto payload = Assert.IsType<MaterialEventDto>(ContractJson.Deserialize<MaterialEventDto>(item.PayloadJson));
        Assert.Equal(commit.AgentSequence, payload.EventSequence);
        Assert.Equal(commit.EventId, payload.MessageId);
    }

    [Fact]
    public async Task SessionExactAndStaleRetriesCannotRegressStoredMilestones()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        AgentSessionRecord completed = Session(
            readyAtUtc: Now.AddMinutes(1),
            endedAtUtc: Now.AddMinutes(2),
            plannedShutdown: true);
        await journal.RecordSessionAsync(completed, CancellationToken.None);

        await journal.RecordSessionAsync(completed, CancellationToken.None);
        await journal.RecordSessionAsync(Session(), CancellationToken.None);

        Assert.Equal(completed, await ReadSessionAsync(database));
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM agent_sessions;"));
    }

    [Fact]
    public async Task SessionMilestonesProgressForwardMonotonically()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        await journal.RecordSessionAsync(Session(), CancellationToken.None);
        await journal.RecordSessionAsync(Session(readyAtUtc: Now.AddMinutes(1)), CancellationToken.None);
        AgentSessionRecord completed = Session(
            readyAtUtc: Now.AddMinutes(1),
            endedAtUtc: Now.AddMinutes(2),
            plannedShutdown: true);

        await journal.RecordSessionAsync(completed, CancellationToken.None);

        Assert.Equal(completed, await ReadSessionAsync(database));
    }

    [Fact]
    public async Task SessionConflictingStartAndMilestonesAreRejectedWithoutRewritingHistory()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        AgentSessionRecord stored = Session(
            readyAtUtc: Now.AddMinutes(1),
            endedAtUtc: Now.AddMinutes(2),
            plannedShutdown: true);
        await journal.RecordSessionAsync(stored, CancellationToken.None);

        AgentSessionConflictException start = await Assert.ThrowsAsync<AgentSessionConflictException>(
            () => journal.RecordSessionAsync(stored with { StartedAtUtc = Now.AddSeconds(1) }, CancellationToken.None));
        AgentSessionConflictException ready = await Assert.ThrowsAsync<AgentSessionConflictException>(
            () => journal.RecordSessionAsync(stored with { ReadyAtUtc = Now.AddSeconds(30) }, CancellationToken.None));
        AgentSessionConflictException ended = await Assert.ThrowsAsync<AgentSessionConflictException>(
            () => journal.RecordSessionAsync(stored with { EndedAtUtc = Now.AddMinutes(3) }, CancellationToken.None));

        Assert.Equal("immutable_start_conflict", start.ReasonCode);
        Assert.Equal("ready_timestamp_conflict", ready.ReasonCode);
        Assert.Equal("ended_timestamp_conflict", ended.ReasonCode);
        Assert.Equal(stored, await ReadSessionAsync(database));
    }

    [Fact]
    public async Task SessionInvalidChronologyIsRejectedBeforePersistence()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);

        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.RecordSessionAsync(Session(readyAtUtc: Now.AddSeconds(-1)), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.RecordSessionAsync(Session(endedAtUtc: Now.AddSeconds(-1)), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => journal.RecordSessionAsync(
                Session(readyAtUtc: Now.AddMinutes(2), endedAtUtc: Now.AddMinutes(1)),
                CancellationToken.None));

        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public async Task FailureBeforeCommitRollsBackEventOutboxAndSequence()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal setup = CreateJournal(database.Path);
        await AddSessionAsync(setup);
        using SqliteAgentJournal faulted = CreateJournal(database.Path, new ThrowingFaultInjector(beforeCommit: true));
        await InitializeExistingAsync(faulted);

        await Assert.ThrowsAsync<SyntheticJournalFaultException>(
            () => faulted.AppendMaterialEventAsync(Event("event-before-fault"), CancellationToken.None));

        await AssertCountsAsync(database, materialEvents: 0, outbox: 0);
        MaterialEventCommit next = await setup.AppendMaterialEventAsync(Event("event-after-rollback"), CancellationToken.None);
        Assert.Equal(1, next.AgentSequence);
    }

    [Fact]
    public async Task FailureAfterCommitLeavesBothRowsDurable()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal setup = CreateJournal(database.Path);
        await AddSessionAsync(setup);
        using SqliteAgentJournal faulted = CreateJournal(database.Path, new ThrowingFaultInjector(afterCommit: true));
        await InitializeExistingAsync(faulted);

        await Assert.ThrowsAsync<SyntheticJournalFaultException>(
            () => faulted.AppendMaterialEventAsync(Event("event-after-fault"), CancellationToken.None));

        await AssertCountsAsync(database, materialEvents: 1, outbox: 1);
        Assert.Single(await setup.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));
    }

    [Fact]
    public async Task RestartContinuesWithNextCommittedSequence()
    {
        using TestDatabase database = new();
        using (SqliteAgentJournal first = CreateJournal(database.Path))
        {
            await AddSessionAsync(first);
            Assert.Equal(1, (await first.AppendMaterialEventAsync(Event("event-one"), CancellationToken.None)).AgentSequence);
        }

        using SqliteAgentJournal restarted = CreateJournal(database.Path);
        Assert.Equal(AgentJournalHealthState.Healthy, (await InitializeExistingAsync(restarted)).State);
        Assert.Equal(2, (await restarted.AppendMaterialEventAsync(Event("event-two"), CancellationToken.None)).AgentSequence);
    }

    [Fact]
    public async Task DuplicateEventIdIsRejectedWithoutPartialWriteOrSequenceGap()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        Assert.Equal(1, (await journal.AppendMaterialEventAsync(Event("event-duplicate"), CancellationToken.None)).AgentSequence);

        await Assert.ThrowsAsync<DuplicateEventIdException>(
            () => journal.AppendMaterialEventAsync(Event("event-duplicate"), CancellationToken.None));

        await AssertCountsAsync(database, materialEvents: 1, outbox: 1);
        Assert.Equal(2, (await journal.AppendMaterialEventAsync(Event("event-next"), CancellationToken.None)).AgentSequence);
    }

    [Fact]
    public async Task OutboxReadUsesStableAgentSequenceOrder()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        await journal.AppendMaterialEventAsync(Event("event-c-000"), CancellationToken.None);
        await journal.AppendMaterialEventAsync(Event("event-a-000"), CancellationToken.None);
        await journal.AppendMaterialEventAsync(Event("event-b-000"), CancellationToken.None);

        IReadOnlyList<PendingOutboxItem> first = await journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None);
        IReadOnlyList<PendingOutboxItem> second = await journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None);

        Assert.Equal([1L, 2L, 3L], first.Select(item => item.AgentSequence));
        Assert.Equal(first, second);
        Assert.Equal(["event-c-000", "event-a-000", "event-b-000"], first.Select(item => item.EventId));
    }

    [Fact]
    public async Task ConcurrentWritersAllocateUniqueContiguousSequences()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal setup = CreateJournal(database.Path);
        await AddSessionAsync(setup);
        const int writerCount = 20;
        SqliteAgentJournal[] journals = Enumerable.Range(0, writerCount).Select(_ => CreateJournal(database.Path)).ToArray();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Task.WhenAll(journals.Select(InitializeExistingAsync));
            Task<MaterialEventCommit>[] writes = Enumerable.Range(0, writerCount)
                .Select(index => Task.Run(async () =>
                {
                    await start.Task;
                    return await journals[index].AppendMaterialEventAsync(Event($"event-concurrent-{index:D2}"), CancellationToken.None);
                }))
                .ToArray();
            start.SetResult();

            MaterialEventCommit[] commits = await Task.WhenAll(writes);

            Assert.Equal(Enumerable.Range(1, writerCount).Select(value => (long)value), commits.Select(commit => commit.AgentSequence).Order());
            Assert.Equal(writerCount, commits.Select(commit => commit.AgentSequence).Distinct().Count());
            await AssertCountsAsync(database, writerCount, writerCount);
        }
        finally
        {
            foreach (SqliteAgentJournal journal in journals)
            {
                journal.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("plain-text-not-a-database")]
    [InlineData("SQLite format 3\0truncated")]
    public async Task InvalidDatabaseEntersVisibleDegradedStateWithoutReplacement(string content)
    {
        using TestDatabase database = new();
        byte[] original = Encoding.UTF8.GetBytes(content);
        await File.WriteAllBytesAsync(database.Path, original);
        using SqliteAgentJournal journal = CreateJournal(database.Path);

        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.NotEqual("healthy", health.ReasonCode);
        Assert.Equal(health, journal.Health);
        Assert.Equal(original, await File.ReadAllBytesAsync(database.Path));
        await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.ReadPendingOutboxAsync("agent-synthetic", 1, CancellationToken.None));
    }

    [Fact]
    public async Task HealthyDatabasePassesExplicitIntegrityCheck()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        await journal.AppendMaterialEventAsync(Event("event-integrity"), CancellationToken.None);

        AgentJournalHealth health = await journal.VerifyIntegrityAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Healthy, health.State);
        Assert.Equal("ok", health.IntegrityResult);
    }

    [Fact]
    public async Task DeletedInitializedDatabaseDegradesWithoutRecreation()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        Assert.Equal(AgentJournalHealthState.Healthy, (await InitializeFreshAsync(journal)).State);
        File.Delete(database.Path);

        AgentJournalHealth health = await journal.VerifyIntegrityAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("database_missing", health.ReasonCode);
        Assert.Equal("not_run", health.IntegrityResult);
        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public async Task PriorLifecycleInitializationWithMissingDatabaseDegradesWithoutCreatingArtifacts()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);

        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("database_missing", health.ReasonCode);
        Assert.Equal("not_run", health.IntegrityResult);
        AssertJournalFilesAbsent(database.Path);
    }

    [Fact]
    public async Task VerificationAndOperationsOnUninitializedMissingJournalDoNotCreateArtifacts()
    {
        using TestDatabase verificationDatabase = new();
        using SqliteAgentJournal verificationJournal = CreateJournal(verificationDatabase.Path);
        AgentJournalDegradedException verificationError = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => verificationJournal.VerifyIntegrityAsync(CancellationToken.None));
        Assert.Equal("not_initialized", verificationError.Health.ReasonCode);
        AssertJournalFilesAbsent(verificationDatabase.Path);

        using TestDatabase operationalDatabase = new();
        using SqliteAgentJournal operationalJournal = CreateJournal(operationalDatabase.Path);
        AgentJournalDegradedException operationalError = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => operationalJournal.ReadPendingOutboxAsync("agent-synthetic", 1, CancellationToken.None));
        Assert.Equal("not_initialized", operationalError.Health.ReasonCode);
        AssertJournalFilesAbsent(operationalDatabase.Path);
    }

    [Fact]
    public async Task OperationalAccessDoesNotRecreateDeletedInitializedDatabase()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        File.Delete(database.Path);

        AgentJournalDegradedException error = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.ReadPendingOutboxAsync("agent-synthetic", 1, CancellationToken.None));

        Assert.Equal("database_missing", error.Health.ReasonCode);
        Assert.Equal(AgentJournalHealthState.Degraded, journal.Health.State);
        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public async Task EmptyReplacementDatabaseDegradesWithoutMigration()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        File.Delete(database.Path);
        await using (SqliteConnection replacement = await database.OpenAsync())
        {
            Assert.Equal("ok", await ScalarStringAsync(replacement, "PRAGMA integrity_check;"));
        }

        AgentJournalHealth health = await journal.VerifyIntegrityAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("schema_validation_failed", health.ReasonCode);
        Assert.Equal("ok", health.IntegrityResult);
        await using SqliteConnection preserved = await database.OpenAsync();
        Assert.Equal(0L, await ScalarInt64Async(preserved, "PRAGMA user_version;"));
        Assert.Empty(await NamesAsync(preserved, "table"));
    }

    [Fact]
    public async Task DroppedRequiredTableDegradesEvenWhenIntegrityCheckIsOk()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(connection, "DROP TABLE probe_samples;");
            Assert.Equal("ok", await ScalarStringAsync(connection, "PRAGMA integrity_check;"));
        }

        AgentJournalHealth health = await journal.VerifyIntegrityAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("schema_validation_failed", health.ReasonCode);
        Assert.Equal("ok", health.IntegrityResult);
    }

    [Fact]
    public async Task MissingRequiredIndexDegradesEvenWhenIntegrityCheckIsOk()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(connection, "DROP INDEX idx_outbox_agent_sequence;");
            Assert.Equal("ok", await ScalarStringAsync(connection, "PRAGMA integrity_check;"));
        }

        AgentJournalHealth health = await journal.VerifyIntegrityAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("schema_validation_failed", health.ReasonCode);
        Assert.Equal("ok", health.IntegrityResult);
    }

    [Fact]
    public async Task MissingMigrationRecordDegradesEvenWhenIntegrityCheckIsOk()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM schema_migrations WHERE version = 1;");
            Assert.Equal("ok", await ScalarStringAsync(connection, "PRAGMA integrity_check;"));
        }

        AgentJournalHealth health = await journal.VerifyIntegrityAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("schema_validation_failed", health.ReasonCode);
        Assert.Equal("ok", health.IntegrityResult);
    }

    [Fact]
    public async Task FirstProbeAndMaterialEventReferencePreRegistrationLocalSession()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        await journal.RecordSessionAsync(Session(), CancellationToken.None);

        await journal.AppendProbeSampleAsync(
            new ProbeSampleWrite(
                "sample-first",
                "agent-contract-synthetic",
                "session-synthetic",
                Now,
                "tcp",
                "reachable",
                "target-synthetic",
                ["probe_succeeded"]),
            CancellationToken.None);
        MaterialEventCommit committed = await journal.AppendMaterialEventAsync(
            Event("event-first") with { AgentId = "agent-contract-synthetic" },
            CancellationToken.None);

        Assert.Equal(1, committed.AgentSequence);
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM probe_samples WHERE session_id = 'session-synthetic';"));
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM material_events WHERE session_id = 'session-synthetic';"));
        Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(
            1L,
            await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM pragma_foreign_key_list('probe_samples') WHERE [table] = 'agent_sessions' AND [from] = 'session_id' AND [to] = 'session_id';"));
        Assert.Equal(
            1L,
            await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM pragma_foreign_key_list('material_events') WHERE [table] = 'agent_sessions' AND [from] = 'session_id' AND [to] = 'session_id';"));
    }

    [Fact]
    public async Task SecretBearingInputIsRejectedAndAbsentFromDatabaseArtifacts()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        await journal.AppendMaterialEventAsync(Event("event-safe"), CancellationToken.None);
        const string syntheticSecret = "password=local-development-only";
        MaterialEventWrite prohibited = Event("event-secret") with { TargetLabel = syntheticSecret };

        await Assert.ThrowsAsync<ArgumentException>(() => journal.AppendMaterialEventAsync(prohibited, CancellationToken.None));
        await journal.VerifyIntegrityAsync(CancellationToken.None);

        await AssertCountsAsync(database, materialEvents: 1, outbox: 1);
        byte[] marker = Encoding.UTF8.GetBytes(syntheticSecret);
        foreach (string path in Directory.EnumerateFiles(database.DirectoryPath, "agent.db*"))
        {
            Assert.False(ContainsSequence(await File.ReadAllBytesAsync(path), marker), $"Secret marker was persisted in {Path.GetFileName(path)}.");
        }
    }

    [Fact]
    public async Task PreCanceledOperationsPropagateCancellationWithoutDegradingHealth()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => journal.InitializeAsync(AgentJournalInitializationMode.CreateIfMissingForFreshLifecycle, canceled.Token));
        Assert.Equal(AgentJournalHealthState.NotInitialized, journal.Health.State);
        await InitializeFreshAsync(journal);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => journal.ReadPendingOutboxAsync("agent-synthetic", 1, canceled.Token));
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
    }

    private static SqliteAgentJournal CreateJournal(string path, IAgentJournalFaultInjector? faultInjector = null) =>
        new(
            Options.Create(new AgentJournalOptions { DatabasePath = path, BusyTimeout = TimeSpan.FromSeconds(15) }),
            new FixedTimeProvider(Now),
            faultInjector ?? NoOpAgentJournalFaultInjector.Instance,
            NullLogger<SqliteAgentJournal>.Instance);

    private static async Task AddSessionAsync(SqliteAgentJournal journal)
    {
        await InitializeFreshAsync(journal);
        await journal.RecordSessionAsync(
            new AgentSessionRecord("session-synthetic", Now),
            CancellationToken.None);
    }

    private static Task<AgentJournalHealth> InitializeFreshAsync(SqliteAgentJournal journal) =>
        journal.InitializeAsync(
            AgentJournalInitializationMode.CreateIfMissingForFreshLifecycle,
            CancellationToken.None);

    private static Task<AgentJournalHealth> InitializeExistingAsync(SqliteAgentJournal journal) =>
        journal.InitializeAsync(
            AgentJournalInitializationMode.RequireExistingForPriorLifecycle,
            CancellationToken.None);

    private static void AssertJournalFilesAbsent(string databasePath)
    {
        Assert.False(File.Exists(databasePath));
        Assert.False(File.Exists($"{databasePath}-wal"));
        Assert.False(File.Exists($"{databasePath}-shm"));
    }

    private static AgentSessionRecord Session(
        DateTimeOffset? readyAtUtc = null,
        DateTimeOffset? endedAtUtc = null,
        bool plannedShutdown = false) =>
        new("session-synthetic", Now, readyAtUtc, endedAtUtc, plannedShutdown);

    private static MaterialEventWrite Event(string eventId) =>
        new(
            eventId,
            "agent-synthetic",
            "property-synthetic",
            "session-synthetic",
            Now,
            MaterialEventType.TargetStateChanged,
            "target-synthetic",
            "Synthetic target",
            "up",
            "down",
            ["probe_timeout"],
            "up",
            "up");

    private static async Task AssertCountsAsync(TestDatabase database, int materialEvents, int outbox)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(materialEvents, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM material_events;"));
        Assert.Equal(outbox, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM outbox;"));
    }

    private static async Task<AgentSessionRecord> ReadSessionAsync(TestDatabase database)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, started_at_utc, ready_at_utc, ended_at_utc, planned_shutdown
            FROM agent_sessions
            WHERE session_id = 'session-synthetic';
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new AgentSessionRecord(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetInt64(4) != 0);
    }

    private static async Task<IReadOnlyList<string>> NamesAsync(SqliteConnection connection, string type)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = $type AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        command.Parameters.AddWithValue("$type", type);
        List<string> names = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql) =>
        Convert.ToInt64(await ScalarAsync(connection, sql), CultureInfo.InvariantCulture);

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql) =>
        Convert.ToString(await ScalarAsync(connection, sql), CultureInfo.InvariantCulture)!;

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static bool ContainsSequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value) => source.IndexOf(value) >= 0;

    private sealed class TestDatabase : IDisposable
    {
        public TestDatabase()
        {
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rpw-agent-journal-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Path = System.IO.Path.Combine(DirectoryPath, "agent.db");
        }

        public string DirectoryPath { get; }

        public string Path { get; }

        public async Task<SqliteConnection> OpenAsync()
        {
            SqliteConnection connection = new($"Data Source={Path};Pooling=False");
            await connection.OpenAsync();
            return connection;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class ThrowingFaultInjector(bool beforeCommit = false, bool afterCommit = false) : IAgentJournalFaultInjector
    {
        public Task BeforeMaterialEventCommitAsync(CancellationToken cancellationToken) =>
            beforeCommit ? Task.FromException(new SyntheticJournalFaultException()) : Task.CompletedTask;

        public Task AfterMaterialEventCommitAsync(CancellationToken cancellationToken) =>
            afterCommit ? Task.FromException(new SyntheticJournalFaultException()) : Task.CompletedTask;
    }

    private sealed class SyntheticJournalFaultException : Exception;
}
