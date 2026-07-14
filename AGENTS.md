# AGENTS.md — Remote Property Watchdog

## Mission

Build a dependable remote-property connectivity and security-infrastructure watchdog. The product must tell a property owner when the property, internet connection, monitoring agent, camera, recorder, or selected critical device stops functioning. Reliability, evidence integrity, privacy, and honest uncertainty take priority over feature breadth.

## Instruction precedence

1. This file contains repository-wide non-negotiable rules.
2. The assigned file in `tasks/` defines the permitted scope for that task.
3. Files in `docs/` define product and architecture requirements.
4. Accepted files in `adr/` define architecture decisions.
5. Tests and schemas define executable contracts.

When sources conflict, stop expanding scope, document the conflict, and prefer the safer behavior. Do not silently invent a new product rule.

## Required working method

For every task:

1. Read the assigned task, its dependencies, and relevant docs/ADRs.
2. Inspect existing code and tests before proposing changes.
3. State a compact implementation plan in the task thread.
4. Modify only what is needed for the assigned task.
5. Add or update automated tests for all material behavior.
6. Run the task's required commands plus `pwsh ./scripts/test.ps1` when available.
7. Review the final diff for secrets, privacy leaks, scope creep, and misleading alert language.
8. Report files changed, commands/results, acceptance status, assumptions, and risks.
9. Do not start the next ticket unless explicitly assigned.

## Non-negotiable architecture rules

- Initial edge runtime: native Windows Service on .NET 10 LTS.
- Normal connectivity: outbound TLS/HTTPS only.
- Do not open router ports, expose a LAN web server, create a general remote shell, or add an inbound tunnel.
- LAN discovery and health probes execute locally at the property.
- Local high-frequency evidence uses SQLite and a transactional outbox.
- Cloud system of record uses PostgreSQL.
- Cloud begins as a modular monolith with background workers, not microservices.
- Camera, DVR/NVR, and network credentials remain local and protected using Windows-bound secure storage.
- Do not upload video, audio, snapshots, or camera credentials in R0.
- All cloud writes must be tenant-scoped, authenticated, idempotent where retried, and auditable.
- Heartbeats and material events require stable IDs, sequence numbers, UTC timestamps, and server receive timestamps.
- A complete communication loss must remain detectable from the cloud after the property cannot transmit.

## Safety and trust language

The system observes technical conditions. It usually cannot prove why they occurred.

Keep these layers separate:

- **Confirmed observation:** what the system directly measured.
- **Interpretation:** a likely technical explanation supported by evidence.
- **Limitation:** what the system cannot distinguish.
- **Security implication:** a cautious reason the user may wish to inspect the property.

Never generate customer text such as “intruder,” “sabotage confirmed,” or “someone cut the line” from network evidence alone. Approved language includes “possible disconnection,” “unusual sequential failure pattern,” and “cause unknown until additional evidence is available.”

## Security and privacy rules

- Never commit credentials, tokens, phone numbers, provider secrets, connection strings, or real customer network data.
- Redact authorization headers, secrets, camera URLs, usernames, and passwords from logs and support bundles.
- Use synthetic data in automated tests.
- Do not weaken TLS, authentication, authorization, signing, or secret storage merely to simplify local development.
- Do not add telemetry containing raw LAN inventory beyond the minimum approved fields.
- All support and administrative actions must be auditable and least-privileged.
- New production dependencies require a documented license/security review and, when architectural, an ADR.

## Code and design conventions

- Prefer clear modules and explicit interfaces over clever abstractions.
- Use nullable reference types, warnings as errors, deterministic builds, and analyzers.
- Use async I/O for network and persistence work; accept cancellation tokens on long-running operations.
- Use dependency injection at module boundaries.
- Use `TimeProvider` or an equivalent injected clock for timing-sensitive tests.
- Keep state machines deterministic and pure where practical.
- Use structured logging with correlation IDs and approved field names.
- Do not log entire payloads by default.
- Use UTC internally. Preserve `observed_at`, `received_at`, and monotonic sequence separately.
- Prefer explicit enums/value objects over magic strings in domain code.
- APIs must return stable machine-readable error codes.
- Database migrations must be forward-safe and reviewed; never auto-delete production data.

## Testing rules

At minimum, changes must include appropriate:

- unit tests for state transitions and pure logic;
- integration tests for SQLite/PostgreSQL and HTTP contracts;
- contract tests against JSON schemas;
- failure-scenario tests for retry, duplicate delivery, restart, and clock anomalies;
- security tests for tenant isolation, redaction, credential reuse, and invalid signatures/tokens.

Timing-sensitive tests must use a controllable clock rather than real multi-minute waits.

No task is complete when tests are skipped, flaky, or dependent on a live SMS provider. Provider tests must use fakes/sandboxes; physical SMS validation belongs in controlled acceptance testing.

## Scope control

R0 does not include:

- parental controls or device blocking;
- antivirus or generalized vulnerability scanning;
- packet capture or deep packet inspection;
- cloud video recording;
- a native mobile application;
- a public marketplace of device integrations;
- full automatic LAN discovery polish;
- billing, broad consumer onboarding, or public launch operations;
- claims that observed failures prove criminal conduct.

## Documentation and ADR rules

Update an ADR when changing a decision that affects runtime, trust boundaries, persistence, communication, credentials, deployment shape, or external dependencies. Mark old decisions superseded rather than rewriting history.

Update docs and schemas in the same change when public behavior or contracts change.

## Completion report template

```text
Task:
Summary:
Files changed:
Commands run:
Test results:
Acceptance criteria:
Security/privacy review:
Assumptions:
Known limitations or follow-up risks:
```
