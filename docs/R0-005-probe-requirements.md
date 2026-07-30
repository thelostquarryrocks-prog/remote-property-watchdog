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

This resolution applies repository precedence in this order: `AGENTS.md`, the assigned task and dependency tasks, repository Markdown, accepted ADRs, executable contracts/tests, and the three approved planning references (Technical Feasibility, Data Model/API, and MVP Backlog). The reference files were inspected structurally and were not modified. In particular, this document does not modify or claim to rewrite the approved `Remote_Property_Watchdog_Technical_Feasibility_Prototype_Spec_v0.1.docx` source.

Technical Feasibility section 7.2 contains prototype planning heuristics that are deliberately superseded by this binding contract for R0-005 only. The material cadence and threshold differences are:

| Concern | Technical Feasibility prototype | Binding R0-005 decision |
| --- | --- | --- |
| Gateway cadence | 15 seconds | 30-second base interval with ±10% jitter |
| Gateway threshold | 3 failures | missing route 2; ICMP timeout/unreachable 3 |
| DNS threshold | 3 failures | 2 counted failures |
| HTTPS threshold | all endpoints fail for 3 rounds | aggregate counted failure for 2 rounds |
| TCP-service threshold | 3 failures | 2 counted failures |

These are deliberate R0-005 decisions affecting detection latency and false-positive tradeoffs. The feasibility document is a prototype planning source, not the executable R0-005 implementation contract. R0-005 must also satisfy constraints and semantics that the earlier heuristic does not define: fixed-delay scheduling from completion, ±10% jitter, a global 60-attempt/minute limit, a strict charged-network budget, up to 25 explicit TCP targets, independent DNS state, multi-provider HTTPS aggregation, partial-success handling, DNS-blocked handling, incomplete-round handling, and the prohibition on user incident or notification emission.

The binding rationales are:

- The 30-second Gateway, DNS, and HTTPS cadence preserves capacity for 25 configured targets under the global attempt and charged-network budgets and reduces always-on background traffic.
- A missing active interface or default route is higher-confidence local evidence than an ICMP timeout or unreachable response. Missing-route evidence therefore uses threshold 2, while ICMP retains threshold 3.
- DNS uses threshold 2 because it remains an independently exposed local state and cannot alone declare the HTTPS aggregate offline.
- HTTPS uses threshold 2 because a counted aggregate failure requires every configured provider attempt to start and complete with no success. Partial provider success, `blocked_by_dns`, correlation-unavailable rounds, cancellation, and capacity/deadline incompleteness do not count toward that threshold.
- TCP uses threshold 2 because every target is one explicitly configured host/port service and R0-005 creates no user incident or alert. Later notification policy may add user-facing debounce without changing the local technical evidence.

Nominal base-interval multiples are not detection-time service-level agreements. Actual elapsed detection time also includes completion-based scheduling, jitter, admission delay, transport timeout, and logical-round deadlines.

The remaining deliberate reconciliations are:

- Technical Feasibility supplies the approved resource caps, 25-target boundary, three-check “protected” concept, and later seven-day soak. Its disk cap is explicitly a persistent “Disk growth” target tested by “Database size trend”; R0-005 therefore measures attributable runtime-file-size growth rather than aggregate process I/O. R0-005 adopts the caps and target count but keeps “protected,” alert truth, and the long soak outside its local operational-state merge gate.
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
- On every process restart, configuration is rebound from startup sources and every enabled probe is reconstructed as `unknown`. Scheduler activation after readiness moves it to `baselining` and clears all failure, recovery, rate, and flapping counters.
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

#### Registrable-domain normalization and pinned suffix data

HTTPS provider hosts must be DNS names. IPv4 and IPv6 literals are invalid for HTTPS providers even though explicit TCP targets may use IP literals. Provider-host normalization is deterministic and occurs before host uniqueness and registrable-domain validation:

1. Trim exactly one optional terminal dot.
2. Reject an empty result, empty labels, consecutive dots, whitespace, control characters, or other invalid DNS syntax.
3. Convert each Unicode label independently to ASCII with .NET 10 `System.Globalization.IdnMapping` and `UseStd3AsciiRules = true`.
4. Lowercase the resulting ASCII form using invariant rules.
5. Enforce the normal DNS limits after ASCII conversion: each label is 1–63 octets and the complete name is at most 253 ASCII characters excluding the optional terminal dot.

R0-005 pins the following official Public Suffix List snapshot:

| Field | Binding value |
| --- | --- |
| Official upstream repository | <https://github.com/publicsuffix/list> |
| Full upstream commit SHA | `e1b8015c3b2f0f4f8c18659c2480fc1a22c07b20` |
| Source file | `public_suffix_list.dat` |
| File SHA-256 | `fe6adc7fb8014f57d28d69b18d0aa3e581efb432544922e12131a5d4a87bd954` |
| License | Mozilla Public License 2.0 (`MPL-2.0`) |
| Included sections | ICANN and PRIVATE |

This source was independently verified on 2026-07-30: the official repository's Git `HEAD` and GitHub API `main` commit both resolved to the full SHA above; the raw file bytes produced the recorded SHA-256; the file contained one complete ICANN section and one complete PRIVATE section; and the repository `LICENSE` and GitHub license metadata identified MPL-2.0.

The future implementation must vendor the exact `public_suffix_list.dat` bytes identified by that commit and SHA-256. Suffix processing performs no startup download, background refresh, network access, or automatic data update. Configuration validity therefore cannot change without a reviewed repository update.

Public-suffix matching must implement the official Public Suffix List algorithm:

- process exact, wildcard, and exception rules;
- choose the prevailing rule according to the official algorithm;
- include both the ICANN and PRIVATE sections;
- apply the implicit `*` rule only as defined by that algorithm.

The registrable domain is the label immediately preceding the prevailing public suffix plus that suffix. A host that is itself a public suffix, or that has no registrable label, is invalid. All configured provider hosts must produce distinct normalized registrable domains under ordinal case-insensitive comparison.

This pinned snapshot remains binding for R0-005. Any suffix-data update requires a reviewed requirements change, a new full upstream commit SHA, a new file SHA-256, license review, security review, and full configuration-boundary regression tests. Existing deployed configuration must not be silently reinterpreted by a live list update.

This documentation correction authorizes no new production package. If implementation requires a parser dependency instead of a repository-owned parser, stop for explicit package, license, security, and vulnerability approval. No dependency may fetch suffix data at runtime.

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

`icmp_unavailable` retains route evidence and cannot by itself move a target to `offline`. When `icmp_unavailable` occurs before any successful method result while the target is `baselining`, the state remains `baselining` (`Unknown` projection). From `healthy`, it lowers confidence to `suspect` (`Degraded` projection) without incrementing either gateway failure streak. From `baselining`, `suspect`, `recovering`, or `offline`, state and counters remain unchanged. An already `offline` state remains offline until an actual success begins recovery.

The missing-active-interface/default-route threshold is two consecutive rounds. The ICMP timeout/unreachable threshold is three consecutive counted failures. A successful gateway round resets both gateway failure streaks. A missing-route failure resets the ICMP streak before incrementing the missing-route streak, and an ICMP timeout/unreachable resets the missing-route streak before incrementing the ICMP streak. The two classifications therefore form separate consecutive streaks and never combine toward either threshold.

### 3.2 DNS

DNS is a separate logical probe with its own state, counter, transition history, and evidence. It uses the system resolver and the configured query name.

Success requires a valid A or AAAA result within two seconds. Timeout, resolver failure, or no valid address is a counted DNS failure. The offline threshold is two consecutive counted failures.

A DNS-only failure cannot declare the HTTPS aggregate or total internet state offline. Consumers must be able to see DNS state independently.

After every completed DNS logical round, the DNS reducer atomically publishes one immutable process-local DNS-correlation snapshot. It contains only:

- a strictly increasing process-local DNS round sequence;
- the monotonic DNS round-completion timestamp;
- the DNS round classification: `success`, `counted_failure`, or `non_counting`;
- the resulting DNS operational state.

These fields are internal coordination data. They must not enter logs, health output, resource reports, durable evidence, support output, or metrics. When the DNS probe is disabled, no DNS-correlation snapshot is published or expected.

### 3.3 HTTPS aggregate

One logical HTTPS round attempts every configured provider, subject to global limits and the logical-round deadline. The HTTPS aggregate is one logical target and has at most one active logical round. Each configured provider invocation is a child transport attempt within that round, not another logical target. Within the one active round, at most one attempt per configured provider may be active, provider attempts may overlap, and no provider may be attempted more than once. A second HTTPS aggregate round cannot be admitted or started until the first round has reduced to one terminal aggregate result.

At the HTTPS logical-round start timestamp, the reducer captures exactly one immutable copy of the most recent published DNS snapshot whose completion timestamp is less than or equal to the HTTPS round start. A DNS round that completes after the HTTPS round starts can never alter that HTTPS round. Snapshot age uses only the injected monotonic clock:

```text
dns_snapshot_age =
  https_round_start - dns_snapshot_completion
```

A captured snapshot is fresh when `dns_snapshot_age <= 60 seconds`; the exact 60-second boundary is fresh. A negative age, timestamp reversal, or non-increasing DNS round sequence is an implementation invariant failure, not an inferred operational classification.

An endpoint succeeds only when:

- DNS/connect/TLS completes within the five-second endpoint timeout;
- normal certificate-chain and hostname validation succeeds;
- no redirect occurs;
- the exact configured status is returned;
- when and only when an expected body is configured, the exact bounded body matches.

Aggregation is deterministic:

1. If cancellation, the deadline, or capacity prevents even one configured endpoint from starting, or prevents an admitted endpoint from producing a classified transport result, the logical round is `incomplete`; operational state-machine counters and state do not change. An endpoint invocation that never starts does not enter attempt accounting. An invocation that starts is still accounted separately at attempt level: a classified timeout is `timed_out`, while an invocation with no terminal outcome when accounting closes is attempt-level `incomplete` and fails acceptance under section 8.8.
2. If every endpoint completes and at least one succeeds, the aggregate round succeeds.
3. If every endpoint completes, at least one succeeds, and at least one fails, emit `partial_endpoint_failure`; reset the aggregate failure streak and apply a success transition. Do not create an incident or change the user-facing state because of the failed provider.
4. If every endpoint completes and all providers fail exclusively during DNS resolution, apply exactly one of these mutually exclusive reductions using the snapshot captured at HTTPS round start:
   - DNS probe disabled: no snapshot is expected and `blocked_by_dns` is unavailable; reduce the HTTPS aggregate as an ordinary counted HTTPS failure.
   - No snapshot or stale snapshot: classify the HTTPS logical round as incomplete with `dns_correlation_unavailable`; do not advance HTTPS failure or recovery counters.
   - Fresh snapshot classified `non_counting`: classify the HTTPS logical round as incomplete with `dns_correlation_unavailable`; do not advance HTTPS counters.
   - Fresh snapshot classified `counted_failure` whose resulting DNS state is `suspect` or `offline`: classify the HTTPS round as `blocked_by_dns`; do not advance HTTPS counters.
   - Fresh snapshot classified `success`: reduce the all-provider DNS-resolution failure as an ordinary counted HTTPS failure.
   - Any snapshot classification/resulting-state combination not permitted above is an implementation invariant failure rather than an inferred classification.
