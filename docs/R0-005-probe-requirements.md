# R0-005 probe requirements

Status: Binding implementation requirements

Applies to: R0-005 only

Decision boundary: local gateway, DNS, external HTTPS, and static TCP probe execution

## 1. Purpose and authority

This document resolves the implementation-blocking requirements for R0-005. It narrows the broader feasibility, data-model, and backlog documents into a deterministic local-probe contract without changing any public schema or advancing registration, cloud delivery, target management, or notification work.

Where this document is intentionally narrower:

- The canonical data model describes durable configuration, evidence, baselines, and state. R0-005 temporarily keeps all of those probe concerns in memory because R0-006 has not yet supplied authenticated identity.
- The MVP backlog places most target management in R1. R0-005 pulls forward only static TCP definitions, TCP connect execution, local operational states, and bounded flapping evidence needed to prove the edge runtime.
- “Healthy” in this document is a local operational result. It is not the feasibility document's user-facing “protected” status, which requires at least three successful checks and later notification/management policy.
- The seven-day soak remains an R0 release-exit or controlled-acceptance gate. The R0-005 merge gate is the specified 30-minute resource run.

The implementation must not silently broaden these decisions.

### Source precedence and reconciliation

This resolution applies repository precedence in this order: `AGENTS.md`, the assigned task and dependency tasks, repository Markdown, accepted ADRs, executable contracts/tests, and the three approved planning references (Technical Feasibility, Data Model/API, and MVP Backlog). The reference files were inspected structurally and were not modified.

No unresolved conflict was found. The deliberate reconciliations are:

- Technical Feasibility supplies the approved resource caps, 25-target boundary, three-check “protected” concept, and later seven-day soak. R0-005 adopts the caps and target count but keeps “protected,” alert truth, and the long soak outside its local operational-state merge gate.
- Data Model/API supplies the broader persisted state vocabulary. R0-005 maps to it without changing contracts and defers durability until authenticated identity exists.
- MVP Backlog assigns target management to the later local-configuration task. R0-005 pulls forward only the narrow execution/evidence primitives listed in section 2.5.

## 2. Resolved planning blockers

### 2.1 State vocabulary and compatibility

R0-005 owns these exact runtime states:

| R0-005 state | Meaning | Existing `ProbeState` projection | Canonical data-model projection |
| --- | --- | --- | --- |
| `unknown` | No usable outcome is available, including immediately after construction. | `Unknown` | `unknown` |
| `baselining` | Enabled and ready to probe, but no successful round has completed since activation or restart. | `Unknown` | `learning` |
| `healthy` | The latest transition rules establish current success. | `Up` | `healthy` |
| `suspect` | One or more counted failures have occurred, below the method's offline threshold. | `Degraded` | `suspected_down` |
| `offline` | Counted consecutive failures reached the method's threshold. Cause remains unknown. | `Down` | `offline` |
| `recovering` | One success followed `offline`; a second consecutive success is required. | `Degraded` | `degraded` |

`snoozed` and `retired` remain management states outside R0-005. No contract or JSON schema is changed to add these runtime values. Compatibility projections are adapters only; domain logic must use the six R0-005 states rather than reverse-engineering transitions from `ProbeState`.

### 2.2 Pre-registration identity and persistence

R0-005 runs before R0-006 registration. Therefore:

- It must not fabricate, synthesize, default, infer, or persist an `AgentId`, `PropertyId`, tenant ID, enrollment ID, cloud identity, or registration token.
- It must not call `AppendProbeSampleAsync` or `AppendMaterialEventAsync`.
- It must not create schema version 2, add probe tables, repurpose session identifiers as cloud identity, or write probe state into the R0-004 journal/outbox.
- Static configuration, schedules, state, counters, transition history, and flapping evidence exist in process memory only.
- On every process restart, configuration is rebound from startup sources, every enabled probe returns to `baselining`, and all failure, recovery, rate, and flapping counters reset.
- Durable probe evidence begins only after authenticated identity and tenant/property association are available in a later task. That later task must define migration and replay semantics rather than retroactively adopting pre-registration observations.

This deliberate non-persistence is recorded in [ADR-010](../adr/ADR-010-pre-registration-probe-boundary.md).

### 2.3 Static startup configuration

