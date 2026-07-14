# R0-002 — Shared contracts and canonical enums

Priority: P0  
Release: R0  
Depends on: R0-001

## Objective

Implement versioned shared contracts for heartbeats, material events, incidents, errors, and configuration without coupling domain logic to transport models.

## In scope

- Canonical enums/value objects.
- Heartbeat and event-batch DTOs matching `schemas/`.
- Validation and serialization settings.
- Stable error envelope and idempotency-key conventions.

## Out of scope

- Database entities.
- Incident correlation logic.
- Provider-specific models.

## Implementation notes

- Use explicit schema version fields.
- Reject unknown required values safely while allowing additive optional fields.
- UTC timestamps and agent sequence are mandatory.

## Acceptance criteria

- Example payloads round-trip without field loss.
- Invalid IDs, missing sequence, oversized heartbeat, and non-UTC time fail validation.
- JSON output is deterministic enough for contract snapshots.
- Transport DTOs do not reference infrastructure projects.

## Required tests

- Unit tests for validation.
- Contract tests against every JSON schema.
- Backward-compatible additive-field test.

## Required evidence

- Generated/checked example JSON.
- Contract matrix linking DTOs to schemas.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
