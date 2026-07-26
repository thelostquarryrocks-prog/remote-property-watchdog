# R0-004 SQLite Journal and Transactional Outbox Evidence

## Design boundary

`IAgentJournal` is the agent persistence boundary. `SqliteAgentJournal` is its only R0-004 implementation and uses parameterized `Microsoft.Data.Sqlite` commands directly. The file-backed R0-003 lifecycle marker remains the lifecycle authority and is backward-compatible: authentic R0-003 JSON has no `journal_ownership_version`, while R0-004 adds that optional bounded integer field. The existing Windows lifecycle script ignores the additive field and remains compatible.

Operational startup is ordered: the `AgentWorker` starts the file-backed lifecycle session, validates prior marker evidence, and checks the versioned ownership field. A fresh installation or proven authentic R0-003 marker has no ownership version; before any create-capable SQLite open, the coordinator atomically publishes `journal_ownership_version=1`. An established R0-004 marker selects `RequireExistingForPriorLifecycle`. This ownership-before-open adjustment is intentional: a missing or untrusted lifecycle claim cannot authorize mutation of an unrelated database. The journal then opens/configures SQLite, applies only permitted transactional migrations, validates the complete expected schema and constraints, requires `PRAGMA integrity_check` to return exactly one `ok` row, requires `PRAGMA foreign_key_check` to return zero rows, enables WAL, and only then reports healthy. The worker cannot persist the started session until that sequence succeeds. It prepares one ready state and timestamp without changing `CurrentState` or the marker, commits that exact ready milestone to SQLite, publishes the same state to the lifecycle marker and coordinator, and only then sets `AgentWorker` and `IAgentServiceHealth` ready. On a non-faulted stop, the worker first records the authoritative planned stop in the file marker and then persists the same stopped timestamp and planned classification in SQLite under the existing bounded shutdown timeout. A faulted worker writes neither clean-stop marker, so it cannot falsely classify the session as clean.

Lifecycle marker validation requires UTC and `started_at_utc <= ready_at_utc <= stopped_at_utc` for the timestamps that are present. Previously, readiness and stopped transitions used the raw wall clock even when it had stepped backward, so the service could write a marker that its next process rejected as contradictory. All service-produced transition timestamps now pass through one coordinator normalization rule: convert the supplied clock value to UTC and clamp it to the greatest persisted/current lower-bound lifecycle timestamp. A new session is no earlier than the prior accepted session's start, readiness, or stop; readiness is no earlier than its start; and planned stop is no earlier than its start or published readiness. Equality is valid and yields zero readiness elapsed time. The rule never adds a tick, performs arithmetic near `DateTimeOffset` limits, relies on process-only state, or changes already accepted historical timestamps. Genuine forward time still enforces the configured readiness timeout. Existing validation of malformed, non-UTC, contradictory, self-referential, or unsupported R0-003/R0-004 markers is unchanged.

`agent_sessions` represents a local, pre-registration lifecycle identity keyed only by the globally unique session ID. It deliberately has no `agent_id`: property enrollment, authenticated agent identity, credentials, and revocation belong to R0-006. Probe samples and material events retain their separate contract `agent_id` fields, while their local referential-integrity link targets `agent_sessions.session_id`. This avoids a shared sentinel or fake cloud identity and lets a later schema migration associate local evidence with an enrolled identity without rewriting lifecycle history.

A degraded journal remains observable through `IAgentJournal.Health` and a bounded structured error log, blocks readiness, and leaves the worker alive for a clean file-marker stop. `AgentWorker` is registered once and that same singleton is exposed as both the hosted service and `IAgentServiceHealth`, so worker readiness and the formal service-health surface cannot drift because of duplicate worker instances. Worker and formal service readiness now derive from the same facts: worker execution has not faulted, shutdown has not started, `AgentLifecycleCoordinator.CurrentState` is ready, and journal health is healthy. There is no separate post-marker readiness flag that a stale startup continuation can enable. The degraded log contains only reason code, schema version, and integrity result.

