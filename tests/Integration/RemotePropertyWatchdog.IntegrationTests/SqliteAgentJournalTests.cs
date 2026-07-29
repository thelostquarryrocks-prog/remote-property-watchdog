using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RemotePropertyWatchdog.Agent.Service;
using RemotePropertyWatchdog.Contracts;

namespace RemotePropertyWatchdog.IntegrationTests;

public sealed class SqliteAgentJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly string SyntheticBasicAuthorizationValue =
        string.Concat("Basic", " ", "synthetic-value");

    public static TheoryData<string> SqliteWildcardConfusionObjectNames { get; } =
        new()
        {
            "sqlitedata",
            "sqliteXdata",
            "sqlite-data",
            "asqlite_data",
            "application_table"
        };

    public static TheoryData<string, string, string, string, string, string> SchemaTamperingCases { get; } =
        new()
        {
            {
                "missing_column",
                "replace",
                "table",
                "agent_sessions",
                "ready_at_utc TEXT NULL,",
                ""
            },
            {
                "incorrect_column",
                "replace",
                "table",
                "agent_sessions",
                "ready_at_utc TEXT NULL",
                "ready_at_epoch INTEGER NULL"
            },
            {
                "incorrect_nullability",
                "replace",
                "table",
                "agent_sessions",
                "ready_at_utc TEXT NULL",
                "ready_at_utc TEXT NOT NULL"
            },
            {
                "incorrect_default",
                "replace",
                "table",
                "agent_sessions",
                "DEFAULT 0 CHECK (planned_shutdown IN (0, 1))",
                "DEFAULT 1 CHECK (planned_shutdown IN (0, 1))"
            },
            {
                "incorrect_primary_key",
                "replace",
                "table",
                "agent_sessions",
                "session_id TEXT NOT NULL PRIMARY KEY",
                "session_id TEXT NOT NULL UNIQUE"
            },
            {
                "missing_strict",
                "replace",
                "table",
                "agent_sessions",
                ") STRICT",
                ")"
            },
            {
                "altered_identifier_bound",
                "replace",
                "table",
                "agent_sessions",
                "CHECK (length(session_id) BETWEEN 1 AND 128)",
                "CHECK (length(session_id) BETWEEN 1 AND 129)"
            },
            {
                "altered_nonnegative_sequence_check",
                "replace",
                "table",
                "local_state",
                "CHECK (last_event_sequence >= 0)",
                "CHECK (last_event_sequence > 0)"
            },
            {
                "altered_positive_sequence_check",
                "replace",
                "table",
                "material_events",
                "CHECK (agent_sequence > 0)",
                "CHECK (agent_sequence >= 0)"
            },
            {
                "altered_json_check",
                "replace",
                "table",
                "outbox",
                "CHECK (json_valid(payload_json))",
                "CHECK (length(payload_json) > 0)"
            },
            {
                "altered_boolean_check",
                "replace",
                "table",
                "agent_sessions",
                "CHECK (planned_shutdown IN (0, 1))",
                "CHECK (planned_shutdown IN (0, 1, 2))"
            },
            {
                "missing_foreign_key",
                "replace",
                "table",
                "outbox",
                "FOREIGN KEY (event_id) REFERENCES material_events(event_id) ON UPDATE RESTRICT ON DELETE RESTRICT",
                "CHECK (event_id IS NOT NULL)"
            },
            {
                "altered_foreign_key_target_and_actions",
                "replace",
                "table",
                "probe_samples",
                "REFERENCES agent_sessions(session_id) ON UPDATE RESTRICT ON DELETE RESTRICT",
                "REFERENCES local_state(agent_id) ON UPDATE CASCADE ON DELETE CASCADE"
            },
            {
                "wrong_index_columns",
                "replace",
                "index",
                "idx_outbox_agent_sequence",
                "ON outbox(agent_id, agent_sequence, outbox_id)",
                "ON outbox(agent_sequence, agent_id, outbox_id)"
            },
            {
                "wrong_index_uniqueness",
                "replace",
                "index",
                "ux_outbox_event",
                "CREATE UNIQUE INDEX ux_outbox_event",
                "CREATE INDEX ux_outbox_event"
            },
            {
                "unexpected_partial_index",
                "replace",
                "index",
                "ux_outbox_event",
                "ON outbox(event_id)",
                "ON outbox(event_id) WHERE event_id <> ''"
            },
            {
                "wrong_index_direction_and_collation",
                "replace",
                "index",
                "idx_agent_sessions_started",
                "ON agent_sessions(started_at_utc, session_id)",
                "ON agent_sessions(started_at_utc COLLATE NOCASE DESC, session_id)"
            },
            {
                "unexpected_application_objects",
                "sql",
                "",
                "",
                "",
                "CREATE TABLE unexpected_evidence(value TEXT) STRICT; CREATE INDEX unexpected_evidence_index ON unexpected_evidence(value);"
            },
            {
                "sqlite_wildcard_confusion_application_object",
                "sql",
                "",
                "",
                "",
                "CREATE TABLE sqlitedata(value TEXT NOT NULL) STRICT; INSERT INTO sqlitedata(value) VALUES ('retained');"
            },
            {
                "incorrect_migration_evidence",
                "sql",
                "",
                "",
                "",
                "UPDATE schema_migrations SET name = 'unexpected-migration' WHERE version = 1;"
            },
            {
                "additional_migration_evidence",
                "sql",
                "",
                "",
                "",
                "INSERT INTO schema_migrations(version, name, applied_at_utc) VALUES (2, 'unexpected-migration', '2026-07-19T12:00:00.0000000+00:00');"
            }
        };

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
    public async Task ExistingUnrelatedVersionZeroDatabaseIsRefusedAndPreserved()
    {
        using TestDatabase database = new();
        string originalJournalMode;
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(
                connection,
                """
                CREATE TABLE unrelated_data(value TEXT NOT NULL);
                CREATE INDEX unrelated_index ON unrelated_data(value);
                CREATE VIEW unrelated_view AS SELECT value FROM unrelated_data;
                CREATE TRIGGER unrelated_trigger
                AFTER UPDATE ON unrelated_data
                BEGIN
                    SELECT 1;
                END;
                INSERT INTO unrelated_data(value) VALUES ('retained');
                """);
            Assert.Equal(0L, await ScalarInt64Async(connection, "PRAGMA user_version;"));
            originalJournalMode = await ScalarStringAsync(connection, "PRAGMA journal_mode;");
        }

        using SqliteAgentJournal journal = CreateJournal(database.Path);
        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("database_not_owned", health.ReasonCode);
        Assert.Equal("not_run", health.IntegrityResult);
        await using SqliteConnection preserved = await database.OpenAsync();
        Assert.Equal(0L, await ScalarInt64Async(preserved, "PRAGMA user_version;"));
        Assert.Equal(originalJournalMode, await ScalarStringAsync(preserved, "PRAGMA journal_mode;"));
        Assert.Equal("retained", await ScalarStringAsync(preserved, "SELECT value FROM unrelated_data;"));
        Assert.Equal(["unrelated_data"], await NamesAsync(preserved, "table"));
        Assert.Equal(["unrelated_index"], await NamesAsync(preserved, "index"));
        Assert.Equal(["unrelated_view"], await NamesAsync(preserved, "view"));
        Assert.Equal(["unrelated_trigger"], await NamesAsync(preserved, "trigger"));
        Assert.False(File.Exists($"{database.Path}-wal"));
        Assert.False(File.Exists($"{database.Path}-shm"));
    }

    [Theory]
    [MemberData(nameof(SqliteWildcardConfusionObjectNames))]
    public async Task VersionZeroSqliteWildcardConfusionObjectIsRefusedWithoutAdoption(
        string objectName)
    {
        using TestDatabase database = new();
        string originalJournalMode;
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(
                connection,
                $"CREATE TABLE [{objectName}](value TEXT NOT NULL); INSERT INTO [{objectName}](value) VALUES ('retained');");
            Assert.Equal(0L, await ScalarInt64Async(connection, "PRAGMA user_version;"));
            originalJournalMode = await ScalarStringAsync(connection, "PRAGMA journal_mode;");
        }

        using SqliteAgentJournal journal = CreateJournal(database.Path);
        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("database_not_owned", health.ReasonCode);
        Assert.Equal("not_run", health.IntegrityResult);
        await using SqliteConnection preserved = await database.OpenAsync();
        Assert.Equal(0L, await ScalarInt64Async(preserved, "PRAGMA user_version;"));
        Assert.Equal(originalJournalMode, await ScalarStringAsync(preserved, "PRAGMA journal_mode;"));
        Assert.Equal("retained", await ScalarStringAsync(preserved, $"SELECT value FROM [{objectName}];"));
        Assert.Equal([objectName], await NamesAsync(preserved, "table"));
        Assert.Empty(await NamesAsync(preserved, "index"));
        Assert.Empty(await NamesAsync(preserved, "view"));
        Assert.Empty(await NamesAsync(preserved, "trigger"));
        Assert.DoesNotContain(SqliteJournalSchema.Tables, table => StringComparer.Ordinal.Equals(table, objectName));
        Assert.False(File.Exists($"{database.Path}-wal"));
        Assert.False(File.Exists($"{database.Path}-shm"));
    }

    [Fact]
    public async Task GenuineSqliteReservedPrefixObjectDoesNotBlockEmptyVersionZeroMigration()
    {
        using TestDatabase database = new();
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(
                connection,
                """
                CREATE TABLE transient_sequence_source(id INTEGER PRIMARY KEY AUTOINCREMENT);
                DROP TABLE transient_sequence_source;
                """);
            Assert.Equal(0L, await ScalarInt64Async(connection, "PRAGMA user_version;"));
            Assert.Equal(
                1L,
                await ScalarInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'sqlite_sequence';"));
            Assert.Empty(await NamesAsync(connection, "table"));
        }

        using SqliteAgentJournal journal = CreateJournal(database.Path);
        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Healthy, health.State);
        Assert.Equal(SqliteJournalSchema.CurrentVersion, health.SchemaVersion);
        await using SqliteConnection migrated = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(migrated, "PRAGMA user_version;"));
        Assert.Equal(SqliteJournalSchema.Tables, await NamesAsync(migrated, "table"));
        Assert.Equal(
            1L,
            await ScalarInt64Async(
                migrated,
                "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'sqlite_sequence';"));
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
    public async Task ExactProbeRetryIsAnIdempotentNoOpAndLaterProbeSucceeds()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        ProbeSampleWrite sample = Probe("sample-exact-retry");

        await journal.AppendProbeSampleAsync(sample, CancellationToken.None);
        await journal.AppendProbeSampleAsync(sample, CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        Assert.Equal(1L, await ReadProbeSampleCountAsync(database));

        await journal.AppendProbeSampleAsync(Probe("sample-after-exact-retry"), CancellationToken.None);
        Assert.Equal(2L, await ReadProbeSampleCountAsync(database));
    }

    [Fact]
    public async Task ProbeRetryWithDifferentContentLeavesOriginalRowUnchanged()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        await journal.RecordSessionAsync(
            new AgentSessionRecord("session-alternate", Now.AddMinutes(-1)),
            CancellationToken.None);
        ProbeSampleWrite original = Probe("sample-content-retry");
        await journal.AppendProbeSampleAsync(original, CancellationToken.None);
        string originalSignature = await ReadProbeSampleSignatureAsync(database, original.SampleId);

        ProbeSampleWrite conflictingRetry = original with
        {
            AgentId = "agent-alternate",
            SessionId = "session-alternate",
            ObservedAtUtc = Now.AddMinutes(1),
            ProbeKind = "icmp",
            ProbeState = "unreachable",
            TargetId = "target-alternate",
            Evidence = ["probe_timeout"]
        };
        await journal.AppendProbeSampleAsync(conflictingRetry, CancellationToken.None);

        Assert.Equal(originalSignature, await ReadProbeSampleSignatureAsync(database, original.SampleId));
        Assert.Equal(1L, await ReadProbeSampleCountAsync(database));
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);

        await journal.AppendProbeSampleAsync(Probe("sample-after-content-retry"), CancellationToken.None);
        Assert.Equal(2L, await ReadProbeSampleCountAsync(database));
    }

    [Fact]
    public async Task ConcurrentProbeRetriesCommitExactlyOneRowWithoutDegrading()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal setup = CreateJournal(database.Path);
        await AddSessionAsync(setup);
        const int writerCount = 12;
        SqliteAgentJournal[] journals = Enumerable.Range(0, writerCount)
            .Select(_ => CreateJournal(database.Path))
            .ToArray();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Task.WhenAll(journals.Select(InitializeExistingAsync));
            Task[] writes = journals
                .Select(journal => Task.Run(async () =>
                {
                    await start.Task;
                    await journal.AppendProbeSampleAsync(Probe("sample-concurrent-retry"), CancellationToken.None);
                }))
                .ToArray();

            start.SetResult();
            await Task.WhenAll(writes);

            Assert.Equal(1L, await ReadProbeSampleCountAsync(database));
            Assert.All(journals, journal => Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State));
            await setup.AppendProbeSampleAsync(Probe("sample-after-concurrent-retry"), CancellationToken.None);
            Assert.Equal(2L, await ReadProbeSampleCountAsync(database));
        }
        finally
        {
            foreach (SqliteAgentJournal journal in journals)
            {
                journal.Dispose();
            }
        }
    }

    [Fact]
    public async Task ProbeForeignKeyFailureStillDegradesAndBlocksLaterWork()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);

        AgentJournalDegradedException error = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendProbeSampleAsync(
                Probe("sample-invalid-session") with { SessionId = "session-missing" },
                CancellationToken.None));

        AssertRuntimeDegraded(journal, error, "runtime_write_failed");
        Assert.Equal(0L, await ReadProbeSampleCountAsync(database));
        Assert.Equal(0L, await ReadForeignKeyViolationCountAsync(database));
        AgentJournalDegradedException blocked = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendProbeSampleAsync(Probe("sample-after-invalid-session"), CancellationToken.None));
        Assert.Equal(error.Health, blocked.Health);
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
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
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
    public async Task SessionPreCommitStorageFailureDegradesRollsBackAndBlocksFurtherWrites()
    {
        using TestDatabase database = new();
        SessionFaultInjector faultInjector = new(
            failOnCall: 1,
            new IOException("synthetic storage failure"));
        using SqliteAgentJournal journal = CreateJournal(database.Path, faultInjector);
        await InitializeFreshAsync(journal);

        AgentJournalDegradedException error = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.RecordSessionAsync(Session(), CancellationToken.None));

        AssertRuntimeDegraded(journal, error, "runtime_write_failed");
        Assert.Equal(1, faultInjector.Calls);
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM agent_sessions;"));
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM material_events;"));
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM outbox;"));
        }

        AgentJournalDegradedException blocked = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.RecordSessionAsync(Session(), CancellationToken.None));
        Assert.Equal(error.Health, blocked.Health);
        Assert.Equal(1, faultInjector.Calls);
    }

    [Fact]
    public async Task SessionPreCommitSyntheticFaultRollsBackWithoutDegrading()
    {
        using TestDatabase database = new();
        SessionFaultInjector faultInjector = new(
            failOnCall: 1,
            new SyntheticJournalFaultException());
        using SqliteAgentJournal journal = CreateJournal(database.Path, faultInjector);
        await InitializeFreshAsync(journal);

        await Assert.ThrowsAsync<SyntheticJournalFaultException>(
            () => journal.RecordSessionAsync(Session(), CancellationToken.None));

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM agent_sessions;"));
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
        }

        await journal.RecordSessionAsync(Session(), CancellationToken.None);
        Assert.Equal(2, faultInjector.Calls);
        Assert.Equal(Session(), await ReadSessionAsync(database));
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

        Assert.Equal(AgentJournalHealthState.Healthy, faulted.Health.State);
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

        Assert.Equal(AgentJournalHealthState.Healthy, faulted.Health.State);
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

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
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

    [Theory]
    [InlineData("probe_samples_to_agent_sessions")]
    [InlineData("material_events_to_agent_sessions")]
    [InlineData("outbox_to_material_events")]
    public async Task InitializationRejectsAndPreservesEveryDeclaredForeignKeyViolation(string relationship)
    {
        using TestDatabase database = new();
        await CreateValidDeleteModeDatabaseAsync(database);
        await InsertForeignKeyViolationAsync(database, relationship);
        string before = await ReadJournalContentSignatureAsync(database);
        Assert.Equal("ok", await ReadIntegrityResultAsync(database));
        Assert.Equal(1L, await ReadForeignKeyViolationCountAsync(database));

        using SqliteAgentJournal journal = CreateJournal(database.Path);
        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("integrity_check_failed", health.ReasonCode);
        Assert.Equal("failed", health.IntegrityResult);
        Assert.InRange(health.ReasonCode.Length, 1, 64);
        await AssertAllRuntimeOperationsBlockedAsync(journal, "integrity_check_failed");
        Assert.Equal(before, await ReadJournalContentSignatureAsync(database));
        Assert.Equal("ok", await ReadIntegrityResultAsync(database));
        Assert.Equal(1L, await ReadForeignKeyViolationCountAsync(database));
        Assert.False(File.Exists($"{database.Path}-wal"));
        Assert.False(File.Exists($"{database.Path}-shm"));
    }

    [Fact]
    public async Task ExplicitVerificationRejectsAndPreservesExistingForeignKeyViolation()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        await journal.AppendMaterialEventAsync(Event("event-valid-before-foreign-key-check"), CancellationToken.None);
        await SetDeleteJournalModeAsync(database);
        await InsertForeignKeyViolationAsync(database, "probe_samples_to_agent_sessions");
        string before = await ReadJournalContentSignatureAsync(database);
        Assert.Equal("ok", await ReadIntegrityResultAsync(database));
        Assert.Equal(1L, await ReadForeignKeyViolationCountAsync(database));

        AgentJournalHealth health = await journal.VerifyIntegrityAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("integrity_check_failed", health.ReasonCode);
        Assert.Equal("failed", health.IntegrityResult);
        Assert.Equal(before, await ReadJournalContentSignatureAsync(database));
        Assert.Equal(1L, await ReadForeignKeyViolationCountAsync(database));
    }

    [Fact]
    public async Task ForeignKeyCheckReadFailureFailsClosedWithoutModifyingJournal()
    {
        using TestDatabase database = new();
        await CreateValidDeleteModeDatabaseAsync(database);
        string before = await ReadJournalContentSignatureAsync(database);
        using SqliteAgentJournal journal = CreateJournal(
            database.Path,
            beforeForeignKeyCheckRead:
                static _ => Task.FromException(new IOException("Synthetic foreign-key check read failure.")));

        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("integrity_check_failed", health.ReasonCode);
        Assert.Equal("failed", health.IntegrityResult);
        Assert.InRange(health.ReasonCode.Length, 1, 64);
        Assert.Equal(before, await ReadJournalContentSignatureAsync(database));
        Assert.Equal(0L, await ReadForeignKeyViolationCountAsync(database));
        Assert.False(File.Exists($"{database.Path}-wal"));
        Assert.False(File.Exists($"{database.Path}-shm"));
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
    public async Task SessionStorageFailureDegradesAndBlocksSubsequentOperations()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await InitializeFreshAsync(journal);
        await ExecuteAgainstDatabaseAsync(database, "DROP TABLE agent_sessions;");

        AgentJournalDegradedException error = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.RecordSessionAsync(Session(), CancellationToken.None));

        AssertRuntimeDegraded(journal, error, "runtime_write_failed");
        AgentJournalDegradedException subsequent = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.ReadPendingOutboxAsync("agent-synthetic", 1, CancellationToken.None));
        Assert.Equal(error.Health, subsequent.Health);
    }

    [Fact]
    public async Task ProbeStorageFailureDegradesJournal()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        await ExecuteAgainstDatabaseAsync(database, "DROP TABLE probe_samples;");

        AgentJournalDegradedException error = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendProbeSampleAsync(Probe("sample-runtime-failure"), CancellationToken.None));

        AssertRuntimeDegraded(journal, error, "runtime_write_failed");
    }

    [Fact]
    public async Task MaterialEventStorageFailureDegradesAndRollsBackEventAndSequence()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        await ExecuteAgainstDatabaseAsync(database, "DROP TABLE outbox;");

        AgentJournalDegradedException error = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendMaterialEventAsync(Event("event-runtime-failure"), CancellationToken.None));

        AssertRuntimeDegraded(journal, error, "runtime_write_failed");
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM material_events;"));
        Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
        AgentJournalDegradedException subsequent = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendMaterialEventAsync(Event("event-after-runtime-failure"), CancellationToken.None));
        Assert.Equal(error.Health, subsequent.Health);
    }

    [Fact]
    public async Task OutboxReadStorageFailureDegradesJournal()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        await ExecuteAgainstDatabaseAsync(database, "DROP TABLE outbox;");

        AgentJournalDegradedException error = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.ReadPendingOutboxAsync("agent-synthetic", 1, CancellationToken.None));

        AssertRuntimeDegraded(journal, error, "runtime_read_failed");
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

    [Theory]
    [MemberData(nameof(SchemaTamperingCases))]
    public async Task InitializationRejectsAndPreservesDefinitionLevelSchemaTampering(
        string caseName,
        string tamperingKind,
        string objectType,
        string objectName,
        string originalFragment,
        string replacement)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        using TestDatabase database = new();
        await CreateValidDeleteModeDatabaseAsync(database);
        await ApplySchemaTamperingAsync(
            database,
            tamperingKind,
            objectType,
            objectName,
            originalFragment,
            replacement);
        string before = await ReadDiagnosticSignatureAsync(database);
        Assert.Equal("ok", await ReadIntegrityResultAsync(database));

        using SqliteAgentJournal journal = CreateJournal(database.Path);
        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("schema_validation_failed", health.ReasonCode);
        Assert.Equal("not_run", health.IntegrityResult);
        Assert.Equal(before, await ReadDiagnosticSignatureAsync(database));
        Assert.Equal("ok", await ReadIntegrityResultAsync(database));
        Assert.False(File.Exists($"{database.Path}-wal"));
        Assert.False(File.Exists($"{database.Path}-shm"));
    }

    [Theory]
    [MemberData(nameof(SchemaTamperingCases))]
    public async Task ExplicitVerificationRejectsAndPreservesDefinitionLevelSchemaTampering(
        string caseName,
        string tamperingKind,
        string objectType,
        string objectName,
        string originalFragment,
        string replacement)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        Assert.Equal(AgentJournalHealthState.Healthy, (await InitializeFreshAsync(journal)).State);
        await SetDeleteJournalModeAsync(database);
        await ApplySchemaTamperingAsync(
            database,
            tamperingKind,
            objectType,
            objectName,
            originalFragment,
            replacement);
        string before = await ReadDiagnosticSignatureAsync(database);
        Assert.Equal("ok", await ReadIntegrityResultAsync(database));

        AgentJournalHealth health = await journal.VerifyIntegrityAsync(CancellationToken.None);

        Assert.Equal(AgentJournalHealthState.Degraded, health.State);
        Assert.Equal("schema_validation_failed", health.ReasonCode);
        Assert.Equal("ok", health.IntegrityResult);
        Assert.Equal(before, await ReadDiagnosticSignatureAsync(database));
        Assert.Equal("ok", await ReadIntegrityResultAsync(database));
    }

    [Fact]
    public async Task IrrelevantSchemaWhitespaceDifferencesRemainValid()
    {
        using TestDatabase database = new();
        await CreateValidDeleteModeDatabaseAsync(database);
        await ApplySchemaTamperingAsync(
            database,
            "replace",
            "table",
            "agent_sessions",
            "CREATE TABLE agent_sessions (",
            "CREATE   TABLE   agent_sessions   (");

        using SqliteAgentJournal journal = CreateJournal(database.Path);
        AgentJournalHealth health = await InitializeExistingAsync(journal);

        Assert.Equal(AgentJournalHealthState.Healthy, health.State);
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
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
    public async Task ReviewedCredentialUrlIsRejectedBeforeEventOutboxOrSequenceMutationAndAbsentFromArtifacts()
    {
        using TestDatabase database = new();
        ListLogger<SqliteAgentJournal> logger = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path, logger: logger);
        await AddSessionAsync(journal);
        const string prohibitedUrl = "https://camera.local/live?token=synthetic-value";
        MaterialEventWrite prohibited = Event("event-credential-url") with { TargetLabel = prohibitedUrl };

        await AssertCredentialValidationRejectedAsync(
            () => journal.AppendMaterialEventAsync(prohibited, CancellationToken.None));
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId is 101 or 104);
        await AssertCountsAsync(database, materialEvents: 0, outbox: 0);
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
        }

        await journal.AppendProbeSampleAsync(Probe("sample-after-credential-rejection"), CancellationToken.None);
        MaterialEventCommit next = await journal.AppendMaterialEventAsync(
            Event("event-after-credential-rejection"),
            CancellationToken.None);
        Assert.Equal(1, next.AgentSequence);
        Assert.Single(await journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));
        Assert.Equal(1L, await ReadProbeSampleCountAsync(database));
        Assert.Equal(AgentJournalHealthState.Healthy, (await journal.VerifyIntegrityAsync(CancellationToken.None)).State);
        await CheckpointAsync(database);

        byte[] urlMarker = Encoding.UTF8.GetBytes(prohibitedUrl);
        byte[] valueMarker = Encoding.UTF8.GetBytes("synthetic-value");
        foreach (string path in Directory.EnumerateFiles(database.DirectoryPath, "agent.db*"))
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.False(ContainsSequence(bytes, urlMarker), $"Credential-bearing URL was persisted in {Path.GetFileName(path)}.");
            Assert.False(ContainsSequence(bytes, valueMarker), $"Synthetic credential value was persisted in {Path.GetFileName(path)}.");
        }
    }

    [Fact]
    public async Task ReviewedBareCredentialAssignmentsAreRejectedBeforeEventOutboxOrSequenceMutationAndAbsentFromArtifacts()
    {
        using TestDatabase database = new();
        ListLogger<SqliteAgentJournal> logger = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path, logger: logger);
        await AddSessionAsync(journal);
        string[] prohibitedAssignments =
        [
            "api_key=synthetic-value",
            "token=synthetic-value"
        ];

        for (int index = 0; index < prohibitedAssignments.Length; index++)
        {
            MaterialEventWrite prohibited = Event($"event-bare-credential-{index:D2}") with
            {
                TargetLabel = prohibitedAssignments[index]
            };
            await AssertCredentialValidationRejectedAsync(
                () => journal.AppendMaterialEventAsync(prohibited, CancellationToken.None));
        }

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId is 101 or 104);
        Assert.All(
            prohibitedAssignments,
            assignment => Assert.DoesNotContain(
                logger.Entries,
                entry => entry.Message.Contains(assignment, StringComparison.Ordinal)));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("synthetic-value", StringComparison.Ordinal));
        await AssertCountsAsync(database, materialEvents: 0, outbox: 0);
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
        }

        await journal.AppendProbeSampleAsync(Probe("sample-after-bare-credential-rejection"), CancellationToken.None);
        MaterialEventCommit next = await journal.AppendMaterialEventAsync(
            Event("event-after-bare-credential-rejection"),
            CancellationToken.None);
        Assert.Equal(1, next.AgentSequence);
        Assert.Single(await journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));
        Assert.Equal(1L, await ReadProbeSampleCountAsync(database));
        Assert.Equal(AgentJournalHealthState.Healthy, (await journal.VerifyIntegrityAsync(CancellationToken.None)).State);
        await CheckpointAsync(database);

        byte[][] prohibitedMarkers =
        [
            .. prohibitedAssignments.Select(Encoding.UTF8.GetBytes),
            Encoding.UTF8.GetBytes("synthetic-value")
        ];
        foreach (string path in Directory.EnumerateFiles(database.DirectoryPath, "agent.db*"))
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.All(
                prohibitedMarkers,
                marker => Assert.False(
                    ContainsSequence(bytes, marker),
                    $"Bare credential assignment content was persisted in {Path.GetFileName(path)}."));
        }
    }

    [Fact]
    public async Task ReviewedColonCredentialAssignmentsAreRejectedBeforeEventOutboxOrSequenceMutationAndAbsentFromArtifacts()
    {
        using TestDatabase database = new();
        ListLogger<SqliteAgentJournal> logger = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path, logger: logger);
        await AddSessionAsync(journal);
        string[] prohibitedAssignments =
        [
            "api_key: synthetic-value",
            "token: synthetic-value",
            "\"api_key\": \"synthetic-value\"",
            "{\"token\":\"synthetic-value\"}",
            $"authorization={SyntheticBasicAuthorizationValue}",
            $"authorization: {SyntheticBasicAuthorizationValue}",
            $"\"authorization\":\"{SyntheticBasicAuthorizationValue}\"",
            $"{{\"authorization\":\"{SyntheticBasicAuthorizationValue}\"}}",
            """{\"api_key\":\"synthetic-value\"}""",
            BuildEscapedJsonAssignment("authorization", SyntheticBasicAuthorizationValue, 1),
            """{\\\"token\\\":\\\"synthetic-value\\\"}""",
            BuildEscapedJsonAssignment("authorization", SyntheticBasicAuthorizationValue, 3)
        ];

        for (int index = 0; index < prohibitedAssignments.Length; index++)
        {
            await AssertCredentialValidationRejectedAsync(
                () => journal.AppendMaterialEventAsync(
                    Event($"event-colon-credential-{index:D2}") with
                    {
                        TargetLabel = prohibitedAssignments[index]
                    },
                    CancellationToken.None));
        }

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId is 101 or 104);
        Assert.All(
            prohibitedAssignments,
            assignment => Assert.DoesNotContain(
                logger.Entries,
                entry => entry.Message.Contains(assignment, StringComparison.Ordinal)));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("synthetic-value", StringComparison.Ordinal));
        await AssertCountsAsync(database, materialEvents: 0, outbox: 0);
        Assert.Equal(0L, await ReadProbeSampleCountAsync(database));
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
        }

        await journal.AppendProbeSampleAsync(Probe("sample-after-colon-credential-rejection"), CancellationToken.None);
        MaterialEventCommit next = await journal.AppendMaterialEventAsync(
            Event("event-after-colon-credential-rejection"),
            CancellationToken.None);
        Assert.Equal(1, next.AgentSequence);
        Assert.Single(await journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));
        Assert.Equal(1L, await ReadProbeSampleCountAsync(database));
        Assert.Equal(AgentJournalHealthState.Healthy, (await journal.VerifyIntegrityAsync(CancellationToken.None)).State);
        await CheckpointAsync(database);

        byte[][] prohibitedMarkers =
        [
            .. prohibitedAssignments.Select(Encoding.UTF8.GetBytes),
            Encoding.UTF8.GetBytes("synthetic-value")
        ];
        foreach (string path in Directory.EnumerateFiles(database.DirectoryPath, "agent.db*"))
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.All(
                prohibitedMarkers,
                marker => Assert.False(
                    ContainsSequence(bytes, marker),
                    $"Colon credential assignment content was persisted in {Path.GetFileName(path)}."));
        }
    }

    [Fact]
    public async Task SharedSensitiveKeyPolicyRejectsUrlParametersAndEqualsOrColonAssignments()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        string[] sensitiveKeys =
        [
            "access_token",
            "api_key",
            "api_token",
            "apikey",
            "auth_token",
            "authorization",
            "client_secret",
            "connection-string-password",
            "connection_string_password",
            "credential",
            "credentials",
            "enrollment-token",
            "enrollment_token",
            "passwd",
            "password",
            "provider-secret",
            "provider_secret",
            "pwd",
            "secret",
            "sig",
            "signature",
            "token",
            "x-amz-credential",
            "x-amz-security-token",
            "x-amz-signature",
            "x-goog-api-key",
            "x-goog-credential",
            "x-goog-signature"
        ];
        Assert.Equal(28, sensitiveKeys.Length);
        Assert.Equal(28, sensitiveKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        string onceEscapedQuote = "\\\"";
        string twiceEscapedQuote = $"{new string('\\', 3)}\"";
        List<string> prohibitedValues = sensitiveKeys
            .SelectMany(
                key => new[]
                {
                    $"https://camera.local/live?mode=preview&{key}=synthetic-value",
                    $"status=up; {key}=synthetic-value",
                    $"status: up; {key}: synthetic-value",
                    $"{{\"{key}\":\"synthetic-value\"}}",
                    $"{{{onceEscapedQuote}{key}{onceEscapedQuote}:{onceEscapedQuote}synthetic-value{onceEscapedQuote}}}",
                    $"{{{twiceEscapedQuote}{key}{twiceEscapedQuote}:{twiceEscapedQuote}synthetic-value{twiceEscapedQuote}}}",
                    $"{onceEscapedQuote}{key}{onceEscapedQuote}=synthetic-value",
                    $"{twiceEscapedQuote}{key}{twiceEscapedQuote}:synthetic-value"
                })
            .ToList();
        prohibitedValues.AddRange(
        [
            "https://camera.local/live?ToKeN=synthetic-value",
            "status=up; ToKeN=synthetic-value",
            "status: up; ToKeN: synthetic-value",
            "https://camera.local/live?mode=preview&quality=high&API_KEY=synthetic-value",
            "https://camera.local/live#access_token=synthetic-value",
            "https://camera.local/live#route?client_secret=synthetic-value",
            "https://camera.local/live?%74oken=synthetic-value",
            "https://camera.local/live?%2574oken=synthetic-value",
            "https://camera.local/live?mode=preview%26api_key%3Dsynthetic-value",
            "https://camera.local/live?mode=preview%2526api_key%253Dsynthetic-value",
            "https://camera.local/live?token=synthetic-value%ZZ",
            $"AuThOrIzAtIoN={SyntheticBasicAuthorizationValue}",
            $"\"AuThOrIzAtIoN\":\"{SyntheticBasicAuthorizationValue}\"",
            "%7B%5C%22authorization%5C%22%3A%5C%22Basic%20synthetic-value%5C%22%7D",
            "%257B%255C%2522authorization%255C%2522%253A%255C%2522Basic%2520synthetic-value%255C%2522%257D"
        ]);

        for (int index = 0; index < prohibitedValues.Count; index++)
        {
            MaterialEventWrite prohibited = Event($"event-credential-form-{index:D2}") with
            {
                TargetLabel = prohibitedValues[index]
            };
            await AssertCredentialValidationRejectedAsync(
                () => journal.AppendMaterialEventAsync(prohibited, CancellationToken.None));
        }

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        await AssertCountsAsync(database, materialEvents: 0, outbox: 0);
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
    }

    [Fact]
    public async Task AssignmentGrammarHandlesDelimitersQuotesPlacementWhitespaceEncodingMalformedInputAndBenignBoundaries()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        string[] prohibitedValues =
        [
            "token=synthetic-value",
            "api_key \t= synthetic-value",
            "mode=preview; token=synthetic-value",
            "api_key=synthetic-value; mode=preview",
            "mode=preview, token=synthetic-value, quality=high",
            "mode=preview&api_key=synthetic-value",
            "%74oken=synthetic-value",
            "%2574oken=synthetic-value",
            "token%3Dsynthetic-value",
            "%74oken%253Dsynthetic-value",
            "mode=preview; to%6ben=synthetic-value",
            "mode=preview; token=synthetic-value%ZZ",
            "api_key: synthetic-value",
            "token:synthetic-value",
            "api_key \t: synthetic-value",
            "\"api_key\": \"synthetic-value\"",
            "\"api_key\" \t: \"synthetic-value\"",
            "{\"token\":\"synthetic-value\"}",
            "'token': synthetic-value",
            "prefix text, api_key: synthetic-value",
            "token:",
            "token:\u2603",
            "\"api_key\":",
            "mode: up; token: synthetic-value",
            "token: synthetic-value; mode: up",
            "{\"mode\":\"up\",\"api_key\":\"synthetic-value\",\"quality\":\"high\"}",
            "ToKeN: synthetic-value",
            "%61pi_key: synthetic-value",
            "%2561pi_key: synthetic-value",
            "api_key%3Asynthetic-value",
            "api_key%253Asynthetic-value",
            "api_%6bey%3Asynthetic-value",
            "%61pi_key%253Asynthetic-value",
            "api_key: synthetic-value%ZZ",
            """{\"api_key\":\"synthetic-value\"}""",
            """{\\\"token\\\":\\\"synthetic-value\\\"}""",
            """{\"token\"=synthetic-value}""",
            """{\\\"api_key\\\"=synthetic-value}""",
            """{"api_key\":\"synthetic-value\"}""",
            """{\"api_key":"synthetic-value"}""",
            """{\"mode\":\"up\",\"api_key\":\"synthetic-value\",\"quality\":\"high\"}""",
            """{\\\"mode\\\":\\\"up\\\",\\\"token\\\":\\\"synthetic-value\\\"}""",
            """{\\"api_key\\":\\"synthetic-value\\"}""",
            "%7B%5C%22api_key%5C%22%3A%5C%22synthetic-value%5C%22%7D",
            "%257B%255C%2522token%255C%2522%253A%255C%2522synthetic-value%255C%2522%257D",
            $"authorization={SyntheticBasicAuthorizationValue}",
            $"authorization: {SyntheticBasicAuthorizationValue}",
            $"\"authorization\":\"{SyntheticBasicAuthorizationValue}\"",
            $"{{\"authorization\":\"{SyntheticBasicAuthorizationValue}\"}}",
            BuildEscapedJsonAssignment("authorization", SyntheticBasicAuthorizationValue, 1),
            BuildEscapedJsonAssignment("AuThOrIzAtIoN", SyntheticBasicAuthorizationValue, 3)
        ];

        for (int index = 0; index < prohibitedValues.Length; index++)
        {
            await AssertCredentialValidationRejectedAsync(
                () => journal.AppendMaterialEventAsync(
                    Event($"event-bare-grammar-rejected-{index:D2}") with
                    {
                        TargetLabel = prohibitedValues[index]
                    },
                    CancellationToken.None));
        }

        await AssertCountsAsync(database, materialEvents: 0, outbox: 0);
        await using (SqliteConnection connection = await database.OpenAsync())
        {
            Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
        }

        string[] benignValues =
        [
            "tokenizer=enabled",
            "mytoken=enabled",
            "prefix-token=enabled",
            "token_backup=enabled",
            "access_token_backup=enabled",
            "api_keyring=enabled",
            "mode=preview; note=synthetic-value",
            "mode=preview; token%ZZ=synthetic-value",
            "%252574oken=synthetic-value",
            "tokenizer: enabled",
            "mytoken: enabled",
            "prefix-token: enabled",
            "token_backup: enabled",
            "benign_key: value",
            "ordinary prose: monitoring remains available",
            "{\"mode\":\"token\",\"note\":\"api_key\"}",
            "https://camera.local:8443/live?mode=preview#status",
            "2026-07-27T06:32:33Z",
            "host.local:8443",
            "urn:synthetic:status",
            "status: token",
            "note: api_key",
            "secretary: present",
            "api_key%ZZ: synthetic-value",
            "%252561pi_key: synthetic-value",
            "api_key%25253Asynthetic-value",
            """{\"mode\":\"preview\",\"note\":\"api_key\"}""",
            """ordinary escaped prose says \"token\" without an assignment""",
            "\\\"token\\\"",
            """{\"note\":\"authorization\"}""",
            """{\"token_backup\":\"enabled\"}""",
            """{\"tokenizer\":\"enabled\"}""",
            """{\"prefix-token\":\"enabled\"}""",
            """{\\\"note\":\"value\"}""",
            "%25257B%25255C%252522api_key%25255C%252522%25253Asynthetic-value%25257D"
        ];
        for (int index = 0; index < benignValues.Length; index++)
        {
            MaterialEventCommit commit = await journal.AppendMaterialEventAsync(
                Event($"event-bare-grammar-benign-{index:D2}") with
                {
                    TargetLabel = benignValues[index]
                },
                CancellationToken.None);
            Assert.Equal(index + 1, commit.AgentSequence);
        }

        await AssertCountsAsync(database, materialEvents: benignValues.Length, outbox: benignValues.Length);
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
    }

    [Fact]
    public async Task CredentialUrlValidationCoversEveryPersistedStringSurface()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        const string prohibitedUrl = "https://x/?token=v";
        ProbeSampleWrite probe = Probe("sample-surface");
        MaterialEventWrite materialEvent = Event("event-surface");
        Func<Task>[] prohibitedWrites =
        [
            () => journal.RecordSessionAsync(
                new AgentSessionRecord(prohibitedUrl, Now),
                CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { SampleId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { AgentId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { SessionId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { ProbeKind = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { ProbeState = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { TargetId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { Evidence = [prohibitedUrl] }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { EventId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { AgentId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { PropertyId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { SessionId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { TargetId = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { TargetLabel = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { FromState = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { ToState = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { Evidence = [prohibitedUrl] }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { GatewayState = prohibitedUrl }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { InternetState = prohibitedUrl }, CancellationToken.None)
        ];

        foreach (Func<Task> prohibitedWrite in prohibitedWrites)
        {
            await AssertCredentialValidationRejectedAsync(prohibitedWrite);
        }

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        await AssertCountsAsync(database, materialEvents: 0, outbox: 0);
        Assert.Equal(0L, await ReadProbeSampleCountAsync(database));
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM agent_sessions;"));
        Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
    }

    [Theory]
    [InlineData("token=synthetic-value", "equals")]
    [InlineData("token: synthetic-value", "colon")]
    [InlineData("{\\\"token\\\":\\\"v\\\"}", "escaped-json")]
    public async Task CredentialAssignmentValidationCoversEveryPersistedStringSurface(
        string prohibitedAssignment,
        string delimiterName)
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        ProbeSampleWrite probe = Probe($"sample-{delimiterName}-surface");
        MaterialEventWrite materialEvent = Event($"event-{delimiterName}-surface");
        Func<Task>[] prohibitedWrites =
        [
            () => journal.RecordSessionAsync(
                new AgentSessionRecord(prohibitedAssignment, Now),
                CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { SampleId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { AgentId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { SessionId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { ProbeKind = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { ProbeState = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { TargetId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendProbeSampleAsync(probe with { Evidence = [prohibitedAssignment] }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { EventId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { AgentId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { PropertyId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { SessionId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { TargetId = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { TargetLabel = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { FromState = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { ToState = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { Evidence = [prohibitedAssignment] }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { GatewayState = prohibitedAssignment }, CancellationToken.None),
            () => journal.AppendMaterialEventAsync(materialEvent with { InternetState = prohibitedAssignment }, CancellationToken.None)
        ];

        foreach (Func<Task> prohibitedWrite in prohibitedWrites)
        {
            await AssertCredentialValidationRejectedAsync(prohibitedWrite);
        }

        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        await AssertCountsAsync(database, materialEvents: 0, outbox: 0);
        Assert.Equal(0L, await ReadProbeSampleCountAsync(database));
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM agent_sessions;"));
        Assert.Equal(0L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM local_state;"));
    }

    [Fact]
    public async Task ExistingSecretRejectionsRemainAndBenignUrlsAndSubstringBoundariesStayPermitted()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal journal = CreateJournal(database.Path);
        await AddSessionAsync(journal);
        string[] prohibitedValues =
        [
            "authorization: synthetic-value",
            "bearer synthetic-value",
            "camera://synthetic-value",
            "connection-string-password=local-development-only",
            "enrollment_token=synthetic-value",
            "enrollment-token=synthetic-value",
            "password=local-development-only",
            "password:synthetic-value",
            "provider-secret=synthetic-value",
            "pwd=synthetic-value",
            "rtsp://camera.test/live",
            "https://synthetic-user:synthetic-value@camera.local/live"
        ];

        for (int index = 0; index < prohibitedValues.Length; index++)
        {
            await AssertCredentialValidationRejectedAsync(
                () => journal.AppendMaterialEventAsync(
                    Event($"event-existing-secret-{index:D2}") with { TargetLabel = prohibitedValues[index] },
                    CancellationToken.None));
        }

        MaterialEventCommit benign = await journal.AppendMaterialEventAsync(
            Event("event-benign-url") with
            {
                TargetLabel = "https://camera.local/live?mode=tokenized&signature_algorithm=synthetic#status",
                FromState = "tokenizer-ready",
                ToState = "credentialed-mode",
                Evidence = ["authentication_mode_known", "secretary_contact_absent"]
            },
            CancellationToken.None);
        MaterialEventCommit malformedBenign = await journal.AppendMaterialEventAsync(
            Event("event-benign-malformed-url") with
            {
                TargetLabel = "https://camera.local/live?mode=synthetic%ZZ"
            },
            CancellationToken.None);
        MaterialEventCommit boundedEncoding = await journal.AppendMaterialEventAsync(
            Event("event-benign-triple-encoded-url") with
            {
                TargetLabel = "https://camera.local/live?%252574oken=synthetic-value"
            },
            CancellationToken.None);

        Assert.Equal(1, benign.AgentSequence);
        Assert.Equal(2, malformedBenign.AgentSequence);
        Assert.Equal(3, boundedEncoding.AgentSequence);
        await AssertCountsAsync(database, materialEvents: 3, outbox: 3);
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
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
            () => journal.AppendProbeSampleAsync(Probe("sample-cancelled"), canceled.Token));
        Assert.Equal(0L, await ReadProbeSampleCountAsync(database));
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => journal.ReadPendingOutboxAsync("agent-synthetic", 1, canceled.Token));
        Assert.Equal(AgentJournalHealthState.Healthy, journal.Health.State);
    }

    private static SqliteAgentJournal CreateJournal(
        string path,
        IAgentJournalFaultInjector? faultInjector = null,
        Func<CancellationToken, Task>? beforeForeignKeyCheckRead = null,
        ILogger<SqliteAgentJournal>? logger = null) =>
        new(
            Options.Create(new AgentJournalOptions { DatabasePath = path, BusyTimeout = TimeSpan.FromSeconds(15) }),
            new FixedTimeProvider(Now),
            faultInjector ?? NoOpAgentJournalFaultInjector.Instance,
            NoOpAgentJournalDegradationHandler.Instance,
            ImmediateReadinessTransitionGate.Instance,
            beforeForeignKeyCheckRead ?? (static _ => Task.CompletedTask),
            logger ?? NullLogger<SqliteAgentJournal>.Instance);

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

    private static ProbeSampleWrite Probe(string sampleId) =>
        new(
            sampleId,
            "agent-synthetic",
            "session-synthetic",
            Now,
            "tcp",
            "reachable",
            "target-synthetic",
            ["probe_succeeded"]);

    private static void AssertRuntimeDegraded(
        SqliteAgentJournal journal,
        AgentJournalDegradedException error,
        string reasonCode)
    {
        Assert.Equal(AgentJournalHealthState.Degraded, error.Health.State);
        Assert.Equal(reasonCode, error.Health.ReasonCode);
        Assert.Equal("not_run", error.Health.IntegrityResult);
        Assert.Equal(error.Health, journal.Health);
        Assert.InRange(error.Health.ReasonCode.Length, 1, 64);
    }

    private static async Task AssertCredentialValidationRejectedAsync(Func<Task> operation)
    {
        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(operation);
        Assert.Equal("values", error.ParamName);
        Assert.Contains(
            "Persistence input contains a prohibited secret-bearing value.",
            error.Message,
            StringComparison.Ordinal);
    }

    private static string BuildEscapedJsonAssignment(
        string key,
        string value,
        int wrapperBackslashCount)
    {
        string quoteWrapper = $"{new string('\\', wrapperBackslashCount)}\"";
        return $"{{{quoteWrapper}{key}{quoteWrapper}:{quoteWrapper}{value}{quoteWrapper}}}";
    }

    private static async Task CheckpointAsync(TestDatabase database)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    private static async Task ExecuteAgainstDatabaseAsync(TestDatabase database, string sql)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private static async Task InsertForeignKeyViolationAsync(TestDatabase database, string relationship)
    {
        string sql = relationship switch
        {
            "probe_samples_to_agent_sessions" => """
                INSERT INTO probe_samples(
                    sample_id, agent_id, session_id, observed_at_utc, probe_kind, probe_state, target_id, evidence_json)
                VALUES (
                    'sample-orphan', 'agent-synthetic', 'session-missing', '2026-07-19T12:00:00.0000000+00:00',
                    'tcp', 'unreachable', 'target-synthetic', '["probe_timeout"]');
                """,
            "material_events_to_agent_sessions" => """
                INSERT INTO material_events(
                    event_id, schema_version, agent_id, property_id, agent_sequence, session_id,
                    observed_at_utc, event_type, target_id, target_label, from_state, to_state,
                    evidence_json, gateway_state, internet_state, created_at_utc)
                VALUES (
                    'event-orphan', 'v1', 'agent-synthetic', 'property-synthetic', 1, 'session-missing',
                    '2026-07-19T12:00:00.0000000+00:00', 'target_state_changed', 'target-synthetic',
                    'Synthetic target', 'up', 'down', '["probe_timeout"]', 'up', 'up',
                    '2026-07-19T12:00:00.0000000+00:00');
                """,
            "outbox_to_material_events" => """
                INSERT INTO outbox(
                    outbox_id, event_id, agent_id, agent_sequence, payload_json, created_at_utc)
                VALUES (
                    'outbox-orphan', 'event-missing', 'agent-synthetic', 1, '{}',
                    '2026-07-19T12:00:00.0000000+00:00');
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(relationship))
        };

        await using SqliteConnection connection = await database.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;");
        await ExecuteAsync(connection, sql);
    }

    private static async Task AssertAllRuntimeOperationsBlockedAsync(
        SqliteAgentJournal journal,
        string reasonCode)
    {
        AgentJournalDegradedException session = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.RecordSessionAsync(Session(), CancellationToken.None));
        AgentJournalDegradedException probe = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendProbeSampleAsync(Probe("sample-blocked"), CancellationToken.None));
        AgentJournalDegradedException materialEvent = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.AppendMaterialEventAsync(Event("event-blocked"), CancellationToken.None));
        AgentJournalDegradedException outbox = await Assert.ThrowsAsync<AgentJournalDegradedException>(
            () => journal.ReadPendingOutboxAsync("agent-synthetic", 10, CancellationToken.None));

        Assert.All(
            new[] { session, probe, materialEvent, outbox },
            failure => Assert.Equal(reasonCode, failure.Health.ReasonCode));
    }

    private static async Task CreateValidDeleteModeDatabaseAsync(TestDatabase database)
    {
        using (SqliteAgentJournal journal = CreateJournal(database.Path))
        {
            Assert.Equal(AgentJournalHealthState.Healthy, (await InitializeFreshAsync(journal)).State);
        }

        await SetDeleteJournalModeAsync(database);
        Assert.False(File.Exists($"{database.Path}-wal"));
        Assert.False(File.Exists($"{database.Path}-shm"));
    }

    private static async Task SetDeleteJournalModeAsync(TestDatabase database)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
        Assert.Equal("delete", await ScalarStringAsync(connection, "PRAGMA journal_mode = DELETE;"));
    }

    private static async Task ApplySchemaTamperingAsync(
        TestDatabase database,
        string tamperingKind,
        string objectType,
        string objectName,
        string originalFragment,
        string replacement)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        if (StringComparer.Ordinal.Equals(tamperingKind, "sql"))
        {
            await ExecuteAsync(connection, replacement);
            return;
        }

        Assert.Equal("replace", tamperingKind);
        await ExecuteAsync(connection, "PRAGMA writable_schema = ON;");
        try
        {
            await using SqliteCommand update = connection.CreateCommand();
            update.CommandText = """
                UPDATE sqlite_schema
                SET sql = replace(sql, $original_fragment, $replacement)
                WHERE type = $object_type
                  AND name = $object_name
                  AND instr(sql, $original_fragment) > 0;
                """;
            update.Parameters.AddWithValue("$original_fragment", originalFragment);
            update.Parameters.AddWithValue("$replacement", replacement);
            update.Parameters.AddWithValue("$object_type", objectType);
            update.Parameters.AddWithValue("$object_name", objectName);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }
        finally
        {
            await ExecuteAsync(connection, "PRAGMA writable_schema = OFF;");
        }

        long schemaVersion = await ScalarInt64Async(connection, "PRAGMA schema_version;");
        await ExecuteAsync(connection, $"PRAGMA schema_version = {schemaVersion + 1};");
    }

    private static async Task<string> ReadIntegrityResultAsync(TestDatabase database)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        return await ScalarStringAsync(connection, "PRAGMA integrity_check;");
    }

    private static async Task<long> ReadForeignKeyViolationCountAsync(TestDatabase database)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        return await ScalarInt64Async(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;");
    }

    private static async Task<string> ReadJournalContentSignatureAsync(TestDatabase database)
    {
        List<string> parts = [await ReadDiagnosticSignatureAsync(database)];
        await using SqliteConnection connection = await database.OpenAsync();
        foreach (string table in SqliteJournalSchema.Tables)
        {
            parts.Add($"{table}={await ScalarInt64Async(connection, $"SELECT COUNT(*) FROM {table};")}");
        }

        parts.Add($"foreign_key_violations={await ScalarInt64Async(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;")}");
        return string.Join("\u001f", parts);
    }

    private static async Task<string> ReadDiagnosticSignatureAsync(TestDatabase database)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        List<string> parts =
        [
            $"user_version={await ScalarInt64Async(connection, "PRAGMA user_version;")}",
            $"journal_mode={await ScalarStringAsync(connection, "PRAGMA journal_mode;")}"
        ];

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT type, name, COALESCE(sql, '<null>')
                FROM sqlite_schema
                WHERE name NOT GLOB 'sqlite_*'
                ORDER BY type, name;
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                parts.Add($"object={reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}");
            }
        }

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT version, name, applied_at_utc FROM schema_migrations ORDER BY version, name;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                parts.Add($"migration={reader.GetInt64(0)}|{reader.GetString(1)}|{reader.GetString(2)}");
            }
        }

        return string.Join("\u001f", parts);
    }

    private static async Task AssertCountsAsync(TestDatabase database, int materialEvents, int outbox)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(materialEvents, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM material_events;"));
        Assert.Equal(outbox, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM outbox;"));
    }

    private static async Task<long> ReadProbeSampleCountAsync(TestDatabase database)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        return await ScalarInt64Async(connection, "SELECT COUNT(*) FROM probe_samples;");
    }

    private static async Task<string> ReadProbeSampleSignatureAsync(TestDatabase database, string sampleId)
    {
        await using SqliteConnection connection = await database.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT agent_id, session_id, observed_at_utc, probe_kind, probe_state, target_id, evidence_json
            FROM probe_samples
            WHERE sample_id = $sample_id;
            """;
        command.Parameters.AddWithValue("$sample_id", sampleId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        string signature = string.Join(
            "\u001f",
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? "<null>" : reader.GetString(5),
            reader.GetString(6));
        Assert.False(await reader.ReadAsync());
        return signature;
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
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = $type AND name NOT GLOB 'sqlite_*' ORDER BY name;";
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

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId.Id, exception, formatter(state, exception)));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, int EventId, Exception? Exception, string Message);

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

    private sealed class ThrowingFaultInjector(bool beforeCommit = false, bool afterCommit = false) : IAgentJournalFaultInjector
    {
        public Task BeforeSessionCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterSessionCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BeforeMaterialEventCommitAsync(CancellationToken cancellationToken) =>
            beforeCommit ? Task.FromException(new SyntheticJournalFaultException()) : Task.CompletedTask;

        public Task AfterMaterialEventCommitAsync(CancellationToken cancellationToken) =>
            afterCommit ? Task.FromException(new SyntheticJournalFaultException()) : Task.CompletedTask;
    }

    private sealed class SyntheticJournalFaultException : Exception;
}
