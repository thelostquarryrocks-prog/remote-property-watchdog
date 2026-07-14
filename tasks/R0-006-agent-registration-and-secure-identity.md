# R0-006 — Agent registration and secure identity

Priority: P0  
Release: R0  
Depends on: R0-002, R0-003, R0-004

## Objective

Pair one agent to one property using a single-use enrollment token and issue revocable per-agent credentials.

## In scope

- cloud enrollment-token issuance/consumption.
- agent key/credential generation and protected local storage.
- authenticated agent requests.
- revocation and re-enrollment.
- audit records.

## Out of scope

- customer billing/authentication.
- certificate PKI unless selected by a new ADR.

## Implementation notes

- Token is short-lived, single-use, property-scoped, and never logged.
- Credentials are protected with Windows-bound storage and strict ACLs.
- Revoked agent cannot send heartbeats/events.

## Acceptance criteria

- Reusing enrollment token fails.
- Agent cannot register to a foreign property.
- Revocation takes effect within defined cache period.
- Credential material is absent from logs/support outputs.
- Registration is auditable.

## Required tests

- API integration tests.
- tenant boundary tests.
- token replay/expiry tests.
- local secret-protection test.

## Required evidence

- Enrollment sequence diagram.
- Redacted audit/log examples.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