Every production journal degradation passes through one `IAgentJournalDegradationHandler`, implemented by the same singleton lifecycle coordinator. Journal health becomes degraded first, which immediately makes `AgentWorker` and `IAgentServiceHealth` not ready. The coordinator then clears `CurrentState` readiness before attempting the normal atomic lifecycle-marker replacement, so in-process readiness remains false even if that file write fails. A successful transition removes `ready_at_utc` and readiness elapsed from the durable marker; the Windows lifecycle readiness observer therefore cannot continue accepting stale readiness.

The unresolved race was between two separately serialized operations. Startup committed the journal ready milestone under the journal runtime-operation semaphore, released it, and later called `PublishReadyAsync` under only the lifecycle-coordinator semaphore. A concurrent session, verification, probe, event/outbox, or pending-outbox operation could acquire the journal semaphore in that gap, set `IAgentJournal.Health` to degraded, and invoke the lifecycle degradation handler while `CurrentState` was still not ready. That handler correctly had nothing to clear and returned. The already-started startup continuation then acquired the lifecycle gate and atomically replaced the marker with ready evidence from its stale prepared state. Journal health and formal service health were false, but the coordinator and durable marker were ready and the Windows lifecycle observer could accept the marker. Because the first degraded health reason is terminal, later ordinary operations did not invoke the handler again.

`IAgentReadinessTransitionGate` is now the single asynchronous ordering authority for journal initialization, runtime verification, every ordinary journal operation, and readiness publication. The ready-session milestone still commits first as a journal operation. Startup then reacquires the shared gate for `ReadinessPublication`, rechecks the live journal health inside the gate, and publishes only while it remains healthy. The ready linearization point is the atomic lifecycle-marker replacement followed immediately by the matching `CurrentState` assignment inside `PublishReadyAsync`, while both the transition gate and lifecycle gate remain held. The degradation linearization point is the first degraded health publication inside a journal operation holding the transition gate; that same operation completes coordinator and marker not-ready propagation before releasing the gate. Marker I/O intentionally remains inside both gates. The only nested lock order is transition gate then lifecycle gate; preparation and shutdown release the lifecycle gate before entering a journal operation, and no lifecycle transition re-enters the transition gate.

All competing orders therefore reduce to two outcomes:

- If readiness publication acquires the transition gate first, journal health is rechecked, the marker/coordinator publication completes, and all derived readiness surfaces become true together. A pending journal operation cannot inspect storage or change health until publication releases the gate. If it then detects degradation, health becomes degraded, worker/formal readiness becomes false immediately, and coordinator plus marker readiness are cleared. The final state is not ready everywhere.
- If a journal operation acquires the transition gate first, it establishes degraded health and completes not-ready propagation before release. Startup later acquires the gate, observes the degraded health, rejects publication, logs only the existing bounded journal reason, and waits for shutdown. Its prepared state and already-committed journal milestone never reach the marker or coordinator.

This ordering also covers a journal failure requested while ready-marker I/O is paused: the failing operation waits outside the transition gate and cannot publish degraded health until the older atomic marker write completes. It then becomes the later serialized transition and overwrites ready evidence with not-ready evidence. Conversely, a degradation completed in the pre-publication gap prevents the older prepared state from writing at all. There is no step after marker publication that independently enables worker or formal service readiness.

Runtime verification and ordinary session, probe, event/outbox, and pending-outbox operations use the same transition gate. The operation that detects storage failure establishes the first exact bounded reason, completes lifecycle propagation, and all later ordinary operations fail from health state without another SQLite open, retry, sequence allocation, event, probe, or outbox write.

