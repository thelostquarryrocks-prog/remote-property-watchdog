# R0-014 — End-to-end simulated outage gate

Priority: P0  
Release: R0  
Depends on: R0-001 through R0-013

## Objective

Demonstrate the complete logical path from healthy agent to remote alert, offline evidence, restoration, replay, and enriched incident.

## In scope

- deploy local agent and cloud test environment.
- register agent and configure targets.
- simulate WAN loss while LAN remains active.
- verify remote alert timing.
- continue local probes and queue evidence.
- restore path, replay evidence, and send one restoration.

## Out of scope

- commercial UI.
- external beta users.

## Implementation notes

- Use a fresh environment and documented build.
- Capture server and agent version, configuration, and timestamps.

## Acceptance criteria

- AC-001, AC-002, AC-005, AC-006, and AC-007 pass for the end-to-end path.
- Incident initially states cause unknown.
- Replay can enrich to internet unavailable when evidence supports it.
- Exactly one initial and one restoration logical SMS.
- No secrets appear in captured evidence.

## Required tests

- repeated end-to-end scenario at least 20 times for silence uniqueness.
- worker/agent restart during selected runs.
- failure evidence review.

## Required evidence

- Signed/identified build artifacts.
- Completed acceptance test record.
- Demo recording or screenshots.
- Defect list and disposition.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
