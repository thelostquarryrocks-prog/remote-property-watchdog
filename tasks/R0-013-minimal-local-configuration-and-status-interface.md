# R0-013 — Minimal local configuration and status interface

Priority: P0  
Release: R0  
Depends on: R0-003, R0-005, R0-006

## Objective

Provide a loopback-only interface to register the agent, define critical targets, view readiness, and run notification/probe tests.

## In scope

- bind only to loopback.
- property/agent status.
- gateway/internet current state.
- target CRUD with labels and TCP settings.
- Away/Normal mode.
- sleep/readiness warnings.
- test notification and recent events.

## Out of scope

- polished customer design.
- LAN/public access.
- remote command console.

## Implementation notes

- Require local anti-CSRF protection and reasonable local authorization boundary.
- Camera credentials, if experimental, are never rendered back in plaintext.

## Acceptance criteria

- Interface is unreachable from another LAN host.
- Invalid target config cannot replace last-known-good config.
- Protection status clearly shows active/degraded/not-ready and reason.
- Test notification records the outcome.
- User can label a camera and monitor its TCP service.

## Required tests

- loopback binding integration test.
- configuration validation tests.
- browser smoke test.
- secret-display test.

## Required evidence

- Screenshots and configuration export with secrets absent.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