If the lifecycle-marker replacement fails during runtime degradation, the service retains degraded journal health and false in-memory worker, coordinator, and formal service-health readiness; it never resumes or republishes readiness. The separate result is logged only as `lifecycle_marker_write_failed`, without exception text, paths, SQL, payloads, or identifiers. No journal transaction is invented for this transition and the already-degraded SQLite journal is not used to record it. The durable marker can necessarily remain stale until a later lifecycle write succeeds or the service restarts. On restart, the first lifecycle start replaces the prior marker with a new not-ready session before journal initialization; established journal ownership still prevents recreation of a missing database. This is the remaining conservative durable-state limitation when both runtime journal access and independent marker persistence fail at the same time.

Session persistence uses a single worker boundary for started, ready, and stopped milestones. Cancellation is rethrown unchanged. A ready failure before commit rolls back to the started milestone, retains the exact bounded degradation reason, never publishes readiness, blocks later ordinary journal work, and waits for shutdown without retry. A failure after the SQLite commit but before publication leaves the committed ready milestone intact while the marker, coordinator, worker, and service-health surfaces remain not ready; the unclean marker makes the next process start deterministic, and the existing owned journal is validated and reused. A degradation in that post-commit/pre-publication interval has the same durable SQLite outcome but wins the transition gate and rejects publication. A cancellation while readiness waits for the transition gate never enters the publication delegate. Cancellation during lifecycle-marker serialization cannot expose a partial JSON marker because the store writes a separate temporary file and replaces atomically; `CurrentState` changes only after replacement succeeds. No code falsifies or rolls back an already committed SQLite milestone. Stopped or planned-shutdown lifecycle state is never reported ready.

## Credential-bearing URL rejection

The previous defense-in-depth validation rejected known literal secret markers and absolute URIs containing `UserInfo`, but it did not inspect query or fragment parameter names. The reviewed synthetic form `https://camera.local/live?token=synthetic-value` therefore passed validation and could be written to both `material_events` and its replayable outbox payload. This was an input-validation gap, not a SQLite health failure.

The persistence boundary now treats the following exact query or fragment parameter names as sensitive, case-insensitively:

```text
access_token,
api_key,
api_token,
apikey,
auth_token,
client_secret,
connection-string-password,
connection_string_password,
credential,
credentials,
enrollment-token,
enrollment_token,
passwd,
password,
provider-secret,
provider_secret,
pwd,
secret,
sig,
signature,
token,
x-amz-credential,
x-amz-security-token,
x-amz-signature,
x-goog-api-key,
x-goog-credential,
x-goog-signature
```

The parser examines parameter names rather than values. It recognizes ordinary ampersand-separated query parameters, direct fragment parameters such as `#access_token=...`, and query-like parameters inside a fragment such as `#route?client_secret=...`. URI `UserInfo` remains prohibited. The existing authorization, bearer, camera, RTSP, password, enrollment-token, provider-secret, and other literal-marker rejections remain unchanged.

URI parameter syntax is inspected as supplied and after at most two deterministic ASCII percent-decoding passes. This detects single- or double-encoded sensitive names, underscores, equals signs, ampersands, and fragment-query separators without recursively decoding arbitrary input. Valid escapes are decoded without allocating unbounded buffers; malformed or non-ASCII escapes remain literal and cannot throw a decoding exception. If normal `Uri` parsing rejects malformed escaping but the input still has a syntactically valid absolute-URI scheme, bounded raw query and fragment inspection still applies. A malformed value attached to a sensitive raw parameter name is therefore rejected without exposing the value or degrading the journal.

The selected boundary is intentionally narrow. Non-credential-bearing absolute HTTPS URLs remain permitted, as do benign parameter names, values that merely contain words such as `token`, and unrelated words such as `tokenizer`. Known camera URL schemes remain prohibited by the existing policy. This preserves ordinary non-secret identifiers and evidence while preventing credential-bearing camera/provider URLs from entering local persistence or cloud replay.