5. If every endpoint completes and none succeeds for any other combination of reasons, the aggregate round is an ordinary counted HTTPS failure.

The HTTPS aggregate offline threshold is two consecutive counted failures. A single provider can never independently declare the aggregate offline.

### 3.4 TCP

Each enabled TCP target is an independent logical target with its own state, counter, transition history, and schedule.

A round performs one asynchronous TCP connection attempt to exactly the configured host and port. Successful connection is success. Refused, timed out, unreachable, DNS/connect error, or other bounded transport failure is a counted failure. The socket is disposed immediately; no application protocol, payload, banner, authentication, TLS substitution, retry, alternate address/port, or discovery occurs.

The TCP offline threshold is two consecutive counted failures.

## 4. Scheduler contract

### 4.1 Activation and degradation

The scheduler may activate only after R0-004 reports both journal readiness and formal service readiness. A configured logical target is constructed as `unknown`. At the exact injected-monotonic timestamp of successful scheduler activation, activation moves every enabled target from `unknown` to `baselining`, clears every failure streak, recovery progress, and flapping history, makes every first round due immediately through the same global controls as later work, and creates the scheduler's one process-local token bucket with exactly eight token-seconds of credit and that activation timestamp as its refill anchor. No transport round may be dispatched or reduced while a target remains `unknown`; a round result observed for an `unknown` target is an implementation invariant failure, not an operational transition.

If journal health or formal service readiness becomes false:

- stop admitting new rounds;
- cancel queued and active attempts promptly;
- clear schedules, discard the complete token-bucket credit and refill anchor, and clear counters, recovery progress, and flapping history;
- keep the process alive under R0-004's existing degraded-service behavior;
- do not collect, log, buffer, persist, or emit observations while degraded;
- do not recreate the journal, restore readiness, or invent writes from R0-005.

Recovery of probe execution requires a healthy process restart. No token credit or anchor survives degradation or restart. On restart, all enabled targets are reconstructed as `unknown` and enter `baselining` only through scheduler activation after readiness; that later valid activation creates a new full eight-token bucket and a new injected-monotonic activation anchor.

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

Jitter comes from an injected deterministic source in tests. Scheduling uses fixed delay from completion, does not overlap a Gateway, DNS, or TCP logical target's transport attempt, does not overlap HTTPS aggregate logical rounds, and does not catch up or burst after delay, sleep, or backpressure. Concurrent provider children inside one HTTPS aggregate round are the sole permitted within-target transport overlap.

Nominal threshold × base-interval calculations are not elapsed-time guarantees or service-level agreements because completion-based delay, jitter, admission delay, transport timeouts, and the logical-round deadline also contribute.

### 4.3 Timeouts and deadline

| Operation | Timeout |
| --- | ---: |
| Gateway ICMP | 1.5 seconds |
| DNS resolution | 2 seconds |
| TCP connect | 2 seconds |
| Each HTTPS endpoint | 5 seconds |
| Any logical round, including capacity/rate waiting | 10 seconds |

There is no retry within a round. A timed-out transport that was actually started is a counted failure. Each HTTPS child timeout runs independently from that child's transport-start instant, while the ten-second deadline runs once from HTTPS logical-round admission and includes all child slot/token waiting. Token availability is evaluated by the continuous lazy-refill rules in section 4.4 at exact injected-monotonic instants; neither a coarse refill tick nor an operating-system timer cadence may delay a token that has accrued exactly one token-second of credit. A child terminal classification timestamped at or before the round deadline is included; when a child timeout and the round deadline have the same monotonic timestamp, the child timeout classification is applied before testing round completion. No child may start after the round deadline. A logical round that expires before all required attempts can start or complete is a scheduler-level incomplete outcome and does not change operational state-machine counters or state. A required transport that never starts does not enter attempt accounting or consume a token; any transport invocation that did start remains subject to the attempt-level closure rules in section 8.8 and never receives a token refund.

### 4.4 Capacity, configured budgets, and token consumption

- At most four network attempts are active process-wide.
- Gateway, DNS, and each TCP logical target have at most one active transport attempt.
- The HTTPS aggregate has at most one active logical round; inside it, at most one child transport attempt per configured provider may be active, and up to four distinct provider children may overlap subject to the process-wide cap.
- The active scheduler owns exactly one process-local token bucket shared by every probe method. Its refill rate is exactly one token per monotonic second, equivalent to 60 tokens per minute, and its capacity is exactly eight tokens.
- ICMP sends, DNS queries, HTTPS endpoint requests, and TCP connects each consume exactly one token at transport start. A started attempt is never refunded, regardless of success, failure, timeout, cancellation, or incomplete accounting.
- Local route/interface discovery consumes no token and no network-attempt slot.
- Waiting for a slot or token is cancelable and counts against the ten-second logical-round deadline.
- Rate-limited or capacity-delayed work that cannot start in time is incomplete, not failed.

Token credit uses exact monotonic-duration arithmetic or an equivalent fixed-point/rational representation. One token is exactly one second of refill credit, and capacity is exactly eight seconds of credit. Binary floating-point accumulation, rounded token counts, periodic timer ticks, a background refill loop, operating-system timer cadence, wall-clock time, and scheduler tick counts are not permitted inputs.

The bucket is initialized only by successful scheduler activation under section 4.1: credit is exactly eight token-seconds and the refill anchor is the exact injected-monotonic activation timestamp. Refill is continuous and evaluated lazily whenever availability or consumption is tested. At an evaluation timestamp `now`, the dispatcher:

1. obtains `now` only from the injected monotonic clock;
2. fails an implementation invariant if `now` is earlier than the prior refill anchor;
3. adds exactly `now - prior_refill_anchor` to credit;
4. caps credit at exactly eight token-seconds; and
5. advances the refill anchor to `now`.

Fractional credit is retained exactly. One-half second contributes one-half token but cannot start a transport by itself. A token becomes available at the exact instant cumulative credit reaches one token-second. Multiple eligibility evaluations at the same monotonic timestamp receive no refill between evaluations and remain ordered by section 4.5. Sleep, suspend, scheduler delay, or other long elapsed time can refill only to capacity; it creates no catch-up balance beyond eight tokens.

Credit and the refill anchor remain process-local internal scheduler state. They are not persisted or exposed through a public contract, schema, cloud evidence, health output, support output, logs, or metrics and contain no target, provider, endpoint, or customer value.

Configuration validation calculates the worst-case demand atomically before scheduling any probe. The minimum possible scheduled interval is 27 seconds for gateway, DNS, and HTTPS aggregate rounds, which is 90% of their 30-second base interval. For each enabled TCP target:

`minimum_possible_tcp_interval_seconds = max(15, 0.9 × configured_interval_seconds)`

Using double-precision arithmetic without rounding down before comparison:

```text
dns_enabled_indicator =
  1 when DNS probe is enabled
  0 when DNS probe is disabled

gateway_attempt_demand = 60 / 27
dns_attempt_demand = dns_enabled_indicator × (60 / 27)
https_attempt_demand = provider_count × (60 / 27)
tcp_attempt_demand =
  sum(60 / minimum_possible_tcp_interval_seconds
      for every enabled TCP target)

worst_case_attempts_per_minute =
    gateway_attempt_demand
  + dns_attempt_demand
  + https_attempt_demand
  + tcp_attempt_demand
```

Configuration is valid only when `worst_case_attempts_per_minute <= 60`. Excess demand rejects the entire configuration before scheduling with bounded code `probe_rate_budget_exceeded`.

A second deterministic startup budget and all runtime acceptance accounting use one closed exact charge table for each started attempt:

| Probe method | Exact charged bytes per started attempt |
| --- | ---: |
| Gateway ICMP | 128 |
| DNS | 1,024 |
| TCP connect | 1,024 |
| HTTPS endpoint | 8,192 |

For a recognized method, `charged_method_bytes` is derived exclusively from this table and equals the table value exactly. It is not a lower bound, configuration value, adapter estimate, transport-provided value, measured-wire value, or implementation-selected uplift. Transport adapters may provide measured application bytes sent and received where available, but they must not provide, override, increase, or decrease `charged_method_bytes`. Concurrent HTTPS provider children are distinct started attempts and each derives exactly 8,192 charged bytes. Startup formulas and runtime acceptance must use the same table definition rather than duplicated or independently configurable values. Per-attempt lookup and runtime byte summation use deterministic integer semantics independent of platform and culture; configured-demand multiplication retains the explicitly specified double-precision comparison below.

```text
gateway_charged_network_demand =
  gateway_attempt_demand × 128

dns_charged_network_demand =
  dns_enabled_indicator × 1,024 × (60 / 27)

https_charged_network_demand =
  https_attempt_demand × 8,192

tcp_charged_network_demand =
  sum((60 / minimum_possible_tcp_interval_seconds) × 1,024
      for every enabled TCP target)

worst_case_charged_network_bytes_per_minute =
    gateway_charged_network_demand
  + dns_charged_network_demand
  + https_charged_network_demand
  + tcp_charged_network_demand
```

Configuration is valid only when `worst_case_charged_network_bytes_per_minute < 100,000`. A value at or above the approved decimal cap rejects the entire configuration before scheduling with bounded code `probe_network_budget_exceeded`. This exact charged-byte model is deterministic conservative accounting for configuration and acceptance; it is not packet capture or a claim of exact physical-wire traffic.

A disabled DNS probe creates no DNS scheduler target, consumes no DNS token or network-attempt slot, and contributes zero DNS demand to both startup budgets. HTTPS provider DNS resolution remains charged as part of each HTTPS provider attempt; disabling the independent DNS probe does not remove or reduce HTTPS demand. Configuration validation remains atomic, and neither budget error may echo target IDs, intervals, hosts, ports, provider details, operator identifiers, endpoints, calculated customer-specific values, or other sensitive configuration.

