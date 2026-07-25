# R0-004 SQLite Journal and Transactional Outbox Evidence

## Design boundary

`IAgentJournal` is the agent persistence boundary. `SqliteAgentJournal` is its only R0-004 implementation and uses parameterized `Microsoft.Data.Sqlite` commands directly. The existing file-backed R0-003 lifecycle marker remains registered and unchanged; the SQLite session journal is additive, so the Windows lifecycle script remains compatible.

Operational startup is ordered: the `AgentWorker` starts the file-backed lifecycle session, uses the returned `PreviousSessionId` to choose an explicit journal initialization mode, initializes and validates the journal, persists the local started session, records monitoring-ready in the file marker, and then persists the ready milestone in SQLite before entering steady running state. `CreateIfMissingForFreshLifecycle` is selected only when the authoritative lifecycle result has no previous session; `RequireExistingForPriorLifecycle` is selected when it does. If either startup session write degrades the journal, the worker re-reads the journal health, preserves its bounded reason, clears readiness, and waits for shutdown without performing another ordinary journal operation. A ready-milestone failure also calls `MarkNotReadyAsync`, which serializes under the lifecycle gate and clears both the authoritative in-memory state and file marker. On a non-faulted stop, the worker first records the authoritative planned stop in the file marker and then persists the same stopped timestamp and planned classification in SQLite under the existing bounded shutdown timeout. A faulted worker writes neither clean-stop marker, so it cannot falsely classify the session as clean.

`agent_sessions` represents a local, pre-registration lifecycle identity keyed only by the globally unique session ID. It deliberately has no `agent_id`: property enrollment, authenticated agent identity, credentials, and revocation belong to R0-006. Probe samples and material events retain their separate contract `agent_id` fields, while their local referential-integrity link targets `agent_sessions.session_id`. This avoids a shared sentinel or fake cloud identity and lets a later schema migration associate local evidence with an enrolled identity without rewriting lifecycle history.

A degraded journal remains observable through `IAgentJournal.Health` and a bounded structured error log, blocks readiness, and leaves the worker alive for a clean file-marker stop. `AgentWorker` is registered once and that same singleton is exposed as both the hosted service and `IAgentServiceHealth`, so worker readiness and the formal service-health surface cannot drift because of duplicate worker instances. `IAgentServiceHealth.IsReady` requires both the worker's monitoring-ready transition and healthy journal state. The degraded log contains only reason code, schema version, and integrity result.

Session persistence uses a single worker boundary for started, ready, and stopped milestones. Cancellation is rethrown unchanged. For other failures, the worker re-reads `IAgentJournal.Health`; healthy journals preserve validation, lifecycle-conflict, duplicate, and deliberate synthetic-fault behavior, while degraded journals transition the worker, lifecycle coordinator, marker, and service-health surface to not-ready without retrying storage. Stopped or planned-shutdown lifecycle state is never reported ready.

The configured database path must be absolute. The default is `RemotePropertyWatchdog/Agent/agent-journal.db` below the operating system's common application-data directory. Logs contain schema/health codes and sequence numbers, not database paths, payloads, target labels, evidence contents, or exception text.

## Schema version and migration behavior

- Current `PRAGMA user_version`: **1**.
- `schema_migrations` records `R0-004-initial-journal-outbox` once.
- Version 0 is adopted only when the file has no application-defined tables, indexes, views, or triggers. That ownership check and the version 0-to-1 migration share one immediate transaction, so an unrelated SQLite file is preserved at the schema/data level and enters `database_not_owned` without a migration or WAL mode change. A genuinely empty version 0 journal upgrades to version 1, and concurrent journal instances either apply the migration once or observe the already-supported version.
- `InitializeAsync` requires an `AgentJournalInitializationMode`. Only `CreateIfMissingForFreshLifecycle` may create the database and parent directory, preserving a genuine first lifecycle start by opening a missing path with `ReadWriteCreate`.
- `RequireExistingForPriorLifecycle` checks for the existing journal before opening it. A missing journal becomes degraded with `database_missing` and `integrity_result=not_run`; no connection is opened, no migration runs, and the database, WAL, and SHM remain absent.
- An existing journal opens with `ReadWrite` in either startup mode, applies a supported migration when needed, validates the exact schema-v1 definitions and migration inventory, and runs integrity checking normally.
- Verification and all operational journal access require prior successful initialization and open only with `ReadWrite`. An uninitialized call fails with `not_initialized`; a journal deleted after initialization fails with `database_missing`. Neither case creates a database or parent directory, and no runtime path runs migrations.
- Explicit runtime verification never runs migrations. A deleted database becomes `database_missing`; a valid empty replacement, missing or unexpected inventory, wrong runtime schema version, altered object definitions, or incorrect/additional migration evidence becomes `schema_validation_failed` and is preserved for diagnosis.
- Repeated initialization validates the same schema and never reapplies the migration.
- A version greater than 1 enters `schema_too_new` degraded state without schema or file replacement.
- Missing or unexpected application objects, altered table/index definitions, or incorrect/additional migration records enter `schema_validation_failed` degraded state.
- No migration deletes, renames, or rewrites existing evidence.