The same centralized check covers every persisted string accepted by R0-004: session IDs; probe sample, agent, and session IDs; probe kind/state, target ID, and evidence; material-event, agent, property, and session IDs; target ID/label; prior/current, gateway, and internet state; and material-event evidence. The validated material event is the sole source of both the `material_events` row and serialized outbox payload, and its validated agent ID is the source of sequence state. Validation completes before the readiness transition gate, SQLite connection, immediate transaction, sequence allocation, payload serialization, event insert, and outbox insert. Rejection raises the existing bounded generic `ArgumentException`; it is outside the runtime storage-failure classifier, so it does not publish `runtime_write_failed`, degrade health, clear worker/coordinator/formal readiness, or rewrite the lifecycle marker. Later valid probe, event, sequence, and outbox operations remain available.

Deterministic integration coverage rejects the reviewed token-query form before any event, outbox, or sequence mutation; exercises every listed key plus mixed case, later position, multiple parameters, query and fragment forms, single/double encoding, and malformed escaping; verifies all persisted string surfaces; retains URI `UserInfo` and legacy marker rejection; and accepts the documented benign boundaries. After checkpoint and connection close, neither the complete rejected URL nor its synthetic credential value is present in database, WAL, or SHM artifacts. A real-host test proves journal health, `AgentWorker`, `AgentLifecycleCoordinator.CurrentState`, `IAgentServiceHealth`, and the durable ready marker remain coherent, captures no rejection log or exception, and completes later valid probe, material-event, sequence, and outbox work.

## Duplicate probe retry semantics

`ProbeSampleWrite.SampleId` is caller-supplied and is the `probe_samples.sample_id` primary key. The original insertion used an unconditional `INSERT`, so a legitimate retry of an already committed sample raised a SQLite primary-key exception. The common runtime storage-failure boundary then classified that expected domain collision as `runtime_write_failed`, terminally degraded the otherwise healthy journal, propagated not-ready state to every readiness surface, and blocked later ordinary work.

`IAgentJournal.AppendProbeSampleAsync` returns `Task` and defines no duplicate result object or duplicate exception. Its established duplicate contract is therefore idempotent success: the first accepted sample is durable, and later submissions with the same `sample_id` complete as successful no-ops. The insert now uses `ON CONFLICT(sample_id) DO NOTHING`. This is an atomic conflict target for only the probe primary key; it is not `INSERT OR IGNORE`, exception-message parsing, a read-before-insert check, or broad classification of SQLite constraint errors.

Same-ID/same-content and same-ID/different-content retries both retain the original durable row byte-for-value at the column level. The retry never replaces, updates, or merges the stored timestamp, session, agent, probe kind/state, target, or evidence. Concurrent submissions serialize through SQLite's unique primary-key enforcement: exactly one row commits and every other caller receives the same successful no-op outcome. Duplicate handling emits no degradation reason or duplicate log and does not expose sample IDs, payloads, SQL, paths, or customer/LAN data.

All non-`sample_id` failures retain the existing fail-closed path. A new probe ID with a missing session still raises the foreign-key constraint, becomes bounded `runtime_write_failed`, clears readiness through the shared transition gate, and blocks later work. CHECK, NOT NULL, schema, database I/O, read-only/full/locked-after-policy, transaction, corruption, lifecycle, and unexpected constraint failures are not suppressed. Pre-cancellation still propagates `OperationCanceledException` without writing or degrading. Deterministic integration coverage proves exact retry, changed-content retry, coordinated concurrent retry, original-row preservation, later valid probe activity, foreign-key classification, and cancellation. A real-host test additionally proves duplicate handling leaves `AgentWorker`, `AgentLifecycleCoordinator.CurrentState`, `IAgentServiceHealth`, and the durable lifecycle marker ready and unchanged; later probe, sequence, material-event, and outbox operations remain available, with no `runtime_write_failed` or `lifecycle_marker_write_failed` log.

