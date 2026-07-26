using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using RemotePropertyWatchdog.Contracts;

namespace RemotePropertyWatchdog.Agent.Service;

public sealed partial class SqliteAgentJournal : IAgentJournal, IDisposable
{
    private static readonly string[] ProhibitedSecretMarkers =
    [
        "authorization:",
        "bearer ",
        "camera://",
        "connection-string-password",
        "enrollment_token",
        "enrollment-token",
        "password=",
        "password:",
        "provider-secret",
        "pwd=",
        "rtsp://"
    ];

    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly SemaphoreSlim runtimeOperationGate = new(1, 1);
    private readonly AgentJournalOptions options;
    private readonly TimeProvider timeProvider;
    private readonly IAgentJournalFaultInjector faultInjector;
    private readonly IAgentJournalDegradationHandler degradationHandler;
    private readonly ILogger<SqliteAgentJournal> logger;
    private readonly string databasePath;
    private AgentJournalHealth health = AgentJournalHealth.NotInitialized;

    public SqliteAgentJournal(
        IOptions<AgentJournalOptions> options,
        TimeProvider timeProvider,
        IAgentJournalFaultInjector faultInjector,
        IAgentJournalDegradationHandler degradationHandler,
        ILogger<SqliteAgentJournal> logger)
    {
        this.options = options.Value;
        this.timeProvider = timeProvider;
        this.faultInjector = faultInjector;
        this.degradationHandler = degradationHandler;
        this.logger = logger;
        databasePath = ResolveDatabasePath(this.options.DatabasePath);
    }

    public AgentJournalHealth Health => Volatile.Read(ref health);

    public async Task<AgentJournalHealth> InitializeAsync(
        AgentJournalInitializationMode mode,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (Health.State is not AgentJournalHealthState.NotInitialized)
            {
                return Health;
            }

            try
            {
                bool databaseExists = File.Exists(databasePath);
                if (!databaseExists && mode == AgentJournalInitializationMode.RequireExistingForPriorLifecycle)
                {
                    return await SetDegradedAsync(null, "database_missing", "not_run", sqliteErrorCode: null);
                }

                await using SqliteConnection connection = await OpenConnectionAsync(
                    allowCreate: !databaseExists,
                    cancellationToken);
                int schemaVersion = await MigrateToCurrentVersionAsync(connection, cancellationToken);

                bool schemaIsValid = await ValidateSchemaAsync(connection, cancellationToken);
                if (!schemaIsValid)
                {
                    return await SetDegradedAsync(schemaVersion, "schema_validation_failed", "not_run", sqliteErrorCode: null);
                }

                string integrityResult = await RunIntegrityCheckAsync(connection, cancellationToken);
                if (!StringComparer.Ordinal.Equals(integrityResult, "ok"))
                {
                    return await SetDegradedAsync(schemaVersion, "integrity_check_failed", "failed", sqliteErrorCode: null);
                }

                await EnableWalAsync(connection, cancellationToken);
                AgentJournalHealth initialized = new(AgentJournalHealthState.Healthy, schemaVersion, "healthy", "ok");
                Volatile.Write(ref health, initialized);
                LogInitialized(logger, schemaVersion, "wal", "full", "ok");
                return initialized;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (UnsupportedAgentJournalSchemaException ex)
            {
                return await SetDegradedAsync(ex.ActualVersion, "schema_too_new", "not_run", sqliteErrorCode: null);
            }
            catch (UnownedAgentJournalException)
            {
                return await SetDegradedAsync(0, "database_not_owned", "not_run", sqliteErrorCode: null);
            }
            catch (SqliteException ex)
            {
                string reason = !File.Exists(databasePath)
                    ? "database_missing"
                    : ex.SqliteErrorCode == 26
                        ? "database_invalid"
                        : "initialization_failed";
                return await SetDegradedAsync(null, reason, "failed", ex.SqliteErrorCode);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                string reason = !File.Exists(databasePath) ? "database_missing" : "initialization_failed";
                return await SetDegradedAsync(null, reason, "failed", sqliteErrorCode: null);
            }
        }
        finally
        {
            initializationGate.Release();
        }
    }

