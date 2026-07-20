# R0-004 SQLite Journal and Transactional Outbox Evidence

## Design boundary

`IAgentJournal` is the agent persistence boundary. `SqliteAgentJournal` is its only R0-004 implementation and uses parameterized `Microsoft.Data.Sqlite` commands directly. The existing file-backed R0-003 lifecycle marker remains registered and unchanged; the SQLite session journal is additive, so the Windows lifecycle script remains compatible.

Operational startup is ordered: the `AgentWorker` starts the lifecycle session, initializes and validates the journal, and only then records monitoring-ready. A degraded journal remains observable through `IAgentJournal.Health` and a bounded structured error log, blocks readiness, and leaves the worker alive for a clean service stop. The degraded log contains only reason code, schema version, and integrity result.

The configured database path must be absolute. The default is `RemotePropertyWatchdog/Agent/agent-journal.db` below the operating system's common application-data directory. Logs contain schema/health codes and sequence numbers, not database paths, payloads, target labels, evidence contents, or exception text.

## Schema version and migration behavior

- Current `PRAGMA user_version`: **1**.
- `schema_migrations` records `R0-004-initial-journal-outbox` once.
- Version 0 upgrades to version 1 in one immediate transaction. The version is read again after acquiring that database-wide writer transaction, so concurrent journal instances either apply the migration once or observe the already-supported version.
- Repeated initialization validates the same schema and never reapplies the migration.
- A version greater than 1 enters `schema_too_new` degraded state without schema or file replacement.
- Missing required tables, indexes, or the migration record enter `schema_validation_failed` degraded state.
- No migration deletes, renames, or rewrites existing evidence.

## Table and key inventory

| Table | Important columns | Keys and constraints |
|---|---|---|
| `schema_migrations` | `version`, `name`, `applied_at_utc` | `version` primary key; `name` unique |
| `agent_sessions` | `session_id`, `agent_id`, start/ready/end UTC timestamps, `planned_shutdown` | `session_id` primary key; unique `(session_id, agent_id)` parent key; shutdown Boolean check |
| `local_state` | `agent_id`, `last_event_sequence`, `configuration_version`, `non_secret_configuration_json`, `updated_at_utc` | `agent_id` primary key; nonnegative sequence/version; optional JSON must be valid |
| `probe_samples` | `sample_id`, agent/session identity, observed UTC, probe kind/state, target, evidence JSON | `sample_id` primary key; `(session_id, agent_id)` foreign key; bounded identifiers; valid evidence JSON |
| `material_events` | `event_id`, schema/agent/property/session identity, `agent_sequence`, observed/created UTC, event fields, evidence JSON | `event_id` primary key; unique `(agent_id, agent_sequence)`; `(session_id, agent_id)` foreign key; positive sequence; valid evidence JSON |
| `outbox` | `outbox_id`, `event_id`, `agent_id`, `agent_sequence`, payload JSON, created UTC | `outbox_id` primary key; `event_id` unique foreign key to `material_events`; unique `(agent_id, agent_sequence)`; positive sequence; valid payload JSON |

Session writes use an immediate transaction. Session ID, agent ID, and start time are immutable; null milestone retries cannot erase stored readiness or end timestamps; planned shutdown can progress only from false to true; and conflicting non-null milestones fail with a bounded `AgentSessionConflictException` reason code. Input and merged session chronology must satisfy start <= readiness <= end for milestones that exist.

The optional `non_secret_configuration_json` column reserves versioned local non-secret configuration state, but R0-004 exposes no generic configuration writer and the schema has no credential-storage fields. Known credential-like markers and credential-bearing URLs are rejected as defense-in-depth. This bounded heuristic does not prove that arbitrary free text contains no secret. Camera/provider credentials, enrollment tokens, authorization headers, and connection-string passwords remain outside this database; Windows-bound secret storage is a later scoped task.

## Index inventory