The configured database path must be absolute. The default is `RemotePropertyWatchdog/Agent/agent-journal.db` below the operating system's common application-data directory. Logs contain schema/health codes and sequence numbers, not database paths, payloads, target labels, evidence contents, or exception text.

## Schema version and migration behavior

- Current `PRAGMA user_version`: **1**.
- `schema_migrations` records `R0-004-initial-journal-outbox` once.
- Version 0 is adopted only when the file has no application-defined tables, indexes, views, or triggers. That ownership check and the version 0-to-1 migration share one immediate transaction, so an unrelated SQLite file is preserved at the schema/data level and enters `database_not_owned` without a migration or WAL mode change. A genuinely empty version 0 journal upgrades to version 1, and concurrent journal instances either apply the migration once or observe the already-supported version.
- `InitializeAsync` requires an `AgentJournalInitializationMode`. `CreateIfMissingForFreshLifecycle` is authorized only after the atomic lifecycle marker has durably crossed to ownership version 1 from either no marker or validated legacy R0-003 evidence. It may create the database and parent directory by opening a missing path with `ReadWriteCreate`.
- If a process stops before the ownership claim, no create-capable journal operation occurred and the validated legacy path may be retried. If it stops after the claim but before database creation, migration, validation, or session commit, the retained ownership version fails closed: the next start requires the journal and reports `database_missing` if it is absent.
- `RequireExistingForPriorLifecycle` is selected from established ownership rather than incidental previous-session presence. It checks for the existing journal before opening it. A missing journal becomes degraded with `database_missing` and `integrity_result=not_run`; no connection is opened, no migration runs, and the database, WAL, and SHM remain absent.
- Missing, malformed, chronologically inconsistent, or unsupported lifecycle evidence cannot authorize creation. Authentic R0-003 markers remain readable because the new ownership field is optional; an unsupported non-null ownership version is rejected.
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

Every connection enables foreign keys, uses a bounded busy timeout, and sets `PRAGMA synchronous=FULL`. `PRAGMA foreign_keys=ON` prevents ordinary compliant connections from creating new violations, but it does not prove that a pre-existing owned journal has no orphaned rows. `PRAGMA foreign_key_check` supplies that complete relational validation and supplements rather than replaces `PRAGMA integrity_check`, which validates SQLite structure but can still return `ok` for orphaned evidence. Initialization enables and verifies WAL only after ownership/migration, schema validation, structural integrity, and foreign-key integrity all succeed. The migration, session, and event/outbox writes use explicit immediate transactions where monotonic merge or atomic allocation requires them. Database pooling is disabled so test/process boundaries do not retain hidden handles.

Initialization and explicit verification use the same definition-level validator and run both integrity pragmas. The validator creates an isolated in-memory reference schema directly from the exact `MigrationVersion1` SQL and shared migration identity, then compares it with the candidate. Structured `table_list`, `table_xinfo`, `foreign_key_list`, `index_list`, and `index_xinfo` metadata verifies exact application-object inventory; table column count/order, names, declared types, nullability, defaults, primary-key ordering, hidden-column kind, STRICT and WITHOUT ROWID status; foreign-key source/target columns, referenced tables, match mode, and update/delete actions; and index membership/order, uniqueness, origin, partial status, sort direction, collation, and key-column status. A strict `sqlite_schema.sql` comparison, normalizing only whitespace outside quoted values/identifiers, additionally binds required CHECK constraints and complete DDL token/literal/constraint ordering to that same migration source. The migration inventory must contain exactly version 1 and `R0-004-initial-journal-outbox`, while `PRAGMA user_version` must be exactly 1.

Runtime verification opens only the existing file and requires exactly one integrity row equal to:

```text
ok
```