R0-005 configuration uses the normal .NET configuration providers already available to the service:

- `appsettings.json` and environment-specific appsettings;
- environment variables;
- command-line configuration.

Configuration is bound and validated exactly once during service startup. A restart is required to apply changes. R0-005 provides no CRUD API, LAN or cloud management endpoint, UI, hot reload, SQLite configuration, or “last known good” fallback.

Validation is atomic. If any enabled probe or provider entry is invalid, the probe subsystem schedules no work. It exposes bounded validation codes without echoing customer-controlled values. It must not partly activate a valid subset.

#### TCP target definition

Each statically configured TCP target contains:

| Field | Requirement |
| --- | --- |
| `id` | Required stable local identifier; trimmed; 1–128 characters; unique ignoring case; not a cloud identity. |
| `label` | Required operator label; trimmed; 1–160 characters; never logged or used as evidence. |
| `host` | Required single DNS hostname, IPv4 literal, or IPv6 literal; trimmed; DNS names are at most 253 ASCII characters with punycode allowed; no URI syntax. |
| `port` | Integer from 1 through 65535. |
| `enabled` | Boolean; only enabled entries are scheduled and counted toward the limit. |
| `interval` | Optional duration; default 60 seconds; minimum 15 seconds; maximum 15 minutes. |

Zero TCP targets is valid. At most 25 TCP targets may be enabled. All IDs, including disabled entries, must be unique ignoring case.

The host field rejects a scheme, user information, path, query, fragment, whitespace, control characters, credential-assignment syntax, wildcard, subnet, address range, or multiple-host expression. Credentials are forbidden in every target field. One definition represents one explicit host/port service; no range expansion, discovery, scan, or automatic fallback is allowed.

Validation errors identify only the configuration section, bounded field name, and stable error code. They do not reproduce IDs, labels, hosts, values, exception messages, or provider payloads.

#### DNS definition

The DNS probe has one enabled flag and one query name:

- default query name: `example.com`;
- override: one trimmed ASCII DNS name, maximum 253 characters, with punycode allowed;
- no scheme, port, user information, path, query, fragment, wildcard, credentials, or customer identifier.

The system resolver is used. A round succeeds when at least one valid A or AAAA address is returned within the timeout. Returned addresses are never logged, persisted, or added to evidence.

#### HTTPS provider definition

The default configuration contains exactly two independent providers:

| Stable ID | URL | Required result |
| --- | --- | --- |
| `google-generate-204` | `https://www.google.com/generate_204` | Valid TLS and hostname; no redirect; HTTP 204; no required body. |
| `cloudflare-trace` | `https://www.cloudflare.com/cdn-cgi/trace` | Valid TLS and hostname; no redirect; HTTP 200; no expected-body rule. |

The Google default is supported by current Chromium source, which defines `https://www.google.com/generate_204` as its HTTPS-first default probe and treats HTTP 204 as validated:

- <https://chromium.googlesource.com/chromium/src/+/HEAD/chrome/android/java/src/org/chromium/chrome/browser/net/connectivitydetector/ConnectivityDetector.java>

Cloudflare's WARP documentation directs clients to `https://www.cloudflare.com/cdn-cgi/trace` to verify connectivity/routing:

- <https://developers.cloudflare.com/warp-client/get-started/linux/>

Cloudflare documents that the trace response contains connection metadata, including an apparent public IP and Cloudflare colo/location information:

- <https://developers.cloudflare.com/privacy-proxy/get-started/>

For that reason, the Cloudflare default is status-only. The request uses response-headers-first streaming; once status is known, the implementation disposes the response. It must not parse, log, persist, compare, expose as health evidence, or otherwise retain the body. A defensive reader used by a transport must stop at 256 bytes and still discard the bytes.

`http://www.msftconnecttest.com/connecttest.txt` is explicitly not a default. Microsoft's NCSI documentation defines it as a plain-HTTP probe with HTTP 200 and expected text:

- <https://learn.microsoft.com/en-us/windows-server/networking/ncsi/ncsi-frequently-asked-questions>

Plain HTTP conflicts with this repository's outbound HTTPS-only rule. During requirements verification, the corresponding HTTPS request also failed strict Windows hostname validation. R0-005 must not disable certificate validation, fall back to HTTP, add a provider exception, or treat the Microsoft endpoint as required.