| Index | Purpose |
|---|---|
| `ux_agent_sessions_session_agent` | Composite parent key for session/agent foreign keys |
| `idx_agent_sessions_agent_started` | Deterministic agent session timeline |
| `idx_probe_samples_agent_observed` | Agent probe history by observation time and stable sample ID |
| `ux_material_events_agent_sequence` | Prevent duplicate agent sequences |
| `idx_material_events_session_observed` | Session evidence timeline |
| `ux_outbox_event` | Exactly one outbox row per event |
| `ux_outbox_sequence` | Prevent duplicate outbox sequence positions |
| `idx_outbox_agent_sequence` | Stable pending-read order by agent sequence then outbox ID |

SQLite also creates internal primary-key indexes; those are intentionally omitted from the named application index inventory.

## Transaction and sequence guarantees

Material event persistence uses one `BEGIN IMMEDIATE` transaction:

1. Atomically insert or increment `local_state.last_event_sequence` and return the allocated value.
2. Insert the material event with `ON CONFLICT(event_id) DO NOTHING RETURNING event_id`; no returned row becomes the stable `DuplicateEventIdException`.
3. Insert the outbox row with the same stable event ID and assigned sequence.
4. Invoke the deterministic pre-commit fault hook and check cancellation.
5. Commit once.
6. Invoke the post-commit fault hook, which models an ambiguous caller result after durable commit.

A pre-commit exception rolls back the sequence, event, and outbox together. A post-commit exception leaves both rows durable. A retry with the same event ID is rejected and cannot create another outbox row. Immediate writer transactions plus unique database constraints prevent concurrent sequence duplication. Restarts allocate from the committed `local_state.last_event_sequence`; UTC timestamps remain separate from sequence ordering.

## Crash-safety and integrity settings

Every connection enables foreign keys, uses a bounded busy timeout, and sets `PRAGMA synchronous=FULL`. Initialization requires `PRAGMA journal_mode=WAL` to return `wal`. The migration and event/outbox writes use explicit transactions. Database pooling is disabled so test/process boundaries do not retain hidden handles.

Initialization and explicit verification run `PRAGMA integrity_check`. A healthy synthetic database returns exactly:

```text
ok
```

Any other output, invalid/truncated/non-database input, missing required schema, or SQLite integrity error produces an observable `AgentJournalHealthState.Degraded` with a bounded reason code. The implementation never deletes, replaces, renames, or silently recreates a corrupt database.

`Microsoft.Data.Sqlite` exposes asynchronous ADO.NET methods but SQLite performs underlying file I/O synchronously. The boundary remains task-based and cancellation-aware: tokens are checked before initialization and immediate transactions, passed to every provider operation, and checked again before commit. Tests verify pre-canceled operations propagate cancellation without marking a healthy database degraded.

## Review evidence

The automated test suite demonstrates:

- fresh migration, table/index/foreign-key inventory, WAL, FULL synchronization, and schema version 1;
- idempotent initialization, version 0 upgrade, and two coordinated journal instances applying exactly one migration record;
- real worker startup initializing and validating SQLite after lifecycle start and before readiness;
- corrupt real-host startup remaining visibly degraded, not ready, and byte-for-byte unmodified through clean shutdown;
- session exact/stale retry non-regression, forward milestone progression, deterministic conflict rejection, and chronology validation;
- all-or-none rollback before commit and both rows after a post-commit fault;
- restart continuation at the next committed sequence;
- deterministic duplicate-ID rejection without an outbox row or sequence gap;
- stable outbox order and 20 coordinated concurrent writers with unique contiguous sequences;
- visible degraded state for synthetic corrupt/truncated/non-database files without replacement;
- healthy `integrity_check` output of `ok`;
- defense-in-depth rejection of known credential-like synthetic input and byte-level absence checks across database/WAL/SHM artifacts;
- cancellation propagation.

The repository ignore rules cover `*.db`, `*.sqlite`, and `*.sqlite3` databases plus their `-journal`, `-wal`, and `-shm` companions. Generated databases use operating-system temporary directories and are not committed. Test identities and values are synthetic; no customer or LAN data is used.

R0-004 performs no automatic retention deletion. The prototype default is to retain journal evidence until a later reviewed retention policy is implemented; this avoids destructive recovery behavior but leaves disk-growth tuning as a known follow-up risk.