A 15-second TCP interval remains individually permitted only when the complete configured set passes both global budgets. With DNS enabled and the two default HTTPS providers, 25 targets at 15 seconds are invalid: worst-case demand is approximately 108.89 attempts/minute and 141,368.89 charged bytes/minute. The same 25 targets at a 60-second configured interval are valid: worst-case demand is approximately 36.67 attempts/minute and 67,413.33 charged bytes/minute. Different provider counts and the DNS enabled state can change configuration validity.

After a transport attempt becomes eligible through the fair-dispatch rules, it may start only when its applicable target/round/provider guard, one global execution slot, and at least one exact token-second of credit are all available. The dispatcher must not hold a global slot while waiting for token credit and must not subtract credit while waiting for a slot. At one deterministic atomic transport-start boundary it obtains one injected-monotonic `now`, uses that same value for lazy refill and `transport_start`, claims the guards and slot, subtracts exactly one token-second, records `transport_start = now`, and immediately invokes the transport. Cancellation before that boundary consumes no token. Once transport invocation starts, its token is never refunded. Each HTTPS provider child performs this acquisition and accounting independently even when multiple children start at the same timestamp in one dispatch cycle; same-timestamp children receive no intervening refill and follow the captured stable provider/fair-dispatch order. Acceptance phase membership uses each child's own transport-start timestamp, never fair-queue admission or aggregate-round admission.

### 4.5 Deterministic fair dispatch

The dispatcher uses a deterministic rotating fair queue over due logical targets. Gateway, DNS, HTTPS aggregate, and every enabled TCP target are logical targets. Each has one stable scheduler key that determines a stable order and is never logged or exposed.

- The dispatcher maintains a rotating cursor over stable key order.
- Gateway, DNS, and each due TCP logical target contribute at most one attempt per pass.
- When an HTTPS logical round is admitted, its provider children are sorted by stable provider ID using `.NET StringComparer.Ordinal`. The captured order is immutable for that round and is never logged or reported.
- On the HTTPS target's dispatch turn, the dispatcher offers every not-yet-started provider child in that stable order during one bounded fan-out. Every child for which a slot and at least one exact token-second of credit are available starts without waiting for an earlier provider to complete and without waiting for a complete pass over all other logical targets. Children evaluated at the same timestamp consume available credit in that stable order with no intervening refill.
- If the initial fan-out leaves provider children pending because a slot or token credit was unavailable, subsequent dispatch opportunities alternate one pending child with one next due non-HTTPS logical target, beginning with the pending child and skipping an empty lane. Waiting for credit never reserves a global slot, and waiting for a slot never reserves or subtracts credit. Pending provider children therefore remain bound to the same round and deadline but are never moved behind a complete pass over all other logical targets.
- Child completion order does not affect aggregation. The round completes only after every configured provider has exactly one terminal child result; reduction reads results in the captured stable provider order.
- Cancellation stops new child starts and promptly cancels active children. Deadline expiry stops new child starts, applies every terminal child result timestamped at or before the deadline, and then reduces the round as `incomplete` if any required child never started or lacks a terminal classification.
- Canceled, disabled, or no-longer-due work is skipped.
- A provider fan-out is bounded by the configured maximum of four children, by the process-wide cap of four, and by one active HTTPS round. After that bounded fan-out, the rotating non-HTTPS cursor continues from its prior position; no second HTTPS round can consume slots or tokens until fixed-delay scheduling after completion makes it due.
- Bounded HTTPS occupancy must not cause systematic starvation of Gateway, DNS, or TCP work under sustainable valid mixed demand.
- Transient backpressure can still make a round incomplete under the ten-second deadline.

Fair-queue selection is the admission step. Execution-slot acquisition and token consumption then follow the ordering in section 4.4.

### 4.6 Shutdown

On shutdown:

- stop admitting new work first;
- cancel queued and active attempts;
- discard token credit and its refill anchor after no further transport start can occur;
- dispose transports;
- complete scheduler shutdown within five seconds.

No fixed unbounded sleep or real multi-minute wait is allowed. Timing-sensitive production code uses `TimeProvider` or an equivalent injected clock.

## 5. State machine

### 5.1 State variables and activation invariant

The six operational states remain `unknown`, `baselining`, `healthy`, `suspect`, `offline`, and `recovering`.

DNS, the HTTPS aggregate, and each TCP target maintain one consecutive-failure streak. The streak is bounded by saturation at that method's offline threshold. Gateway maintains two separate streaks: one consecutive missing-route streak with threshold 2 and one consecutive ICMP timeout/unreachable streak with threshold 3. The gateway streaks never combine.

Each logical target also has one recovery-success count. It is meaningful only while the target is `recovering` and is zero in every other state.

Activation is separate from round processing:

- a configured logical target is constructed as `unknown`;
- scheduler activation after readiness moves it from `unknown` to `baselining`;
- activation clears all failure streaks, recovery progress, and flapping history;
- no transport round may be dispatched or reduced while the target remains `unknown`;
- a round result observed for an `unknown` target is an implementation invariant failure, not another operational transition.

### 5.2 Failure-streak rules and thresholds

The state reducer applies these rules in order:

1. A counted failure increments the applicable failure streak before testing its threshold.
2. A failure is below threshold when the post-increment applicable streak is less than the threshold.
3. A failure reaches threshold when the post-increment applicable streak is greater than or equal to the threshold.
4. Failure streaks saturate at their thresholds and never grow without bound.
5. For Gateway, a missing-route failure increments the missing-route streak and resets the ICMP timeout/unreachable streak to zero. An ICMP timeout/unreachable increments the ICMP streak and resets the missing-route streak to zero. Changing gateway failure classification therefore starts a new consecutive streak and cannot combine prior failures.
6. A successful round resets all failure streaks for that logical target.
7. An incomplete or other non-counting round leaves all failure streaks unchanged.
8. Any counted failure resets recovery progress to zero.
9. A successful transition from `offline` sets recovery successes to 1.
10. The next consecutive success from `recovering` completes recovery and returns the logical target to `healthy`; after the transition, recovery successes reset to zero.

The R0-005 offline thresholds remain:

| Logical target and failure classification | Threshold |
| --- | ---: |
| Gateway missing active interface/default route | 2 |
| Gateway ICMP timeout/unreachable | 3 |
| DNS | 2 |
| HTTPS aggregate | 2 |
| TCP | 2 |

### 5.3 Total active-state transition table

After activation, the reducer uses this total transition table:

| Current state | Round result | Next state | Failure-counter effect | Recovery-counter effect |
| --- | --- | --- | --- | --- |
| `baselining` | success | `healthy` | reset all failure streaks | reset to 0 |
| `baselining` | counted failure below threshold | `suspect` | increment applicable streak; apply gateway exclusivity when applicable | reset to 0 |
| `baselining` | counted failure reaches threshold | `offline` | increment and saturate applicable streak; apply gateway exclusivity when applicable | reset to 0 |
| `healthy` | success | `healthy` | reset all failure streaks | reset to 0 |
| `healthy` | counted failure | `suspect` | set applicable streak to 1; reset incompatible gateway streak | reset to 0 |
| `suspect` | success | `healthy` | reset all failure streaks | reset to 0 |
| `suspect` | counted failure below threshold | `suspect` | increment applicable streak; apply gateway exclusivity when applicable | reset to 0 |
| `suspect` | counted failure reaches threshold | `offline` | increment and saturate applicable streak; apply gateway exclusivity when applicable | reset to 0 |
| `offline` | success | `recovering` | reset all failure streaks | set to 1 |
| `offline` | counted failure | `offline` | increment and saturate applicable streak; apply gateway exclusivity when applicable | reset to 0 |
| `recovering` | success | `healthy` | reset all failure streaks | record second success, then reset to 0 |
| `recovering` | counted failure | `offline` | set applicable streak to 1; reset incompatible gateway streak | reset to 0 |
| any active state | incomplete or ordinary non-counting result | unchanged | unchanged | unchanged |
| Gateway `healthy` | `icmp_unavailable` | `suspect` | unchanged | reset to 0 |

For the final two rows, an active state is exactly `baselining`, `healthy`, `suspect`, `offline`, or `recovering`.

The Gateway `healthy` plus `icmp_unavailable` row is the sole exception to the general non-counting/unchanged row and takes precedence over it. For Gateway `baselining`, `suspect`, `recovering`, or `offline`, `icmp_unavailable` leaves state and counters unchanged. `offline` remains `offline` after every counted failure and can begin recovery only after a successful round. Repeated success in `healthy` remains `healthy`, and repeated below-threshold failures in `suspect` remain `suspect`. These self-transitions do not count as flapping transitions because the state does not change.

Every method-specific successful logical round uses the success rows. This includes partial HTTPS provider failure when at least one provider succeeds, repeated normal success while already `healthy`, success that begins recovery from `offline`, and the second consecutive success that completes recovery.

Cancellation, rate limitation, supersession, configuration invalidity, capacity incompleteness, deadline incompleteness, method unavailability, and scheduler-level incomplete rounds do not advance failure or recovery counters. The only non-counting state change is Gateway `healthy` plus `icmp_unavailable` to `suspect`. This operational rule does not excuse a started transport invocation from attempt-level outcome accounting.

State is observational, not causal. `offline` means repeated technical failure; it does not prove disconnection, sabotage, intrusion, or intent.

## 6. Flapping evidence

A logical target is flapping when at least four state transitions among `healthy`, `suspect`, `offline`, and `recovering` occur in a rolling ten-minute window.

- Entry into `unknown` or `baselining` is not qualifying.
- Repeated rounds that leave state unchanged are not transitions.
- In particular, repeated success in `healthy`, below-threshold failure in `suspect`, and counted failure in `offline` do not add flapping transitions.
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
| HTTPS | `https_succeeded`, `https_dns_failed`, `https_connect_failed`, `https_tls_failed`, `https_timeout`, `https_redirect_rejected`, `https_unexpected_status`, `https_body_mismatch`, `https_body_too_large`, `endpoint_not_started`, `partial_endpoint_failure`, `blocked_by_dns`, `dns_correlation_unavailable` |
| TCP | `tcp_connect_succeeded`, `tcp_connect_refused`, `tcp_connect_timeout`, `tcp_connect_failed` |

Codes are not user-facing causal claims. Production logs and metrics may include the probe kind, code, aggregate counts, state names, and bounded duration. They must not contain:

- exception text, stack traces from expected transport failures, or operating-system error strings;
- IDs, labels, hostnames, IP addresses, ports, URLs, query names, DNS answers, headers, bodies, interface descriptions, route details, or machine paths;
- payloads, credentials, authorization, user information, cookies, customer identifiers, or provider trace metadata;
- DNS-correlation sequence, completion timestamp, round classification, or captured resulting state;
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

`-DurationMinutes 30` means 30 complete steady-state minutes after the service first reaches monitoring-ready and the steady-state baseline bundle below is committed. Initialization and the complete start barrier are additional and do not reduce that window.

- Initialization begins immediately before the service process is started and ends when monitoring-ready is first observed. Immediately before process launch, the sampler creates or verifies the dedicated monitored runtime directory and captures the complete recursive process-start runtime-file snapshot used for startup disk attribution. Failure to reach readiness within 60 seconds fails the run.
- The exact service PID is captured after process creation.
- Process exit, PID change, automatic restart, or unplanned termination after initial PID capture fails immediately. The sampler must not resume against a replacement PID.
- Startup CPU is reported separately and is excluded from the approved steady-state average CPU calculation.
- Every successfully collected working-set sample after process creation, including initialization, barrier, steady-state, and drain samples, remains subject to the absolute working-set cap.

The transport-start instant is the monotonic timestamp captured at the same point the token is consumed, immediately before invoking the transport. Admission time, queue time, slot-wait time, and token-wait time do not determine resource-phase membership.

After monitoring-ready, the acceptance coordinator establishes a clean steady-state start barrier:

1. Pause all new network-attempt admissions.
2. Cancel queued attempts that have not started as scheduler-level non-counting/incomplete work. They consume no token and do not enter a transport-attempt array.
3. Allow every started attempt to reach one terminal attempt outcome.
4. After no started attempt remains and while admissions remain paused, establish one steady-state baseline bundle containing the complete consistent recursive runtime-file snapshot, the exact process-CPU baseline, the acceptance-only attempt/network-accounting reset, and one injected-monotonic boundary timestamp. The reset seals the separately reported startup attempt/network totals and initializes the steady-state attempt/network accumulators to zero; it does not erase startup evidence.
5. Capture the process-CPU baseline immediately before the monotonic bundle-commit timestamp. Commit the bundle atomically; `steady_state_start` is exactly that successful commit timestamp. There is no wait for a later pre-existing sampling-grid boundary.
6. Resume admissions only after the baseline bundle and `steady_state_start` exist. Begin the requested 30 complete steady-state minutes at that exact timestamp.

The complete start-barrier operation, from the admission pause through successful baseline-bundle commit, has one ten-monotonic-second deadline. Exceeding it adds `measurement_boundary_timeout` and fails the run; drain completion does not start a new allowance. The runtime-file component must be a stable complete snapshot. If the monitored tree changes during capture and consistency cannot be proven, the sampler may retry only within the same bounded barrier. If no stable complete snapshot can be committed before the deadline, the sampler fails with `runtime_file_snapshot_incomplete` and `measurement_boundary_timeout` as independently applicable rather than accepting an ambiguous baseline.

The acceptance pause does not initialize, reset, discard, or refund scheduler token-bucket credit. While admissions are paused, exact elapsed monotonic duration may lazily refill the active bucket only up to eight token-seconds under section 4.4. The next eligible starts after resume use that existing bucket and anchor. Only scheduler activation, readiness degradation, restart, or shutdown has the token-bucket lifecycle effects defined in sections 4.1 and 4.6.

`steady_state_start_barrier_elapsed_ms` measures from the admission pause immediately after readiness through the baseline-bundle commit at `steady_state_start`. For report attribution, `startup` spans process start up to that commit and therefore includes both initialization and the complete start barrier. All process CPU consumed before the committed CPU baseline, all runtime-file growth present in the committed snapshot, and every transport invocation whose transport-start instant precedes `steady_state_start` are startup-only. Steady-state CPU begins from the same committed CPU baseline, steady-state disk begins from the same committed runtime-file snapshot, and steady-state attempts/network begin at the same monotonic boundary. No CPU or disk interval is omitted, double-counted, or assigned to both phases.

Steady-state membership is the half-open interval:

```text
steady_state_start <= transport_start < steady_state_end
```

An invocation started before `steady_state_start` is startup work. An invocation started within the half-open interval is steady-state work. Each started invocation belongs to exactly one phase, and its bytes and outcome are never split across phases.

At exact `steady_state_end`, the acceptance coordinator establishes the end barrier:

1. Pause new network-attempt admissions before another token can be consumed.
2. Cancel attempts admitted but not started before the endpoint as scheduler-level non-counting work. They consume no token and do not enter steady-state attempt accounting.
3. Allow attempts started before the endpoint to drain to one terminal result. They remain steady-state attempts and contribute their complete sent, received, and charged bytes plus exactly one complete outcome even when completion occurs during the drain.
4. Fail the run if the end drain exceeds 10 monotonic seconds. A started invocation without a terminal result at that deadline becomes attempt-level `incomplete`, adds `attempt_accounting_incomplete`, and fails the run.

No invocation may start at or after `steady_state_end`. Such a start adds `measurement_boundary_violation` and fails the run. `steady_state_end_drain_elapsed_ms` measures from exact `steady_state_end` until every started steady-state attempt has a terminal accounting outcome or the drain deadline expires.

The top-level `attempts` object represents steady-state attempts only. Steady-state network attribution includes the full bytes of every steady-state attempt, including an attempt that completes during the end drain. After the end drain, the sampler captures the complete recursive final runtime-file snapshot. Startup disk attribution compares the process-start snapshot with the runtime-file snapshot committed in the steady-state baseline bundle; steady-state disk attribution compares that same committed snapshot with the final snapshot. Startup CPU ends at the bundle's committed CPU baseline; the steady-state CPU numerator begins at that identical baseline and includes process CPU through end-drain completion, while its denominator remains the exact requested 30 steady-state minutes. Working-set absolute-cap enforcement continues through end drain. Growth medians use only steady-state minutes 1–5 and 26–30 and never include drain samples.

The 30-minute run is an R0-005 merge gate. A seven-day soak with zero false Critical alerts and no unplanned termination remains a later R0 release-exit/controlled-acceptance gate because R0-005 creates no alerts.

### 8.2 Merge-gate Windows acceptance-host profile

The merge-gate profile identifier is exactly `r0-005-windows11-x64/v1`. The required host is:

- Windows 11, OS build 26100 or later;
- x64 operating system and x64 agent process;
- .NET runtime major version 10;
- Release configuration;
- 4 through 32 logical processors, inclusive;
- at least 8,000,000,000 bytes total physical memory;
- AC power with battery saver disabled;
- Windows Balanced power scheme GUID `381b4222-f694-41f0-9685-ff5bb260df2e`;
- agent process priority `Normal`;
- no debugger attached;
- no managed or native profiler attached;
- no concurrent repository build or test process during sampling.

A physical machine or dedicated virtual machine may be used, but virtualization status must be recorded. Shared or CPU-quota-limited runners are not valid native merge-gate hosts.

Before starting the agent, the sampler runs a 60-monotonic-second host preflight. It collects exactly 12 samples at the fixed 5-, 10-, ..., 60-second boundaries. The average non-agent system CPU across those samples must be less than 10.0%, and available physical memory at the final sample must be at least 2,000,000,000 bytes. A missing preflight sample fails. An unsupported static profile or unavailable required metadata adds `host_profile_unsupported`; a failed idle/resource preflight adds `host_profile_preflight_failed`.

Every required host-profile field in section 8.8 must be available. Processor vendor, family, model, and stepping are bounded hardware-class metadata, not arbitrary environment dumps. The report must never include machine name, username, domain membership, serial number, BIOS serial, device UUID, Windows product key, MAC address, IP address, disk serial, cloud instance ID, or arbitrary environment variables.

The 30-minute result is evidence for the exact executable on the recorded profile; it is not a universal performance claim for every Windows computer. Another architecture or materially different acceptance profile requires a separate reviewed profile and resource report. Exact bounded OS, runtime, build, and hardware-class metadata remains in the evidence so later runs can be compared.

### 8.3 Clock and sampling

Elapsed time and sampling deadlines use a monotonic clock. Pre-steady-state initialization/barrier sampling may use a separately reported fixed-rate grid anchored at initialization start, immediately before process launch; those samples remain subject to the absolute working-set cap but do not define the steady-state boundary, CPU baseline, disk baseline, complete-minute windows, or acceptance accounting reset. `steady_state_start` is the baseline-bundle commit itself. The steady-state fixed-rate grid is re-anchored there: the first steady-state sample boundary is exactly `steady_state_start + 5 seconds`, and every later boundary is an exact five-second multiple from `steady_state_start`. `steady_state_end` is exactly `steady_state_start + requested_steady_state_duration`. UTC wall-clock timestamps may be recorded for correlation, but no calculation uses wall-clock elapsed time.

The sampler uses fixed-rate monotonic schedules with one sample every five seconds and no catch-up burst, continuing through end-drain completion. `valid_sample_count` and `missing_sample_count` cover every required process sample after process creation through that completion, across the separately anchored pre-steady-state and steady-state schedules without duplicating a sample. A sample is missing when no valid sample is collected within ten seconds of its scheduled deadline or when any complete steady-state minute contains fewer than 11 valid samples. Any missing sample fails the run. Only samples at steady-state boundaries derived from `steady_state_start` and within the 30-minute half-open steady-state interval participate in complete-minute growth windows.

At each valid sample, the sampler refreshes the exact service process and records:

- process total processor time;
- working set in bytes;
- private bytes in bytes;
- handle count;
- thread count;
- monotonic sample time and bounded UTC correlation time.

### 8.4 CPU

`logical_processor_count = Environment.ProcessorCount` is captured once at run start. For consecutive valid process samples, with processor time and monotonic wall time expressed in the same unit:

```text
normalized_cpu_percent =
  100 × delta_process_total_processor_time
      / (delta_monotonic_wall_time × logical_processor_count)
```

Negative processor-time deltas, zero or negative wall-time deltas, PID changes, and counter resets fail the run. Only after raw inputs have been validated may a reported sampled percentage be bounded to 0–100.

The baseline bundle captures one process-CPU value immediately before its monotonic commit. Startup CPU attribution ends at that value, and steady-state average CPU uses the process-CPU delta from that identical committed baseline through end-drain completion with the same logical-processor normalization. No CPU interval between drain completion and `steady_state_start` exists outside those phases because the commit is the boundary. The denominator remains the exact requested 30 steady-state minutes; drain duration does not extend it. The pass condition is `steady_state_average_cpu_percent < 2.0`. Startup observation duration, startup total CPU time, and startup maximum sampled normalized CPU are reported separately; a startup peak neither replaces nor relaxes the steady-state rule.