    public async Task<AgentJournalHealth> VerifyIntegrityAsync(CancellationToken cancellationToken)
    {
        await runtimeOperationGate.WaitAsync(cancellationToken);
        try
        {
            AgentJournalHealth current = await EnsureHealthyAsync(cancellationToken);
            if (!File.Exists(databasePath))
            {
                return await SetDegradedAsync(current.SchemaVersion, "database_missing", "not_run", sqliteErrorCode: null);
            }

            JournalVerificationStage stage = JournalVerificationStage.Opening;
            try
            {
                await using SqliteConnection connection = await OpenConnectionAsync(
                    allowCreate: false,
                    cancellationToken);
                stage = JournalVerificationStage.Integrity;
                string result = await RunIntegrityCheckAsync(connection, cancellationToken);
                if (!StringComparer.Ordinal.Equals(result, "ok"))
                {
                    return await SetDegradedAsync(current.SchemaVersion, "integrity_check_failed", "failed", sqliteErrorCode: null);
                }

                stage = JournalVerificationStage.Schema;
                int schemaVersion = await ReadSchemaVersionAsync(connection, transaction: null, cancellationToken);
                bool schemaIsValid =
                    schemaVersion == SqliteJournalSchema.CurrentVersion &&
                    await ValidateSchemaAsync(connection, cancellationToken);
                if (!schemaIsValid)
                {
                    return await SetDegradedAsync(schemaVersion, "schema_validation_failed", "ok", sqliteErrorCode: null);
                }

                AgentJournalHealth verified = new(
                    AgentJournalHealthState.Healthy,
                    schemaVersion,
                    "healthy",
                    "ok");
                Volatile.Write(ref health, verified);
                LogIntegrityVerified(logger, schemaVersion, "ok");
                return verified;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SqliteException ex)
            {
                string reasonCode = !File.Exists(databasePath)
                    ? "database_missing"
                    : stage switch
                    {
                        JournalVerificationStage.Opening => "database_open_failed",
                        JournalVerificationStage.Integrity => "integrity_check_failed",
                        _ => "schema_validation_failed"
                    };
                string integrityResult = stage == JournalVerificationStage.Schema ? "ok" : "failed";
                return await SetDegradedAsync(current.SchemaVersion, reasonCode, integrityResult, ex.SqliteErrorCode);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                string reasonCode = !File.Exists(databasePath)
                    ? "database_missing"
                    : stage switch
                    {
                        JournalVerificationStage.Opening => "database_open_failed",
                        JournalVerificationStage.Integrity => "integrity_check_failed",
                        _ => "schema_validation_failed"
                    };
                string integrityResult = stage == JournalVerificationStage.Schema ? "ok" : "failed";
                return await SetDegradedAsync(current.SchemaVersion, reasonCode, integrityResult, sqliteErrorCode: null);
            }
        }
        finally
        {
            runtimeOperationGate.Release();
        }
    }

