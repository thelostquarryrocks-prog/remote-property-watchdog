# R0-011 — Offline event replay and reconciliation

Priority: P0  
Release: R0  
Depends on: R0-004, R0-007, R0-009

## Objective

Upload locally queued events after recovery in order and merge them into the incident without duplication.

## In scope

- event batch endpoint.
- sequence checkpoints and acknowledgments.
- newest heartbeat first, then ordered batches.
- partial retry and duplicate handling.
- incident enrichment after replay.

## Out of scope

- bulk historical analytics.
- media upload.

## Implementation notes

- Cloud acknowledgment is explicit and durable.
- Agent resumes from last acknowledged sequence.
- Received time never replaces observed time.

## Acceptance criteria

- Four-hour outage replays every material event exactly once logically.
- Duplicate batch creates no duplicate evidence/incident/notification.
- Partial failure resumes from correct checkpoint.
- Clock anomaly is flagged while sequence ordering remains intact.
- Restoration message waits for required stable/evidence policy.

## Required tests

- integration tests with forced disconnect/reconnect.
- duplicate/partial batch tests.
- restart on both agent and worker.
- material clock-change test.

## Required evidence

- Before/after database counts and incident timeline.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
