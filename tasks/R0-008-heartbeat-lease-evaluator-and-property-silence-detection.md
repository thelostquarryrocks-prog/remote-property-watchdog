# R0-008 — Heartbeat lease evaluator and property-silence detection

Priority: P0  
Release: R0  
Depends on: R0-007

## Objective

Open exactly one property-unreachable incident after the configurable missed-heartbeat threshold and transition through recovering/resolved states.

## In scope

- 30-second prototype cadence model.
- late/internal state.
- three misses plus 15-second grace default.
- 15-second evaluator.
- recovering and two-heartbeat restoration.
- concurrency-safe evaluator.

## Out of scope

- notification delivery.
- local cause enrichment.

## Implementation notes

- Use injected time and database concurrency control/uniqueness.
- Evaluator retries and concurrent workers must remain idempotent.

## Acceptance criteria

- 20 of 20 scripted silence tests open exactly one incident.
- Concurrent evaluator invocations cannot duplicate incidents.
- Late heartbeat during grace prevents incident.
- Delayed heartbeat after open starts recovery but does not delete timeline.
- Two stable heartbeats resolve according to policy.

## Required tests

- State-machine unit tests with fake time.
- concurrent integration test.
- worker restart test.
- clock-skew test.

## Required evidence

- Timing trace for healthy→late→unreachable→recovering→resolved.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
