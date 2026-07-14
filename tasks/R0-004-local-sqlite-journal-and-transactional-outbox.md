# R0-004 — Local SQLite journal and transactional outbox

Priority: P0  
Release: R0  
Depends on: R0-001, R0-002, R0-003

## Objective

Persist local state and material evidence so cloud loss or process restart cannot lose or duplicate events.

## In scope

- Versioned SQLite schema.
- probe samples/material events/outbox/config/session tables.
- transactional event + outbox write.
- stable event ID and monotonic agent sequence.
- integrity check and safe degraded mode.

## Out of scope

- Retention tuning beyond prototype defaults.
- cloud acknowledgment logic (R0-011).

## Implementation notes

- Enable appropriate WAL/crash-safe settings.
- Use migrations and idempotent initialization.
- Never store plaintext camera/provider credentials.

## Acceptance criteria

- Crash between state transition and commit yields either all or none.
- Restart resumes next sequence without reuse.
- Database corruption enters visible degraded state.
- Duplicate event IDs are rejected.
- Outbox order is stable.

## Required tests

- SQLite migration tests.
- transaction rollback/crash simulation.
- restart/sequence test.
- corruption handling test.
- secret-redaction/storage inspection.

## Required evidence

- Schema diagram/table inventory.
- Test database integrity output.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
