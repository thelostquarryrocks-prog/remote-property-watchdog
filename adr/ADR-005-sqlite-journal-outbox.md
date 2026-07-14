# ADR-005 — SQLite journal outbox

Status: Accepted

## Context

Outages require durable local evidence and replay without a separate local database service.

## Decision

Use SQLite for local state, evidence journal, and transactional outbox.

## Consequences

- Use crash-safe transactions and integrity checks.
- Keep unresolved incident evidence under retention pressure.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
