namespace RemotePropertyWatchdog.Agent.Service;

public static class SqliteJournalSchema
{
    public const int CurrentVersion = 1;
    internal const string MigrationVersion1Name = "R0-004-initial-journal-outbox";

    public static IReadOnlyList<string> Tables { get; } =
    [
        "agent_sessions",
        "local_state",
        "material_events",
        "outbox",
        "probe_samples",
        "schema_migrations"
    ];

    public static IReadOnlyList<string> Indexes { get; } =
    [
        "idx_agent_sessions_started",
        "idx_material_events_session_observed",
        "idx_outbox_agent_sequence",
        "idx_probe_samples_agent_observed",
        "ux_material_events_agent_sequence",
        "ux_outbox_event",
        "ux_outbox_sequence"
    ];

    internal const string MigrationVersion1 = """
        CREATE TABLE schema_migrations (
            version INTEGER NOT NULL PRIMARY KEY CHECK (version > 0),
            name TEXT NOT NULL UNIQUE,
            applied_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE agent_sessions (
            session_id TEXT NOT NULL PRIMARY KEY CHECK (length(session_id) BETWEEN 1 AND 128),
            started_at_utc TEXT NOT NULL,
            ready_at_utc TEXT NULL,
            ended_at_utc TEXT NULL,
            planned_shutdown INTEGER NOT NULL DEFAULT 0 CHECK (planned_shutdown IN (0, 1))
        ) STRICT;

        CREATE INDEX idx_agent_sessions_started
            ON agent_sessions(started_at_utc, session_id);

        CREATE TABLE local_state (
            agent_id TEXT NOT NULL PRIMARY KEY CHECK (length(agent_id) BETWEEN 1 AND 128),
            last_event_sequence INTEGER NOT NULL DEFAULT 0 CHECK (last_event_sequence >= 0),
            configuration_version INTEGER NOT NULL DEFAULT 0 CHECK (configuration_version >= 0),
            non_secret_configuration_json TEXT NULL CHECK (non_secret_configuration_json IS NULL OR json_valid(non_secret_configuration_json)),
            updated_at_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE probe_samples (
            sample_id TEXT NOT NULL PRIMARY KEY CHECK (length(sample_id) BETWEEN 1 AND 128),
            agent_id TEXT NOT NULL CHECK (length(agent_id) BETWEEN 1 AND 128),
            session_id TEXT NOT NULL CHECK (length(session_id) BETWEEN 1 AND 128),
            observed_at_utc TEXT NOT NULL,
            probe_kind TEXT NOT NULL CHECK (length(probe_kind) BETWEEN 1 AND 64),
            probe_state TEXT NOT NULL CHECK (length(probe_state) BETWEEN 1 AND 32),
            target_id TEXT NULL CHECK (target_id IS NULL OR length(target_id) BETWEEN 1 AND 128),
            evidence_json TEXT NOT NULL CHECK (json_valid(evidence_json)),
            FOREIGN KEY (session_id)
                REFERENCES agent_sessions(session_id) ON UPDATE RESTRICT ON DELETE RESTRICT
        ) STRICT;

        CREATE INDEX idx_probe_samples_agent_observed
            ON probe_samples(agent_id, observed_at_utc, sample_id);

        CREATE TABLE material_events (
            event_id TEXT NOT NULL PRIMARY KEY CHECK (length(event_id) BETWEEN 1 AND 128),
            schema_version TEXT NOT NULL,
            agent_id TEXT NOT NULL CHECK (length(agent_id) BETWEEN 1 AND 128),
            property_id TEXT NOT NULL CHECK (length(property_id) BETWEEN 1 AND 128),
            agent_sequence INTEGER NOT NULL CHECK (agent_sequence > 0),
            session_id TEXT NOT NULL CHECK (length(session_id) BETWEEN 1 AND 128),
            observed_at_utc TEXT NOT NULL,
            event_type TEXT NOT NULL,
            target_id TEXT NULL,
            target_label TEXT NULL,
            from_state TEXT NULL,
            to_state TEXT NULL,
            evidence_json TEXT NOT NULL CHECK (json_valid(evidence_json)),
            gateway_state TEXT NULL,
            internet_state TEXT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (session_id)
                REFERENCES agent_sessions(session_id) ON UPDATE RESTRICT ON DELETE RESTRICT
        ) STRICT;

        CREATE UNIQUE INDEX ux_material_events_agent_sequence
            ON material_events(agent_id, agent_sequence);
        CREATE INDEX idx_material_events_session_observed
            ON material_events(session_id, observed_at_utc, event_id);

        CREATE TABLE outbox (
            outbox_id TEXT NOT NULL PRIMARY KEY CHECK (length(outbox_id) BETWEEN 1 AND 128),
            event_id TEXT NOT NULL,
            agent_id TEXT NOT NULL CHECK (length(agent_id) BETWEEN 1 AND 128),
            agent_sequence INTEGER NOT NULL CHECK (agent_sequence > 0),
            payload_json TEXT NOT NULL CHECK (json_valid(payload_json)),
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (event_id) REFERENCES material_events(event_id) ON UPDATE RESTRICT ON DELETE RESTRICT
        ) STRICT;

        CREATE UNIQUE INDEX ux_outbox_event ON outbox(event_id);
        CREATE UNIQUE INDEX ux_outbox_sequence ON outbox(agent_id, agent_sequence);
        CREATE INDEX idx_outbox_agent_sequence ON outbox(agent_id, agent_sequence, outbox_id);
        """;
}