### 8.5 Memory, handles, threads, and growth

Every valid working-set sample after process creation must be less than 150,000,000 bytes. Reaching or exceeding that decimal-byte cap fails independently of growth calculations.

Growth calculations use complete steady-state per-minute medians anchored to `steady_state_start`. Each steady-state sample is assigned to exactly one complete half-open minute window derived from that boundary. For each minute, values are sorted without rounding; an odd count uses the middle value and an even count uses the arithmetic mean of the two middle values. The first-window value is the median of the five per-minute medians for steady-state minutes 1–5. The final-window value is the median of the five per-minute medians for minutes 26–30.

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

### 8.6 Disk attribution

Disk evidence measures attributable positive file-size growth beneath the dedicated acceptance runtime directory. It measures persistent runtime-footprint growth, matching the approved Technical Feasibility disk-growth/database-size intent; aggregate process write I/O is not disk evidence. Microsoft documents that its process write-transfer accounting includes file, network, and device operations, so aggregate process I/O counters are prohibited inputs to `attributed_disk_bytes` and `disk_mb_per_day`:

- <https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-wsrm/c0a51f88-04b5-4945-ba86-e8e8cf2df1fe>

Network or device I/O therefore cannot change either disk value.

Published binaries, immutable input configuration, and sampler-owned evidence/report files must remain outside the monitored runtime directory. The process-start snapshot, the snapshot committed in the steady-state baseline bundle, and the final snapshot recursively enumerate every file beneath that directory. A snapshot keys each file only by its normalized bounded relative path and records a nonnegative integer length. No absolute path enters a report. Enumeration or normalization must not skip or filter a runtime file; a path that escapes the monitored directory, including through traversal or a reparse target, fails rather than being followed or omitted.

The steady-state-start snapshot is committed atomically with the CPU baseline, accounting reset, and monotonic `steady_state_start`. It must be complete and consistent: monitored-tree changes during capture require a bounded retry under section 8.1, and failure to prove a stable snapshot within that barrier fails rather than creating an ambiguous baseline. The same calculation applies to the process-start/steady-state-start pair for `startup.attributed_disk_bytes` and the steady-state-start/final pair for `steady_state.attributed_disk_bytes`:

```text
positive_runtime_file_growth_bytes(baseline, ending) =
  sum(max(0, ending_size[path] - baseline_size[path])
      for every path present in ending,
      where baseline_size[path] is 0 when path is absent from baseline)

startup.attributed_disk_bytes =
  positive_runtime_file_growth_bytes(
    process_start_snapshot,
    steady_state_start_snapshot)

steady_state.attributed_disk_bytes =
  positive_runtime_file_growth_bytes(
    steady_state_start_snapshot,
    final_snapshot)

disk_mb_per_day =
  steady_state.attributed_disk_bytes
  × (1,440 / steady_state_minutes)
  / 1,000,000
```

For every snapshot pair, a new ending file has baseline size zero; an existing file contributes only `max(0, ending_size - baseline_size)`; and an unchanged, same-size rewritten, or reduced file contributes zero. A baseline file that is missing from the corresponding ending snapshot fails the run instead of contributing a negative value. An unreadable file, invalid or negative length, arithmetic overflow, directory escape, or incomplete recursive snapshot also fails deterministically.

The bounded violations are `runtime_file_disappeared` for a missing baseline file, `runtime_file_scope_violation` for directory escape, `runtime_file_snapshot_incomplete` for unreadable or incompletely enumerated snapshot data, and `runtime_file_snapshot_invalid` for an invalid length or arithmetic overflow. The generic counter-reset, counter-unavailable, counter-negative, and counter-overflow rules remain applicable to independently specified process CPU, sampling, and network-accounting inputs, but they are not disk-attribution rules.

For the required run, `steady_state_minutes = 30`. The pass condition is `disk_mb_per_day < 100`, using decimal MB/day.

R0-005 intentionally persists no probe evidence. Any unexpected runtime artifact creation must be reported only by bounded relative artifact name/category, never by private absolute path; disk growth attributable to probe results fails even if the aggregate cap would otherwise pass.

### 8.7 Network attribution

System-wide adapter counters, packet capture, payload capture, addresses, interface values, and endpoint values are prohibited. The implementation exposes acceptance-only aggregate accounting through its injected probe transport boundaries.

For every started attempt, accounting records only probe kind, measured application bytes sent, measured application bytes received, and `charged_method_bytes` derived exclusively from the closed exact table in section 4.4. No payload or endpoint value is retained. Measured application bytes are exact byte counts crossing the injected transport boundary where available. For a recognized method, the charged value is exactly 128 for Gateway ICMP, 1,024 for DNS, 1,024 for TCP connect, or 8,192 for an HTTPS provider request. It is never supplied or overridden by a transport adapter, configuration, measurement, or platform-specific uplift.

Concurrent HTTPS provider children are never coalesced. Each provider invocation has its own internal round/provider identity, token consumption, transport-start timestamp, sent/received/charged byte values, outcome, and phase membership. The reports aggregate those records without exposing provider IDs or endpoint values; concurrency changes neither the per-attempt formula nor completeness requirements.

```text
attributed_attempt_network_bytes =
  max(measured_sent + measured_received, exact_charged_method_bytes)

attributed_network_bytes =
  sum(attributed_attempt_network_bytes
      for attempts whose transport_start is in
      [steady_state_start, steady_state_end))

network_bytes_per_minute =
  attributed_network_bytes / steady_state_minutes
```

The pass condition is `network_bytes_per_minute < 100,000`, using decimal bytes/minute. Bytes from a steady-state attempt are included in full even when it completes during end drain; bytes are never split between phases. Different adapters that report identical measured sent/received counts must produce byte-identical attributed totals and reports. This is deterministic conservative probe-traffic attribution, not a claim of exact physical-wire or TLS/IP overhead measurement. Attempt counts are reported separately and packet capture remains prohibited.

An unknown probe method has no table charge and can never pass acceptance. To keep the failing report and aggregate totals complete without inventing a charge, its internal accounting record uses the contract-defined zero sentinel for `charged_method_bytes`, includes its nonnegative measured sent plus received bytes in `attributed_network_bytes`, counts once under `attempts.by_probe_kind["unknown"]`, and adds `unknown_probe_method`. The zero sentinel is not a recognized charge and is not configurable or adapter-supplied. Missing, malformed, negative, nonintegral, adapter-supplied, overridden, or conflicting charged values for a recognized method add `attempt_accounting_incomplete` and fail; the report builder still derives the exact table value for deterministic totals. Counter overflow, negative or missing measured bytes, missing accounting for a started attempt, or any unknown method also fails. Human and JSON reports are rendered from the same derived charge table, attempt records, and totals.

### 8.8 Deterministic reports

The sampler emits a bounded human-readable report and UTF-8 JSON from the same in-memory report model under ignored `artifacts/r0-005/`. The JSON schema identifier is `r0-005-resource-report/v1`. It uses invariant-culture numeric formatting, ISO-8601 UTC timestamps, fixed property names, stable enum/code values, deterministic array ordering, and no NaN or infinity.

The JSON has these exact top-level properties in this order:

1. `schema_version`
2. `source`
3. `configuration`
4. `host_profile`
5. `timing`
6. `startup`
7. `steady_state`
8. `growth`
9. `attempts`
10. `caps`
11. `violations`
12. `result`

`schema_version` is exactly `r0-005-resource-report/v1`. Required nested properties are:

| Object | Fixed properties |
| --- | --- |
| `source` | `commit_sha`, `configuration_name`, `executable_sha256` |
| `configuration` | `target_count`, `https_provider_count`, `logical_processor_count`, `sampling_interval_seconds`, `requested_steady_state_minutes` |
| `host_profile` | `profile_id`, `os_product`, `os_version`, `os_build`, `os_revision`, `os_architecture`, `process_architecture`, `framework_description`, `runtime_version`, `runtime_identifier`, `gc_mode`, `logical_processor_count`, `total_physical_memory_bytes`, `processor_vendor`, `processor_family`, `processor_model`, `processor_stepping`, `virtualization`, `power_source`, `power_scheme_guid`, `battery_saver_enabled`, `process_priority`, `debugger_attached`, `profiler_attached`, `idle_preflight_average_cpu_percent`, `idle_preflight_available_memory_bytes` |
| `timing` | `process_start_utc`, `readiness_utc`, `startup_elapsed_ms`, `steady_state_start_barrier_elapsed_ms`, `steady_state_start_utc`, `steady_state_end_utc`, `steady_state_end_drain_elapsed_ms`, `valid_sample_count`, `missing_sample_count` |
| `startup` | `cpu_observation_duration_ms`, `total_cpu_ms`, `maximum_normalized_cpu_percent`, `maximum_working_set_bytes`, `attributed_disk_bytes`, `attributed_network_bytes`, `attempts_started`, `attempts_completed`, `attempts_succeeded`, `attempts_failed`, `attempts_timed_out`, `attempts_canceled`, `attempts_incomplete` |
| `steady_state` | `average_normalized_cpu_percent`, `maximum_working_set_bytes`, `maximum_private_bytes`, `maximum_handle_count`, `maximum_thread_count`, `attributed_disk_bytes`, `disk_mb_per_day`, `attributed_network_bytes`, `network_bytes_per_minute` |
| `growth` | `working_set`, `private_bytes`, `handles`, `threads` |
| `attempts` | `by_probe_kind`, `by_outcome`, `started`, `completed`, `succeeded`, `failed`, `timed_out`, `canceled`, `incomplete` |
| `caps` | `readiness_ms`, `working_set_bytes`, `average_cpu_percent`, `disk_mb_per_day`, `network_bytes_per_minute`, `growth_percent`, `working_set_growth_noise_bytes`, `private_bytes_growth_noise_bytes`, `handle_growth_noise_count`, `thread_growth_noise_count` |
| `result` | the string `pass` or `fail` |