It then consumes `PRAGMA foreign_key_check` only as a zero-row requirement. Any returned row fails validation without reading or exposing the affected table, nullable row ID, parent table, or constraint identifier. Any other structural-integrity output, any foreign-key row, invalid/truncated/non-database input, missing required schema, or SQLite open/integrity execution/read error produces an observable `AgentJournalHealthState.Degraded` with bounded `reason_code=integrity_check_failed` and `integrity_result=failed` for the integrity boundary. Logs and health never include table names, row IDs, constraint IDs, SQL, exception text, database paths, payloads, or customer/LAN information. Initialization additionally uses `database_invalid`, `database_not_owned`, `initialization_failed`, and `schema_too_new`. Runtime opening and verification use `database_missing`, `database_open_failed`, `schema_validation_failed`, and `integrity_check_failed`; runtime persistence maps provider/I/O/access/format failures to `runtime_write_failed`, and pending-outbox reads map those failures to `runtime_read_failed`. Cancellation, validation failures, duplicate/session conflicts, and synthetic commit fault-injection exceptions remain their original non-degrading outcomes. An orphaned owned journal is preserved exactly for diagnosis: there is no repair, row deletion, rewrite, replacement, migration to a misleadingly healthy state, or silent recreation, and no startup-session or normal journal work occurs after rejection.

`Microsoft.Data.Sqlite` exposes asynchronous ADO.NET methods but SQLite performs underlying file I/O synchronously. The boundary remains task-based and cancellation-aware: tokens are checked before initialization and immediate transactions, passed to every provider operation, and checked again before commit. Tests verify pre-canceled operations propagate cancellation without marking a healthy database degraded.

## Review evidence

The automated test suite demonstrates:

