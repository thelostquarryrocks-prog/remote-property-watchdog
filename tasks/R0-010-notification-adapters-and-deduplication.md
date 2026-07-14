# R0-010 — Notification adapters and deduplication

Priority: P0  
Release: R0  
Depends on: R0-008, R0-009

## Objective

Send one logical initial alert and one linked restoration through provider-neutral email/SMS adapters.

## In scope

- fake provider; one email provider sandbox; one SMS provider sandbox.
- delivery attempt records.
- dedupe key per incident/recipient/channel/stage.
- retry/backoff and provider callback verification.
- test-notification function.

## Out of scope

- marketing messages.
- unlimited contact escalation.
- provider failover unless time permits.

## Implementation notes

- Do not call live providers in unit/integration CI.
- Consent/verification status is required before SMS to a trusted contact.
- Provider response IDs are data, not idempotency authority.

## Acceptance criteria

- Concurrent workers produce one logical initial send.
- Retry after uncertain provider result does not create a second logical record.
- One restoration links to original incident.
- Invalid callback signature changes no state.
- Templates preserve confirmed/interpretation/limitation separation.

## Required tests

- fake-provider unit tests.
- concurrency/dedup integration tests.
- callback signature tests.
- sandbox manual test record.

## Required evidence

- Delivery timeline and redacted provider receipts.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