The table is normative for nested property presence and property order. `configuration.logical_processor_count` must exactly equal `host_profile.logical_processor_count`. The two barrier timing durations are nonnegative JSON integers in milliseconds. `timing.steady_state_start_utc` represents the UTC correlation value captured for the exact monotonic baseline-bundle commit; `steady_state_end_utc` correlates to exactly the requested duration after that commit. `startup.total_cpu_ms`, `steady_state.average_normalized_cpu_percent`, `startup.attributed_disk_bytes`, and `steady_state.attributed_disk_bytes` all use the one committed boundary model in sections 8.1, 8.4, and 8.6. The disk integers are the results of the process-start/steady-state-start and steady-state-start/final snapshot pairs; `steady_state.disk_mb_per_day` is calculated only from the latter. `startup.attempts_*` values are nonnegative JSON integers for startup transport invocations and satisfy the same started/completed/outcome identities as the top-level steady-state attempt scalars. Queued work canceled before transport start appears in neither set of transport-attempt counts. The human report uses the identical baseline-bundle timestamp, CPU baseline, snapshot pairs, phase membership, exact method-charge derivation, and totals.

```text
startup.attempts_completed =
    startup.attempts_succeeded
  + startup.attempts_failed
  + startup.attempts_timed_out
  + startup.attempts_canceled

startup.attempts_started =
    startup.attempts_completed
  + startup.attempts_incomplete
```

`host_profile.profile_id` is exactly `r0-005-windows11-x64/v1`. The bounded host-profile values and types are:

| Property | Required representation |
| --- | --- |
| `os_product` | `windows_11` |
| `os_version` | bounded invariant Windows version string |
| `os_build` | JSON integer, at least 26100 |
| `os_revision` | nonnegative JSON integer |
| `os_architecture`, `process_architecture` | `x64` |
| `framework_description` | bounded invariant .NET framework-description string |
| `runtime_version` | bounded invariant runtime-version string whose major version is 10 |
| `runtime_identifier` | bounded invariant runtime identifier for Windows x64 |
| `gc_mode` | `workstation` or `server` |
| `logical_processor_count` | JSON integer from 4 through 32 |
| `total_physical_memory_bytes` | JSON integer, at least 8,000,000,000 |
| `processor_vendor` | bounded invariant ASCII CPU-vendor class, at most 32 characters |
| `processor_family`, `processor_model`, `processor_stepping` | nonnegative JSON integers |
| `virtualization` | `physical`, `virtual`, or `unknown` |
| `power_source` | `ac` |
| `power_scheme_guid` | lowercase `381b4222-f694-41f0-9685-ff5bb260df2e` |
| `battery_saver_enabled` | JSON Boolean; must be `false` |
| `process_priority` | `normal` |
| `debugger_attached`, `profiler_attached` | JSON Boolean; both must be `false` |
| `idle_preflight_average_cpu_percent` | finite JSON number; must be less than 10.0 |
| `idle_preflight_available_memory_bytes` | JSON integer; must be at least 2,000,000,000 |

All host-profile strings are derived from the listed bounded OS/runtime/hardware sources, not from arbitrary environment variables. Missing, malformed, or out-of-profile values fail as defined in sections 8.2 and 8.9.

The following synthetic fragment is normative for the adjacency and property order of `configuration`, `host_profile`, `timing`, and `startup`; its concrete measurement values are illustrative:

```json
"configuration": {
  "target_count": 25,
  "https_provider_count": 2,
  "logical_processor_count": 8,
  "sampling_interval_seconds": 5,
  "requested_steady_state_minutes": 30
},
"host_profile": {
  "profile_id": "r0-005-windows11-x64/v1",
  "os_product": "windows_11",
  "os_version": "10.0.26100.1",
  "os_build": 26100,
  "os_revision": 1,
  "os_architecture": "x64",
  "process_architecture": "x64",
  "framework_description": ".NET 10.0.0",
  "runtime_version": "10.0.0",
  "runtime_identifier": "win-x64",
  "gc_mode": "workstation",
  "logical_processor_count": 8,
  "total_physical_memory_bytes": 8000000000,
  "processor_vendor": "GenuineIntel",
  "processor_family": 6,
  "processor_model": 1,
  "processor_stepping": 1,
  "virtualization": "unknown",
  "power_source": "ac",
  "power_scheme_guid": "381b4222-f694-41f0-9685-ff5bb260df2e",
  "battery_saver_enabled": false,
  "process_priority": "normal",
  "debugger_attached": false,
  "profiler_attached": false,
  "idle_preflight_average_cpu_percent": 0.0,
  "idle_preflight_available_memory_bytes": 2000000000
},
"timing": {
  "process_start_utc": "2026-01-01T00:00:00.0000000Z",
  "readiness_utc": "2026-01-01T00:00:01.0000000Z",
  "startup_elapsed_ms": 1000,
  "steady_state_start_barrier_elapsed_ms": 9000,
  "steady_state_start_utc": "2026-01-01T00:00:10.0000000Z",
  "steady_state_end_utc": "2026-01-01T00:30:10.0000000Z",
  "steady_state_end_drain_elapsed_ms": 0,
  "valid_sample_count": 362,
  "missing_sample_count": 0
},
"startup": {
  "cpu_observation_duration_ms": 10000,
  "total_cpu_ms": 0,
  "maximum_normalized_cpu_percent": 0.0,
  "maximum_working_set_bytes": 0,
  "attributed_disk_bytes": 0,
  "attributed_network_bytes": 0,
  "attempts_started": 0,
  "attempts_completed": 0,
  "attempts_succeeded": 0,
  "attempts_failed": 0,
  "attempts_timed_out": 0,
  "attempts_canceled": 0,
  "attempts_incomplete": 0
}
```

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

The top-level `attempts` object is scoped only to invocations whose transport-start instant is within the steady-state half-open interval. Both `attempts.by_probe_kind` and `attempts.by_outcome` use a common entry shape. Every entry has exactly these properties in this order:

1. `code`
2. `count`

`code` is one of the closed values defined below. `count` is a nonnegative JSON integer; floating-point and negative counts are forbidden. Unknown or extension codes are forbidden in `r0-005-resource-report/v1`. Every defined code appears exactly once, including mandatory zero-count entries. Additional properties are forbidden. The arrays use the exact fixed orders below, which are also ordinal code order; implementations must not depend on locale, dictionary iteration, discovery order, or runtime sorting.

`attempts.by_probe_kind` counts steady-state transport invocations that actually started. It contains exactly five entries in this order:

| `code` | Definition |
| --- | --- |
| `dns` | One admitted system-resolver transport invocation. |
| `gateway_icmp` | One admitted ICMP transport invocation. |
| `https` | One admitted HTTPS provider transport invocation. Each provider request counts separately. |
| `tcp` | One admitted TCP-connect transport invocation. |
| `unknown` | A started invocation whose method cannot be mapped to one of the four supported methods. |

`unknown` exists only so a failing report remains completely accountable and deterministic. Any `unknown` count greater than zero adds violation `unknown_probe_method`; the zero-valued entry remains present. Local route/interface discovery is not a transport attempt and is not counted. Work rejected or canceled before transport invocation is not counted by probe kind. The sum of all five `by_probe_kind.count` values equals scalar `attempts.started`.

Every HTTPS provider child counts as one `https` invocation even when children start concurrently. Concurrent children retain distinct internal attempt records through byte and outcome accounting; neither start-time equality nor a shared aggregate-round identity permits records, tokens, bytes, outcomes, or phase membership to be merged.

For each recognized `by_probe_kind` invocation, the report model derives its exact charged bytes from the one section 4.4 table: `gateway_icmp` 128, `dns` 1,024, `tcp` 1,024, and `https` 8,192. The report never accepts an adapter-provided charge. An `unknown` invocation uses only the fixed zero sentinel and measured bytes defined in section 8.7, so totals serialize deterministically while the run remains failed. These derivations do not add per-attempt customer, provider, endpoint, or payload fields to the JSON contract.

`attempts.by_outcome` contains exactly five entries in this order:

| `code` | Definition |
| --- | --- |
| `canceled` | A started invocation observed cancellation before success, ordinary failure, or timeout classification. |
| `failed` | A started invocation reached a bounded non-timeout, non-cancellation failure classification. |
| `incomplete` | A started invocation did not reach any of the four terminal classifications before attempt accounting closed. |
| `succeeded` | A started transport invocation reached its method-specific success classification. |
| `timed_out` | A started invocation reached its defined transport timeout. |

These categories are mutually exclusive, and every started invocation is assigned to exactly one outcome entry.

Scheduler-level and attempt-level incompleteness are distinct:

- A logical scheduler round may become incomplete before any transport invocation begins. That round does not increment `attempts.started`, any `attempts.by_probe_kind` entry, or any `attempts.by_outcome` entry. It remains only a scheduler/state-machine outcome and, under the existing operational rule, does not advance failure or recovery counters.
- Attempt-level `incomplete` applies only when a transport invocation actually started and attempt accounting closes before that invocation receives exactly one of `succeeded`, `failed`, `timed_out`, or `canceled`.
- Attempt-level `incomplete` is always an acceptance failure and adds violation `attempt_accounting_incomplete`.

The scalar attempt properties have these exact definitions:

| Property | Definition |
| --- | --- |
| `started` | Total steady-state transport invocations whose transport-start instant is in the half-open membership interval. |
| `completed` | `succeeded + failed + timed_out + canceled`. |
| `succeeded` | Identical to the `by_outcome` entry `succeeded`. |
| `failed` | Identical to `by_outcome.failed`; excludes timeouts, cancellations, and incomplete attempts. |
| `timed_out` | Identical to `by_outcome.timed_out`. |
| `canceled` | Identical to `by_outcome.canceled`. |
| `incomplete` | Identical to `by_outcome.incomplete`. |

The report must satisfy these exact identities:

```text
started =
  sum(attempts.by_probe_kind[*].count)

started =
  succeeded
  + failed
  + timed_out
  + canceled
  + incomplete

completed =
  succeeded
  + failed
  + timed_out
  + canceled

started =
  completed + incomplete
```

The following object fragment is the normative zero-attempt representation:

```json
"attempts": {
  "by_probe_kind": [
    { "code": "dns", "count": 0 },
    { "code": "gateway_icmp", "count": 0 },
    { "code": "https", "count": 0 },
    { "code": "tcp", "count": 0 },
    { "code": "unknown", "count": 0 }
  ],
  "by_outcome": [
    { "code": "canceled", "count": 0 },
    { "code": "failed", "count": 0 },
    { "code": "incomplete", "count": 0 },
    { "code": "succeeded", "count": 0 },
    { "code": "timed_out", "count": 0 }
  ],
  "started": 0,
  "completed": 0,
  "succeeded": 0,
  "failed": 0,
  "timed_out": 0,
  "canceled": 0,
  "incomplete": 0
}
```