- fresh migration, table/index/foreign-key inventory, WAL, FULL synchronization, and schema version 1;
- idempotent initialization, empty version 0 upgrade, preservation and `database_not_owned` degradation for an unrelated version 0 SQLite schema, and two coordinated journal instances applying exactly one migration record;
- real worker startup persisting the exact file-backed session ID and started milestone, preparing readiness without publishing it, committing the identical ready timestamp to SQLite, and only then publishing lifecycle, marker, worker, and formal service-health readiness;
- a blocked ready-session transaction leaving every readiness surface false and all probe/event/outbox/sequence tables empty until commit is released;
- storage failure during the first, second (ready), and third (planned-stop) session persistence calls preserving `runtime_write_failed`, rolling back the failed transaction, retaining not-ready state without a transient marker, blocking later writes without retry, and leaving probe/event/outbox/sequence tables untouched;
- a synthetic post-commit/pre-publication failure leaving the SQLite ready milestone durable, all external readiness surfaces false, and the marker unclean; restart requires and reuses that existing journal and reaches readiness through a new session;
- a genuine first real-host lifecycle start with no marker or database claiming ownership, selecting the explicit creation mode, creating schema version 1, and becoming ready;
- an exact representative R0-003 lifecycle marker with no journal upgrading successfully, persisting its first R0-004 session, restarting against the same journal without losing the prior session, and refusing recreation with `database_missing` after established ownership and later deletion;
- interruption before ownership claim remaining safely retryable, while interruption after the atomic claim and before journal creation fails closed as `database_missing` without DB/WAL/SHM artifacts;
- malformed or unsupported lifecycle ownership evidence failing before journal initialization and never authorizing creation;
- clean worker shutdown persisting the identical stopped timestamp and `planned_shutdown=true` after the authoritative file marker, while faulted shutdown remains unclean in both stores;
- corrupt real-host startup remaining visibly degraded, not ready, and byte-for-byte unmodified through clean shutdown;
- session exact/stale retry non-regression, forward milestone progression, deterministic conflict rejection, and chronology validation;
- first synthetic probe and material event writes satisfying the local-session foreign key without adding a fake or authenticated agent identity to the session;
- same-content and changed-content duplicate probe retries completing as idempotent no-ops, retaining exactly one unchanged original row, preserving healthy journal state, and permitting a later distinct probe;
- twelve coordinated concurrent submissions for one probe `sample_id` committing exactly one row with no exception, degradation, deadlock, partial transaction, arbitrary delay, or timing-based assertion;
- a new probe ID with an invalid session retaining bounded terminal `runtime_write_failed` behavior, creating no row or foreign-key violation, and blocking later ordinary work;
- real-host duplicate handling preserving worker, coordinator, formal service-health, and durable marker readiness while later probe, sequence, material-event, and outbox activity succeeds and no degradation reason is logged;
- all-or-none rollback before commit and both rows after a post-commit fault;
- restart continuation at the next committed sequence;
- deterministic duplicate-ID rejection without an outbox row or sequence gap;
- stable outbox order and 20 coordinated concurrent writers with unique contiguous sequences;
- visible degraded state for synthetic corrupt/truncated/non-database files without replacement;
- structurally valid owned journals with an orphan in each declared relationship (`probe_samples.session_id` to `agent_sessions.session_id`, `material_events.session_id` to `agent_sessions.session_id`, and `outbox.event_id` to `material_events.event_id`) retaining `integrity_check=ok` while initialization rejects them as `integrity_check_failed`; all rows, schema/version, DELETE mode, and zero WAL/SHM state remain unchanged, and all later session/probe/event/sequence/outbox operations stay blocked;
- explicit verification detecting and preserving an orphan introduced after healthy startup, plus deterministic foreign-key result-read failure entering the same bounded fail-closed classification;
- a real owned-journal restart with an orphan writing no new startup session or normal journal rows, keeping `AgentWorker`, `AgentLifecycleCoordinator.CurrentState`, `IAgentServiceHealth`, and the durable lifecycle marker not ready, emitting only bounded non-sensitive degradation fields, and preserving the orphan through planned host shutdown;
- healthy definition-level schema-plus-integrity verification, uninitialized missing verification/operations remaining non-creating, deleted-database preservation without recreation, empty-replacement preservation without migration, and detection of dropped tables, indexes, and migration records even when `integrity_check` returns `ok`;
- initialization and explicit verification rejection of integrity-clean look-alike databases with altered columns/types/order/nullability/defaults/primary keys, missing STRICT, altered identifier/sequence/JSON/Boolean CHECK constraints, missing or redirected foreign keys and actions, wrong index columns/order/uniqueness/partial predicates/sort/collation, unexpected application objects, or incorrect/additional migration evidence; invalid startup candidates retain their definitions, data, version, and journal mode without repair or WAL conversion;
- runtime session, probe, material-event/outbox, and pending-outbox-read storage failures entering bounded `runtime_write_failed` or `runtime_read_failed` degradation and blocking later operations, while cancellation, validation, duplicate/session conflicts, and synthetic pre/post-commit faults do not spuriously degrade health;
- the production readiness transition gate preventing journal operations from overtaking ready publication and preventing a canceled waiter from entering;
- degradation immediately before publication rejecting a stale prepared ready state after the journal milestone has committed;
- degradation requested after readiness acquires the gate, during a blocked ready-marker write, and after marker/coordinator publication serializing after readiness and then clearing every durable and in-process surface;
- all three readiness/degradation orders repeated deterministically without sleeps, with no final ready or mixed state while journal health is degraded;
- a ready marker already published while startup remains inside the transition gate exposing no separate worker/formal-health enablement step because both derive from coordinator state and journal health;
- degradation marker-write failure during the race retaining false journal, worker, coordinator, and formal readiness, blocking all ordinary activity, logging only `lifecycle_marker_write_failed`, and preventing the old startup path from running again;
- cancellation while publication waits outside the gate retaining the committed journal milestone but never publishing partial external readiness;
- backward and equal clock behavior during each repeated race producing a chronological planned-stop marker, with restart replacing stale ready evidence conservatively before a new readiness publication;
- backward and equal wall-clock steps across session start/restart, ready publication, runtime not-ready propagation, and planned stop producing UTC markers that preserve chronology and remain readable after restart without artificial tick advancement;
- genuine database loss after committed-and-published readiness immediately making journal, worker, coordinator, shared service health, and the persisted lifecycle marker not ready, preserving `database_missing`, blocking every later ordinary operation, and remaining conservative on restart;
- combined runtime degradation and a five-minute backward clock step producing a valid not-ready marker and equality-safe planned-stop/restart timestamps;
- lifecycle-marker write failure during runtime degradation retaining false in-process readiness and blocked journal work, emitting only `lifecycle_marker_write_failed`, leaving the unavoidable stale durable marker explicit, and replacing it with conservative not-ready evidence on restart;
- credential-bearing absolute-URL rejection for the complete bounded query/fragment key policy, URI `UserInfo`, case/position/multiple-parameter forms, single/double percent encoding, malformed escaping, every persisted string surface, benign URL/subword boundaries, pre-sequence/event/outbox validation, byte-level database/WAL/SHM absence, bounded logs, preserved real-host readiness, and later valid journal work;
- cancellation propagation.

