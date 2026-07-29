# ADR-010: Keep R0-005 probes pre-registration and in memory

- Status: Accepted
- Date: 2026-07-29
- Deciders: Remote Property Watchdog maintainers
- Related task: R0-005

## Context

R0-005 must exercise local gateway, DNS, external HTTPS, and static TCP probes. The existing R0-004 SQLite journal and transactional outbox require cloud-shaped agent and property identity for durable probe samples and material events. R0-006, which owns authenticated registration and identity association, has not run yet.

Inventing identity, reusing a local session ID as cloud identity, or adding an unauthenticated persistence shape would weaken tenant boundaries and create ambiguous migration/replay behavior. Adding target-management persistence now would also pull R0-013 into this task.

Probe communication choices also need a safe default. Repository policy permits outbound TLS/HTTPS only, while one commonly used Windows connectivity endpoint is documented as plain HTTP. External response content can expose metadata that is unnecessary for health classification.

## Decision

R0-005 is a pre-registration, local-only probe stage:

- No agent, property, tenant, enrollment, registration, or cloud identity is fabricated or inferred.
- R0-005 does not call the R0-004 probe-sample or material-event append APIs.
- It adds no journal schema version, probe table, target table, or outbox contract.
- Static probe configuration and all schedules, states, counters, transition history, and flapping evidence remain in process memory.
- Restart reloads startup configuration, clears runtime history, and returns enabled probes to baselining.
- Durable evidence integration waits for authenticated identity and explicit migration/replay requirements in a later task.
- Target CRUD, durable target configuration, management APIs, and UI remain with R0-013.

External HTTPS connectivity defaults to two reviewed, separately operated providers over strict HTTPS:

- Google `https://www.google.com/generate_204`, declared as operator `google` and requiring HTTP 204;
- Cloudflare `https://www.cloudflare.com/cdn-cgi/trace`, declared as operator `cloudflare` and requiring HTTP 200 with no body rule.

Redirects, certificate exceptions, HTTP fallback, authentication, cookies, uploads, and customer identifiers are prohibited. The Cloudflare trace body is not parsed or retained because it can contain public-IP and location/colo metadata.

Provider overrides require a truthful configuration-author declaration of operator identity. Runtime structurally enforces distinct provider IDs, endpoint hosts, registrable domains, and declared operators; it cannot prove real-world corporate ownership through domain comparison or network probing. The built-in declarations are reviewed architecture defaults. A false custom declaration is configuration-author error, and registrable-domain separation alone is not evidence of independently operated providers.

The exact execution, privacy, aggregation, and override rules are binding in `docs/R0-005-probe-requirements.md`.

## Consequences

Positive:

- Tenant and evidence integrity are not weakened to make task sequencing convenient.
- R0-005 can validate local probe behavior without prematurely committing a durable schema.
- Restart semantics and acceptance evidence are deterministic.
- External connectivity uses TLS and structurally enforceable declared-operator/domain diversity while minimizing response data.

Tradeoffs:

- Pre-registration observations and learned runtime state are intentionally lost on restart.
- No probe history is journaled or delivered to the cloud in R0-005.
- A later identity-aware task must define durable storage, migration, idempotency, and replay boundaries.
- Static configuration requires service restart and provides no management experience.
- Runtime cannot independently verify the truth of a custom provider ownership declaration.

## Alternatives

### Fabricate placeholder agent/property identity

Rejected. Placeholder identity could be mistaken for authenticated tenant identity and would make later adoption or replay unsafe.

### Add anonymous probe tables or schema version 2

Rejected. This creates a second evidence lifecycle before identity and expands persistence scope without defined migration rules.

### Reuse local agent-session identity

Rejected. A process/session identifier does not establish cloud agent, property, or tenant ownership.

### Pull target persistence and CRUD forward from R0-013

Rejected. Static startup configuration is sufficient for R0-005 acceptance and keeps management behavior in its assigned task.

### Use the Microsoft NCSI endpoint as a default

Rejected. The documented probe is plain HTTP, which conflicts with the repository's outbound HTTPS-only boundary; strict HTTPS verification also did not provide an acceptable substitute during requirements resolution.

### Validate Cloudflare trace response content

Rejected. HTTP status is sufficient for this provider, while its body can expose connection metadata that R0-005 neither needs nor is permitted to retain.

## Evidence

- R0-004 journal APIs require agent/property identity, while R0-006 owns registration.
- The data model requires tenant-scoped durable evidence and treats local configuration/state as durable in the identity-aware architecture.
- Current Chromium source defines Google's HTTPS `generate_204` endpoint and recognizes HTTP 204 as validated.
- Cloudflare documentation uses `cdn-cgi/trace` for connectivity/routing verification and documents metadata in its response.
- Microsoft documents its NCSI `connecttest.txt` web probe as HTTP.
- A bounded 2026-07-29 verification observed Google 204/no body and Cloudflare 200/no redirect; no response body was retained.

## Review trigger

Review or supersede this ADR before any task:

- persists or uploads probe observations;
- associates observations with agent/property/tenant identity;
- replays pre-registration evidence;
- adds target-management persistence or APIs;
- changes default external providers, transport security, response-body handling, or provider-diversity policy.