This example is normative for property names, property order, array order, closed vocabularies, mandatory zero entries, integer count encoding, and scalar representation. It is an object fragment from the complete `r0-005-resource-report/v1` report, not a standalone full report.

`violations` is an ordinal-sorted array containing only these stable bounded codes when applicable:

- `acceptance_cleanup_failed`
- `attempt_accounting_incomplete`
- `counter_negative`
- `counter_overflow`
- `counter_reset`
- `counter_unavailable`
- `disk_cap_exceeded`
- `handle_growth_exceeded`
- `host_profile_preflight_failed`
- `host_profile_unsupported`
- `measurement_boundary_timeout`
- `measurement_boundary_violation`
- `network_cap_exceeded`
- `pid_changed`
- `private_bytes_growth_exceeded`
- `process_exited`
- `readiness_timeout`
- `report_serialization_invalid`
- `runtime_file_disappeared`
- `runtime_file_scope_violation`
- `runtime_file_snapshot_incomplete`
- `runtime_file_snapshot_invalid`
- `sample_missing`
- `steady_state_cpu_cap_exceeded`
- `synthetic_listener_cleanup_failed`
- `thread_growth_exceeded`
- `unknown_probe_method`
- `working_set_cap_exceeded`
- `working_set_growth_exceeded`

Violation `attempt_accounting_incomplete` is mandatory when any of the following occurs:

- any required probe-kind code is missing;
- any required outcome code is missing;
- any code appears more than once;
- an unknown or extension code is emitted;
- a count is negative;
- a count is nonintegral;
- an entry contains additional properties;
- required property order differs;
- required array order differs;
- a mandatory zero-count entry is omitted;
- any scalar/array identity fails;
- one invocation is assigned to more than one probe kind;
- one invocation is assigned to more than one outcome;
- a started invocation has no outcome;
- an outcome exists without a corresponding started invocation;
- a recognized invocation lacks its exact table-derived charged value;
- a recognized charged value is malformed, negative, nonintegral, adapter-supplied, overridden, or conflicts with the closed method table;
- the configured-budget and acceptance-accounting charge tables differ;
- `incomplete` is greater than zero.

Violation `unknown_probe_method` is mandatory whenever `attempts.by_probe_kind["unknown"] > 0`. Both `attempt_accounting_incomplete` and `unknown_probe_method` appear when both conditions apply. Every applicable violation appears exactly once, and the `violations` array remains in ordinal code order.

Schema, property-order, array-order, and type defects also make the report `result` equal `fail` and cause a nonzero sampler exit. This is required even when the report can still serialize a bounded violation list; every other independently applicable violation code also appears exactly once.

The report contains no machine name, username, domain membership, serial number, BIOS serial, device UUID, Windows product key, MAC address, IP address, disk serial, cloud instance ID, arbitrary environment variable, absolute path, endpoint, target, interface, DNS answer, payload, header, body, exception text, credential, customer identifier, or DNS-correlation snapshot field. The human report must contain the same required host-profile, timing, startup-attempt, steady-state-attempt, cap, and violation values and must not apply different calculations, phase membership, property meaning, or privacy rules.

### 8.9 Failure rules

The sampler exits nonzero when:

- readiness exceeds 60 seconds;
- any static acceptance-host requirement is unsupported, required host metadata is missing, or a prohibited shared/CPU-quota-limited host is used;
- the 60-second host preflight is missing a required sample, has average non-agent system CPU greater than or equal to 10.0%, or ends with less than 2,000,000,000 bytes available physical memory;
- the process exits, the PID changes, or a replacement/restart occurs;
- the complete start barrier from admission pause through baseline-bundle commit, or the end drain, exceeds 10 seconds;
- an invocation starts at or after `steady_state_end`;
- a sample is missing;
- a required non-disk counter or network-accounting value is unavailable, negative, reset, or overflows;
- a recursive runtime-file snapshot is incomplete, unstable within the bounded start barrier, unreadable, contains an invalid length, overflows during subtraction or summation, escapes the monitored directory, or loses a baseline file;
- working set reaches or exceeds 150,000,000 bytes;
- steady-state average CPU reaches or exceeds 2.0%;
- disk reaches or exceeds 100 decimal MB/day;
- network reaches or exceeds 100,000 bytes/minute;
- any growth rule fails;
- attempt accounting, exact recognized-method charge derivation, or configured-budget/acceptance table consistency is incomplete under any mandatory `attempt_accounting_incomplete` condition in section 8.8;
- an unknown probe method is encountered;
- report serialization violates schema, property or array ordering, type, numeric, or privacy requirements;
- synthetic-listener or acceptance cleanup fails.

## 9. Required verification for implementation

### 9.1 Unit tests

Use fake transports, an injected clock, and deterministic jitter to cover:

- table-driven activation from `unknown` to `baselining`;
- no round dispatch or reduction in `unknown`, with an observed result treated as an invariant failure;
- `healthy` plus success remaining `healthy`;
- `suspect` plus a below-threshold counted failure remaining `suspect`;
- Gateway ICMP failures 1, 2, and 3 producing `suspect`, `suspect`, and `offline`;
- repeated counted failure while `offline` remaining `offline`;
- `offline` plus success becoming `recovering`;
- `recovering` plus success becoming `healthy`;
- `recovering` plus a counted failure returning to `offline`;
- failure-streak saturation at every method threshold;
- success resetting every applicable failure streak;
- Gateway missing-route and ICMP streaks resetting one another rather than combining;
- non-counting results preserving state and counters;
- `icmp_unavailable` precedence from every Gateway active state;
- self-transitions not adding flapping transitions;
- every reachable active-state/result pair appearing exactly once in the effective transition model;
- a table-driven completeness test that fails when a reachable pair lacks an explicit rule;
- every remaining state transition and each method threshold;
- a documentation or generated-constant consistency check that fails if any cadence or threshold in the source-reconciliation table differs from the binding interval and threshold sections;
- ordinary scheduler-level incomplete/canceled/rate-limited/method-unavailable outcomes not advancing operational state counters, including pre-start incomplete rounds remaining absent from attempt arrays;
- no overlap of Gateway/DNS/TCP attempts or HTTPS aggregate rounds, fixed-delay scheduling, jitter bounds, no catch-up, timeouts, deadline, global concurrency, and per-provider concurrency;
- successful activation creating exactly eight available tokens and an activation-time refill anchor, with the ninth otherwise eligible same-timestamp attempt unable to start;
- exact lazy monotonic refill with zero credit before one full second when starting empty, one token available at exactly one second, exact fractional-credit retention, same-timestamp evaluations receiving no refill, and long elapsed duration capping at eight tokens;
- one started attempt consuming exactly one token with no refund for any outcome, pre-start cancellation consuming none, slot waiting reserving no credit, token waiting reserving no slot, and monotonic reversal failing as an implementation invariant;
- degradation and shutdown discarding token-bucket state, healthy restart/activation creating a new full bucket and anchor, and no prior credit surviving;
- four concurrent HTTPS children using four initial tokens while respecting global concurrency four, stable same-timestamp provider order, deterministic starvation-free sustainable mixed demand under an injected clock, and coarse operating-system timer cadence having no effect;
- worst-case attempt-demand and exact charged-network formulas, DNS enabled/disabled behavior, provider-count effects, and double-precision boundaries immediately below, exactly at, and immediately above 60 attempts/minute and 100,000 charged bytes/minute as applicable;
- exact fixed charge for each supported method, a shared configured-budget/acceptance table with no drift, adapter charge overrides rejected, measured bytes below the exact charge using the table value, measured bytes above it using measured bytes, and integer/culture/platform-stable arithmetic;
- two adapters with identical measured sent/received counts producing byte-identical totals and reports, each concurrent HTTPS provider receiving its own exact 8,192-byte charge, and deterministic totals immediately below, exactly at, and above 100,000 bytes/minute;
- unknown methods always failing with the deterministic zero-sentinel/measured-byte representation, and missing, malformed, negative, nonintegral, supplied, overridden, or conflicting recognized-method charges failing deterministically;
- exact budget boundaries with DNS enabled and disabled, including a configuration that is valid only when DNS is disabled;
- rejection of 25 targets at 15 seconds, acceptance of a 25-target configuration with sufficiently long intervals, and the valid two-default-provider 25-target/60-second example;
- all provider children in valid two-, three-, and four-provider HTTPS rounds starting without serial five-second waits when the required slots and tokens are available;
- three- and four-provider rounds in which every child times out at five seconds completing as one counted aggregate failure within the ten-second round deadline, and two consecutive such rounds advancing HTTPS state under the existing threshold;
- partial HTTPS success remaining aggregate success with `partial_endpoint_failure` when one provider succeeds and the other provider children fail;
- the process-wide concurrency cap never exceeding four, no provider having two concurrent attempts, and no two HTTPS aggregate rounds overlapping;
- stable provider-child order, independent child slot/token acquisition, one token and one attempt record per child, and separate complete provider counts, bytes, outcomes, and phase membership under concurrent starts;
- stable rotating fair-queue order, bounded HTTPS fan-out, pending-child/non-HTTPS alternation, no systematic Gateway/DNS/TCP starvation under sustained valid mixed demand, cancellation before token consumption, and a started timeout consuming one token;
- genuine capacity or token contention that prevents required provider completion still producing the existing non-counting incomplete aggregate result;
- shutdown and degradation cancellation;
- flapping detection, rolling-window eviction, clearing, restart reset, and degradation reset;
- configuration bounds, atomic rejection, duplicate IDs, 0/25/26 targets, interval/port limits, credential-like values, and URI/range rejection;
- missing or invalid `operator_id`, duplicate operator IDs, two domains with the same declared operator, and two declared operators on the same host/domain;
- valid built-in Google/Cloudflare declarations and valid custom providers with distinct IDs, hosts, registrable domains, and declared operators;
- registrable-domain cases for `example.com`, a multi-label suffix such as `example.co.uk`, an exact rule, wildcard rule, exception rule, PRIVATE-section suffix, Unicode and equivalent punycode input, one optional terminal dot, a host equal to a public suffix, IP-literal rejection, and two subdomains sharing one registrable domain;
- byte-for-byte pinned suffix-data verification and deterministic registrable-domain behavior across restarts;
- provider-set count, provider/domain/declared-operator separation, HTTPS-only URLs, strict statuses, redirect rejection, and bounded optional-body validation;
- HTTPS endpoint outcomes for 204, 200 status-only, wrong status, redirect, TLS failure, timeout, cancellation, oversized body, body not retained, partial provider failure, all-provider failure, and two-provider independence;
- DNS snapshot correlation for DNS completion before HTTPS start, DNS completion after HTTPS start, concurrent completions in opposite task orders producing the same HTTPS result, the exact 60-second freshness boundary, stale snapshot, no snapshot, fresh success, fresh counted failure, fresh non-counting result, DNS disabled, and timestamp reversal;
- DNS-blocked HTTPS aggregation, `dns_correlation_unavailable`, DNS independence, and absence of snapshot fields from logs and evidence;
- gateway route changes, missing route, missing gateway, ICMP success/timeout/unreachable/unavailable;
- evidence-code allowlisting and redaction.