## Validation

The P1 credential-bearing URL correction was validated with:

- `python scripts/validate-kickoff.py`
- `python scripts/scan-secrets.py`
- `dotnet restore ./RemotePropertyWatchdog.sln`
- `dotnet build ./RemotePropertyWatchdog.sln --configuration Release --no-restore`
- `dotnet test ./RemotePropertyWatchdog.sln --configuration Release --no-build`
- `pwsh ./scripts/test.ps1`
- `dotnet list ./RemotePropertyWatchdog.sln package --vulnerable --include-transitive`
- `git diff --check`

Final authorized local result: **171 passed, 0 failed, 0 skipped** (72 unit, 90 integration, 8 contract, 1 build). Both the direct Release suite and `scripts/test.ps1` passed with those totals. The Release build completed with 0 warnings and 0 errors. Kickoff validation, the unchanged secret scanner, the aggregate repository test script, diff checks, and generated database/WAL/SHM and path/data scans passed. The vulnerability audit found no known vulnerable direct or transitive packages.

The sandbox-only direct suite produced the established environment diagnostic: 160 tests passed and 11 host-heavy tests failed only because the sandbox identity could not write the Windows Event Log. The first aggregate invocation stopped at the unchanged secret scanner because contiguous synthetic policy examples resembled credential assignments; the documentation and tests were represented without scanner suppression or allow-list changes, and the scanner then passed. The sandbox vulnerability audit could not authenticate its TLS connection to NuGet. The unchanged direct suite, aggregate script, and vulnerability audit were rerun through the authorized normal Windows project context and passed. No production logging or security behavior was weakened.

The repository ignore rules cover `*.db`, `*.sqlite`, and `*.sqlite3` databases plus their `-journal`, `-wal`, and `-shm` companions. Generated databases use operating-system temporary directories and are not committed. Test identities and values are synthetic; no customer or LAN data is used.

R0-004 performs no automatic retention deletion. The prototype default is to retain journal evidence until a later reviewed retention policy is implemented; this avoids destructive recovery behavior but leaves disk-growth tuning as a known follow-up risk. SQLite is additive rather than the lifecycle recovery authority. A failed ready-milestone transaction leaves SQLite at the started milestone and never publishes lifecycle readiness; a failed planned-stop transaction can leave SQLite conservatively behind the authoritative clean file marker; and a post-commit/pre-publication failure can leave SQLite ahead of an unready, unclean marker. None of these paths fabricates persisted evidence, retries a degraded journal, or rolls back a durable commit. If the independent lifecycle marker is deleted but an existing valid journal remains, startup claims ownership and validates that journal without replacing it. If an operator deletes both the lifecycle marker and SQLite journal, no retained evidence remains by which this prototype can distinguish loss from a genuine first install, so it will treat that state as fresh and cannot recover the deleted history. Enrollment and binding the contract `agent_id` to a property remain R0-006 work.