## Table and key inventory

| Table | Important columns | Keys and constraints |
|---|---|---|
| `schema_migrations` | `version`, `name`, `applied_at_utc` | `version` primary key; `name` unique |
| `agent_sessions` | `session_id`, start/ready/end UTC timestamps, `planned_shutdown` | `session_id` primary key and local lifecycle identity; shutdown Boolean check |
| `local_state` | `agent_id`, `last_event_sequence`, `configuration_version`, `non_secret_configuration_json`, `updated_at_utc` | `agent_id` primary key; nonnegative sequence/version; optional JSON must be valid |
| `probe_samples` | `sample_id`, contract agent identity, local session identity, observed UTC, probe kind/state, target, evidence JSON | `sample_id` primary key; `session_id` foreign key to the local lifecycle session; bounded identifiers; valid evidence JSON |
| `material_events` | `event_id`, schema/agent/property/session identity, `agent_sequence`, observed/created UTC, event fields, evidence JSON | `event_id` primary key; unique `(agent_id, agent_sequence)`; `session_id` foreign key to the local lifecycle session; positive sequence; valid evidence JSON |
| `outbox` | `outbox_id`, `event_id`, `agent_id`, `agent_sequence`, payload JSON, created UTC | `outbox_id` primary key; `event_id` unique foreign key to `material_events`; unique `(agent_id, agent_sequence)`; positive sequence; valid payload JSON |

Session writes use an immediate transaction. Session ID and start time are immutable; null milestone retries cannot erase stored readiness or end timestamps; planned shutdown can progress only from false to true; and conflicting non-null milestones fail with a bounded `AgentSessionConflictException` reason code. Input and merged session chronology must satisfy start <= readiness <= end for milestones that exist.

The optional `non_secret_configuration_json` column reserves versioned local non-secret configuration state, but R0-004 exposes no generic configuration writer and the schema has no credential-storage fields. Known credential-like markers and credential-bearing URLs are rejected as defense-in-depth. This bounded heuristic does not prove that arbitrary free text contains no secret. Camera/provider credentials, enrollment tokens, authorization headers, and connection-string passwords remain outside this database; Windows-bound secret storage is a later scoped task.

## Index inventory

| Index | Purpose |
|---|---|
| `idx_agent_sessions_started` | Deterministic local session timeline |
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

Every connection enables foreign keys, uses a bounded busy timeout, and sets `PRAGMA synchronous=FULL`. Initialization enables and verifies WAL only after ownership/migration, schema validation, and integrity checks succeed. The migration, session, and event/outbox writes use explicit immediate transactions where monotonic merge or atomic allocation requires them. Database pooling is disabled so test/process boundaries do not retain hidden handles.

Initialization and explicit verification use the same definition-level validator and run `PRAGMA integrity_check`. The validator creates an isolated in-memory reference schema directly from the exact `MigrationVersion1` SQL and shared migration identity, then compares it with the candidate. Structured `table_list`, `table_xinfo`, `foreign_key_list`, `index_list`, and `index_xinfo` metadata verifies exact application-object inventory; table column count/order, names, declared types, nullability, defaults, primary-key ordering, hidden-column kind, STRICT and WITHOUT ROWID status; foreign-key source/target columns, referenced tables, match mode, and update/delete actions; and index membership/order, uniqueness, origin, partial status, sort direction, collation, and key-column status. A strict `sqlite_schema.sql` comparison, normalizing only whitespace outside quoted values/identifiers, additionally binds required CHECK constraints and complete DDL token/literal/constraint ordering to that same migration source. The migration inventory must contain exactly version 1 and `R0-004-initial-journal-outbox`, while `PRAGMA user_version` must be exactly 1.

Runtime verification opens only the existing file and requires exactly one integrity row equal to:

```text
ok
```

Any other output, invalid/truncated/non-database input, missing required schema, or SQLite open/integrity error produces an observable `AgentJournalHealthState.Degraded` with a bounded reason code. Initialization additionally uses `database_invalid`, `database_not_owned`, `initialization_failed`, and `schema_too_new`. Runtime opening and verification use `database_missing`, `database_open_failed`, `schema_validation_failed`, and `integrity_check_failed`; runtime persistence maps provider/I/O/access/format failures to `runtime_write_failed`, and pending-outbox reads map those failures to `runtime_read_failed`. Cancellation, validation failures, duplicate/session conflicts, and synthetic fault-injection exceptions remain their original non-degrading outcomes. The implementation never deletes, replaces, renames, migrates, or silently recreates a missing, replaced, or corrupt runtime database.

`Microsoft.Data.Sqlite` exposes asynchronous ADO.NET methods but SQLite performs underlying file I/O synchronously. The boundary remains task-based and cancellation-aware: tokens are checked before initialization and immediate transactions, passed to every provider operation, and checked again before commit. Tests verify pre-canceled operations propagate cancellation without marking a healthy database degraded.

