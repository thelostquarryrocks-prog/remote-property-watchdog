# R0-009 — Incident state machine and evidence model

Priority: P0  
Release: R0  
Depends on: R0-004, R0-005, R0-008

## Objective

Create deterministic incident types and evidence timelines that separate confirmed observations, interpretations, and limitations.

## In scope

- property unreachable; internet unavailable; gateway/local network unavailable; critical device offline; service unavailable; monitoring stopped; shared dependency; sequential failure; restoration.
- incident/evidence persistence.
- severity and Away-mode rules.
- parent/child correlation and suppression.

## Out of scope

- machine-learning classification.
- claims of criminal intent.

## Implementation notes

- Sequential pattern: at least two independent critical targets within ten minutes while gateway/internet remain healthy.
- Component failures may appear as child evidence under one pattern incident.
- Customer headline must be a confirmed observation.

## Acceptance criteria

- Same evidence always produces same state transition.
- Single camera with healthy upstream never becomes property outage.
- Sequential failures create one pattern incident, not an SMS storm.
- Every incident records limitations.
- Late replay may enrich classification without rewriting historical observations.

## Required tests

- exhaustive state-transition unit tests.
- classification table tests.
- property-vs-platform separation test.
- alert-language snapshot tests.

## Required evidence

- Incident examples for five core scenarios.
- Criterion trace to CF matrix.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