An operator may override the full HTTPS provider set at startup, subject to all of these rules:

- exactly 2–4 entries;
- stable IDs are required, 1–64 characters, and unique ignoring case;
- every URL is absolute HTTPS with an explicit host and no user information, credentials, query, or fragment;
- endpoint hosts are unique ignoring case;
- providers use at least two distinct registrable domains and at least two separately operated providers;
- redirects are disabled and rejected;
- cookies, authentication, uploads, custom authorization, and customer identifiers are forbidden;
- each entry contains an exact integer expected status from 100 through 599;
- an optional exact expected body may be configured as UTF-8 text of at most 256 bytes;
- if no exact body is configured, body content is not a health condition and is not read;
- if an exact body is configured, at most 256 bytes plus one overflow-detection byte are read, compared without normalization, and discarded;
- endpoint bodies, headers, hostnames, URLs, and DNS answers never enter logs, persistence, evidence, support output, or metrics.

The provider-set validation is intentionally structural. It does not perform network calls during startup and does not attempt to infer common ownership beyond the configured provider/domain separation.

### 2.4 External endpoint verification record

Requirements resolution performed a bounded, read-only verification on 2026-07-29 using system TLS validation, disabled redirects, response-headers-first completion, and no retained bodies:

| Endpoint | Observed status | Redirect | Bounded first read |
| --- | ---: | --- | ---: |
| Google `generate_204` | 204 | no | 0 bytes |
| Cloudflare `cdn-cgi/trace` | 200 | no | 200 bytes, immediately discarded |

This is requirements evidence, not a CI dependency or uptime guarantee. Runtime behavior is governed by the aggregation rules below, and automated tests use fakes or disposable local endpoints.

### 2.5 Backlog ownership

R0-005 pulls forward only:

- static startup TCP target definitions;
- TCP connect execution;
- local operational states in section 2.1;
- bounded flapping evidence in section 6.

R0-013 retains:

- target CRUD and persistent target configuration;
- target-management APIs and UI;
- richer target selection and editing;
- vendor/device integration;
- automatic port selection or discovery;
- registration/tenant/property association;
- cloud delivery and user notification policy;
- long learned baselines and the definition of “protected.”

R0-005 must not leave placeholder endpoints or tables that pre-implement R0-013.

## 3. Probe methods and independence

### 3.1 Gateway

Each gateway round discovers the active default route and interface from current operating-system state. It must not pin the result from startup.

The round records only bounded method evidence for:

- whether an active interface exists;
- whether a usable default route exists;
- whether a gateway address exists;
- whether ICMP was attempted;
- the bounded ICMP outcome.

The gateway address, interface name, interface description, route details, and operating-system exception text are never logged or surfaced as evidence.

ICMP is the only network reachability method in R0-005. The implementation must not try arbitrary ports, scan the gateway, or use a generic TCP fallback. Outcomes are classified as follows:

- no active interface or no usable default route: counted missing-route gateway failure;
- route exists but has no usable gateway address: incomplete method evidence, not a counted reachability failure;
- ICMP success: success;
- ICMP timeout or unreachable: counted failure;
- ICMP unavailable because the platform, API, or permissions do not permit it: `icmp_unavailable`, not a failure.

`icmp_unavailable` retains route evidence and cannot by itself move a target to `offline`. Before any successful method result, the state remains `baselining` (`Unknown` projection). From `healthy`, it lowers confidence to `suspect` (`Degraded` projection) without incrementing the failure counter. From `suspect` or `recovering`, state and counters remain unchanged. An already `offline` state remains offline until an actual success begins recovery.

The missing-active-interface/default-route threshold is two consecutive rounds. The ICMP timeout/unreachable threshold is three consecutive counted failures. A success by the applicable method resets its counter; switching failure classifications does not combine the two thresholds.

### 3.2 DNS

DNS is a separate logical probe with its own state, counter, transition history, and evidence. It uses the system resolver and the configured query name.

Success requires a valid A or AAAA result within two seconds. Timeout, resolver failure, or no valid address is a counted DNS failure. The offline threshold is two consecutive counted failures.

A DNS-only failure cannot declare the HTTPS aggregate or total internet state offline. Consumers must be able to see DNS state independently.