## Review evidence

The automated test suite demonstrates:

- fresh migration, table/index/foreign-key inventory, WAL, FULL synchronization, and schema version 1;
- idempotent initialization, empty version 0 upgrade, preservation and `database_not_owned` degradation for an unrelated version 0 SQLite schema, and two coordinated journal instances applying exactly one migration record;
- real worker startup persisting the exact file-backed session ID and started milestone before readiness, then persisting readiness before steady running;
- storage failure during the first, second (ready), and third (planned-stop) session persistence calls preserving `runtime_write_failed`, rolling back the failed transaction, clearing worker/lifecycle/marker/service-health readiness, blocking later writes without retry, and leaving probe/event/outbox/sequence tables untouched;
- a genuine first real-host lifecycle start with no marker or database selecting the explicit creation mode, creating schema version 1, and becoming ready;
- a later real-host process start with `PreviousSessionId` present and its journal deliberately removed selecting existing-only initialization, entering `database_missing`, never becoming ready, and leaving database/WAL/SHM absent through shutdown;
- clean worker shutdown persisting the identical stopped timestamp and `planned_shutdown=true` after the authoritative file marker, while faulted shutdown remains unclean in both stores;
- corrupt real-host startup remaining visibly degraded, not ready, and byte-for-byte unmodified through clean shutdown;
- session exact/stale retry non-regression, forward milestone progression, deterministic conflict rejection, and chronology validation;
- first synthetic probe and material event writes satisfying the local-session foreign key without adding a fake or authenticated agent identity to the session;
- all-or-none rollback before commit and both rows after a post-commit fault;
- restart continuation at the next committed sequence;
- deterministic duplicate-ID rejection without an outbox row or sequence gap;
- stable outbox order and 20 coordinated concurrent writers with unique contiguous sequences;
- visible degraded state for synthetic corrupt/truncated/non-database files without replacement;
- healthy definition-level schema-plus-integrity verification, uninitialized missing verification/operations remaining non-creating, deleted-database preservation without recreation, empty-replacement preservation without migration, and detection of dropped tables, indexes, and migration records even when `integrity_check` returns `ok`;
- initialization and explicit verification rejection of integrity-clean look-alike databases with altered columns/types/order/nullability/defaults/primary keys, missing STRICT, altered identifier/sequence/JSON/Boolean CHECK constraints, missing or redirected foreign keys and actions, wrong index columns/order/uniqueness/partial predicates/sort/collation, unexpected application objects, or incorrect/additional migration evidence; invalid startup candidates retain their definitions, data, version, and journal mode without repair or WAL conversion;
- runtime session, probe, material-event/outbox, and pending-outbox-read storage failures entering bounded `runtime_write_failed` or `runtime_read_failed` degradation and blocking later operations, while cancellation, validation, duplicate/session conflicts, and synthetic pre/post-commit faults do not spuriously degrade health;
- defense-in-depth rejection of known credential-like synthetic input and byte-level absence checks across database/WAL/SHM artifacts;
- cancellation propagation.

## Validation

The completed P2 correction was validated with:

- `python scripts/validate-kickoff.py`
- `python scripts/scan-secrets.py`
- `dotnet restore ./RemotePropertyWatchdog.sln`
- `dotnet build ./RemotePropertyWatchdog.sln --configuration Release --no-restore`
- `dotnet test ./RemotePropertyWatchdog.sln --configuration Release --no-build`
- `pwsh ./scripts/test.ps1`
- `dotnet list ./RemotePropertyWatchdog.sln package --vulnerable --include-transitive`
- `git diff --check`

Final local result: **135 passed, 0 failed, 0 skipped** (48 unit, 78 integration, 8 contract, 1 build). The Release build completed with 0 warnings and 0 errors. The vulnerability audit found no known vulnerable direct or transitive packages. Secret, path, diff, and generated database/WAL/SHM artifact checks passed.

The repository ignore rules cover `*.db`, `*.sqlite`, and `*.sqlite3` databases plus their `-journal`, `-wal`, and `-shm` companions. Generated databases use operating-system temporary directories and are not committed. Test identities and values are synthetic; no customer or LAN data is used.

R0-004 performs no automatic retention deletion. The prototype default is to retain journal evidence until a later reviewed retention policy is implemented; this avoids destructive recovery behavior but leaves disk-growth tuning as a known follow-up risk. SQLite is additive rather than the lifecycle recovery authority. A failed ready-milestone transaction leaves SQLite at the started milestone and clears lifecycle readiness; a failed planned-stop transaction can leave SQLite conservatively behind the authoritative clean file marker, but neither path fabricates persisted evidence or retries a degraded journal. The creation decision assumes the independent R0-003 lifecycle marker remains available: if an operator manually deletes both that marker and the SQLite journal, no retained evidence remains by which this prototype can distinguish loss from a genuine first start, so it will treat that state as fresh and cannot recover the deleted history. Enrollment and binding the contract `agent_id` to a property remain R0-006 work.
