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

The default configuration contains exactly two reviewed, separately operated providers:

| Stable ID | Declared `operator_id` | URL | Required result |
| --- | --- | --- | --- |
| `google-generate-204` | `google` | `https://www.google.com/generate_204` | Valid TLS and hostname; no redirect; HTTP 204; no required body. |
| `cloudflare-trace` | `cloudflare` | `https://www.cloudflare.com/cdn-cgi/trace` | Valid TLS and hostname; no redirect; HTTP 200; no expected-body rule. |

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
- `operator_id` is required for every entry, trimmed, 1–64 ASCII characters, and unique ignoring case across the configured provider set;
- `operator_id` contains only lowercase ASCII letters, digits, and hyphens and begins and ends with an ASCII letter or digit;
- every URL is absolute HTTPS with an explicit host and no user information, credentials, query, or fragment;
- endpoint hosts are unique ignoring case;
- providers use at least two distinct registrable domains and at least two distinct declared `operator_id` values;
- redirects are disabled and rejected;
- cookies, authentication, uploads, custom authorization, and customer identifiers are forbidden;
- each entry contains an exact integer expected status from 100 through 599;
- an optional exact expected body may be configured as UTF-8 text of at most 256 bytes;
- if no exact body is configured, body content is not a health condition and is not read;
- if an exact body is configured, at most 256 bytes plus one overflow-detection byte are read, compared without normalization, and discarded;
- endpoint bodies, headers, hostnames, URLs, DNS answers, and `operator_id` values never enter logs, persistence, probe evidence, health output, support output, or metrics.

Runtime validation deterministically enforces unique provider IDs, unique endpoint hosts, at least two registrable domains, and at least two distinct declared operators. Configuration errors remain bounded and must not echo an operator identifier, endpoint, host, URL, or other customer-controlled value.

This validation is intentionally structural. Runtime can enforce declared-operator and registrable-domain diversity, but registrable-domain separation alone does not prove provider independence and network probing cannot prove real-world corporate ownership. The configuration author is responsible for declaring `operator_id` accurately. The built-in Google and Cloudflare declarations are reviewed defaults; a custom override that falsely declares ownership is configuration-author error that the agent cannot discover through probing. Separately operated providers remain an architectural and operator responsibility.

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

### 4.4 Capacity, configured budgets, and token consumption

- At most four network attempts are active process-wide.
- At most one attempt is active per logical target.
- The token bucket refills at 60 tokens per minute and has burst capacity 8.
- ICMP sends, DNS queries, HTTPS endpoint requests, and TCP connects each consume one token.
- Local route/interface discovery consumes no token and no network-attempt slot.
- Waiting for a slot or token is cancelable and counts against the ten-second logical-round deadline.
- Rate-limited or capacity-delayed work that cannot start in time is incomplete, not failed.

Configuration validation calculates the worst-case demand atomically before scheduling any probe. The minimum possible scheduled interval is 27 seconds for gateway, DNS, and HTTPS aggregate rounds, which is 90% of their 30-second base interval. For each enabled TCP target:

`minimum_possible_tcp_interval_seconds = max(15, 0.9 × configured_interval_seconds)`

Using double-precision arithmetic without rounding down before comparison:

```text
gateway_demand = 60 / 27
dns_demand = 60 / 27
https_demand = provider_count × (60 / 27)
tcp_target_demand = 60 / minimum_possible_tcp_interval_seconds

worst_case_attempts_per_minute =
    gateway_demand
  + dns_demand
  + https_demand
  + sum(all enabled tcp_target_demand values)
```

Configuration is valid only when `worst_case_attempts_per_minute <= 60`. Excess demand rejects the entire configuration before scheduling with bounded code `probe_rate_budget_exceeded`.

A second deterministic startup budget uses these conservative charged amounts for each admitted attempt:

| Probe method | Charged bytes |
| --- | ---: |
| ICMP | 128 |
| DNS | 1,024 |
| TCP connect | 1,024 |
| HTTPS endpoint | 8,192 |

```text
worst_case_charged_network_bytes_per_minute =
    gateway_demand × 128
  + dns_demand × 1,024
  + https_demand × 8,192
  + sum(all enabled tcp_target_demand values × 1,024)
```

