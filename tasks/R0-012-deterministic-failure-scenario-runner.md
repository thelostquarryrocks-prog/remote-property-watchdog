# R0-012 — Deterministic failure scenario runner

Priority: P0  
Release: R0  
Depends on: R0-005, R0-008, R0-009, R0-010, R0-011

## Objective

Create a repeatable simulation harness for the controlled failure matrix without waiting in real time.

## In scope

- virtual clock.
- fake probe outcomes.
- heartbeat/event transport faults.
- provider faults.
- scenario result report with timings and duplicates.

## Out of scope

- physical device control.
- production chaos platform.

## Implementation notes

- Scenarios must be declarative and reproducible by seed/version.
- Harness may not bypass production state-machine code.

## Acceptance criteria

- Automated versions of CF-01, 02, 04, 06, 08, 09, 10, 11, 12, 13, 14, 15, 16, 17, 18 pass.
- Reports show expected vs actual classification/timing.
- Scenario failures produce actionable diffs.
- CI runtime remains practical.

## Required tests

- scenario unit/integration suite.
- reproducibility test.
- report schema test.

## Required evidence

- Machine-readable and Markdown scenario report.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