    public async Task RecordSessionAsync(AgentSessionRecord session, CancellationToken cancellationToken)
    {
        ValidateSession(session);
        await ExecuteRuntimeOperationAsync(
            "runtime_write_failed",
            async () =>
            {
                await EnsureHealthyAsync(cancellationToken);
                await using SqliteConnection connection = await OpenExistingOperationalConnectionAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
                AgentSessionRecord? stored = await ReadSessionAsync(connection, transaction, session.SessionId, cancellationToken);
                if (stored is null)
                {
                    await InsertSessionAsync(connection, transaction, session, cancellationToken);
                }
                else
                {
                    AgentSessionRecord merged = MergeSession(stored, session);
                    if (merged != stored)
                    {
                        await UpdateSessionAsync(connection, transaction, merged, cancellationToken);
                    }
                }

                await faultInjector.BeforeSessionCommitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.CommitAsync(cancellationToken);
                await faultInjector.AfterSessionCommitAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task AppendProbeSampleAsync(ProbeSampleWrite sample, CancellationToken cancellationToken)
    {
        ValidateProbeSample(sample);
        await ExecuteRuntimeOperationAsync(
            "runtime_write_failed",
            async () =>
            {
                await EnsureHealthyAsync(cancellationToken);
                await using SqliteConnection connection = await OpenExistingOperationalConnectionAsync(cancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO probe_samples (
                        sample_id, agent_id, session_id, observed_at_utc, probe_kind, probe_state, target_id, evidence_json)
                    VALUES (
                        $sample_id, $agent_id, $session_id, $observed_at_utc, $probe_kind, $probe_state, $target_id, $evidence_json);
                    """;
                command.Parameters.AddWithValue("$sample_id", sample.SampleId);
                command.Parameters.AddWithValue("$agent_id", sample.AgentId);
                command.Parameters.AddWithValue("$session_id", sample.SessionId);
                command.Parameters.AddWithValue("$observed_at_utc", ToUtcText(sample.ObservedAtUtc));
                command.Parameters.AddWithValue("$probe_kind", sample.ProbeKind);
                command.Parameters.AddWithValue("$probe_state", sample.ProbeState);
                command.Parameters.AddWithValue("$target_id", DbValue(sample.TargetId));
                command.Parameters.AddWithValue("$evidence_json", JsonSerializer.Serialize(sample.Evidence, ContractJson.Options));
                await command.ExecuteNonQueryAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task<MaterialEventCommit> AppendMaterialEventAsync(
        MaterialEventWrite materialEvent,
        CancellationToken cancellationToken)
    {
        ValidateMaterialEvent(materialEvent);
        return await ExecuteRuntimeOperationAsync(
            "runtime_write_failed",
            async () =>
            {
                await EnsureHealthyAsync(cancellationToken);
                await using SqliteConnection connection = await OpenExistingOperationalConnectionAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

                long sequence = await AllocateSequenceAsync(connection, transaction, materialEvent.AgentId, cancellationToken);
                DateTimeOffset createdAtUtc = timeProvider.GetUtcNow();
                MaterialEventDto dto = ToContract(materialEvent, sequence);
                string payloadJson = ContractJson.Serialize(dto);

                bool inserted = await InsertMaterialEventAsync(connection, transaction, materialEvent, dto, createdAtUtc, cancellationToken);
                if (!inserted)
                {
                    throw new DuplicateEventIdException(materialEvent.EventId);
                }

                await InsertOutboxAsync(connection, transaction, materialEvent, sequence, payloadJson, createdAtUtc, cancellationToken);
                await faultInjector.BeforeMaterialEventCommitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.CommitAsync(cancellationToken);
                LogMaterialEventCommitted(logger, sequence);
                await faultInjector.AfterMaterialEventCommitAsync(cancellationToken);

                return new MaterialEventCommit(materialEvent.EventId, materialEvent.EventId, sequence);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<PendingOutboxItem>> ReadPendingOutboxAsync(
        string agentId,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(nameof(agentId), agentId);
        if (maximumCount is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount), "Outbox read count must be between 1 and 500.");
        }

        return await ExecuteRuntimeOperationAsync(
            "runtime_read_failed",
            async () =>
            {
                await EnsureHealthyAsync(cancellationToken);
                await using SqliteConnection connection = await OpenExistingOperationalConnectionAsync(cancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT outbox_id, event_id, agent_id, agent_sequence, payload_json, created_at_utc
                    FROM outbox
                    WHERE agent_id = $agent_id
                    ORDER BY agent_sequence ASC, outbox_id ASC
                    LIMIT $maximum_count;
                    """;
                command.Parameters.AddWithValue("$agent_id", agentId);
                command.Parameters.AddWithValue("$maximum_count", maximumCount);

                List<PendingOutboxItem> items = [];
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(new PendingOutboxItem(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt64(3),
                        reader.GetString(4),
                        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
                }

                return (IReadOnlyList<PendingOutboxItem>)items;
            },
            cancellationToken);
    }

    public void Dispose()
    {
        initializationGate.Dispose();
        runtimeOperationGate.Dispose();
    }

    private async Task<T> ExecuteRuntimeOperationAsync<T>(
        string failureReasonCode,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await runtimeOperationGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                return await operation();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AgentJournalDegradedException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is DbException or IOException or UnauthorizedAccessException or FormatException)
            {
                string reasonCode = !File.Exists(databasePath) ? "database_missing" : failureReasonCode;
                int? sqliteErrorCode = ex is SqliteException sqliteException ? sqliteException.SqliteErrorCode : null;
                AgentJournalHealth degraded = await SetDegradedAsync(
                    Health.SchemaVersion,
                    reasonCode,
                    "not_run",
                    sqliteErrorCode);
                throw new AgentJournalDegradedException(degraded);
            }
        }
        finally
        {
            runtimeOperationGate.Release();
        }
    }

    private Task<AgentJournalHealth> EnsureHealthyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentJournalHealth current = Health;
        if (current.State != AgentJournalHealthState.Healthy)
        {
            throw new AgentJournalDegradedException(current);
        }

        return Task.FromResult(current);
    }

    private async Task<SqliteConnection> OpenExistingOperationalConnectionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            AgentJournalHealth missing = await SetDegradedAsync(
                Health.SchemaVersion,
                "database_missing",
                "not_run",
                sqliteErrorCode: null);
            throw new AgentJournalDegradedException(missing);
        }

        try
        {
            return await OpenConnectionAsync(
                allowCreate: false,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            string reasonCode = !File.Exists(databasePath) ? "database_missing" : "database_open_failed";
            AgentJournalHealth degraded = await SetDegradedAsync(
                Health.SchemaVersion,
                reasonCode,
                "failed",
                ex.SqliteErrorCode);
            throw new AgentJournalDegradedException(degraded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            string reasonCode = !File.Exists(databasePath) ? "database_missing" : "database_open_failed";
            AgentJournalHealth degraded = await SetDegradedAsync(
                Health.SchemaVersion,
                reasonCode,
                "failed",
                sqliteErrorCode: null);
            throw new AgentJournalDegradedException(degraded);
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        bool allowCreate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (allowCreate)
        {
            string? directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = allowCreate ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
            DefaultTimeout = Convert.ToInt32(Math.Ceiling(options.BusyTimeout.TotalSeconds))
        };
        SqliteConnection connection = new(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand settingsCommand = connection.CreateCommand();
            settingsCommand.CommandText = $"PRAGMA foreign_keys = ON; PRAGMA synchronous = FULL; PRAGMA busy_timeout = {Convert.ToInt64(options.BusyTimeout.TotalMilliseconds)};";
            await settingsCommand.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<int> MigrateToCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        int schemaVersion = await ReadSchemaVersionAsync(connection, transaction, cancellationToken);
        if (schemaVersion > SqliteJournalSchema.CurrentVersion)
        {
            throw new UnsupportedAgentJournalSchemaException(schemaVersion, SqliteJournalSchema.CurrentVersion);
        }

        if (schemaVersion == 0)
        {
            bool hasApplicationSchema = await HasApplicationSchemaAsync(connection, transaction, cancellationToken);
            if (hasApplicationSchema)
            {
                throw new UnownedAgentJournalException();
            }

            await using (SqliteCommand schemaCommand = connection.CreateCommand())
            {
                schemaCommand.Transaction = transaction;
                schemaCommand.CommandText = SqliteJournalSchema.MigrationVersion1;
                await schemaCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (SqliteCommand recordCommand = connection.CreateCommand())
            {
                recordCommand.Transaction = transaction;
                recordCommand.CommandText = """
                    INSERT INTO schema_migrations(version, name, applied_at_utc)
                    VALUES (1, $migration_name, $applied_at_utc);
                    PRAGMA user_version = 1;
                    """;
                recordCommand.Parameters.AddWithValue("$migration_name", SqliteJournalSchema.MigrationVersion1Name);
                recordCommand.Parameters.AddWithValue("$applied_at_utc", ToUtcText(timeProvider.GetUtcNow()));
                await recordCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            schemaVersion = SqliteJournalSchema.CurrentVersion;
        }

        await transaction.CommitAsync(cancellationToken);
        return schemaVersion;
    }

    private static async Task<bool> HasApplicationSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE type IN ('table', 'index', 'view', 'trigger')
                  AND name NOT LIKE 'sqlite_%'
            );
            """;
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) != 0;
    }

    private static async Task EnableWalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        object? mode = await command.ExecuteScalarAsync(cancellationToken);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToString(mode, CultureInfo.InvariantCulture), "wal"))
        {
            throw new InvalidOperationException("SQLite did not enable WAL journal mode.");
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ValidateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        return await SqliteJournalSchemaValidator.ValidateAsync(connection, cancellationToken);
    }

    private static async Task<string> RunIntegrityCheckAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        int rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            rows++;
            if (!StringComparer.Ordinal.Equals(reader.GetString(0), "ok"))
            {
                return "failed";
            }
        }

        return rows == 1 ? "ok" : "failed";
    }

    private async Task<long> AllocateSequenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string agentId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO local_state(agent_id, last_event_sequence, updated_at_utc)
            VALUES ($agent_id, 1, $updated_at_utc)
            ON CONFLICT(agent_id) DO UPDATE SET
                last_event_sequence = local_state.last_event_sequence + 1,
                updated_at_utc = excluded.updated_at_utc
            WHERE local_state.last_event_sequence < 9223372036854775807
            RETURNING last_event_sequence;
            """;
        command.Parameters.AddWithValue("$agent_id", agentId);
        command.Parameters.AddWithValue("$updated_at_utc", ToUtcText(timeProvider.GetUtcNow()));
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null
            ? throw new InvalidOperationException("Agent event sequence is exhausted.")
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> InsertMaterialEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MaterialEventWrite materialEvent,
        MaterialEventDto dto,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO material_events (
                event_id, schema_version, agent_id, property_id, agent_sequence, session_id,
                observed_at_utc, event_type, target_id, target_label, from_state, to_state,
                evidence_json, gateway_state, internet_state, created_at_utc)
            VALUES (
                $event_id, $schema_version, $agent_id, $property_id, $agent_sequence, $session_id,
                $observed_at_utc, $event_type, $target_id, $target_label, $from_state, $to_state,
                $evidence_json, $gateway_state, $internet_state, $created_at_utc)
            ON CONFLICT(event_id) DO NOTHING
            RETURNING event_id;
            """;
        command.Parameters.AddWithValue("$event_id", materialEvent.EventId);
        command.Parameters.AddWithValue("$schema_version", dto.SchemaVersion);
        command.Parameters.AddWithValue("$agent_id", materialEvent.AgentId);
        command.Parameters.AddWithValue("$property_id", materialEvent.PropertyId);
        command.Parameters.AddWithValue("$agent_sequence", dto.EventSequence);
        command.Parameters.AddWithValue("$session_id", materialEvent.SessionId);
        command.Parameters.AddWithValue("$observed_at_utc", ToUtcText(materialEvent.ObservedAtUtc));
        command.Parameters.AddWithValue("$event_type", ContractJson.Serialize(materialEvent.EventType).Trim('"'));
        command.Parameters.AddWithValue("$target_id", DbValue(materialEvent.TargetId));
        command.Parameters.AddWithValue("$target_label", DbValue(materialEvent.TargetLabel));
        command.Parameters.AddWithValue("$from_state", DbValue(materialEvent.FromState));
        command.Parameters.AddWithValue("$to_state", DbValue(materialEvent.ToState));
        command.Parameters.AddWithValue("$evidence_json", JsonSerializer.Serialize(materialEvent.Evidence, ContractJson.Options));
        command.Parameters.AddWithValue("$gateway_state", DbValue(materialEvent.GatewayState));
        command.Parameters.AddWithValue("$internet_state", DbValue(materialEvent.InternetState));
        command.Parameters.AddWithValue("$created_at_utc", ToUtcText(createdAtUtc));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task InsertOutboxAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MaterialEventWrite materialEvent,
        long sequence,
        string payloadJson,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO outbox(outbox_id, event_id, agent_id, agent_sequence, payload_json, created_at_utc)
            VALUES ($outbox_id, $event_id, $agent_id, $agent_sequence, $payload_json, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$outbox_id", materialEvent.EventId);
        command.Parameters.AddWithValue("$event_id", materialEvent.EventId);
        command.Parameters.AddWithValue("$agent_id", materialEvent.AgentId);
        command.Parameters.AddWithValue("$agent_sequence", sequence);
        command.Parameters.AddWithValue("$payload_json", payloadJson);
        command.Parameters.AddWithValue("$created_at_utc", ToUtcText(createdAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MaterialEventDto ToContract(MaterialEventWrite materialEvent, long sequence) =>
        new(
            ContractSchema.CurrentVersion,
            materialEvent.EventId,
            materialEvent.AgentId,
            materialEvent.PropertyId,
            sequence,
            materialEvent.SessionId,
            materialEvent.ObservedAtUtc,
            materialEvent.EventType,
            materialEvent.TargetId,
            materialEvent.TargetLabel,
            materialEvent.FromState,
            materialEvent.ToState,
            materialEvent.Evidence,
            materialEvent.GatewayState,
            materialEvent.InternetState);

    private async Task<AgentJournalHealth> SetDegradedAsync(
        int? schemaVersion,
        string reasonCode,
        string integrityResult,
        int? sqliteErrorCode)
    {
        AgentJournalHealth current = Health;
        if (current.State == AgentJournalHealthState.Degraded)
        {
            return current;
        }

        AgentJournalHealth degraded = new(AgentJournalHealthState.Degraded, schemaVersion, reasonCode, integrityResult);
        Volatile.Write(ref health, degraded);
        bool lifecycleMarkerPersisted;
        try
        {
            lifecycleMarkerPersisted = await degradationHandler.TransitionToJournalDegradedAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            lifecycleMarkerPersisted = false;
        }

        try
        {
            LogDegraded(logger, reasonCode, schemaVersion, integrityResult, sqliteErrorCode);
        }
        catch (Exception)
        {
        }

        if (!lifecycleMarkerPersisted)
        {
            try
            {
                LogLifecycleMarkerDegradationPersistenceFailed(logger, "lifecycle_marker_write_failed");
            }
            catch (Exception)
            {
            }
        }

        return degraded;
    }

    private static async Task<AgentSessionRecord?> ReadSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT session_id, started_at_utc, ready_at_utc, ended_at_utc, planned_shutdown
            FROM agent_sessions
            WHERE session_id = $session_id;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AgentSessionRecord(
            reader.GetString(0),
            ParseUtcText(reader.GetString(1)),
            reader.IsDBNull(2) ? null : ParseUtcText(reader.GetString(2)),
            reader.IsDBNull(3) ? null : ParseUtcText(reader.GetString(3)),
            reader.GetInt64(4) != 0);
    }

    private static async Task InsertSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AgentSessionRecord session,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO agent_sessions (
                session_id, started_at_utc, ready_at_utc, ended_at_utc, planned_shutdown)
            VALUES (
                $session_id, $started_at_utc, $ready_at_utc, $ended_at_utc, $planned_shutdown);
            """;
        AddSessionParameters(command, session);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AgentSessionRecord session,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE agent_sessions
            SET ready_at_utc = $ready_at_utc,
                ended_at_utc = $ended_at_utc,
                planned_shutdown = $planned_shutdown
            WHERE session_id = $session_id
              AND started_at_utc = $started_at_utc;
            """;
        AddSessionParameters(command, session);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new AgentSessionConflictException("immutable_start_conflict");
        }
    }

    private static void AddSessionParameters(SqliteCommand command, AgentSessionRecord session)
    {
        command.Parameters.AddWithValue("$session_id", session.SessionId);
        command.Parameters.AddWithValue("$started_at_utc", ToUtcText(session.StartedAtUtc));
        command.Parameters.AddWithValue("$ready_at_utc", DbValue(session.ReadyAtUtc));
        command.Parameters.AddWithValue("$ended_at_utc", DbValue(session.EndedAtUtc));
        command.Parameters.AddWithValue("$planned_shutdown", session.PlannedShutdown ? 1 : 0);
    }

    private static AgentSessionRecord MergeSession(AgentSessionRecord stored, AgentSessionRecord incoming)
    {
        if (stored.StartedAtUtc != incoming.StartedAtUtc)
        {
            throw new AgentSessionConflictException("immutable_start_conflict");
        }

        AgentSessionRecord merged = stored with
        {
            ReadyAtUtc = MergeMilestone(stored.ReadyAtUtc, incoming.ReadyAtUtc, "ready_timestamp_conflict"),
            EndedAtUtc = MergeMilestone(stored.EndedAtUtc, incoming.EndedAtUtc, "ended_timestamp_conflict"),
            PlannedShutdown = stored.PlannedShutdown || incoming.PlannedShutdown
        };
        ValidateSessionChronology(merged);
        return merged;
    }

    private static DateTimeOffset? MergeMilestone(DateTimeOffset? stored, DateTimeOffset? incoming, string conflictReason)
    {
        if (stored.HasValue && incoming.HasValue && stored.Value != incoming.Value)
        {
            throw new AgentSessionConflictException(conflictReason);
        }

        return stored ?? incoming;
    }

    private static void ValidateSession(AgentSessionRecord session)
    {
        ValidateIdentifier(nameof(session.SessionId), session.SessionId);
        ValidateUtc(nameof(session.StartedAtUtc), session.StartedAtUtc);
        ValidateOptionalUtc(nameof(session.ReadyAtUtc), session.ReadyAtUtc);
        ValidateOptionalUtc(nameof(session.EndedAtUtc), session.EndedAtUtc);
        RejectSecrets([session.SessionId]);
        ValidateSessionChronology(session);
    }

    private static void ValidateSessionChronology(AgentSessionRecord session)
    {
        if (session.ReadyAtUtc < session.StartedAtUtc)
        {
            throw new ArgumentException("Session readiness cannot precede session start.", nameof(session));
        }

        if (session.EndedAtUtc < session.StartedAtUtc)
        {
            throw new ArgumentException("Session end cannot precede session start.", nameof(session));
        }

        if (session.ReadyAtUtc.HasValue && session.EndedAtUtc < session.ReadyAtUtc)
        {
            throw new ArgumentException("Session end cannot precede session readiness.", nameof(session));
        }
    }

    private static void ValidateProbeSample(ProbeSampleWrite sample)
    {
        ValidateIdentifier(nameof(sample.SampleId), sample.SampleId);
        ValidateIdentifier(nameof(sample.AgentId), sample.AgentId);
        ValidateIdentifier(nameof(sample.SessionId), sample.SessionId);
        ValidateUtc(nameof(sample.ObservedAtUtc), sample.ObservedAtUtc);
        ValidateBoundedValue(nameof(sample.ProbeKind), sample.ProbeKind, 64);
        ValidateBoundedValue(nameof(sample.ProbeState), sample.ProbeState, 32);
        ValidateOptionalBoundedValue(nameof(sample.TargetId), sample.TargetId, 128);
        ValidateEvidence(sample.Evidence);
        RejectSecrets([sample.SampleId, sample.AgentId, sample.SessionId, sample.ProbeKind, sample.ProbeState, sample.TargetId, .. sample.Evidence]);
    }

    private static void ValidateMaterialEvent(MaterialEventWrite materialEvent)
    {
        MaterialEventDto dto = ToContract(materialEvent, 1);
        IReadOnlyList<ContractValidationError> errors = dto.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Material event failed contract validation ({errors[0].Field}:{errors[0].Code}).", nameof(materialEvent));
        }

        RejectSecrets([
            materialEvent.EventId,
            materialEvent.AgentId,
            materialEvent.PropertyId,
            materialEvent.SessionId,
            materialEvent.TargetId,
            materialEvent.TargetLabel,
            materialEvent.FromState,
            materialEvent.ToState,
            materialEvent.GatewayState,
            materialEvent.InternetState,
            .. materialEvent.Evidence
        ]);
    }

    private static void ValidateIdentifier(string parameterName, string value) => ValidateBoundedValue(parameterName, value, 128);

    private static void ValidateBoundedValue(string parameterName, string value, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"Value must not exceed {maximumLength} characters.", parameterName);
        }
    }

    private static void ValidateOptionalBoundedValue(string parameterName, string? value, int maximumLength)
    {
        if (value is not null)
        {
            ValidateBoundedValue(parameterName, value, maximumLength);
        }
    }

    private static void ValidateEvidence(IReadOnlyList<string> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count > 16)
        {
            throw new ArgumentException("Evidence is limited to 16 bounded codes.", nameof(evidence));
        }

        for (int index = 0; index < evidence.Count; index++)
        {
            ValidateBoundedValue($"evidence[{index}]", evidence[index], 96);
        }
    }

    private static void ValidateUtc(string parameterName, DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }

    private static void ValidateOptionalUtc(string parameterName, DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            ValidateUtc(parameterName, value.Value);
        }
    }

    private static void RejectSecrets(IEnumerable<string?> values)
    {
        foreach (string value in values.Where(static value => value is not null).Cast<string>())
        {
            if (ProhibitedSecretMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
                Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new ArgumentException("Persistence input contains a prohibited secret-bearing value.", nameof(values));
            }
        }
    }

    private static string ToUtcText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtcText(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private static object DbValue(DateTimeOffset? value) => value.HasValue ? ToUtcText(value.Value) : DBNull.Value;

    private static string ResolveDatabasePath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "RemotePropertyWatchdog", "Agent", "agent-journal.db");
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Agent journal initialized. SchemaVersion={SchemaVersion} JournalMode={JournalMode} Synchronous={Synchronous} IntegrityResult={IntegrityResult}")]
    private static partial void LogInitialized(ILogger logger, int schemaVersion, string journalMode, string synchronous, string integrityResult);

    [LoggerMessage(EventId = 101, Level = LogLevel.Error, Message = "Agent journal entered degraded state. ReasonCode={ReasonCode} SchemaVersion={SchemaVersion} IntegrityResult={IntegrityResult} SqliteErrorCode={SqliteErrorCode}")]
    private static partial void LogDegraded(ILogger logger, string reasonCode, int? schemaVersion, string integrityResult, int? sqliteErrorCode);

    [LoggerMessage(EventId = 102, Level = LogLevel.Debug, Message = "Agent journal material event and outbox record committed atomically. AgentSequence={AgentSequence}")]
    private static partial void LogMaterialEventCommitted(ILogger logger, long agentSequence);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "Agent journal integrity verification completed. SchemaVersion={SchemaVersion} IntegrityResult={IntegrityResult}")]
    private static partial void LogIntegrityVerified(ILogger logger, int schemaVersion, string integrityResult);

    [LoggerMessage(EventId = 104, Level = LogLevel.Error, Message = "Agent journal degradation could not be persisted to the lifecycle marker. ReasonCode={ReasonCode}")]
    private static partial void LogLifecycleMarkerDegradationPersistenceFailed(ILogger logger, string reasonCode);

    private enum JournalVerificationStage
    {
        Opening,
        Integrity,
        Schema
    }

    private sealed class UnownedAgentJournalException : InvalidOperationException;
}