Configuration is valid only when `worst_case_charged_network_bytes_per_minute < 100,000`. A value at or above the approved decimal cap rejects the entire configuration before scheduling with bounded code `probe_network_budget_exceeded`. This charged-byte model is deterministic conservative accounting for configuration and acceptance; it is not packet capture or a claim of exact physical-wire traffic.

Neither budget error may echo target IDs, intervals, hosts, ports, provider details, operator identifiers, endpoints, or calculated customer-specific values. A 15-second TCP interval remains individually permitted only when the complete configured set passes both global budgets. With the two default HTTPS providers, 25 targets at 15 seconds are invalid: worst-case demand is approximately 108.89 attempts/minute and 141,368.89 charged bytes/minute. The same 25 targets at a 60-second configured interval are valid: worst-case demand is approximately 36.67 attempts/minute and 67,413.33 charged bytes/minute. Different provider counts can require different target intervals.

After a logical target is selected by the fair queue, the dispatcher acquires the global and per-target execution slots, then consumes one token immediately before invoking the transport. Cancellation before transport invocation consumes no token. Once transport invocation starts, the attempt consumes its token regardless of success, failure, or timeout.

### 4.5 Deterministic fair dispatch

The dispatcher uses a deterministic rotating fair queue over due logical targets. Gateway, DNS, HTTPS aggregate, and every enabled TCP target are logical targets. Each has one stable scheduler key that determines a stable order and is never logged or exposed.

- The dispatcher maintains a rotating cursor over stable key order.
- On each pass, each due logical target may start at most one network attempt before another due target receives a second attempt.
- An HTTPS aggregate with multiple providers contributes at most one endpoint attempt per fair-queue pass.
- Incomplete HTTPS endpoint work remains associated with the same bounded round deadline.
- Canceled, disabled, or no-longer-due work is skipped.
- No logical target may monopolize global slots or tokens.
- Sustainable valid configurations must not experience systematic starvation.
- Transient backpressure can still make a round incomplete under the ten-second deadline.

Fair-queue selection is the admission step. Execution-slot acquisition and token consumption then follow the ordering in section 4.4.

### 4.6 Shutdown

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

### 8.1 Required run, phases, and process identity

The implementation task must add `scripts/r0-005-resource-sampling.ps1` and run:

```powershell
pwsh ./scripts/r0-005-resource-sampling.ps1 `
  -Configuration Release `
  -TargetCount 25 `
  -DurationMinutes 30
```

The run uses 25 synthetic TCP targets and fake or loopback endpoints, never real customer or network data. It exercises the gateway, DNS, HTTPS aggregate, and TCP scheduling abstractions.

`-DurationMinutes 30` means 30 complete steady-state minutes after the service first reaches monitoring-ready. Initialization time is additional and does not reduce that window.

- Initialization begins immediately before the service process is started and ends when monitoring-ready is first observed. Failure to reach readiness within 60 seconds fails the run.
- The exact service PID is captured after process creation. The steady-state phase begins at the first five-second monotonic sampling boundary after readiness and lasts 30 complete minutes.
- Process exit, PID change, automatic restart, or unplanned termination after initial PID capture fails immediately. The sampler must not resume against a replacement PID.
- Initialization CPU is reported separately and is excluded only from the approved steady-state average CPU calculation.
- Every successfully collected working-set sample after process creation, including initialization samples, remains subject to the absolute working-set cap.
- Disk and network caps use the complete steady-state phase. Initialization disk and network totals are reported separately.

The 30-minute run is an R0-005 merge gate. A seven-day soak with zero false Critical alerts and no unplanned termination remains a later R0 release-exit/controlled-acceptance gate because R0-005 creates no alerts.

### 8.2 Clock and sampling

Elapsed time and sampling deadlines use a monotonic clock. The fixed-rate schedule is anchored at initialization start, immediately before process launch, with boundaries at five-second monotonic increments. Boundaries after the exact process is created require a process sample; the first boundary after readiness starts the steady-state phase. UTC wall-clock timestamps may be recorded for correlation, but no calculation uses wall-clock elapsed time.

The sampler uses a fixed-rate monotonic schedule with one sample every five seconds and no catch-up burst. A sample is missing when no valid sample is collected within ten seconds of its scheduled deadline or when any complete steady-state minute contains fewer than 11 valid samples. Any missing sample fails the run.

At each valid sample, the sampler refreshes the exact service process and records:

- process total processor time;
- working set in bytes;
- private bytes in bytes;
- handle count;
- thread count;
- monotonic sample time and bounded UTC correlation time.

### 8.3 CPU

`logical_processor_count = Environment.ProcessorCount` is captured once at run start. For consecutive valid process samples, with processor time and monotonic wall time expressed in the same unit:

```text
normalized_cpu_percent =
  100 × delta_process_total_processor_time
      / (delta_monotonic_wall_time × logical_processor_count)
