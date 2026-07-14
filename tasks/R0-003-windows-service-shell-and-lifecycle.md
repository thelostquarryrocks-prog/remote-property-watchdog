# R0-003 — Windows Service shell and lifecycle

Priority: P0  
Release: R0  
Depends on: R0-001, R0-002

## Objective

Create a native Windows Service that starts automatically, reports readiness, and shuts down/restarts without losing lifecycle evidence.

## In scope

- Worker service project.
- Windows Service integration.
- startup/session IDs; planned shutdown and unclean-restart markers.
- loopback readiness endpoint or temporary status output.
- structured lifecycle logs.

## Out of scope

- Network probes.
- Installer packaging.
- automatic updates.

## Implementation notes

- Service must run interactively in development and as a Windows Service in test/dogfood.
- Use cancellation tokens.
- A crash must be distinguishable from planned shutdown on next start.

## Acceptance criteria

- Service starts after reboot without user login.
- Monitoring-ready state occurs within 60 seconds after network availability.
- Planned stop records a clean lifecycle event.
- Forced termination produces an unclean previous-session indicator after restart.
- Logs contain no secrets.

## Required tests

- Unit tests for lifecycle state.
- Host smoke test.
- Windows integration script for install/start/stop/restart.
- ten-restart acceptance procedure documented.

## Required evidence

- Service status screenshots/log excerpts.
- Restart test record.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