### 9.2 Integration tests

Use disposable local endpoints or fakes only. Cover:

- local TCP accept, refuse, timeout, cancellation, and socket disposal;
- local HTTPS status/body/redirect/streaming behavior with test certificates as appropriate;
- two-, three-, and four-provider local HTTPS fan-out with concurrent all-timeout completion, partial success, global/per-provider limits, non-overlapping aggregate rounds, contention incompleteness, and sustained mixed-work fairness;
- system-resolver adapter success/failure through a controlled abstraction;
- scheduler activation after readiness creating one full process-local bucket, exact monotonic lazy refill during local fake/endpoint work, and prompt stop plus bucket discard on readiness degradation;
- restart rebinds static configuration, reconstructs enabled probes as `unknown`, moves them to `baselining` only through scheduler activation after readiness, and creates a new full bucket/anchor without prior credit;
- no probe journal/outbox calls and no database/schema artifacts;
- no response body, DNS answer, endpoint, target, interface, or exception leakage.

Live Google, Cloudflare, Microsoft, DNS, SMS, camera, or customer-network access is not required or permitted as an automated test dependency.

### 9.3 Security and privacy tests

Verify:

- URI user information, credentials, credential-assignment syntax, query strings, fragments, and customer/device identifiers are rejected;
- HTTPS redirects and invalid TLS are rejected without fallback or weakened validation;
- endpoint URLs/hosts, DNS queries/answers, DNS-correlation snapshot fields, target IDs/labels/hosts/ports, response bodies, headers, interface/route data, exception text, machine paths, and other customer-controlled values do not appear in production logs or evidence;
- response-body readers stop at the 256-byte bound plus one overflow-detection byte and retain no bytes;
- no production inbound listener, firewall mutation, packet capture, subnet/range expansion, broad port scan, or arbitrary gateway TCP probe exists;
- no probe journal/outbox call, schema artifact, identity fabrication, or raw LAN inventory is introduced.

### 9.4 Native Windows acceptance

Native acceptance must verify:

- the host matches `r0-005-windows11-x64/v1`, records every required bounded profile field, and passes the 60-second idle/resource preflight;
- the host is physical or a dedicated VM rather than a shared or CPU-quota-limited runner, with virtualization recorded;
- active default-route/interface discovery on Windows;
- bounded route evidence without interface/address disclosure;
- ICMP success when permitted and `icmp_unavailable` behavior when it is not;
- cancellation and service stop complete within five seconds;
- the 25-target resource sampler passes;
- the start barrier commits one stable steady-state baseline bundle while admissions remain paused, with `steady_state_start` equal to that commit and no later pre-existing sampling-grid wait;
- steady-state sampling is anchored at exact `steady_state_start + 5n seconds`, and startup/steady-state CPU and disk attribution share the committed boundary without an omitted or double-counted interval;
- monitoring-ready is achieved within 60 seconds after network availability;
- no inbound listener or firewall rule is added;
- every synthetic listener and service acceptance artifact is cleaned up.

Changing Windows services, firewall policy, ICMP policy, certificates, or machine configuration requires separate explicit authorization.

### 9.5 Resource sampler contract tests

Deterministic automated tests must cover:

- every CPU, growth, disk, network, rate-demand, and unit-conversion formula at values below, exactly at, and above each pass boundary, including values immediately below, exactly at, and immediately above 100 decimal MB/day;
- separately anchored pre-steady-state sampling and exact steady-state fixed-rate boundaries at `steady_state_start + 5n seconds`, no catch-up burst, the ten-second collection deadline, and fewer than 11 valid samples in a complete minute;
- initialization/steady-state phase boundaries, `steady_state_start` equal to baseline-bundle commit rather than a later grid point, all 30 complete minute assignments anchored to that commit, and first/final five-minute median windows including the even-count median rule;
- admission before start with transport start after `steady_state_start`, admission before end with transport start exactly at `steady_state_end`, and transport start one monotonic tick before `steady_state_end`;
- a drain ending between pre-existing five-second grid points producing no baseline gap, complete start-barrier success/timeout including bounded stable-snapshot retry, end-drain success/timeout, exact half-open interval behavior, and deterministic `measurement_boundary_timeout` and `measurement_boundary_violation` codes;
- start/end admission pauses consuming no pre-start token, issuing no refund, preserving the active bucket/anchor, and allowing elapsed barrier time to refill lazily only to the eight-token cap before post-resume dispatch;
- no byte splitting, full bytes from a steady-state attempt that completes during end drain, startup attempts excluded from top-level attempts, and queued pre-start/end work excluded from transport-attempt arrays;
- process CPU and network attribution through end drain, disk attribution through the process-start/committed-steady-state-start/final recursive snapshots, and growth windows excluding drain samples;
- CPU consumed before baseline-bundle commit counted startup-only, CPU consumed after commit counted steady-state-only, file growth before the committed snapshot counted startup-only, file growth after it counted steady-state-only, and no CPU or file-growth interval omitted, duplicated, or assigned to both phases;
- 80,000 process/network write bytes per minute with zero runtime-file growth producing zero `attributed_disk_bytes` and zero `disk_mb_per_day`, proving that aggregate process I/O counters are not disk inputs;
- new-file and existing-file growth summing correctly, unchanged and same-size rewritten files contributing zero, file shrinkage contributing zero, and a disappearing baseline file failing;
- startup and steady-state disk attribution using their separate pairs around the one committed snapshot, with timing, CPU, disk, attempts, network, and phase-boundary values/calculations identical in JSON and human reports;
- an unstable/incomplete steady-state-start snapshot, unreadable or incomplete snapshots, invalid lengths, arithmetic overflow, directory escape/reparse escape, bounded retry/deadline exhaustion, and each corresponding bounded runtime-file violation;
- PID change, process exit, restart, negative/zero/reset/unavailable/overflowed non-disk counters, unknown methods, and missing started-attempt accounting;
- decimal-byte, decimal-MB/day, bytes/minute, millisecond, percentage, and logical-processor normalization;
- every stable resource violation code and ordinal violation ordering;
- every static host-profile boundary, OS build below minimum, x86 or ARM process rejection, insufficient processors or physical memory, battery/power-scheme failure, debugger/profiler rejection, the idle CPU boundary, and the available-memory boundary;
- physical, virtual, and unknown virtualization values;
- exact `r0-005-resource-report/v1` top-level, `host_profile`, `timing`, `startup`, and remaining nested property names/order, invariant numeric and UTC serialization, no NaN/infinity, and identical values/calculations in the JSON and human report;
- exact `by_probe_kind` vocabulary and exact `dns`, `gateway_icmp`, `https`, `tcp`, `unknown` order;
- exact `by_outcome` vocabulary and exact `canceled`, `failed`, `incomplete`, `succeeded`, `timed_out` order;
- exact `{code,count}` property order, mandatory zero-count entries, and the normative zero-attempt representation;
- rejection of an omitted zero entry, duplicate code, unknown or extension code, additional entry property, negative count, and nonintegral count;
- equality between each outcome scalar and its corresponding array entry, plus every started/completed/outcome accounting identity;
- mutually exclusive probe-kind assignment and mutually exclusive outcome assignment for every invocation;
- every started invocation receiving exactly one outcome and rejection of an outcome without a corresponding started invocation;
- separate accounting for every HTTPS provider request;
- exact fixed charge derivation for every supported method, adapter charge input/override rejection, measured-below-table and measured-above-table attribution, and one shared configured-budget/acceptance table without drift;
- byte-identical totals and reports from different adapters with identical measured bytes, one exact 8,192-byte charge per concurrent HTTPS provider, and deterministic network totals immediately below, exactly at, and above the 100,000-byte/minute boundary;
- route/interface discovery remaining outside transport-invocation counts;
- cancellation before transport invocation not counting as started and cancellation after start counting only as `canceled`;
- timeout counting only as `timed_out` and ordinary failure counting only as `failed`;
- attempt-level incomplete adding `attempt_accounting_incomplete` and failing acceptance;
- scheduler-level pre-start incomplete remaining absent from both attempt arrays and not incrementing `attempts.started`;
- an unknown method counting under `unknown`, using the deterministic zero-sentinel/measured-byte failing representation, adding `unknown_probe_method`, and never passing acceptance;
- missing, malformed, negative, nonintegral, adapter-supplied, overridden, and conflicting charged values for recognized methods adding `attempt_accounting_incomplete` while totals remain table-derived;
- both `attempt_accounting_incomplete` and `unknown_probe_method` appearing exactly once and in ordinal order when both apply;
- byte-for-byte stable JSON for identical report models;
- identical attempt counts, exact method-charge-derived network totals, timing boundaries, startup values, steady-state values, and disk values in the human and JSON reports;
- privacy exclusion of machine names, usernames, domain membership, serials, device UUIDs, product keys, MAC/IP addresses, disk serials, cloud instance IDs, arbitrary environment variables, absolute paths, endpoints, targets, interfaces, DNS answers, DNS-correlation snapshot fields, payloads, headers, bodies, exception text, credentials, and customer identifiers, including identical exclusions in JSON and human disk evidence;
- deterministic `host_profile` property order and rejection of missing or additional host-profile fields;
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

Pre-registration configuration and all operational evidence are intentionally volatile. Restart or readiness degradation loses counters, transitions, and flapping history. Restart reconstructs enabled targets as `unknown`; scheduler activation after readiness moves them to `baselining`. In addition, the inherited R0-004 limitation remains: simultaneous journal and lifecycle-marker failure can leave the durable marker stale until restart even though in-process readiness is false. R0-005 must stop on that in-process false state.

## 12. Delivery boundary

R0-005 is complete only when the implementation, deterministic tests, native evidence, and resource report satisfy this document. Completion does not authorize:

- registration or identity creation;
- persistence or cloud delivery;
- target-management surfaces;
- alerts, incidents, causal claims, or “protected” status;
- R0-006 or R0-013 work.

Any implementation need that conflicts with this boundary is a new requirements decision and must stop for review.
