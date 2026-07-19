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

        AgentJournalHealth health = await journal.InitializeAsync(CancellationToken.None);

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
            AgentJournalHealth once = await first.InitializeAsync(CancellationToken.None);
            AgentJournalHealth twice = await first.InitializeAsync(CancellationToken.None);
            Assert.Equal(once, twice);
        }

        using (SqliteAgentJournal restarted = CreateJournal(database.Path))
        {
            Assert.Equal(AgentJournalHealthState.Healthy, (await restarted.InitializeAsync(CancellationToken.None)).State);
        }

        await using SqliteConnection connection = await database.OpenAsync();
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM schema_migrations;"));
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
            Assert.Equal(1, (await journal.InitializeAsync(CancellationToken.None)).SchemaVersion);
        }

        using TestDatabase future = new();
        await using (SqliteConnection connection = await future.OpenAsync())
        {
            await ExecuteAsync(connection, "PRAGMA user_version = 2;");
        }

        using (SqliteAgentJournal journal = CreateJournal(future.Path))
        {
            AgentJournalHealth health = await journal.InitializeAsync(CancellationToken.None);
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
    public async Task FailureBeforeCommitRollsBackEventOutboxAndSequence()
    {
        using TestDatabase database = new();
        using SqliteAgentJournal setup = CreateJournal(database.Path);
        await AddSessionAsync(setup);
        using SqliteAgentJournal faulted = CreateJournal(database.Path, new ThrowingFaultInjector(beforeCommit: true));

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

        AgentJournalHealth health = await journal.InitializeAsync(CancellationToken.None);

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.InitializeAsync(canceled.Token));
        Assert.Equal(AgentJournalHealthState.NotInitialized, journal.Health.State);
        await journal.InitializeAsync(CancellationToken.None);
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

    private static async Task AddSessionAsync(SqliteAgentJournal journal) =>
        await journal.RecordSessionAsync(
            new AgentSessionRecord("session-synthetic", "agent-synthetic", Now),
            CancellationToken.None);

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