```

Negative processor-time deltas, zero or negative wall-time deltas, PID changes, and counter resets fail the run. Only after raw inputs have been validated may a reported sampled percentage be bounded to 0–100.

Steady-state average CPU uses the total process-CPU delta over the complete steady-state window with the same logical-processor normalization. The pass condition is `steady_state_average_cpu_percent < 2.0`. Startup observation duration, startup total CPU time, and startup maximum sampled normalized CPU are reported separately; a startup peak neither replaces nor relaxes the steady-state rule.

### 8.4 Memory, handles, threads, and growth

Every valid working-set sample after process creation must be less than 150,000,000 bytes. Reaching or exceeding that decimal-byte cap fails independently of growth calculations.

Growth calculations use complete steady-state per-minute medians. Each sample is assigned to exactly one complete steady-state minute. For each minute, values are sorted without rounding; an odd count uses the middle value and an even count uses the arithmetic mean of the two middle values. The first-window value is the median of the five per-minute medians for steady-state minutes 1–5. The final-window value is the median of the five per-minute medians for minutes 26–30.

| Metric | Noise floor |
| --- | ---: |
| Working set | 1,500,000 bytes |
| Private bytes | 1,500,000 bytes |
| Handles | 1 count |
| Threads | 1 count |

For each metric:

```text
absolute_increase = final_window_median - first_window_median
percent_increase = 100 × absolute_increase / first_window_median
```

Zero first-window medians use deterministic boundary handling: zero-to-zero is 0% growth; zero-to-positive is treated as exceeding 20% for rule evaluation and serializes `percent_increase` as JSON `null`, never NaN or infinity. A metric fails when the final-window median is more than 20% above the first-window median and the absolute increase is greater than or equal to its noise floor. Noise floors excuse bounded measurement noise only and do not relax any absolute cap.

### 8.5 Disk attribution

The sampler reads the Windows process I/O `WriteTransferCount` through `GetProcessIoCounters` for the exact service PID at steady-state start and end and computes a nonnegative delta.

It also measures positive file-size growth beneath the dedicated acceptance runtime directory. New files have an initial size of zero. The calculation excludes pre-existing published binaries, immutable input configuration, and sampler-owned evidence/report files, which must be stored outside the monitored runtime directory. A monitored file that disappears during the phase fails the run.

```text
attributed_disk_bytes =
  max(process_write_transfer_delta, positive_runtime_file_growth_bytes)

disk_mb_per_day =
  attributed_disk_bytes
  × (1,440 / steady_state_minutes)
  / 1,000,000
```

For the required run, `steady_state_minutes = 30`. The pass condition is `disk_mb_per_day < 100`, using decimal MB/day. Negative counter deltas, PID changes, unreadable counters, counter reset/overflow, or disappearing monitored files fail the run.

R0-005 intentionally persists no probe evidence. Any unexpected runtime artifact creation must be reported only by bounded relative artifact name/category, never by private absolute path; disk growth attributable to probe results fails even if the aggregate cap would otherwise pass.

### 8.6 Network attribution

System-wide adapter counters, packet capture, payload capture, addresses, interface values, and endpoint values are prohibited. The implementation exposes acceptance-only aggregate accounting through its injected probe transport boundaries.

For every admitted attempt, accounting records only probe kind, measured application bytes sent, measured application bytes received, and the deterministic conservative charged-byte amount from section 4.4. No payload or endpoint value is retained. Measured application bytes are exact byte counts crossing the injected transport boundary where available, and the charged amount is at least the fixed method value.

```text
attributed_attempt_network_bytes =
  max(measured_sent + measured_received, charged_method_bytes)

attributed_network_bytes =
  sum(attributed_attempt_network_bytes for attempts admitted during steady state)

network_bytes_per_minute =
  attributed_network_bytes / steady_state_minutes
