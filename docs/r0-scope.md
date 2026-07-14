# R0 Scope and Decision Gate

## R0 purpose

R0 is a technical feasibility prototype. It is not a commercial beta and it does not need a polished consumer dashboard.

The prototype must prove the hardest product mechanism: an always-on Windows agent and off-site cloud service can detect, record, classify, notify, and reconcile meaningful failures without duplicate alerts or misleading certainty.

## P0 in scope

- Windows Service lifecycle, automatic startup, controlled shutdown, and crash restart.
- Gateway, DNS, HTTPS internet, TCP device/service, and optional experimental RTSP handshake probes.
- User-selected critical monitor targets.
- Local SQLite state, event journal, transactional outbox, and offline replay.
- Secure one-time agent registration and revocable per-agent credentials.
- Authenticated heartbeat ingestion and cloud lease evaluation.
- Property-unreachable, internet-unavailable, gateway/local-network, critical-device, service-unavailable, monitoring-stopped, shared-dependency, sequential-failure, and restoration states.
- Provider-neutral notification interfaces with fake, email, and SMS implementations.
- Minimal local configuration/status interface bound to loopback only.
- Internal cloud status view sufficient for development and test evidence.
- Deterministic failure scenario runner.
- Controlled home-network dogfood test.

## P1 experiments that do not block R0

- RTSP handshake or frame-change verification.
- inferred dependency chains;
- trusted secondary-contact escalation;
- limited automatic device discovery;
- enriched classification after offline evidence replay.

## Explicitly out of scope

- polished public onboarding and consumer billing;
- native mobile apps;
- parental controls, firewall, antivirus, or vulnerability scanning;
- generalized remote administration;
- cloud video/audio/snapshot storage;
- advanced router integration;
- macOS/Linux agents;
- full device-vendor catalog;
- claims that the system has proven tampering or criminal activity.

## R0 go/no-go question

Proceed to private-alpha product development only if the prototype demonstrates:

- dependable Windows service operation;
- fast and correct property-silence detection;
- no duplicate incident/notification storms;
- durable evidence and idempotent replay;
- acceptable false-alert behavior;
- understandable customer-facing incident language;
- secure agent identity and tenant isolation;
- repeatable controlled-failure results.