### 3.3 HTTPS aggregate

One logical HTTPS round attempts every configured provider, subject to global limits and the logical-round deadline.

An endpoint succeeds only when:

- DNS/connect/TLS completes within the five-second endpoint timeout;
- normal certificate-chain and hostname validation succeeds;
- no redirect occurs;
- the exact configured status is returned;
- when and only when an expected body is configured, the exact bounded body matches.

Aggregation is deterministic:

1. If cancellation, the deadline, or capacity prevents even one configured endpoint from starting, or prevents an admitted endpoint from producing a classified transport result, the round is `incomplete`; counters and state do not change. A started attempt that returns a classified timeout is a completed failure, not an incomplete attempt.
2. If every endpoint completes and at least one succeeds, the aggregate round succeeds.
3. If every endpoint completes, at least one succeeds, and at least one fails, emit `partial_endpoint_failure`; reset the aggregate failure counter and apply a success transition. Do not create an incident or change the user-facing state because of the failed provider.
4. If every endpoint completes and all fail exclusively during DNS resolution while the independent DNS probe is failing, emit `blocked_by_dns`; the HTTPS aggregate round is incomplete, not failed, and its counter/state do not change.
5. If every endpoint completes and none succeeds for any other combination of reasons, the aggregate round fails.

The HTTPS aggregate offline threshold is two consecutive counted failures. A single provider can never independently declare the aggregate offline.

### 3.4 TCP

Each enabled TCP target is an independent logical target with its own state, counter, transition history, and schedule.

A round performs one asynchronous TCP connection attempt to exactly the configured host and port. Successful connection is success. Refused, timed out, unreachable, DNS/connect error, or other bounded transport failure is a counted failure. The socket is disposed immediately; no application protocol, payload, banner, authentication, TLS substitution, retry, alternate address/port, or discovery occurs.

The TCP offline threshold is two consecutive counted failures.

## 4. Scheduler contract

### 4.1 Activation and degradation

The scheduler may activate only after R0-004 reports both journal readiness and formal service readiness. Enabled targets then enter `baselining`, and their first rounds become due immediately through the same global controls as later work.

If journal health or formal service readiness becomes false:

- stop admitting new rounds;
- cancel queued and active attempts promptly;
- clear schedules, token-bucket state, counters, recovery progress, and flapping history;
- keep the process alive under R0-004's existing degraded-service behavior;
- do not collect, log, buffer, persist, or emit observations while degraded;
- do not recreate the journal, restore readiness, or invent writes from R0-005.

Recovery of probe execution requires a healthy process restart. On restart, all enabled targets begin at `baselining`.

Known R0-004 limitation: a simultaneous journal failure and lifecycle-marker write failure can leave the durable readiness marker stale until restart, although in-process readiness is false. R0-005 must trust the in-process false state and stop.

### 4.2 Intervals and jitter

| Logical target | Base interval |
| --- | ---: |
| Gateway | 30 seconds |
| DNS | 30 seconds |
| HTTPS aggregate | 30 seconds |
| TCP | 60 seconds by default; configured from 15 seconds through 15 minutes |

After a round finishes, its next due time is:

`completion time + uniform random duration in [max(15 seconds, 90% of base interval), 110% of base interval]`

Jitter comes from an injected deterministic source in tests. Scheduling uses fixed delay from completion, does not overlap the same logical target, and does not catch up or burst after delay, sleep, or backpressure.

### 4.3 Timeouts and deadline

| Operation | Timeout |
| --- | ---: |
| Gateway ICMP | 1.5 seconds |
| DNS resolution | 2 seconds |
| TCP connect | 2 seconds |
| Each HTTPS endpoint | 5 seconds |
| Any logical round, including capacity/rate waiting | 10 seconds |

There is no retry within a round. A timed-out transport that was actually started is a counted failure. A round that expires before all required attempts can start/complete is incomplete and does not change counters or state.

### 4.4 Capacity and rate

- At most four network attempts are active process-wide.
- At most one attempt is active per logical target.
- A token bucket admits at most 60 network attempts per minute with burst capacity 8.
- ICMP sends, DNS queries, HTTPS endpoint requests, and TCP connects each consume one token when admitted.
- Local route/interface discovery consumes no token and no network-attempt slot.
- Waiting for a slot or token is cancelable and counts against the ten-second logical-round deadline.
- Rate-limited or capacity-delayed work that cannot start in time is incomplete, not failed.