```

The pass condition is `network_bytes_per_minute < 100,000`, using decimal bytes/minute. This is deterministic conservative probe-traffic attribution, not a claim of exact physical-wire or TLS/IP overhead measurement. Attempt counts are reported separately and packet capture remains prohibited. Counter overflow, negative byte values, missing accounting for a started attempt, or an unknown probe method fails the run.

### 8.7 Deterministic reports

The sampler emits a bounded human-readable report and UTF-8 JSON from the same in-memory report model under ignored `artifacts/r0-005/`. The JSON schema identifier is `r0-005-resource-report/v1`. It uses invariant-culture numeric formatting, ISO-8601 UTC timestamps, fixed property names, stable enum/code values, deterministic array ordering, and no NaN or infinity.

The JSON has these exact top-level properties in this order:

1. `schema_version`
2. `source`
3. `configuration`
4. `timing`
5. `startup`
6. `steady_state`
7. `growth`
8. `attempts`
9. `caps`
10. `violations`
11. `result`

`schema_version` is exactly `r0-005-resource-report/v1`. Required nested properties are:

| Object | Fixed properties |
| --- | --- |
| `source` | `commit_sha`, `configuration_name`, `executable_sha256` |
| `configuration` | `target_count`, `https_provider_count`, `logical_processor_count`, `sampling_interval_seconds`, `requested_steady_state_minutes` |
| `timing` | `process_start_utc`, `readiness_utc`, `startup_elapsed_ms`, `steady_state_start_utc`, `steady_state_end_utc`, `valid_sample_count`, `missing_sample_count` |
| `startup` | `cpu_observation_duration_ms`, `total_cpu_ms`, `maximum_normalized_cpu_percent`, `maximum_working_set_bytes`, `attributed_disk_bytes`, `attributed_network_bytes` |
| `steady_state` | `average_normalized_cpu_percent`, `maximum_working_set_bytes`, `maximum_private_bytes`, `maximum_handle_count`, `maximum_thread_count`, `attributed_disk_bytes`, `disk_mb_per_day`, `attributed_network_bytes`, `network_bytes_per_minute` |
| `attempts` | `by_probe_kind`, `by_outcome`, `started`, `completed`, `succeeded`, `failed`, `timed_out`, `canceled`, `incomplete` |
| `caps` | `readiness_ms`, `working_set_bytes`, `average_cpu_percent`, `disk_mb_per_day`, `network_bytes_per_minute`, `growth_percent`, `working_set_growth_noise_bytes`, `private_bytes_growth_noise_bytes`, `handle_growth_noise_count`, `thread_growth_noise_count` |
| `result` | the string `pass` or `fail` |

The `caps` values and units are exactly:

| Property | Value | Passing relation |
| --- | ---: | --- |
| `readiness_ms` | 60,000 ms | readiness elapsed `<=` value |
| `working_set_bytes` | 150,000,000 bytes | every collected value `<` cap |
| `average_cpu_percent` | 2.0 percent | steady-state average `<` cap |
| `disk_mb_per_day` | 100 decimal MB/day | normalized value `<` cap |
| `network_bytes_per_minute` | 100,000 bytes/minute | normalized value `<` cap |
| `growth_percent` | 20.0 percent | final-window growth `<=` cap unless both failure predicates apply |
| `working_set_growth_noise_bytes` | 1,500,000 bytes | growth fails only when absolute increase `>=` floor |
| `private_bytes_growth_noise_bytes` | 1,500,000 bytes | growth fails only when absolute increase `>=` floor |
| `handle_growth_noise_count` | 1 count | growth fails only when absolute increase `>=` floor |
| `thread_growth_noise_count` | 1 count | growth fails only when absolute increase `>=` floor |

`growth` contains `working_set`, `private_bytes`, `handles`, and `threads` in that order. Each contains `first_window_median`, `final_window_median`, `absolute_increase`, `percent_increase`, `noise_floor`, `unit`, and `result`; `unit` is `bytes` for memory metrics and `count` for handle/thread metrics. Each result is `pass` or `fail`.

`attempts.by_probe_kind` and `attempts.by_outcome` are arrays of `{ "code", "count" }` objects in ordinal code order. `violations` is an ordinal-sorted array containing only these stable bounded codes when applicable:

- `acceptance_cleanup_failed`
- `attempt_accounting_incomplete`
- `counter_negative`
- `counter_overflow`
- `counter_reset`
- `counter_unavailable`
- `disk_cap_exceeded`
- `handle_growth_exceeded`
- `network_cap_exceeded`
- `pid_changed`
- `private_bytes_growth_exceeded`
- `process_exited`
- `readiness_timeout`
- `report_serialization_invalid`
- `runtime_file_disappeared`
- `sample_missing`
- `steady_state_cpu_cap_exceeded`
- `synthetic_listener_cleanup_failed`
- `thread_growth_exceeded`
- `unknown_probe_method`
- `working_set_cap_exceeded`
- `working_set_growth_exceeded`

The report contains no machine name, username, absolute path, endpoint, target, interface, address, DNS answer, payload, header, body, exception text, credential, or customer identifier. The human report must not apply different calculations or privacy rules.

### 8.8 Failure rules

The sampler exits nonzero when:

- readiness exceeds 60 seconds;
- the process exits, the PID changes, or a replacement/restart occurs;
- a sample is missing;
- a required counter is unavailable, negative, reset, or overflows;
- working set reaches or exceeds 150,000,000 bytes;
- steady-state average CPU reaches or exceeds 2.0%;
- disk reaches or exceeds 100 decimal MB/day;
- network reaches or exceeds 100,000 bytes/minute;
- any growth rule fails;
- attempt accounting is incomplete;
- an unknown probe method is encountered;
- report serialization violates schema, ordering, numeric, or privacy requirements;
- synthetic-listener or acceptance cleanup fails.

## 9. Required verification for implementation

### 9.1 Unit tests

Use fake transports, an injected clock, and deterministic jitter to cover:

- every state transition and each method threshold;
- success counter reset and two-success recovery;
- incomplete/canceled/rate-limited/method-unavailable outcomes not counting as failures;
- no overlap, fixed-delay scheduling, jitter bounds, no catch-up, timeouts, deadline, global concurrency, per-target concurrency, and token-bucket burst/refill;
- worst-case attempt-demand and charged-network formulas, provider-count effects, and double-precision boundaries immediately below, exactly at, and immediately above 60 attempts/minute and 100,000 charged bytes/minute as applicable;
- rejection of 25 targets at 15 seconds, acceptance of a 25-target configuration with sufficiently long intervals, and the valid two-default-provider 25-target/60-second example;
- stable rotating fair-queue order, HTTPS endpoint attempts not monopolizing a pass, no systematic starvation under sustained valid demand, cancellation before token consumption, and a started timeout consuming one token;
- shutdown and degradation cancellation;
- flapping detection, rolling-window eviction, clearing, restart reset, and degradation reset;
- configuration bounds, atomic rejection, duplicate IDs, 0/25/26 targets, interval/port limits, credential-like values, and URI/range rejection;
- missing or invalid `operator_id`, duplicate operator IDs, two domains with the same declared operator, and two declared operators on the same host/domain;
- valid built-in Google/Cloudflare declarations and valid custom providers with distinct IDs, hosts, registrable domains, and declared operators;
- provider-set count, provider/domain/declared-operator separation, HTTPS-only URLs, strict statuses, redirect rejection, and bounded optional-body validation;
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

### 9.5 Resource sampler contract tests

Deterministic automated tests must cover:

- every CPU, growth, disk, network, rate-demand, and unit-conversion formula at values below, exactly at, and above each pass boundary;
- fixed-rate five-second scheduling, no catch-up burst, the ten-second collection deadline, and fewer than 11 valid samples in a complete minute;
- initialization/steady-state phase boundaries, all 30 complete minute assignments, and first/final five-minute median windows including the even-count median rule;
- PID change, process exit, restart, negative/zero/reset/unavailable/overflowed counters, disappearing runtime files, unknown methods, and missing started-attempt accounting;
- decimal-byte, decimal-MB/day, bytes/minute, millisecond, percentage, and logical-processor normalization;
- every stable resource violation code and ordinal violation ordering;
- exact `r0-005-resource-report/v1` property names/order, invariant numeric and UTC serialization, no NaN/infinity, and identical calculations in the JSON and human report;
- rejection/redaction of machine names, usernames, absolute paths, endpoints, targets, interfaces, addresses, DNS answers, payloads, headers, bodies, exception text, credentials, and customer identifiers;
- synthetic-listener and acceptance cleanup success/failure.

### 9.6 Standard repository validation

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
