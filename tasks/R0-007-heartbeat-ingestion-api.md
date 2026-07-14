# R0-007 — Heartbeat ingestion API

Priority: P0  
Release: R0  
Depends on: R0-002, R0-006

## Objective

Accept authenticated, idempotent heartbeats and renew the agent lease using server receive time.

## In scope

- heartbeat endpoint.
- payload validation and size limit.
- idempotency/sequence handling.
- current status projection.
- structured metrics/logging.

## Out of scope

- missed-heartbeat incident evaluation.
- event-batch ingestion.

## Implementation notes

- The server receive time is authoritative for lease freshness.
- Delayed heartbeat does not erase incident history.
- Foreign agent/property data is never disclosed.

## Acceptance criteria

- Valid heartbeat renews one lease.
- Duplicate heartbeat causes no duplicate state change.
- stale/out-of-order heartbeat is recorded according to contract without moving current sequence backward.
- invalid/oversized payload returns stable error code.
- tenant isolation passes.

## Required tests

- HTTP integration tests with PostgreSQL.
- duplicate/out-of-order tests.
- authentication and tenant tests.
- payload-size test.

## Required evidence

- Example request/response.
- Lease projection query evidence.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