### 4.5 Shutdown

On shutdown:

- stop admitting new work first;
- cancel queued and active attempts;
- dispose transports;
- complete scheduler shutdown within five seconds.

No fixed unbounded sleep or real multi-minute wait is allowed. Timing-sensitive production code uses `TimeProvider` or an equivalent injected clock.

## 5. State machine

Every enabled logical target starts `unknown`, moves to `baselining` when the ready scheduler activates it, and then uses this exact transition table:

| Current state | Round result | Next state | Counter effect |
| --- | --- | --- | --- |
| `baselining` | success | `healthy` | reset failures |
| `baselining` | counted failure below threshold | `suspect` | increment failures |
| `baselining` or `suspect` | counted failure reaches threshold | `offline` | increment failures |
| `healthy` | counted failure | `suspect` | set failures to 1 |
| `suspect` | success | `healthy` | reset failures |
| `offline` | success | `recovering` | reset failures; recovery successes = 1 |
| `recovering` | success | `healthy` | recovery successes = 2; reset |
| `recovering` | counted failure | `offline` | reset recovery; set consecutive failures to 1 |
| Gateway `healthy` | `icmp_unavailable` | `suspect` | failure counter unchanged |
| any | incomplete/non-counting result | unchanged | unchanged |

Every success resets the relevant consecutive-failure counter. Reaching a threshold means `>=` the method-specific threshold.

Canceled, rate-limited, superseded, configuration-invalid, deadline-incomplete, capacity-incomplete, and method-unavailable results are not transport failures. They do not advance failure or recovery counters. The gateway confidence-only `healthy` to `suspect` transition above is the sole non-counting state change.

State is observational, not causal. `offline` means repeated technical failure; it does not prove disconnection, sabotage, intrusion, or intent.

## 6. Flapping evidence

A logical target is flapping when at least four state transitions among `healthy`, `suspect`, `offline`, and `recovering` occur in a rolling ten-minute window.

- Entry into `unknown` or `baselining` is not qualifying.
- Repeated rounds that leave state unchanged are not transitions.
- The flag clears after 20 consecutive minutes without a qualifying transition.
- History is bounded to timestamps needed for the current ten-minute window plus the last qualifying transition.
- Time comes from the injected clock.
- Flapping does not replace the operational state, change cadence or thresholds, create a cloud/user incident, or suppress notifications.
- Evidence is in memory only and resets on restart or degradation.

Notification cooldown and suppression policy remain outside R0-005.

## 7. Bounded evidence and privacy

Every completed or incomplete round produces only a bounded internal outcome composed of:

- probe kind;
- stable evidence code from the following table;
- state before and after;
- attempt count and bounded elapsed duration;
- success/failure/incomplete classification;
- whether flapping is active.

Allowed evidence codes:

| Scope | Codes |
| --- | --- |
| Common | `round_succeeded`, `round_incomplete`, `cancelled`, `rate_limited`, `capacity_unavailable`, `deadline_exceeded`, `superseded`, `configuration_invalid` |
| Gateway | `active_interface_missing`, `default_route_missing`, `gateway_address_missing`, `icmp_succeeded`, `icmp_timeout`, `icmp_unreachable`, `icmp_unavailable` |
| DNS | `dns_succeeded`, `dns_timeout`, `dns_no_address`, `dns_resolution_failed` |
| HTTPS | `https_succeeded`, `https_dns_failed`, `https_connect_failed`, `https_tls_failed`, `https_timeout`, `https_redirect_rejected`, `https_unexpected_status`, `https_body_mismatch`, `https_body_too_large`, `endpoint_not_started`, `partial_endpoint_failure`, `blocked_by_dns` |
| TCP | `tcp_connect_succeeded`, `tcp_connect_refused`, `tcp_connect_timeout`, `tcp_connect_failed` |

Codes are not user-facing causal claims. Production logs and metrics may include the probe kind, code, aggregate counts, state names, and bounded duration. They must not contain:

- exception text, stack traces from expected transport failures, or operating-system error strings;
- IDs, labels, hostnames, IP addresses, ports, URLs, query names, DNS answers, headers, bodies, interface descriptions, route details, or machine paths;
- payloads, credentials, authorization, user information, cookies, customer identifiers, or provider trace metadata;
- raw configuration or entire serialized outcomes.

Unexpected exceptions may be correlated through an existing bounded correlation ID, but sensitive exception content remains out of ordinary logs and support artifacts.

The production probe engine opens no inbound listener, changes no firewall rule, captures no packet, and performs no broad port scan. Disposable loopback listeners belong only to controlled tests and acceptance cleanup.

## 8. Resource acceptance

### 8.1 Approved steady-state caps

With 25 enabled synthetic TCP targets and all gateway/DNS/HTTPS abstractions active:

- working set: less than 150 MB;
- average CPU: less than 2% on a typical always-on Windows PC, excluding explicitly identified short startup/discovery bursts;
- local disk: less than 100 MB per day before retention cleanup;
- network: less than 100 KB per minute on average, excluding optional diagnostics;
- monitoring-ready: within 60 seconds after network availability.

R0-005 intentionally writes no probe persistence, so any disk growth attributable to probe results is a failure rather than budgeted journal usage.

### 8.2 Required sampler

The implementation task must add `scripts/r0-005-resource-sampling.ps1` and run:

```powershell
pwsh ./scripts/r0-005-resource-sampling.ps1 `
  -Configuration Release `
  -TargetCount 25 `
  -DurationMinutes 30
```

The sampler must:

- use 25 synthetic targets and fake or loopback endpoints; never real customer/network data;
- exercise gateway, DNS, HTTPS aggregate, and TCP scheduling abstractions;
- sample CPU, working set, private bytes, handles, threads, network bytes, disk bytes, attempts, completions, timeouts, cancellations, and state transitions at a bounded cadence;
- separate initialization from steady-state measurement;
- emit a bounded human summary and deterministic JSON under ignored `artifacts/r0-005/`;
- fail nonzero on any approved cap violation, missing sample, unplanned termination, or unbounded-growth violation;
- avoid payloads, endpoint values, addresses, credentials, environment dumps, and machine paths in output.

### 8.3 Growth rule and noise floors

For working set, private bytes, handles, and threads:

1. Compute the median of per-minute steady-state samples for the first five complete steady-state minutes.
2. Compute the median for the final five complete minutes.
3. Fail when the final median exceeds the first median by more than 20%, unless the absolute increase is below the applicable measurement-noise floor.

Noise floors are:

| Metric | Floor | Derivation |
| --- | ---: | --- |
| Working set | 1.5 MB | 1% of the approved 150 MB working-set cap. |
| Private bytes | 1.5 MB | Same process-memory measurement floor; no larger unapproved cap is invented. |
| Handles | 1 handle | Integer counter resolution; “below” means only zero growth is exempt. |
| Threads | 1 thread | Integer counter resolution; “below” means only zero growth is exempt. |

These floors excuse measurement noise only; they do not relax the absolute resource caps. The implementation may propose a different floor only through a reviewed requirements/ADR change backed by repeatable native measurements.

The 30-minute run is an R0-005 merge gate. A seven-day soak with zero false Critical alerts and no unplanned termination remains a later R0 release-exit/controlled-acceptance gate because R0-005 creates no alerts.

## 9. Required verification for implementation

### 9.1 Unit tests

Use fake transports, an injected clock, and deterministic jitter to cover:

- every state transition and each method threshold;
- success counter reset and two-success recovery;
- incomplete/canceled/rate-limited/method-unavailable outcomes not counting as failures;
- no overlap, fixed-delay scheduling, jitter bounds, no catch-up, timeouts, deadline, global concurrency, per-target concurrency, and token-bucket burst/refill;
- shutdown and degradation cancellation;
- flapping detection, rolling-window eviction, clearing, restart reset, and degradation reset;
- configuration bounds, atomic rejection, duplicate IDs, 0/25/26 targets, interval/port limits, credential-like values, and URI/range rejection;
- provider-set count, provider/domain separation, HTTPS-only URLs, strict statuses, redirect rejection, and bounded optional-body validation;
- HTTPS endpoint outcomes for 204, 200 status-only, wrong status, redirect, TLS failure, timeout, cancellation, oversized body, body not retained, partial provider failure, all-provider failure, and two-provider independence;
- DNS-blocked HTTPS aggregation and DNS independence;
- gateway route changes, missing route, missing gateway, ICMP success/timeout/unreachable/unavailable;
- evidence-code allowlisting and redaction.

### 9.2 Integration tests

Use disposable local endpoints or fakes only. Cover:

- local TCP accept, refuse, timeout, cancellation, and socket disposal;
- local HTTPS status/body/redirect/streaming behavior with test certificates as appropriate;
- system-resolver adapter success/failure through a controlled abstraction;
- scheduler activation after readiness and stop on readiness degradation;
- restart rebinds static configuration and returns enabled probes to `baselining`;
- no probe journal/outbox calls and no database/schema artifacts;
- no response body, DNS answer, endpoint, target, interface, or exception leakage.

Live Google, Cloudflare, Microsoft, DNS, SMS, camera, or customer-network access is not required or permitted as an automated test dependency.

### 9.3 Security and privacy tests

Verify:

- URI user information, credentials, credential-assignment syntax, query strings, fragments, and customer/device identifiers are rejected;
- HTTPS redirects and invalid TLS are rejected without fallback or weakened validation;
- endpoint URLs/hosts, DNS queries/answers, target IDs/labels/hosts/ports, response bodies, headers, interface/route data, exception text, machine paths, and other customer-controlled values do not appear in production logs or evidence;
- response-body readers stop at the 256-byte bound plus one overflow-detection byte and retain no bytes;
- no production inbound listener, firewall mutation, packet capture, subnet/range expansion, broad port scan, or arbitrary gateway TCP probe exists;
- no probe journal/outbox call, schema artifact, identity fabrication, or raw LAN inventory is introduced.

### 9.4 Native Windows acceptance

Native acceptance must verify:

- active default-route/interface discovery on Windows;
- bounded route evidence without interface/address disclosure;
- ICMP success when permitted and `icmp_unavailable` behavior when it is not;
- cancellation and service stop complete within five seconds;
- the 25-target resource sampler passes;
- monitoring-ready is achieved within 60 seconds after network availability;
- no inbound listener or firewall rule is added;
- every synthetic listener and service acceptance artifact is cleaned up.

Changing Windows services, firewall policy, ICMP policy, certificates, or machine configuration requires separate explicit authorization.

### 9.5 Standard repository validation

The implementation change must also pass the task commands, `pwsh ./scripts/test.ps1`, secret scanning, package vulnerability review, and diff checks required by repository policy.

## 10. Out of scope and deferred integration

R0-005 does not include:

- authenticated registration, enrollment, agent/property/tenant association, or pre-registration replay;
- durable probe samples/events, database migrations, schema changes, cloud synchronization, or notification delivery;
- runtime target CRUD, persistent target configuration, target-management UI/API, cloud configuration, or hot reload;
- vendor discovery, automatic port discovery, arbitrary service scanning, broad address/range discovery, packet capture, or application-protocol inspection;
- credentials, authenticated probes, inbound listeners, firewall changes, uploads, cookies, redirects, response-body retention, or raw LAN inventory;
- user incidents, notification cooldown/suppression, causal security claims, “protected” status, long learned baselines, or a native mobile surface.

Deferred integration after authenticated identity exists must specify tenant ownership, durable schema, idempotency, replay/migration, retention, audit, and the mapping from local operational state to cloud contracts. R0-013 retains target-management surfaces and persistence unless a later accepted task explicitly changes that ownership.

## 11. Known limitation

Pre-registration configuration and all operational evidence are intentionally volatile. Restart or readiness degradation loses counters, transitions, and flapping history and returns enabled targets to `baselining`. In addition, the inherited R0-004 limitation remains: simultaneous journal and lifecycle-marker failure can leave the durable marker stale until restart even though in-process readiness is false. R0-005 must stop on that in-process false state.

## 12. Delivery boundary

R0-005 is complete only when the implementation, deterministic tests, native evidence, and resource report satisfy this document. Completion does not authorize:

- registration or identity creation;
- persistence or cloud delivery;
- target-management surfaces;
- alerts, incidents, causal claims, or “protected” status;
- R0-006 or R0-013 work.

Any implementation need that conflicts with this boundary is a new requirements decision and must stop for review.
