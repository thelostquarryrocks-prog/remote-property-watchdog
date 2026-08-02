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

These are deliberate R0-005 decisions affecting detection latency and false-positive tradeoffs. The feasibility document is a prototype planning source, not the executable R0-005 implementation contract. R0-005 must also satisfy constraints and semantics that the earlier heuristic does not define: fixed-delay scheduling from completion, ±10% jitter, hard admission limits of at most 60 transport starts and less than 100,000 fixed charged bytes in every rolling monotonic 60-second window, up to 25 explicit TCP targets, independent DNS state, multi-provider HTTPS aggregation, partial-success handling, DNS-blocked handling, incomplete-round handling, and the prohibition on user incident or notification emission.

The binding rationales are:

- The 30-second Gateway, DNS, and HTTPS cadence preserves scheduled-demand capacity for 25 configured targets under the startup formulas, while the separate hard rolling admission budgets prevent activation, synchronization, and refill timing from exceeding the global attempt and charged-network limits.
- A successfully stabilized route snapshot with no eligible default-route candidate, including after mandatory `DisableDefaultRoutes == false` policy filtering, is higher-confidence local evidence than an ICMP timeout or unreachable response. That `missing_route` evidence therefore uses threshold 2, while ICMP retains threshold 3. Failed or unstable route selection is non-counting `route_selection_unavailable`, not missing-route evidence.
- DNS uses threshold 2 because it remains an independently exposed local state and cannot alone declare the HTTPS aggregate offline.
- HTTPS uses threshold 2 because a counted aggregate failure requires every configured provider attempt to start and complete with no success. Partial provider success, `blocked_by_dns`, correlation-unavailable rounds, cancellation, and capacity/deadline incompleteness do not count toward that threshold.
- TCP uses threshold 2 because every target is one explicitly configured host/port service and R0-005 creates no user incident or alert. Later notification policy may add user-facing debounce without changing the local technical evidence.

Nominal base-interval multiples are not detection-time service-level agreements. Actual elapsed detection time also includes completion-based scheduling, jitter, admission delay, transport timeout, and logical-round deadlines.

The remaining deliberate reconciliations are:

- Technical Feasibility supplies the approved resource caps, 25-target boundary, three-check “protected” concept, and later seven-day soak. Its disk cap is explicitly a persistent “Disk growth” target tested by “Database size trend”; R0-005 therefore measures attributable runtime-file-size growth rather than aggregate process I/O. R0-005 adopts the caps and target count but keeps “protected,” alert truth, and the long soak outside its local operational-state merge gate.
- Technical Feasibility section 7.2 proposes two stable DNS names to reduce authoritative-domain-specific false negatives. R0-005 explicitly supersedes that prototype heuristic for R0-005 only and defines one independently scheduled resolver-path probe with one query name, one transport attempt, and one unambiguous reducer result per logical round. Pairwise-distinct multi-provider HTTPS supplies separate internet-path diversity, and the existing correlation rules prohibit one DNS signal from being presented as a broad property outage. A two-name DNS design would require reviewed aggregation, per-child token/charge, concurrency/fairness, coverage-identity, snapshot-publication, and budget semantics that this task does not silently invent. The accepted tradeoff is that failure of the configured authoritative domain can move only the DNS probe through its own `suspect` or `offline` state. Any future multi-name design requires a separately reviewed requirements change.
- Data Model/API supplies the broader persisted state vocabulary. R0-005 maps to it without changing contracts and defers durability until authenticated identity exists.
- MVP Backlog assigns target management to the later local-configuration task. R0-005 pulls forward only the narrow execution/evidence primitives listed in section 2.5.
- [Security and Privacy Guardrails](security-guardrails.md) generally require malformed durable or updateable configuration to preserve a safely stored accepted last-known-good value. R0-005 has no authenticated or durable configuration owner and no stored last-known-good probe configuration, so it uses the guardrail's documented, narrow pre-registration exception: atomic, visible fail-closed startup rejection with no scheduler and no stale substitution. This is an R0-005-only source reconciliation, not general product policy.

For Windows route-policy field semantics, the official Microsoft IP Helper documentation is the source of truth. [`MIB_IPINTERFACE_ROW.DisableDefaultRoutes`](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_ipinterface_row) is a Boolean on the family-specific IP-interface row: `true` means use of the default route on that interface is disabled. Microsoft documents this as a security control VPN clients can use to restrict split tunneling and documents that [`GetIpInterfaceEntry`](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getipinterfaceentry) obtains its current value. R0-005 therefore treats `DisableDefaultRoutes == false` as a mandatory operating-system route-policy constraint, not an optional heuristic, metric penalty, preference, or fallback rule.

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

Configuration is bound and validated exactly once during service startup. The atomically accepted configuration is immutable for the process lifetime; a restart is required to apply changes. R0-005 provides no CRUD API, LAN or cloud management endpoint, UI, hot reload, SQLite configuration, authenticated or durable configuration owner, or stored last-known-good fallback. Process memory is not a last-known-good store and is never reused across restart.

Validation is atomic. If any enabled probe or provider entry is invalid, the probe subsystem schedules no work, activates no valid subset, reuses no prior in-memory configuration, and silently substitutes no stale provider or target set. It exposes only bounded local validation codes without echoing customer-controlled values. Monitoring-ready remains false or visibly degraded for the probe subsystem; correcting configuration plus a healthy process restart is required before normal activation.

This fail-closed behavior is the narrow R0-005 exception documented in [Security and Privacy Guardrails](security-guardrails.md). It records an explicit availability/security tradeoff: a malformed environment or command-line override at restart can stop probing, but reusing stale configuration could silently preserve outdated or unsafe targets or providers. R0-005 chooses visible fail-closed startup because it owns no authenticated or durable configuration store. It neither creates persistence nor establishes a rule for durable/updateable configuration, which must continue to preserve a safely stored accepted last-known-good value.

At the same atomic acceptance boundary and before acceptance workload measurement or probe scheduling begins, the implementation freezes the acceptance workload manifest defined in section 8.1. The manifest and the accepted configuration cannot be replaced, reordered, or mutated during the run. Any detected configuration or manifest mutation is an acceptance failure rather than a hot-reload operation.

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
- no scheme, port, user information, path, query, fragment, wildcard, credentials, or customer identifier;
- no list, array, fallback name, comma- or whitespace-separated multiple-name syntax, wildcard/search-domain expansion, or other expression that can produce more than one query name.

The system resolver is used. Exactly one DNS transport attempt is made for the one configured name in each DNS logical round. A round succeeds when at least one valid A or AAAA address is returned within the timeout. Returned addresses are never logged, persisted, or added to evidence. One-name configuration is an explicit R0-005 source override, not an accidental omission or an authorization for fallback/search expansion.

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
- normalized endpoint hosts are pairwise distinct under ordinal case-insensitive comparison after the normalization below;
- across the complete configured set, every provider ID, declared `operator_id`, normalized endpoint host, and normalized registrable domain is pairwise distinct;
- redirects are disabled and rejected;
- cookies, authentication, uploads, custom authorization, and customer identifiers are forbidden;
- each entry contains an exact integer expected status from 200 through 299, inclusive; every 1xx, 3xx (including 300–308), 4xx, and 5xx value is invalid configuration;
- an optional exact expected body may be configured as UTF-8 text of at most 256 bytes;
- an expected body is invalid when expected status is 204 or 205; either status remains valid without an expected body;
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

The registrable domain is the label immediately preceding the prevailing public suffix plus that suffix. A host that is itself a public suffix, or that has no registrable label, is invalid. For a configured provider count `N` from 2 through 4, all configured provider hosts must produce pairwise-distinct normalized registrable domains under ordinal case-insensitive comparison:

```text
distinct_registrable_domain_count = N
```

Subdomains of one registrable domain never count as independent providers. For example, the three-provider set `a.example.com`, `b.example.com`, and `c.example.net` is invalid because the first two normalized hosts share registrable domain `example.com`. The pairwise rule applies to the complete configured set and is independent of configuration order.

This pinned snapshot remains binding for R0-005. Any suffix-data update requires a reviewed requirements change, a new full upstream commit SHA, a new file SHA-256, license review, security review, and full configuration-boundary regression tests. Existing deployed configuration must not be silently reinterpreted by a live list update.

This documentation correction authorizes no new production package. If implementation requires a parser dependency instead of a repository-owned parser, stop for explicit package, license, security, and vulnerability approval. No dependency may fetch suffix data at runtime.

Runtime validation deterministically enforces pairwise-distinct provider IDs, declared `operator_id` values, normalized endpoint hosts, and normalized registrable domains across the complete configured set. Any duplicate or cardinality mismatch rejects the entire provider configuration before scheduler activation with bounded `configuration_invalid`; no valid subset may activate. Configuration errors remain bounded and must not echo an operator identifier, endpoint, host, URL, registrable domain, or other customer-controlled value.

This validation is intentionally structural. Pairwise-distinct registrable domains combined with truthful pairwise-distinct operator declarations are required, but neither property proves real-world corporate independence and network probing cannot prove ownership. The configuration author is responsible for declaring `operator_id` accurately. The built-in Google and Cloudflare declarations satisfy the pairwise rule and remain reviewed defaults; a custom override that falsely declares ownership is configuration-author error that the agent cannot discover through probing. Separately operated providers remain an architectural and operator responsibility.

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

Each Gateway logical round discovers and commits one deterministic default-route selection from current operating-system state. It never pins a route from startup. The following selection algorithm is binding R0-005 policy; its IPv4-before-IPv6 tie-break is not presented as a universal Windows routing rule.

The Windows adapter uses these official Microsoft IP Helper contracts:

- [`GetIpForwardTable2`](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getipforwardtable2) and [`MIB_IPFORWARD_ROW2`](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_ipforward_row2) for current-compartment IPv4 and IPv6 route rows;
- [`GetIpInterfaceEntry`](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getipinterfaceentry) and [`MIB_IPINTERFACE_ROW`](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_ipinterface_row) for the associated family-specific IP-interface row; and
- [`GetIfEntry2`](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getifentry2) and [`MIB_IF_ROW2`](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2) for the associated interface row.

For each route and address family, `GetIpInterfaceEntry` supplies one family-matching `MIB_IPINTERFACE_ROW` whose `Connected`, `Metric`, and `DisableDefaultRoutes` values are consumed together. `DisableDefaultRoutes == true` prohibits that interface's default route from entering the eligible candidate set. The adapter must not penalize the route's metric, place it after enabled routes, select it as a fallback, or reinterpret the policy as `icmp_unavailable`.

A default route has an all-zero `DestinationPrefix.Prefix` for its address family and `DestinationPrefix.PrefixLength` exactly zero. Multiple default-route rows may coexist. For each row, the effective metric is the checked sum documented by Microsoft:

```text
effective_metric = route_metric + family_specific_interface_metric
```

At the beginning of every Gateway logical round, before route reduction, the adapter performs this stable-snapshot protocol within the existing ten-second logical-round deadline:

1. Enumerate current-compartment IPv4 and IPv6 route rows.
2. For every possible default-route candidate, resolve its associated `MIB_IPINTERFACE_ROW` for the same address family and its `MIB_IF_ROW2`. The family-specific row used for `Connected`, `Metric`, and `DisableDefaultRoutes` must come from this same complete snapshot pass; the adapter must not combine route data from one state with an IP-interface policy field from another.
3. Filter and canonicalize the complete eligible candidate set as set A.
4. Immediately repeat the complete enumeration, resolution, filtering, and canonicalization as set B.
5. Normalize and ordinal-sort both sets using the exact tuple below.
6. Commit B only when the complete canonical sets are identical. Otherwise discard both and retry only within the same logical-round deadline.

Candidate membership incorporates `DisableDefaultRoutes`. When the value is `false` in both A and B, an otherwise conforming route may remain eligible. When it is `true` in both passes, the route is absent from both eligible sets. A change from `false` to `true` or from `true` to `false` changes candidate membership, makes A and B unequal, and discards the pair. Retry remains bounded by the existing Gateway logical-round deadline. Continuous policy-flag instability through that deadline reduces as non-counting `route_selection_unavailable`; no mixed-state snapshot may commit.

Failure of a required route/interface API, incomplete associated-row resolution, invalid or contradictory row data, invalid metric, checked metric overflow, or inability to obtain identical sets before the deadline reduces the round as non-counting `route_selection_unavailable`. An unstable, incomplete, or failed enumeration must never be reported as a missing route. Once B commits, later route changes cannot alter the active round; they are visible only to the next Gateway round. Route discovery consumes no token and no network-attempt slot.

A row is an eligible default-route candidate only when all of these conditions hold:

- destination prefix is all zero and prefix length is exactly zero;
- address family is IPv4 or IPv6;
- route `Loopback` is false;
- route valid lifetime is nonzero or infinite;
- the family-matching IP-interface row exists, `Connected` is true, and `DisableDefaultRoutes` is false;
- the interface row exists, `AdminStatus` is up, `OperStatus` is `IfOperStatusUp`, and `MediaConnectState` is connected;
- the interface is not software loopback, endpoint-only, or a filter interface;
- `NotAuthenticated`, `NotMediaConnected`, `Paused`, and `LowPower` are all false; and
- route and interface metrics are valid nonnegative values whose checked sum does not overflow.

An associated row that is conclusively absent in an otherwise complete successful pass makes that route ineligible; inability to complete associated-row resolution or any required lookup failure makes the snapshot unavailable. Ethernet, Wi-Fi, PPP, VPN, tunnel, and virtual interfaces use exactly the same `Connected == true`, `DisableDefaultRoutes == false`, metric, status, and flag conditions. Interface type alone neither prefers nor excludes a candidate, and no interface type may bypass the default-route policy field.

Each internal canonical candidate contains only:

- address-family rank;
- effective metric;
- route metric;
- interface metric;
- interface LUID numeric value;
- interface index;
- canonical next-hop bytes;
- route protocol numeric value; and
- route origin numeric value.

Canonical next-hop bytes are the fixed-length address bytes in network order for the candidate family. Candidates identical across the complete canonical field set are deduplicated; any difference in a canonical field remains a separate candidate. Eligible candidates are sorted ascending by exactly this complete tuple:

```text
(
  effective_metric,
  address_family_rank,
  route_metric,
  interface_metric,
  interface_luid,
  interface_index,
  canonical_next_hop_bytes,
  route_protocol,
  route_origin
)
```

Address-family rank is the explicit R0-005 tie-break policy: IPv4 is `0` and IPv6 is `1`. Lower effective metric therefore wins across both families; IPv4 wins only when effective metrics tie; the remaining fields deterministically resolve multi-adapter and duplicate-metric cases. The selected route is exactly the first candidate after sorting.

A successfully stabilized set with zero eligible candidates reduces as counted `missing_route`, including when every potential default route is excluded because its family-matching IP-interface row has `DisableDefaultRoutes == true`. No new public result code is added for this policy exclusion. Route selection and ICMP usability are separate. After selecting a route:

- a usable IPv4 next hop is unicast, nonzero, non-loopback, non-multicast, and non-broadcast;
- a usable IPv6 next hop is unicast, non-unspecified, non-loopback, and non-multicast; and
- an IPv6 link-local next hop uses the selected interface scope.

An unusable, unspecified, or on-link next hop does not turn the selected route into `missing_route`; it reduces through existing non-counting `icmp_unavailable` behavior and may use bounded `gateway_address_missing` evidence. ICMP is the only network reachability method in R0-005. The implementation must not try arbitrary ports, scan the gateway, or use a generic TCP fallback. A usable selected next hop produces ICMP success, counted ICMP timeout/unreachable, or non-counting `icmp_unavailable` when the platform, ICMP API, or permissions do not permit the method.

`icmp_unavailable` retains route evidence and cannot by itself move a target to `offline`. When `icmp_unavailable` occurs before any successful method result while the target is `baselining`, the state remains `baselining` (`Unknown` projection). From `healthy`, it lowers confidence to `suspect` (`Degraded` projection) without incrementing either gateway failure streak. From `baselining`, `suspect`, `recovering`, or `offline`, state and counters remain unchanged. An already `offline` state remains offline until an actual success begins recovery. `route_selection_unavailable` is also non-counting and leaves state and counters unchanged.

The round records only bounded method evidence for whether a stable route selection exists, whether a usable ICMP next hop exists, whether ICMP was attempted, and the bounded result. Honoring `DisableDefaultRoutes` preserves Windows interface route policy and avoids bypassing VPN split-tunneling controls. The policy value, interface identity or type, VPN status, gateway or interface IP address, next hop, LUID, interface index, names, descriptions, metrics, protocols, origins, other candidate fields, raw API values, and operating-system exception text never enter JSON reports, human reports, logs, health output, support evidence, persistence, or metrics. Evidence must not reveal whether any VPN or other interface had the flag set.

The stable zero-eligible-candidate `missing_route` threshold is two consecutive rounds. The ICMP timeout/unreachable threshold is three consecutive counted failures. A successful Gateway round resets both Gateway failure streaks. A `missing_route` failure resets the ICMP streak before incrementing the missing-route streak, and an ICMP timeout/unreachable resets the missing-route streak before incrementing the ICMP streak. The two classifications therefore form separate consecutive streaks and never combine toward either threshold.

### 3.2 DNS

DNS is a separate logical probe with its own state, counter, transition history, and evidence. It uses the system resolver and the configured query name.

R0-005 intentionally performs exactly one transport attempt against exactly one query name per DNS logical round. Technical Feasibility section 7.2's two-name prototype heuristic sought to reduce authoritative-domain-specific false negatives, but adopting it here would require new child aggregation, token/charge, concurrency/fairness, coverage-identity, snapshot-publication, and budget rules. Those semantics are not silently invented. Pairwise-distinct HTTPS providers separately supply internet-path diversity. The accepted one-name tradeoff is that failure of the configured authoritative domain can move this bounded DNS signal through `suspect` or `offline`; it must not by itself be labeled as broad property-outage evidence under the existing correlation rules. This decision leaves the existing DNS timeout, one-token/one-method-charge rule, attempt and charged-network formulas, threshold, one-snapshot publication, and HTTPS correlation model unchanged. A fallback or second-name query requires a separately reviewed future requirements change.

Success requires a valid A or AAAA result within two seconds. Timeout, resolver failure, or no valid address is a counted DNS failure. The offline threshold is two consecutive counted failures.

A DNS-only failure cannot declare the HTTPS aggregate or total internet state offline. Consumers must be able to see DNS state independently.

After every completed DNS logical round, the DNS reducer atomically publishes one immutable process-local DNS-correlation snapshot. It contains only:

- a strictly increasing process-local DNS round sequence;
- the monotonic DNS round-completion timestamp;
- the DNS round classification: `success`, `counted_failure`, or `non_counting`;
- the resulting DNS operational state.

These fields are internal coordination data. They must not enter logs, health output, resource reports, durable evidence, support output, or metrics. When the DNS probe is disabled, no DNS-correlation snapshot is published or expected.

### 3.3 HTTPS aggregate

One logical HTTPS round attempts every provider in the atomically accepted pairwise-distinct set, subject to global limits and the logical-round deadline. Provider count `N` is therefore also the distinct provider-ID, operator-ID, normalized-host, and normalized-registrable-domain count; aggregation never collapses or substitutes providers by shared domain, host, operator, or ID. The HTTPS aggregate is one logical target and has at most one active logical round. Each configured provider invocation is a child transport attempt within that round, not another logical target. Within the one active round, at most one attempt per configured provider may be active, provider attempts may overlap, and no provider may be attempted more than once. A second HTTPS aggregate round cannot be admitted or started until the first round has reduced to one terminal aggregate result.

At the HTTPS logical-round start timestamp, the reducer captures exactly one immutable copy of the most recent published DNS snapshot whose completion timestamp is less than or equal to the HTTPS round start. A DNS round that completes after the HTTPS round starts can never alter that HTTPS round. Snapshot age uses only the injected monotonic clock:

```text
dns_snapshot_age =
  https_round_start - dns_snapshot_completion
```

A captured snapshot is fresh when `dns_snapshot_age <= 60 seconds`; the exact 60-second boundary is fresh. A negative age, timestamp reversal, or non-increasing DNS round sequence is an implementation invariant failure, not an inferred operational classification.

An endpoint succeeds only when:

- DNS/connect/TLS completes within the five-second endpoint timeout;
- normal certificate-chain and hostname validation succeeds;
- no redirect occurs; every actual 3xx response is a terminal provider failure and can never satisfy an expected-status rule;
- the actual status exactly equals the validated configured 2xx status;
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

The scheduler is never constructed or activated when atomic startup configuration validation fails. No prior process-memory configuration or partial provider/target subset may survive that failure. Probe monitoring-ready remains false or visibly degraded with bounded local validation evidence until corrected configuration is supplied and the process is restarted. This is the scoped R0-005 exception to the general last-known-good rule in [Security and Privacy Guardrails](security-guardrails.md); it adds no persistence and does not weaken the rule for configuration that has a safely stored accepted last-known-good value.

The scheduler may activate only after R0-004 reports both journal readiness and formal service readiness. A configured logical target is constructed as `unknown`. At the exact injected-monotonic timestamp of successful scheduler activation, activation creates one stable process-local scheduler epoch, marks it active and enabled, moves every enabled target from `unknown` to `baselining`, clears every failure streak, recovery progress, and flapping history, makes every first round due immediately through the same global controls as later work, creates the epoch's one process-local token bucket with exactly eight token-seconds of credit and that activation timestamp as its refill anchor, and creates the epoch's empty rolling transport-start admission ledger defined in section 4.4. The ledger applies to the initial due wave immediately. No transport round may be dispatched or reduced while a target remains `unknown`; a round result observed for an `unknown` target is an implementation invariant failure, not an operational transition.

If journal health or formal service readiness becomes false:

- stop admitting new rounds;
- cancel queued and active attempts promptly;
- mark the scheduler epoch inactive and disabled, clear schedules, discard the complete token-bucket credit, refill anchor, and rolling transport-start admission ledger, dispose scheduler resources, and clear counters, recovery progress, and flapping history;
- keep the process alive under R0-004's existing degraded-service behavior;
- do not collect, log, buffer, persist, or emit observations while degraded;
- do not recreate the journal, restore readiness, or invent writes from R0-005.

Recovery of probe execution requires a healthy process restart. No scheduler epoch, token credit, refill anchor, or rolling transport-start admission entry survives degradation or restart. On restart, all enabled targets are reconstructed as `unknown` and enter `baselining` only through scheduler activation after readiness; that later valid activation creates a new epoch, full eight-token bucket, injected-monotonic activation anchor, and empty rolling ledger. The acceptance-run restriction in section 8.1 independently forbids using such a restart or later epoch to recover a failed run.

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
- The active scheduler owns exactly one process-local token bucket shared by every probe method. Its refill rate is exactly one token per monotonic second, arithmetically 60 token-seconds per minute, and its capacity is exactly eight tokens. That refill fact is neither a long-run substitute for nor enforcement of the separate rolling admission caps.
- ICMP sends, DNS queries, HTTPS endpoint requests, and TCP connects each consume exactly one token at transport start. A started attempt is never refunded, regardless of success, failure, timeout, cancellation, or incomplete accounting.
- Local route/interface discovery consumes no token and no network-attempt slot.
- Waiting for a slot, token, or either rolling admission budget is cancelable and counts against the ten-second logical-round deadline.
- Rate-limited, rolling-budget-delayed, or capacity-delayed work that cannot start in time is incomplete, not failed.

Token credit uses exact monotonic-duration arithmetic or an equivalent fixed-point/rational representation. One token is exactly one second of refill credit, and capacity is exactly eight seconds of credit. Binary floating-point accumulation, rounded token counts, periodic timer ticks, a background refill loop, operating-system timer cadence, wall-clock time, and scheduler tick counts are not permitted inputs.

The bucket is initialized only by successful scheduler activation under section 4.1: credit is exactly eight token-seconds and the refill anchor is the exact injected-monotonic activation timestamp. Refill is continuous and evaluated lazily whenever availability or consumption is tested. At an evaluation timestamp `now`, the dispatcher:

1. obtains `now` only from the injected monotonic clock;
2. fails an implementation invariant if `now` is earlier than the prior refill anchor;
3. adds exactly `now - prior_refill_anchor` to credit;
4. caps credit at exactly eight token-seconds; and
5. advances the refill anchor to `now`.

Fractional credit is retained exactly. One-half second contributes one-half token but cannot start a transport by itself. A token becomes available at the exact instant cumulative credit reaches one token-second. Multiple eligibility evaluations at the same monotonic timestamp receive no refill between evaluations and remain ordered by section 4.5. Sleep, suspend, scheduler delay, or other long elapsed time can refill only to capacity; it creates no catch-up balance beyond eight tokens.

Credit and the refill anchor remain process-local internal scheduler state. They are not persisted or exposed through a public contract, schema, cloud evidence, health output, support output, logs, or metrics and contain no target, provider, endpoint, or customer value.

Configuration validation calculates minimum-interval scheduled demand atomically before scheduling any probe. These formulas are necessary startup gates; they do not prove that activation, synchronized due work, release from backpressure, sleep/refill, or another runtime timing pattern remains within either rolling per-minute limit. The minimum possible scheduled interval is 27 seconds for gateway, DNS, and HTTPS aggregate rounds, which is 90% of their 30-second base interval. For each enabled TCP target:

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

Configuration is valid only when `worst_case_attempts_per_minute <= 60`. Excess scheduled demand rejects the entire configuration before scheduling with bounded code `probe_rate_budget_exceeded`; passing this formula does not authorize any runtime burst outside the hard rolling cap below.

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

Configuration is valid only when `worst_case_charged_network_bytes_per_minute < 100,000`. A value at or above the approved decimal cap rejects the entire configuration before scheduling with bounded code `probe_network_budget_exceeded`; passing this formula does not authorize any runtime burst outside the hard rolling charged-byte cap below. This exact charged-byte model is deterministic conservative accounting for configuration and acceptance; it is not packet capture or a claim of exact physical-wire traffic.

A disabled DNS probe creates no DNS scheduler target, consumes no DNS token or network-attempt slot, and contributes zero DNS demand to both startup budgets. HTTPS provider DNS resolution remains charged as part of each HTTPS provider attempt; disabling the independent DNS probe does not remove or reduce HTTPS demand. Configuration validation remains atomic, and neither budget error may echo target IDs, intervals, hosts, ports, provider details, operator identifiers, endpoints, calculated customer-specific values, or other sensitive configuration.

A 15-second TCP interval remains individually permitted only when the complete configured set passes both startup formulas. With DNS enabled and the two default HTTPS providers, 25 targets at 15 seconds are invalid: worst-case scheduled demand is approximately 108.89 attempts/minute and 141,368.89 charged bytes/minute. The same 25 targets at a 60-second configured interval passes startup validation: worst-case scheduled demand is approximately 36.67 attempts/minute and 67,413.33 charged bytes/minute. The reviewed example with 11 TCP targets at the fastest interval and 14 at the slowest interval likewise passes the scheduled-demand formulas at approximately 53.93 attempts and 85,100 charged bytes per minute, but neither formula exempts its activation or later synchronized work from rolling admission. Different provider counts and the DNS enabled state can change startup validity, and any formula-valid 25-target configuration can still produce deterministic scheduler-level incomplete work when synchronized demand cannot enter under both rolling caps before its deadline.

The active scheduler also owns exactly one process-local **rolling transport-start admission ledger** covering every transport start in the current scheduler epoch. Each valid entry contains only the exact injected-monotonic `transport_start` and exact fixed `charged_method_bytes` from the closed table above; an exact recognized-method charge must therefore exist before a candidate can cross the start boundary. No target, provider, host, endpoint, port, customer, display, or other workload identity enters this ledger. It is not persisted, logged, exposed, included in public evidence, or carried into a replacement epoch.

At a candidate's exact injected-monotonic timestamp `now`, the serialized dispatcher removes every prior entry whose start timestamp is less than or equal to `now - 60 seconds`. The retained entries are therefore exactly the right-closed window `(now - 60 seconds, now]` before the candidate is added. For enforcement this is equivalent to every half-open 60-second interval: a start exactly 60 seconds after an earlier start does not overlap that earlier start's window. The candidate may start only when both relations would remain true after appending it:

```text
retained_started_attempt_count + 1 <= 60

retained_charged_method_bytes
  + candidate_charged_method_bytes
  < 100000
```

The attempt cap is inclusive at 60, and the fixed charged-byte cap is exclusive at 100,000. “Per minute” throughout this contract means every rolling monotonic 60-second window, never a wall-clock minute, calendar bucket, aligned interval, sampling average, full-run average, or long-run token-refill average. The existing initially full eight-token bucket remains unchanged, but its eight activation starts plus exact one-per-second refill can never produce 67 starts in the first half-open minute because rolling admission stops candidate 61. A full bucket after a long elapsed interval still cannot bypass either rolling cap.

The exact recognized-method charge is known before admission. Lazy token refill, rolling-window expiry, both rolling-budget checks, target/round/provider guard acquisition, global-slot acquisition, subtraction of one token-second, rolling-ledger append, `transport_start = now` recording, and immediate transport invocation form one deterministic atomic transport-start boundary serialized under the same `now`. Same-timestamp candidates follow section 4.5's stable fair-dispatch/provider order; every accepted candidate immediately affects the next same-timestamp candidate's attempt and charged-byte checks.

Waiting for either rolling budget reserves no target or provider guard, reserves no global slot, consumes no token, creates no ledger or attempt entry, remains cancelable, and counts against the existing ten-second logical-round deadline. If admission cannot become available before that deadline, the transport never starts and the logical work follows the existing scheduler-level incomplete rules. It enters neither attempt accounting nor charged accounting. After a transport starts, its rolling-ledger entry is never refunded or removed because of success, failure, timeout, cancellation, or attempt-level incomplete; it expires only by exact monotonic age.

The rolling ledger remains continuous through activation, initialization, readiness barriers, steady state, end drain, and final-snapshot handling wherever starts are permitted. Acceptance phase boundaries never reset it. Degradation, shutdown, or restart discards it with the scheduler epoch, and replacement activation creates an empty ledger. Startup validation rejects configurations whose minimum-interval scheduled demand exceeds either formula, while runtime admission prevents activation, synchronization, sleep/refill, backpressure release, and other timing patterns from exceeding either rolling limit. Fair dispatch and no-starvation rules remain applicable only among work eligible under both runtime budgets.

After a transport attempt becomes eligible through the fair-dispatch rules, it may start only when both rolling budgets, its applicable target/round/provider guard, one global execution slot, and at least one exact token-second of credit are available. The dispatcher must not hold a guard or global slot while waiting for token credit or a rolling budget and must not subtract credit or append a ledger entry while waiting for a slot. Cancellation before the atomic boundary consumes no token or charge and creates no attempt record. Once transport invocation starts, its token and ledger charge are never refunded. Each HTTPS provider child performs this admission and accounting independently even when multiple children start at the same timestamp in one dispatch cycle; every child contributes one 8,192-byte ledger entry, same-timestamp children receive no intervening refill, and they follow the captured stable provider/fair-dispatch order. Acceptance phase membership uses each child's own transport-start timestamp, never fair-queue admission or aggregate-round admission.

### 4.5 Deterministic fair dispatch

The dispatcher uses a deterministic rotating fair queue over due logical targets. Gateway, DNS, HTTPS aggregate, and every enabled TCP target are logical targets. Within one scheduler epoch, each logical target and each configured HTTPS provider child has one stable internal scheduler/member identity. The logical-target identities determine stable queue order and due-round ledger attribution; the provider-member identities determine stable child attribution. These are the same immutable identities used to route work and reconcile the acceptance workload manifest and runtime continuity in section 8.1. They are never logged, reported, derived from display names, mutable array positions, endpoint strings, or log text, or exposed through health, support, or metrics output.

- The dispatcher maintains a rotating cursor over stable key order.
- Gateway, DNS, and each due TCP logical target contribute at most one attempt per pass.
- When an HTTPS logical round is admitted, its provider children are sorted by stable provider ID using `.NET StringComparer.Ordinal`. The captured order is immutable for that round and is never logged or reported.
- On the HTTPS target's dispatch turn, the dispatcher offers every not-yet-started provider child in that stable order during one bounded fan-out. Every child for which both rolling budgets, a slot, and at least one exact token-second of credit are available starts without waiting for an earlier provider to complete and without waiting for a complete pass over all other logical targets. Children evaluated at the same timestamp consume available credit and rolling capacity in that stable order with no intervening refill.
- If the initial fan-out leaves provider children pending because a rolling budget, slot, or token credit was unavailable, subsequent dispatch opportunities alternate one eligible pending child with one next eligible due non-HTTPS logical target, beginning with the pending child and skipping an empty or rolling-ineligible lane. Waiting for credit or a rolling budget never reserves a guard or global slot, and waiting for a slot never reserves or subtracts credit or charge. Pending provider children therefore remain bound to the same round and deadline but are never moved behind a complete pass over all other eligible logical targets.
- Child completion order does not affect aggregation. The round completes only after every configured provider has exactly one terminal child result; reduction reads results in the captured stable provider order.
- Cancellation stops new child starts and promptly cancels active children. Deadline expiry stops new child starts, applies every terminal child result timestamped at or before the deadline, and then reduces the round as `incomplete` if any required child never started or lacks a terminal classification.
- Canceled, disabled, or no-longer-due work is skipped.
- A provider fan-out is bounded by the configured maximum of four children, by the process-wide cap of four, and by one active HTTPS round. After that bounded fan-out, the rotating non-HTTPS cursor continues from its prior position; no second HTTPS round can consume slots or tokens until fixed-delay scheduling after completion makes it due.
- Bounded HTTPS occupancy must not cause systematic starvation of Gateway, DNS, or TCP work under sustainable valid mixed demand eligible under both hard rolling budgets.
- Transient backpressure can still make a round incomplete under the ten-second deadline.

Fair-queue selection establishes candidate order. Runtime-budget admission, guard/slot acquisition, token consumption, ledger append, start recording, and invocation then follow the one atomic boundary in section 4.4.

### 4.6 Shutdown

On shutdown:

- stop admitting new work first;
- cancel queued and active attempts;
- discard token credit, its refill anchor, and the rolling transport-start admission ledger after no further transport start can occur;
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
| Gateway stable snapshot with zero eligible default-route candidates after `DisableDefaultRoutes == false` and every other binding filter (`missing_route`) | 2 |
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
| Gateway | `active_interface_missing`, `default_route_missing`, `gateway_address_missing`, `icmp_succeeded`, `icmp_timeout`, `icmp_unreachable`, `icmp_unavailable`, `route_selection_unavailable` |
| DNS | `dns_succeeded`, `dns_timeout`, `dns_no_address`, `dns_resolution_failed` |
| HTTPS | `https_succeeded`, `https_dns_failed`, `https_connect_failed`, `https_tls_failed`, `https_timeout`, `https_redirect_rejected`, `https_unexpected_status`, `https_body_mismatch`, `https_body_too_large`, `endpoint_not_started`, `partial_endpoint_failure`, `blocked_by_dns`, `dns_correlation_unavailable` |
| TCP | `tcp_connect_succeeded`, `tcp_connect_refused`, `tcp_connect_timeout`, `tcp_connect_failed` |

Codes are not user-facing causal claims. Production logs and metrics may include the probe kind, code, aggregate counts, state names, and bounded duration. They must not contain:

- exception text, stack traces from expected transport failures, or operating-system error strings;
- IDs, labels, hostnames, IP addresses, ports, URLs, query names, DNS answers, headers, bodies, interface identities or types, VPN status, `DisableDefaultRoutes` values, interface descriptions, route/interface candidate fields, next hops, metrics, indices, LUIDs, protocols, origins, or machine paths;
- payloads, credentials, authorization, user information, cookies, customer identifiers, or provider trace metadata;
- DNS-correlation sequence, completion timestamp, round classification, or captured resulting state;
- raw configuration or entire serialized outcomes.

Unexpected exceptions may be correlated through an existing bounded correlation ID, but sensitive exception content remains out of ordinary logs and support artifacts.

Security review explicitly preserves the general malformed-configuration last-known-good rule wherever a safely stored accepted durable/updateable value exists. The R0-005 startup-only exception is narrow because this task owns neither authenticated configuration nor persistence: invalid startup input activates no scheduler or subset, reuses no stale process value, remains visibly unavailable/degraded, and exposes only bounded `configuration_invalid` evidence without customer-controlled values. This availability cost avoids silently probing outdated or unsafe targets/providers and does not authorize a new store, failed-update replacement, or a general fail-closed configuration policy. Failure reports likewise expose boundary availability and `null`, never fabricated measurements or private configuration.

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

Before R0-005 configuration acceptance, static host-profile preflight, process creation, or scheduler activation, the acceptance coordinator completes one **source-capture stage**. Source capture establishes report provenance; it is not an operational measurement and does not add a `measurement_availability` Boolean. The review/acceptance gate supplies the coordinator with one exact full reviewed implementation commit SHA. The coordinator must independently resolve that SHA to one Git commit and its exact root-tree object, materialize and verify that exact tree, build the acceptance artifact itself from that tree during the same source-capture run, validate the complete one-file private closure, securely hash and bind the resulting artifact, and cross-check its immutable embedded commit metadata. It must not accept a user-supplied or previously built artifact or treat metadata inside an artifact as proof of the source that produced its bytes. If any source property cannot be validly produced, the coordinator follows the source-capture failure rule in section 8.8 and begins none of the later stages.

The coordinator creates one fresh, empty, isolated source root containing only the tracked content of the exact reviewed Git tree. A detached fresh worktree, exact Git-tree materialization, or an equivalently strong reviewed mechanism is acceptable only when it proves all of these properties:

- detached `HEAD`, when the mechanism has a worktree `HEAD`, equals the supplied full reviewed SHA; the resolved commit and root-tree object exactly equal the objects referenced by that SHA;
- complete tracked path, mode, and blob-object membership agrees with the reviewed commit tree, with no tracked difference, staged change, untracked file, ignored build output, or preexisting generated source;
- no submodule or gitlink input exists; current R0-005 acceptance permits none;
- no symlink, reparse point, import, compile item, generator input, source overlay, or other build input escapes the exact source root; and
- the developer worktree remains clean as a repository gate but neither contaminates nor substitutes for the isolated source root.

The coordinator verifies that materialized tree immediately after materialization, before restore, immediately before publish, and immediately after publish. Every intermediate, package, binary, and publish output uses a fresh acceptance-controlled location outside the tracked source root. A tracked change, staged change, untracked file, ignored or preexisting `bin`, `obj`, `publish`, package, staging, artifact, or generated-source reuse, source-root write, tree-membership change, reparse escape, or external overlay invalidates provenance. A build step may not modify the reviewed source root and then claim the original commit identity.

The acceptance build-input boundary contains only the exact reviewed Git tree; the explicitly approved .NET SDK/toolchain; dependency/runtime packs and NuGet packages resolved from the reviewed declarations through the approved source policy; and deterministic coordinator-provided properties required by this acceptance contract. It prohibits user-wide or parent-directory `Directory.Build.props`/`Directory.Build.targets`, unreviewed response files or arbitrary external MSBuild imports, compile items outside the exact tree, environment-selected source, unreviewed overlays, user-provided analyzers or generators, preexisting generated source, arbitrary plug-ins/extensions, an alternate project or solution, and package or SDK substitution after the resolved boundary is frozen. SDK targets and resolved NuGet analyzers/generators are permitted only as members of the exact approved toolchain or resolved dependency graph.

The coordinator retains a private internal build-input provenance record sufficient to prove the reviewed commit and root-tree identities, complete tracked-tree agreement, exact project/solution entry point, exact SDK version, exact resolved package IDs/versions/content hashes, absence of unapproved project/props/targets/compile/analyzer/generator/native input, exact publish properties, fresh intermediate/output roots, and no source-root mutation. The record is not part of report v1. Neither report exposes a source-root, worktree, Git object-database, build-log, package-cache, intermediate/output, project, or source-file path; a tree listing or blob ID; an analyzer/generator identity; or an artifact path or filename.

Restore runs from the exact materialized tree using the sole approved public NuGet source. Publish uses the frozen resolved graph without an implicit restore or graph change, the exact reviewed project/solution entry point, and fresh external intermediate and publish roots. The future native R0-005 acceptance run uses a deterministic Windows x64 publish created by the coordinator specifically for acceptance whose effective resulting runtime shape is equivalent to all of these properties:

```text
RuntimeIdentifier = win-x64
SelfContained = true
PublishSingleFile = true
IncludeNativeLibrariesForSelfExtract = true
PublishTrimmed = false
DebugSymbols = false
DebugType = None
```

Presence of property text in a project file is not sufficient; source capture must validate the resulting runtime shape. The single artifact contains the apphost, every Remote Property Watchdog and referenced application assembly, every transitive managed dependency, every application-private native dependency, the self-contained .NET runtime components, and all dependency/runtime metadata required to execute it. Acceptance launch must not depend on an external application DLL, referenced managed DLL, application-private native DLL, `.deps.json`, `.runtimeconfig.json`, second executable, plug-in, probing directory, dynamically selected code file, or framework-dependent shared runtime. Windows-owned system components from the supported acceptance host remain outside the application-private closure and are governed by the pinned host-profile contract. Accepted external startup configuration and generated report/data files are data under their existing validation and privacy rules; they must not select or load executable code.

Source capture rejects before process creation a user-supplied prebuilt artifact; an artifact copied from an unknown or prior build; any artifact from an existing `bin`, `obj`, `publish`, package, staging, or artifact directory; a framework-dependent apphost; `SelfContained=false`; any multi-file application publish; any launch that requires a separate managed or application-private native binary or metadata file; any publish whose complete private closure cannot be proved to reside in the single artifact; plug-in/probing or other dynamic external-code behavior; any unexpected executable-code file in the acceptance publish/runtime closure; and any mutable shared application runtime or assembly directory. The source-capture run that materializes and verifies the exact tree must also restore, publish, validate the private closure, hash, bind, and later launch the artifact. This is a future acceptance-artifact requirement only; it does not add a code or project-file change to this documentation task.

The coordinator validates that publish shape, selects the exact single artifact, opens it with a handle that prevents write and delete replacement, hashes the complete artifact bytes through that bound handle, retains the binding through process creation, launches that same securely bound artifact, and preserves sufficient private identity to prove that the process image corresponds to it. Commit provenance requires one three-way identity: (1) the exact reviewed SHA supplied by the acceptance gate, (2) the exact commit and root tree independently materialized and built by the coordinator, and (3) immutable commit metadata embedded in the resulting artifact. The embedded value is a mandatory cross-check, never self-authenticating proof. A dirty or different tree, external source overlay, alternate project, prebuilt artifact, build outside the coordinator-controlled boundary, or artifact embedding the expected SHA cannot be rescued by matching metadata or a newly computed artifact digest. A mismatch or inability to prove the three-way identity adds `source_identity_invalid` and prevents every later stage. Absolute or relative path, filename, directory, handle, assembly name, module identity, and other private launch identity remain absent from both reports.

The exact-tree provenance and `source.executable_sha256` are separate mandatory proofs. Provenance proves the reviewed source and frozen input boundary from which the coordinator produced the bytes. The digest proves the exact complete securely bound artifact bytes later launched. Neither substitutes for the other, and the digest becomes available only after exact-tree provenance, controlled restore/publish, complete private-closure validation, secure open/hash, and enforceable launch binding all succeed.

If the supported .NET single-file runtime extracts bundled native components, they originate only from the securely hashed bundle and use a fresh acceptance-controlled location. Preexisting or unexpected executable content is rejected, untrusted mutation or substitution is prevented or deterministically detected, private extraction paths and filenames never enter either report, and extraction cannot permit application code to load from an unbound directory. From process creation through the final acceptance endpoint, internal verification must prove that no application-private managed or native executable component loaded from outside the authenticated bundle. Any unexpected external application-code load fails acceptance with `source_identity_invalid` and can never produce a passing report.

The run uses 25 distinct enabled synthetic TCP targets, enabled DNS, the accepted two-provider HTTPS configuration, and fake or loopback endpoints, never real customer or network data. The two accepted providers satisfy the same pairwise provider-ID, operator-ID, normalized-host, and normalized-registrable-domain rule as every 2–4-provider configuration. It exercises the Gateway, DNS, every HTTPS provider child, and every TCP scheduling member. The command's `-TargetCount` must equal 25, the atomically accepted configuration must contain exactly 25 distinct enabled TCP members, and the report's `configuration.target_count` must equal that manifest count.

The implementation freezes one immutable **acceptance workload manifest** from the atomically accepted startup configuration before acceptance workload measurement begins. It contains exactly one stable, internal, non-observable workload-member identity for each required member:

- one Gateway member;
- one DNS member when DNS is enabled and no DNS member when DNS is disabled;
- one member for every configured HTTPS provider, with manifest HTTPS count equal to the pairwise-distinct accepted provider/domain/operator/host count; and
- one member for every enabled TCP target.

The manifest uses the same stable internal scheduler/member identities that route work under section 4.5. It is not reconstructed from display names, array positions that can change, endpoint strings, log text, the reported target count, or aggregate probe-kind counts. Identities must be unique and remain bound to the same accepted members for the complete run. Configuration changes, manifest replacement or mutation, duplicate manifest identities, or an attempt attributed to an unknown, missing, duplicated, or no-longer-configured member fail acceptance.

A required member is `exercised` only by attributable work during the half-open steady-state interval whose transport or required logical-round operation starts and reaches one terminal, completely accounted result under the attempt-closure rules. A member counts once as distinctly exercised regardless of later attempts. Terminal failure, timeout, or cancellation after transport start can exercise the member; endpoint success is not required. Work canceled before transport start, startup-only work, and a started attempt that never reaches terminal accounting do not exercise a member.

Method-specific coverage is binding:

- **Gateway:** at least one steady-state Gateway logical round must complete and the required acceptance configuration must produce at least one terminal `gateway_icmp` transport attempt attributed to the manifest Gateway member. A missing, policy-disabled, or unusable acceptance-host default route cannot satisfy coverage; in particular, a route whose family-matching IP-interface row has `DisableDefaultRoutes == true` cannot satisfy Gateway workload coverage.
- **DNS:** when enabled, at least one terminal steady-state DNS attempt must be attributed to the manifest DNS member. When disabled, DNS is absent from the manifest and both report flags are `false`.
- **HTTPS:** every configured provider member must independently receive at least one terminal steady-state child attempt. Attempts from one provider cannot substitute for another.
- **TCP:** every distinct enabled TCP member, including all 25 required synthetic targets, must independently receive at least one terminal steady-state attempt. Repeated work against one target cannot substitute for a missing target.

The internal reconciliation deduplicates exact member identity and must prove:

```text
distinct_exercised_required_members =
  distinct_required_manifest_members

gateway_exercised_count = gateway_required_count
dns_exercised_count = dns_required_count
https_providers_exercised = https_providers_configured
tcp_targets_exercised = tcp_targets_configured
```

Gateway and DNS internal counts are each zero or one and produce the report booleans in section 8.8. Duplicate accounting for one member never increases a distinct exercised count.

`-DurationMinutes 30` means 30 complete steady-state minutes after the service first reaches monitoring-ready and the steady-state baseline bundle below is committed. Initialization, the mandatory post-launch process-profile stage, and the complete start barrier are additional and do not reduce that window.

- After source capture and configuration acceptance, the coordinator obtains every static host field and completes the 60-second idle preflight. Only then does `host_profile_complete` become true and process creation become permitted. The eight process-specific `host_profile` fields remain `null` at this point.
- Before process creation, the coordinator subscribes to every required R0-004 monitoring-readiness transition and every required process-priority, debugger, and profiler transition source so an early event cannot escape accounting. Exact process-exit observation is subscribed before or atomically with process creation, and the coordinator retains the exact process handle and exact PID so no started-process exit can escape deterministic classification.
- Initialization begins immediately before the service process is started and ends when the first monitoring-ready observation is validly accepted after initial process-profile success. Immediately before process launch, the sampler creates or verifies the dedicated monitored runtime directory and captures the complete recursive process-start runtime-file snapshot used for startup disk attribution. Failure to reach accepted readiness within 60 seconds fails the run.
- The exact service PID is captured after process creation. `process_started` becomes true when that process and exact PID exist and are captured; it does not claim that process-specific profile capture succeeded.
- Immediately after exact-PID capture and before accepting any monitoring-ready observation, activating any scheduler, or beginning acceptance workload, the coordinator performs one mandatory initial process-profile capture against that exact PID. This is a distinct producing boundary with no tenth availability Boolean.
- A readiness event observed before initial process-profile completion may be retained as private transition evidence, but it is not the first accepted monitoring-ready observation unless and until the initial exact-PID profile succeeds.
- Process exit, PID change, automatic restart, or unplanned termination after initial PID capture and before the authorized cleanup-termination intent fails immediately. The sampler must not resume against a replacement PID. The separately defined expected exit after that intent is cleanup rather than an unplanned process failure.
- Startup CPU is reported separately and is excluded from the approved steady-state average CPU calculation.
- Every successfully collected working-set sample after process creation, including initialization, barrier, steady-state, and drain samples, remains subject to the absolute working-set cap.

The initial exact-PID process profile must establish `process_architecture = x64`; a bounded invariant `framework_description`; a bounded invariant `runtime_version` whose major version is 10; the accepted Windows x64 `runtime_identifier`; `gc_mode` exactly `workstation` or `server`; `process_priority = normal`; and `debugger_attached = false` and `profiler_attached = false`. Values come from the exact launched process or an exact process-owned acceptance boundary. Host architecture, artifact or publish names, project properties, the coordinator process, arbitrary environment variables, intended arguments, and configuration values are not substitutes.

On a successful initial capture, the six process architecture/runtime/GC/priority fields are bounded JSON strings and the debugger/profiler fields are JSON Booleans. A readable out-of-profile architecture, runtime, GC mode, or priority is retained as its bounded actual closed value and fails. A measured attached debugger or profiler is retained as `true`. An independently unavailable or unvalidated field is `null`; no sentinel, empty string, intended value, host-derived value, or copied configuration value means unavailable. Process priority uses the stable closed vocabulary `idle`, `below_normal`, `normal`, `above_normal`, `high`, and `real_time`, with `normal` the only passing value.

If the initial exact-PID profile cannot be completely obtained or is out of profile, `process_started` remains true; each valid captured field retains its bounded actual value; each independently unavailable field is `null`; and `process_profile_invalid` is mandatory. No monitoring-ready observation is accepted, no scheduler is activated, and no acceptance workload begins. Every later-boundary value remains `null`. Full final process-profile validation is inapplicable because continuity never became established, but the exact started PID still enters the universal exit/intent arbiter and bounded cleanup: if it remains live at arbitration, authorized intent is committed for that PID and the graceful stop is issued; if it exited before intent, `process_exited` is additionally mandatory and no retroactive intent or stop occurs. An expected exact-PID exit after committed intent is cleanup behavior, not `process_exited`. Skipping the inapplicable final profile validation adds no violation, and cleanup violations appear only when their independent obligations fail. A structurally correct independent process-field null does not require `report_serialization_invalid` merely because capture failed. A malformed non-null value, missing/reordered property, sentinel, contradiction, or human/JSON disagreement still requires it.

Every monitoring-readiness transition is recorded against the injected monotonic clock. The first successful ready observation accepted after initial process-profile success begins one **runtime continuity interval** that ends at the acceptance measurement endpoint: stable final-runtime-snapshot commit on success or the applicable bounded failure-evidence endpoint on failure. Throughout that interval:

1. the existing monitoring-ready level remains continuously `true`;
2. the probe scheduler remains active, enabled, undisposed, and owned by the same scheduler epoch;
3. that epoch continues to own the atomically accepted configuration and immutable acceptance workload manifest;
4. the accepted configuration and manifest remain unchanged; and
5. the captured service PID remains unchanged.

Independently, from successful initial exact-PID process-profile capture through the serialized cleanup-termination-intent boundary defined below, process priority remains `normal`, no managed or native debugger is attached, and no managed or native profiler is attached.

The scheduler epoch is a stable process-local internal identity created only after initial process-profile success by the successful activation that produced the accepted scheduler. It is used only to prove continuity and is never placed in reports, logs, health, support output, persistence, or metrics. The coordinator checks readiness, scheduler state/epoch/ownership, PID, process priority, debugger state, and profiler state at every required five-second process sample and at initial process-profile capture, first readiness acceptance, start-barrier commit, exact steady-state end, end-drain closure, final-snapshot commit or failure endpoint, pre-termination cleanup, and, after successful initial capture, the conditional final exact-PID validation immediately before cleanup arbitration and authorized intent. Event-driven evidence detects short transitions; sampled levels reconcile the current exact-PID state.

Continuous priority/debugger/profiler proof requires either enforced prevention plus sampled verification or a complete event-driven transition source plus sampled reconciliation. Polling alone is insufficient when an attach-and-detach or priority-change-and-restore transition could occur between samples. The reviewed mechanism must close every interval from initial profile success through committed cleanup-termination intent; loss of prevention or observation, a query failure, unavailable state, or event/sample contradiction fails. If the platform cannot prevent or completely observe prohibited transitions, the native acceptance run cannot pass.

A missing transition, unavailable level, sampled/event contradiction, false readiness, scheduler disposal, disablement, replacement, epoch change, ownership loss, configuration/manifest mutation, priority change away from `normal`, debugger attachment, profiler attachment, or loss of continuity proof is permanent evidence of failure; later recovery, restoration, or detachment cannot erase it. A later process-profile defect adds `process_profile_invalid`, stops new admissions permanently, permits only bounded closure of already-started attempts plus final bounded evidence and cleanup, never restarts or replaces the process, never reactivates the scheduler, and retains the initial process-profile values unchanged.

Readiness loss before steady-state entry fails the start barrier. Readiness loss during steady state, end drain, or final-snapshot stabilization also fails the run. A false-to-true transition is not forgiven. `monitoring_readiness_lost` is mandatory when readiness becomes false, unavailable, or internally contradictory after the first ready observation and before the continuity endpoint. `acceptance_scheduler_inactive` is mandatory when the scheduler stops, is disabled or disposed, is replaced, changes or loses its epoch, ceases to own the accepted workload, or otherwise fails the scheduler continuity checks even if readiness incorrectly remains true.

After either continuity failure, the coordinator permanently stops new admissions, performs only bounded closure of already-started attempts, final bounded evidence capture, and cleanup, and never restarts or reactivates a scheduler in the same acceptance run. It does not reset, replace, or refill the token bucket or rolling transport-start admission ledger to hide the interruption. Independently applicable PID, workload, attempt, boundary, report, and cleanup violations remain required.

Continuous readiness and epoch ownership are combined with deterministic due-work reconciliation. The scheduler maintains an internal due-round ledger under the existing fixed-delay, jitter, admission, and ten-second deadline rules. For every Gateway, enabled DNS, HTTPS aggregate, and enabled TCP logical round whose calculated due timestamp occurs before `steady_state_end`, the stable logical-target identity must receive exactly one of these reductions before its round deadline:

- one terminal logical-round reduction, including a valid success, counted failure, or existing non-counting terminal result; or
- one explicit scheduler-level incomplete reduction when admission or completion cannot occur under the existing scheduler rules.

A due round may not silently disappear during a barrier, capacity/token contention, degradation, cancellation, or any other scheduler path. For each required logical target, the ledger proves:

```text
due_rounds =
  terminal_rounds
  + explicit_scheduler_incomplete_rounds
```

The ledger uses the same stable internal scheduler/member identity as dispatch and manifest reconciliation. A due round with neither reduction, duplicate reduction, wrong-member reduction, or reduction after its deadline makes due-work reconciliation false and adds `acceptance_scheduler_inactive`. The counts and identities remain internal and are not exposed per target or provider. This rule requires deterministic round closure, not endpoint success or an exact success count; valid timeout, failure, post-start cancellation, and scheduler-level incomplete semantics remain unchanged.

The acceptance coordinator also retains internal exact transport-start/charge records from the scheduler epoch's rolling admission ledger sufficient to prove over activation and every later runtime interval that no rolling monotonic 60-second window contains more than 60 starts or reaches 100,000 fixed charged bytes. The proof uses the same started-attempt records that feed JSON and human aggregate accounting, includes each HTTPS child independently, preserves deterministic candidate order and exact-boundary expiry, and spans initialization, both acceptance barriers, steady state, drain, and final-snapshot handling without a phase-boundary reset. A lower 30-minute average cannot hide a hard rolling-budget violation. The internal proof exposes no ledger entry, timestamp, identity, path, endpoint, or configuration value in either report.

The transport-start instant is the monotonic timestamp captured at the same point the token is consumed, immediately before invoking the transport. Admission time, queue time, slot-wait time, and token-wait time do not determine resource-phase membership.

After monitoring-ready, the acceptance coordinator establishes a clean steady-state start barrier:

1. Pause all new network-attempt admissions.
2. Cancel queued attempts that have not started and reduce every due-but-not-started logical round, including due work not yet admitted, as explicit scheduler-level non-counting/incomplete work. Reconcile each affected due round in the internal ledger before its deadline. Such work consumes no token and does not enter a transport-attempt array.
3. Allow every started attempt to reach one terminal attempt outcome.
4. After no started attempt remains and while admissions remain paused, establish one steady-state baseline bundle containing the complete consistent recursive runtime-file snapshot, the exact process-CPU baseline, the acceptance-only attempt/network-accounting reset, and one injected-monotonic boundary timestamp. The reset seals the separately reported startup attempt/network totals and initializes the steady-state attempt/network accumulators to zero; it does not erase startup evidence.
5. Capture the process-CPU baseline immediately before the monotonic bundle-commit timestamp. Commit the bundle atomically; `steady_state_start` is exactly that successful commit timestamp. There is no wait for a later pre-existing sampling-grid boundary.
6. Resume admissions only after the baseline bundle and `steady_state_start` exist. Begin the requested 30 complete steady-state minutes at that exact timestamp.

The complete start-barrier operation, from the admission pause through successful baseline-bundle commit, has one ten-monotonic-second deadline. Exceeding it adds `measurement_boundary_timeout` and fails the run; drain completion does not start a new allowance. The runtime-file component uses the shared consecutive-map stable-snapshot protocol in section 8.6. If the monitored tree changes during a pair, the sampler may retry only within the same bounded barrier. If no stable complete snapshot can be committed before the deadline, the sampler fails with `runtime_file_snapshot_incomplete` and `measurement_boundary_timeout` as independently applicable rather than accepting an ambiguous baseline.

The acceptance pause does not initialize, reset, discard, or refund scheduler token-bucket credit or the rolling transport-start admission ledger. While admissions are paused, exact elapsed monotonic duration may lazily refill the active bucket only up to eight token-seconds and may age existing rolling-ledger entries only by exact monotonic duration under section 4.4. The next eligible starts after resume use that existing bucket, anchor, and retained rolling window. Only scheduler activation, readiness degradation, restart, or shutdown has the epoch-state lifecycle effects defined in sections 4.1 and 4.6.

`steady_state_start_barrier_elapsed_ms` measures from the admission pause immediately after readiness through the baseline-bundle commit at `steady_state_start`. For report attribution, `startup` spans process start up to that commit and therefore includes both initialization and the complete start barrier. All process CPU consumed before the committed CPU baseline, all runtime-file growth present in the committed snapshot, and every transport invocation whose transport-start instant precedes `steady_state_start` are startup-only. Steady-state CPU begins from the same committed CPU baseline, steady-state disk begins from the same committed runtime-file snapshot, and steady-state attempts/network begin at the same monotonic boundary. No CPU or disk interval is omitted, double-counted, or assigned to both phases.

Steady-state membership is the half-open interval:

```text
steady_state_start <= transport_start < steady_state_end
```

An invocation started before `steady_state_start` is startup work. An invocation started within the half-open interval is steady-state work. Each started invocation belongs to exactly one phase, and its bytes and outcome are never split across phases.

At exact `steady_state_end`, the acceptance coordinator establishes the end barrier:

1. Pause new network-attempt admissions before another token can be consumed.
2. Cancel attempts admitted but not started before the endpoint and reduce every due-but-not-started logical round, including due work not yet admitted, as explicit scheduler-level non-counting work. Reconcile each affected due round in the internal ledger before its deadline. Such work consumes no token and does not enter steady-state attempt accounting.
3. Allow attempts started before the endpoint to drain to one terminal result. They remain steady-state attempts and contribute their complete sent, received, and charged bytes plus exactly one complete outcome even when completion occurs during the drain.
4. Fail the run if the end drain exceeds 10 monotonic seconds. A started invocation without a terminal result at that deadline becomes attempt-level `incomplete`, adds `attempt_accounting_incomplete`, and fails the run.
5. Keep admissions paused after the drain and immediately begin one separate final-runtime-snapshot stabilization window.
6. Commit the final snapshot through the shared consecutive-map stable-snapshot protocol in section 8.6 within exactly 10 monotonic seconds from end-drain completion.
7. Resume no work after `steady_state_end`; the final-snapshot protocol must not start a transport, restart acceptance accounting, or reset or reactivate the token bucket or rolling transport-start admission ledger.

No invocation may start at or after `steady_state_end`. Such a start adds `measurement_boundary_violation` and fails the run. `steady_state_end_drain_elapsed_ms` measures from exact `steady_state_end` until every started steady-state attempt has a terminal accounting outcome or the drain deadline expires. The final-snapshot window begins only when that drain completes; its ten-second deadline neither resets nor extends the preceding ten-second drain deadline.

The exact service PID must remain alive and unchanged through final-snapshot commit. Exit, restart, or PID replacement during stabilization fails under the existing process/PID violations. If no stable final snapshot commits before the final-snapshot deadline, the sampler adds `runtime_file_snapshot_incomplete` and `measurement_boundary_timeout`, fails acceptance, and does not calculate or report a passing disk result from either ambiguous map.

The top-level `attempts` object represents steady-state attempts only. Attempt membership and steady-state network attribution end at exact `steady_state_end`, with the full bytes of every attempt started before that boundary included even when it completes during the end drain. Startup disk attribution compares the process-start snapshot with the runtime-file snapshot committed in the steady-state baseline bundle; steady-state disk attribution compares that same committed snapshot with the stable final snapshot. Startup CPU ends at the bundle's committed CPU baseline; the steady-state CPU numerator begins at that identical baseline and includes process CPU through stable final-snapshot commit, while its denominator remains the exact requested 30 steady-state minutes. Working-set absolute-cap enforcement and process identity enforcement continue through stable final-snapshot commit. Growth medians use only steady-state minutes 1–5 and 26–30 and never include drain or final-snapshot samples.

Acceptance evidence and cleanup use these five ordered boundaries:

1. **Acceptance measurement endpoint.** On an otherwise successful run, this is stable final-runtime-snapshot commit. On an earlier failure, it is the applicable bounded failure-evidence endpoint after started-attempt closure and every reachable bounded measurement or evidence operation. No new scheduler work or network admission occurs after this endpoint.
2. **Pre-termination cleanup.** Process-profile prevention or complete event observation remains active, and the exact service PID remains the process under evaluation. Cleanup operations that require a live service may occur only here. Source, static-host, initial process-profile, measurement, timing, attempt, coverage, continuity, and cap values are frozen except for completion of a producing boundary already legitimately in progress under an existing bounded failure rule.
3. **Cleanup termination boundary.** Every captured started PID enters one universal exact-PID termination-authorization operation. When initial process-profile capture succeeded, that operation first performs the conditional final exact-PID profile validation and proves continuous priority/debugger/profiler prevention or observation through arbitration. When initial capture failed, no full final profile validation is required and no failed or null initial process field is replaced. Both paths then use the same exit/intent arbiter defined below. If the exact PID remains live at the arbiter boundary, the coordinator commits authorized cleanup-termination intent for it and immediately issues the defined graceful service stop. Conditional validation, arbiter liveness reconciliation, intent commit, and stop issuance are one serialized coordinator operation; no unrelated operation, unbounded wait, or acceptance work may occur between them. Priority/debugger/profiler prevention or complete event observation remains active until the intent is committed when initial capture established that continuity obligation.
4. **Post-termination cleanup.** Expected exit of the exact PID after authorized termination intent is cleanup behavior, not an unplanned `process_exited` failure. Exact-PID process-profile continuity no longer applies after the intent boundary, and no process-profile query is required after intent or expected exit. The coordinator performs bounded removal or verification of synthetic listeners, runtime directories, temporary extraction/build material, service state, and every acceptance-owned artifact required by the existing cleanup contract.
5. **Final cleanup endpoint.** Cleanup completes or reaches its bounded deadline. All non-report acceptance evidence freezes here, and the coordinator derives the final in-memory `result` and complete ordinal violation set. Cleanup may add applicable profile or cleanup violations but cannot rewrite evidence or availability already produced. Neither report is authoritative until the transactional publication contract in section 8.8 succeeds.

Conditional final exact-PID validation applies only after successful initial process-profile capture. It must prove that the same PID still exists and matches, priority remains `normal`, no managed or native debugger is attached, no managed or native profiler is attached, and prevention/observation evidence remains complete and internally consistent through the check. An out-of-profile value, unavailable query, event/sample contradiction, loss of prevention or observation, or inability to prove continuity makes `process_profile_invalid` mandatory exactly once. When the exact PID remains live, universal exit/intent arbitration and every remaining bounded cleanup obligation still proceed; the run remains `fail`, and later restoration or detachment does not forgive the defect. Final-validation failure alone adds no cleanup violation; one is added only when its independently defined cleanup obligation also fails. A process exit detected during final validation is pre-intent because intent has not yet committed.

One private, process-local exact-PID **exit/intent arbiter** serializes every process-exit callback and cleanup-intent attempt. No callback independently classifies an exit. Its closed internal state model is equivalent to `running_no_intent`, `pre_intent_exit_committed`, `termination_intent_committed`, and `post_intent_exit_committed`; the names and internal sequence are not reported. Every transition retains the exact process handle and exact PID, ignores a wrong-PID exit event for classification while failing the applicable identity rule, treats a duplicate exact-PID callback idempotently, and never accepts a replacement PID or restart.

At an intent attempt, the arbiter first reconciles every exit observation already delivered to it and then performs one zero-time exit/liveness check through the retained exact-process handle. An already committed exit or a signaled/exited retained handle commits pre-intent exit, commits no intent, and issues no stop to a nonexistent process. Otherwise, the arbiter commits authorized termination intent for the exact PID as its next internal sequence and immediately issues the graceful stop while still inside the same serialized coordinator operation.

The arbiter sequence is the binding classification order. An exit already committed before intent wins as pre-intent. A retained handle found signaled/exited during the intent-attempt check is pre-intent even when its callback is delivered later. When the retained handle is nonsignaled and no prior exit is committed, the intent commit wins the classification boundary; an exact-PID callback delivered after that commit is post-intent expected cleanup exit, including an exit occurring immediately after the liveness check or while stop issuance is completing. The contract does not reconstruct an unobservable sub-instruction kernel timestamp after applying this tie-break. Callback scheduling, thread scheduling, event-delivery delay, wall-clock precision, and logging order cannot change classification, and identical injected arbiter ordering must produce identical violations and report decisions.

Pre-intent exit is unplanned: `process_exited` is mandatory, `process_profile_invalid` is additionally mandatory when successful initial capture established a continuity obligation that can no longer be proved through intent, no retroactive intent or stop is permitted, and bounded remaining cleanup continues. When initial capture already failed, its existing `process_profile_invalid` still appears exactly once. `acceptance_cleanup_failed` is additionally mandatory only when an independent cleanup obligation fails. Post-intent exact-PID exit is expected cleanup behavior and independently adds none of `process_exited`, `pid_changed`, or `process_profile_invalid`; replacement or restart remains prohibited.

The defined service stop remains bounded by the existing five-second shutdown requirement. Failure to issue the graceful stop after intent commits, or missing that deadline, makes `acceptance_cleanup_failed` mandatory and the final result `fail`; committed intent remains committed, bounded post-intent cleanup proceeds, and a later exact-PID exit is not reclassified as pre-intent. This specification adds no forced-termination fallback. If the process cannot stop and an acceptance-owned cleanup obligation cannot complete, cleanup reaches its bounded deadline and reports `acceptance_cleanup_failed` without an unbounded wait. No restart or replacement process is accepted.

Cleanup never changes a `measurement_availability` flag from `true` to `false` and never changes a previously false flag to true merely because cleanup occurred. Cleanup after complete stable final-snapshot commit retains all nine flags true. Cleanup after start-barrier failure retains flags only through `readiness_reached`; cleanup after early steady-state failure retains flags only through `steady_state_started`; cleanup after final-snapshot failure retains `final_snapshot_window_started = true` and `final_snapshot_committed = false`; and cleanup after initial process-profile failure retains `process_started = true` and `readiness_reached = false`. Cleanup cannot fabricate or complete a failed acceptance boundary.

Already-produced source, static-host, initial process-profile, timing, sealed startup, steady-state measurement, attempt-accounting, workload-coverage, runtime-continuity, and cap values remain unchanged through cleanup. A cleanup-phase dynamic profile violation never rewrites initial process values to current, restored, intended, sentinel, or `null` values. Cleanup-related failure may only add independently applicable violations, make `result = fail`, and preserve correct unavailability for producing boundaries that never existed.

The 30-minute run is an R0-005 merge gate. A seven-day soak with zero false Critical alerts and no unplanned termination remains a later R0 release-exit/controlled-acceptance gate because R0-005 creates no alerts.

### 8.2 Merge-gate Windows acceptance-host profile

The merge-gate profile identifier is exactly `r0-005-windows11-x64/v1`. Its required static host profile is:

- Windows 11, OS build 26100 or later;
- x64 operating system;
- Release configuration;
- 4 through 32 logical processors, inclusive;
- at least 8,000,000,000 bytes total physical memory;
- AC power with battery saver disabled;
- Windows Balanced power scheme GUID `381b4222-f694-41f0-9685-ff5bb260df2e`;
- no concurrent repository build or test process during sampling.

A physical machine or dedicated virtual machine may be used, but virtualization status must be recorded. Shared or CPU-quota-limited runners are not valid native merge-gate hosts.

Before starting the agent, the sampler obtains and validates every required static host field and runs a 60-monotonic-second host preflight. It collects exactly 12 samples at the fixed 5-, 10-, ..., 60-second boundaries. The average non-agent system CPU across those samples must be less than 10.0%, and available physical memory at the final sample must be at least 2,000,000,000 bytes. A missing preflight sample fails. An unsupported or unavailable static requirement adds `host_profile_unsupported`; a failed idle/resource preflight adds `host_profile_preflight_failed`. When all static fields and the complete idle preflight are valid, `host_profile_complete` becomes true before process creation; it says nothing about the eight process-specific fields.

The static fields controlled by `host_profile_complete` are exactly `profile_id`, `os_product`, `os_version`, `os_build`, `os_revision`, `os_architecture`, `logical_processor_count`, `total_physical_memory_bytes`, `processor_vendor`, `processor_family`, `processor_model`, `processor_stepping`, `virtualization`, `power_source`, `power_scheme_guid`, `battery_saver_enabled`, `idle_preflight_average_cpu_percent`, and `idle_preflight_available_memory_bytes`. They are `null` before successful static completion and retain their completed values through every later failure. Processor vendor, family, model, and stepping are bounded hardware-class metadata, not arbitrary environment dumps.

The process-specific fields are exactly `process_architecture`, `framework_description`, `runtime_version`, `runtime_identifier`, `gc_mode`, `process_priority`, `debugger_attached`, and `profiler_attached`. They remain in the fixed `host_profile` object and its existing property order, but their producing boundary is successful initial exact-PID process-profile capture after `process_started`; no additional availability flag is added. Before that boundary they are `null`. Independently unavailable fields remain `null` on capture failure, valid captured fields retain their initial values after every later failure, and a later dynamic defect adds `process_profile_invalid` without clearing or rewriting the initial snapshot.

The report must never include machine name, username, domain membership, serial number, BIOS serial, device UUID, Windows product key, MAC address, IP address, disk serial, cloud instance ID, arbitrary environment-variable value, process command line, executable or module path, debugger identity, profiler identity or CLSID, diagnostic endpoint, process token, security descriptor, event-source payload, or account/user identity.

The 30-minute result is evidence for the exact authenticated one-file acceptance artifact on the recorded profile; it is not a universal performance claim for every Windows computer. Another architecture or materially different acceptance profile requires a separate reviewed profile and resource report. Exact bounded OS, runtime, build, and hardware-class metadata remains in the evidence so later runs can be compared.

### 8.3 Clock and sampling

Elapsed time and sampling deadlines use a monotonic clock. Pre-steady-state initialization/barrier sampling may use a separately reported fixed-rate grid anchored at initialization start, immediately before process launch; those samples remain subject to the absolute working-set cap but do not define the steady-state boundary, CPU baseline, disk baseline, complete-minute windows, or acceptance accounting reset. `steady_state_start` is the baseline-bundle commit itself. The steady-state fixed-rate grid is re-anchored there: the first steady-state sample boundary is exactly `steady_state_start + 5 seconds`, and every later boundary is an exact five-second multiple from `steady_state_start`. `steady_state_end` is exactly `steady_state_start + requested_steady_state_duration`. UTC wall-clock timestamps may be recorded for correlation, but no calculation uses wall-clock elapsed time.

The sampler uses fixed-rate monotonic schedules with one sample every five seconds and no catch-up burst, continuing through stable final-snapshot commit or the failing final-snapshot deadline. `valid_sample_count` and `missing_sample_count` cover every required process sample after process creation through that endpoint, across the separately anchored pre-steady-state and steady-state schedules without duplicating a sample. A sample is missing when no valid sample is collected within ten seconds of its scheduled deadline or when any complete steady-state minute contains fewer than 11 valid samples. Any missing sample fails the run. Only samples at steady-state boundaries derived from `steady_state_start` and within the 30-minute half-open steady-state interval participate in complete-minute growth windows; end-drain and final-snapshot samples enforce absolute caps and process identity but do not enter those medians.

At each valid sample, the sampler refreshes the exact service process and records:

- process total processor time;
- working set in bytes;
- private bytes in bytes;
- handle count;
- thread count;
- monotonic sample time and bounded UTC correlation time.

At that same sample boundary, the coordinator also reconciles the current monitoring-ready level, scheduler active/enabled/disposed state, scheduler epoch continuity, accepted configuration/manifest ownership, due-round ledger, exact PID, process priority, managed/native debugger state, managed/native profiler state, and the continuous prevention/observation mechanism through the sample time. The initial process-profile snapshot remains unchanged in the report; a later priority/debugger/profiler or continuity defect is represented by `process_profile_invalid`. These checks feed only the fixed report fields, four derived `runtime_continuity` Booleans, and bounded violations; no transition timestamp, event payload, debugger/profiler identity, diagnostic endpoint, scheduler identity, route/interface value, target/provider identity, or private configuration value is added to the report.

### 8.4 CPU

`logical_processor_count = Environment.ProcessorCount` is captured once at run start. For consecutive valid process samples, with processor time and monotonic wall time expressed in the same unit:

```text
normalized_cpu_percent =
  100 × delta_process_total_processor_time
      / (delta_monotonic_wall_time × logical_processor_count)
```

Negative processor-time deltas, zero or negative wall-time deltas, PID changes, and counter resets fail the run. Only after raw inputs have been validated may a reported sampled percentage be bounded to 0–100.

The baseline bundle captures one process-CPU value immediately before its monotonic commit. Startup CPU attribution ends at that value, and steady-state average CPU uses the process-CPU delta from that identical committed baseline through stable final-snapshot commit, or through the failing final-snapshot deadline when no commit occurs, with the same logical-processor normalization. No CPU interval between drain completion and `steady_state_start` exists outside those phases because the commit is the boundary. The denominator remains the exact requested 30 steady-state minutes; neither drain nor final-snapshot duration extends it. The pass condition is `steady_state_average_cpu_percent < 2.0`. Startup observation duration, startup total CPU time, and startup maximum sampled normalized CPU are reported separately; a startup peak neither replaces nor relaxes the steady-state rule.

### 8.5 Memory, handles, threads, and growth

Every valid working-set sample after process creation through stable final-snapshot commit or its failing deadline must be less than 150,000,000 bytes. Reaching or exceeding that decimal-byte cap fails independently of growth calculations.

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

Published binaries, immutable input configuration, and sampler-owned evidence/report files must remain outside the monitored runtime directory. The process-start snapshot, the snapshot committed in the steady-state baseline bundle, and the final snapshot recursively enumerate every file beneath that directory. A canonical snapshot map contains only a normalized bounded relative path and a nonnegative integer file length for every runtime file, with entries ordinal-sorted by normalized relative path. No absolute path enters a report. Enumeration or normalization must not skip or filter a runtime file; a path that escapes the monitored directory, including through traversal or a reparse target, fails rather than being followed or omitted.

The steady-state-start baseline and final post-drain snapshot use one shared **stable runtime-file snapshot protocol**. One bounded stability attempt:

1. captures one complete recursive canonical map `A`;
2. immediately captures a second complete recursive canonical map `B`;
3. normalizes and ordinal-sorts both complete maps using the same bounded relative-path rules;
4. requires identical complete path sets and an identical recorded file length for every path;
5. commits `B` as the stable snapshot when the maps are identical; and
6. otherwise discards both maps and retries only within the applicable bounded snapshot window.

The protocol uses the same enumeration, canonicalization, equality, error, and privacy rules at both boundaries. An unreadable file, incomplete enumeration, invalid or negative length, arithmetic overflow, disappearing file during a required comparison, traversal/reparse escape, or absolute-path exposure cannot produce a committed map and retains the bounded failure handling below. No retry may exceed the applicable deadline, and no unbounded retry or sleep is permitted. Same-size rewrites need not be detected because this evidence intentionally measures persistent file-size growth rather than content mutation. Wall-clock timestamps, file-content hashing, ETW, Process Monitor, filesystem drivers, administrator-only monitoring, and new dependencies are prohibited.

The steady-state-start snapshot is committed atomically with the CPU baseline, accounting reset, and monotonic `steady_state_start`. Its stable protocol retries only within the existing single ten-second start barrier. The final protocol begins immediately after end-drain completion, keeps admissions paused, and retries only within its separate ten-second window. Failure to prove either stable snapshot within its applicable window fails rather than creating an ambiguous baseline. The same calculation applies to the process-start/steady-state-start pair for `startup.attributed_disk_bytes` and the steady-state-start/stable-final pair for `steady_state.attributed_disk_bytes`:

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

The bounded violations are `runtime_file_disappeared` for a missing baseline file, `runtime_file_scope_violation` for directory escape, `runtime_file_snapshot_incomplete` for unreadable, incompletely enumerated, or unstable snapshot data, and `runtime_file_snapshot_invalid` for an invalid length or arithmetic overflow. Exhausting either stable-snapshot window also adds `measurement_boundary_timeout`. The generic counter-reset, counter-unavailable, counter-negative, and counter-overflow rules remain applicable to independently specified process CPU, sampling, and network-accounting inputs, but they are not disk-attribution rules.

For the required run, `steady_state_minutes = 30`. The pass condition is `disk_mb_per_day < 100`, using decimal MB/day.

R0-005 intentionally persists no probe evidence. Any unexpected runtime artifact creation must be reported only by bounded relative artifact name/category, never by private absolute path; disk growth attributable to probe results fails even if the aggregate cap would otherwise pass.

### 8.7 Network attribution

System-wide adapter counters, packet capture, payload capture, addresses, interface values, and endpoint values are prohibited. The implementation exposes acceptance-only aggregate accounting through its injected probe transport boundaries.

For every started attempt, accounting records the probe kind, the stable internal workload-member identity required for manifest reconciliation, measured application bytes sent, measured application bytes received, and `charged_method_bytes` derived exclusively from the closed exact table in section 4.4. The member identity is used only for deterministic internal routing, attempt closure, and distinct coverage; it never enters either report. No payload or endpoint value is retained. Measured application bytes are exact byte counts crossing the injected transport boundary where available. For a recognized method, the charged value is exactly 128 for Gateway ICMP, 1,024 for DNS, 1,024 for TCP connect, or 8,192 for an HTTPS provider request. It is never supplied or overridden by a transport adapter, configuration, measurement, or platform-specific uplift.

Concurrent HTTPS provider children are never coalesced. Each provider invocation has its own internal round/provider member identity, token consumption, transport-start timestamp, sent/received/charged byte values, outcome, phase membership, and coverage attribution. The reports aggregate those records without exposing provider IDs or endpoint values; concurrency changes neither the per-attempt formula, per-provider coverage proof, nor completeness requirements.

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

The aggregate pass condition is `network_bytes_per_minute < 100,000`, using decimal bytes/minute. Independently, every started attempt must have passed section 4.4's hard rolling admission and the complete internal records must prove that every rolling monotonic 60-second window contains at most 60 starts and less than 100,000 fixed charged bytes. A lower 30-minute average cannot cure `rolling_attempt_budget_exceeded`, `rolling_network_budget_exceeded`, or inability to prove either invariant. Bytes from a steady-state attempt are included in full even when it completes during end drain; bytes are never split between phases. Different adapters that report identical measured sent/received counts must produce byte-identical attributed totals and reports. This is deterministic conservative probe-traffic attribution, not a claim of exact physical-wire or TLS/IP overhead measurement. Attempt counts are reported separately and packet capture remains prohibited.

An unknown probe method has no table charge and can never pass acceptance. To keep the failing report and aggregate totals complete without inventing a charge, its internal accounting record uses the contract-defined zero sentinel for `charged_method_bytes`, includes its nonnegative measured sent plus received bytes in `attributed_network_bytes`, counts once under `attempts.by_probe_kind["unknown"]`, and adds `unknown_probe_method`. The zero sentinel is not a recognized charge and is not configurable or adapter-supplied. Missing, malformed, negative, nonintegral, adapter-supplied, overridden, or conflicting charged values for a recognized method add `attempt_accounting_incomplete` and fail; the report builder still derives the exact table value for deterministic totals. Counter overflow, negative or missing measured bytes, missing accounting for a started attempt, or any unknown method also fails. Human and JSON reports are rendered from the same derived charge table, attempt records, and totals.

### 8.8 Deterministic reports

The sampler emits a bounded human-readable report and UTF-8 JSON from the same in-memory report model under ignored `artifacts/r0-005/`. The JSON schema identifier is `r0-005-resource-report/v1`. It uses invariant-culture numeric formatting, ISO-8601 UTC timestamps, fixed property names, stable enum/code values, deterministic array ordering, and no NaN or infinity.

A provisional in-memory measurement model may exist before cleanup, but no provisional or staged file is authoritative. The final cleanup endpoint freezes all non-report evidence and supplies cleanup violations; the coordinator then derives one final in-memory report model from which both final reports are generated. Cleanup evidence adds no process details, event payloads, private artifact identities, or cleanup paths.

Each sampler invocation privately selects and retains one fresh current-run final report destination under the ignored acceptance-artifact root. That destination must not already exist, is distinct from every previous invocation's report destination, is never derived from customer, endpoint, tenant, property, target, provider, username, machine, account, or host identity, and is written into neither report. Previous-run report sets are historical artifacts only and are never relabeled, returned, linked, or presented as the current invocation's output. A current invocation that exits without successfully publishing its own final set has no authoritative current-run report.

JSON and human reports are generated from the same final in-memory model into one fresh private staging directory on the same filesystem volume as the retained final destination. Staging names and locations cannot be mistaken for final output, and neither staged file is authoritative. Both files must be completely written, flushed, closed, reread, and validated before publication. JSON is byte-validated against deterministic serialization; the human report is validated against the same result, ordinal violations, boundary-null decisions, retained evidence, and calculations. The staged directory must contain exactly the authorized final JSON and human report files and no additional file.

The complete two-report set is published by one atomic same-volume directory rename from that staging directory to the previously absent current-run final destination. Authority begins only when that single directory publication succeeds. JSON and human reports must not use independent final renames, and the coordinator never overwrites, replaces, merges with, or appends to an existing final destination. An unexpected destination collision is a publication failure; the coordinator selects no alternate path that could conceal it and claims no existing set as current-run evidence.

Only after successful atomic set publication may the command identify the current-run set as authoritative, return success when the model's `result` is `pass`, or return the normal acceptance-failure exit code when the published model's `result` is `fail`. A successfully published fail set remains authoritative evidence for its failed run. Identical final report models and successful publication outcomes produce byte-identical JSON.

If serialization, a staging write or flush, reread, deterministic JSON validation, human/JSON agreement validation, or staged-set completeness fails before publication, `report_serialization_invalid` is added exactly once to the in-memory result when the model can still be represented, `result` becomes `fail`, and no passing set is published. The coordinator may make at most one corrected staging attempt from that updated failure model. If the corrected two-file set validates and publishes atomically, it is the authoritative failed report. The coordinator never loops indefinitely. If that single corrected failure-set attempt cannot be produced, terminal publication-failure behavior applies.

If the atomic directory publication fails, including because the retained destination unexpectedly exists, `report_serialization_invalid` is the mandatory bounded classification, command exit is nonzero, no current-run report is authoritative, and the absent final destination remains absent unless unknown colliding content already occupied it. Staging and provisional files remain explicitly non-authoritative; no prior passing report is returned as current, and no partial JSON-only or human-only authoritative set exists. Because publication itself failed, the violation may exist only in the retained in-memory terminal result and one bounded console/status code containing `report_serialization_invalid`; the contract does not require a failed publication to publish a report proving its own failure.

After publication failure, the coordinator attempts bounded cleanup of acceptance-owned staging and provisional material. Failure to remove that material independently adds `acceptance_cleanup_failed` to the retained in-memory terminal classification, while `report_serialization_invalid` remains mandatory; neither failure makes staging authoritative, and neither permits unbounded retry. After successful publication, the staging source no longer exists because the directory itself was renamed, and no duplicate final set remains.

Report/result lifecycle is therefore ordered: final cleanup freezes non-report evidence; the final in-memory model is derived; serialization, staging, validation, and atomic-set publication run; and current-run report authority begins only upon successful publication. Publication failure is an out-of-band terminal report failure with nonzero exit, bounded `report_serialization_invalid`, and no authoritative current-run report. It changes only final acceptance result, applicable report/cleanup classifications, command exit status, and report authority; it never clears or promotes a measurement flag or rewrites measured evidence.

Publication paths, staging paths, filenames, directory identities, handles, filesystem or operating-system messages, exception text, account identity, and host identity enter neither report nor bounded console/status output.

The JSON has these exact top-level properties in this order:

1. `schema_version`
2. `source`
3. `configuration`
4. `host_profile`
5. `measurement_availability`
6. `timing`
7. `startup`
8. `steady_state`
9. `growth`
10. `attempts`
11. `workload_coverage`
12. `runtime_continuity`
13. `caps`
14. `violations`
15. `result`

`schema_version` is exactly `r0-005-resource-report/v1`. Required nested properties are:

| Object | Fixed properties |
| --- | --- |
| `source` | `commit_sha`, `configuration_name`, `executable_sha256` |
| `configuration` | `target_count`, `https_provider_count`, `logical_processor_count`, `sampling_interval_seconds`, `requested_steady_state_minutes` |
| `host_profile` | `profile_id`, `os_product`, `os_version`, `os_build`, `os_revision`, `os_architecture`, `process_architecture`, `framework_description`, `runtime_version`, `runtime_identifier`, `gc_mode`, `logical_processor_count`, `total_physical_memory_bytes`, `processor_vendor`, `processor_family`, `processor_model`, `processor_stepping`, `virtualization`, `power_source`, `power_scheme_guid`, `battery_saver_enabled`, `process_priority`, `debugger_attached`, `profiler_attached`, `idle_preflight_average_cpu_percent`, `idle_preflight_available_memory_bytes` |
| `measurement_availability` | `configuration_accepted`, `host_profile_complete`, `process_started`, `readiness_reached`, `steady_state_started`, `steady_state_ended`, `end_drain_finished`, `final_snapshot_window_started`, `final_snapshot_committed` |
| `timing` | `process_start_utc`, `readiness_utc`, `startup_elapsed_ms`, `steady_state_start_barrier_elapsed_ms`, `steady_state_start_utc`, `steady_state_end_utc`, `steady_state_end_drain_elapsed_ms`, `final_runtime_snapshot_elapsed_ms`, `valid_sample_count`, `missing_sample_count` |
| `startup` | `cpu_observation_duration_ms`, `total_cpu_ms`, `maximum_normalized_cpu_percent`, `maximum_working_set_bytes`, `attributed_disk_bytes`, `attributed_network_bytes`, `attempts_started`, `attempts_completed`, `attempts_succeeded`, `attempts_failed`, `attempts_timed_out`, `attempts_canceled`, `attempts_incomplete` |
| `steady_state` | `average_normalized_cpu_percent`, `maximum_working_set_bytes`, `maximum_private_bytes`, `maximum_handle_count`, `maximum_thread_count`, `attributed_disk_bytes`, `disk_mb_per_day`, `attributed_network_bytes`, `network_bytes_per_minute` |
| `growth` | `working_set`, `private_bytes`, `handles`, `threads` |
| `attempts` | `by_probe_kind`, `by_outcome`, `started`, `completed`, `succeeded`, `failed`, `timed_out`, `canceled`, `incomplete` |
| `workload_coverage` | `gateway_required`, `gateway_exercised`, `dns_required`, `dns_exercised`, `https_providers_configured`, `https_providers_exercised`, `tcp_targets_configured`, `tcp_targets_exercised`, `all_required_exercised` |
| `runtime_continuity` | `monitoring_ready_continuous`, `scheduler_active_continuous`, `scheduler_epoch_unchanged`, `all_due_rounds_reconciled` |
| `caps` | `readiness_ms`, `working_set_bytes`, `average_cpu_percent`, `disk_mb_per_day`, `network_bytes_per_minute`, `growth_percent`, `working_set_growth_noise_bytes`, `private_bytes_growth_noise_bytes`, `handle_growth_noise_count`, `thread_growth_noise_count` |
| `result` | the string `pass` or `fail` |

`source` is the fixed three-property provenance object immediately after `schema_version`. Its properties remain in exact order: `commit_sha`, `configuration_name`, `executable_sha256`. Each property is produced and validated independently during the coordinator-controlled source-capture build before any later acceptance stage:

- `source.commit_sha` is a JSON string of exactly 40 lowercase ASCII hexadecimal characters matching `[0-9a-f]{40}`. It is the exact full reviewed commit independently resolved, materialized, tree-verified, and built by the coordinator. It is not obtained solely from the artifact. The supplied reviewed SHA, materialized commit/root tree, and immutable commit metadata embedded in the resulting artifact must agree exactly. It is never derived from the developer working directory, a mutable checkout, an environment variable, a branch or symbolic ref, or a shortened SHA; Git operations used by the coordinator apply only to resolving and verifying the supplied full SHA and exact isolated tree. Uppercase hexadecimal, an abbreviated or all-zero SHA, prefixes, suffixes, whitespace, a branch or symbolic ref, malformed text, a different tree embedding the expected value, and any three-way mismatch are invalid and must not be silently normalized.
- `source.configuration_name` is a non-null JSON string exactly equal to the invariant ASCII contract identifier `r0-005-25-target-acceptance/v1` whenever source capture succeeds. It is not a user-supplied display name, filename, or path and is not derived from a machine, user, property, tenant, target, provider, environment variable, or local directory. Any other, empty, or arbitrary value is invalid and must not be normalized or accepted under a generic length bound. Counts and enabled members remain represented by the accepted configuration and immutable workload manifest; this constant identifies the approved acceptance profile, not a customer configuration.
- `source.executable_sha256` is a JSON string of exactly 64 lowercase ASCII hexadecimal characters matching `[0-9a-f]{64}`. It is the SHA-256 digest of the complete exact one-file Windows x64 self-contained native-acceptance launch artifact produced from the proven exact tree in that same source-capture run, including the complete application-private runnable closure. It becomes available only after exact-tree provenance, controlled restore/publish, frozen dependency-graph use, private-closure validation, secure open/hash, and enforceable launch binding succeed. The sampler reads every byte through the retained secure handle, keeps the binding through launch, launches that same bound artifact, and proves the process image corresponds to it rather than a supplied, preexisting, adjacent, copied, subsequently replaced, similarly named, framework-dependent, or multi-file shape. The absolute path, relative path, filename, file handle, directory, assembly name, module identity, extraction location, and every private path component remain internal and enter neither report. Uppercase text, an all-zero, partial, stale, adjacent-file, or substituted-file digest, separators, prefixes, whitespace, malformed length, an incomplete private closure, an external private-code load, or any digest/value, bound-artifact, or launched-image mismatch is invalid and must not be silently normalized.

The valid full independently proven reviewed commit SHA, fixed acceptance-profile identifier, and exact complete single-artifact SHA-256 digest are the only permitted non-null `source` values. Exact-tree provenance proves where the artifact came from; the digest proves the exact accepted bytes. Neither substitutes for the other. Source capture is report provenance rather than a measurement boundary, so the existing `measurement_availability` object remains exactly nine Booleans.

The table is normative for nested property presence and property order. Every top-level object and fixed nested property remains present in both success and failure reports. The only representation for a required value whose producing boundary never existed is JSON `null`; an implementation must not use zero, an empty string or array, an epoch or copied/planned timestamp, `false`, a negative sentinel, NaN, infinity, or property omission to mean unavailable. A measured zero remains numeric zero only when its measurement domain existed, accounting completed, and zero is the genuine result. In the human report the exact rendering for a JSON boundary-null value is `unavailable`.

`measurement_availability` has exactly this ordered shape, and every value is a derived JSON Boolean that is never `null`:

```json
"measurement_availability": {
  "configuration_accepted": true,
  "host_profile_complete": true,
  "process_started": true,
  "readiness_reached": true,
  "steady_state_started": true,
  "steady_state_ended": true,
  "end_drain_finished": true,
  "final_snapshot_window_started": true,
  "final_snapshot_committed": true
}
```

The flags have these exact meanings:

- `configuration_accepted`: atomic R0-005 startup configuration validation succeeded and the immutable acceptance manifest was frozen.
- `host_profile_complete`: every required static acceptance-host field was obtained and valid and the complete 60-second idle preflight succeeded; process-specific fields are not part of this boundary.
- `process_started`: the exact service process and PID were created and captured; process-profile validation is the next producing boundary.
- `readiness_reached`: after successful initial exact-PID process-profile capture, the first valid monitoring-ready observation was accepted.
- `steady_state_started`: the stable start snapshot and complete baseline bundle committed atomically.
- `steady_state_ended`: the exact requested steady-state duration was reached; a planned endpoint is not an actual endpoint.
- `end_drain_finished`: after `steady_state_ended`, the drain completed normally or reached its bounded terminal deadline and attempt accounting closed.
- `final_snapshot_window_started`: final-snapshot stabilization began after end-drain finish.
- `final_snapshot_committed`: one stable final runtime-file snapshot committed through the two-map protocol.

The following implications are mandatory:

```text
final_snapshot_committed
  => final_snapshot_window_started
  => end_drain_finished
  => steady_state_ended
  => steady_state_started
  => readiness_reached
  => process_started

process_started
  => configuration_accepted
  && host_profile_complete
```

Any contradictory flag combination fails serialization with `report_serialization_invalid`. A later failure, including cleanup or report-publication failure, never clears or promotes a boundary that was already reached. Once a non-null value has been produced, a later failure retains that measured value; it does not replace it with zero or `null`. When a required producing boundary fails, independently unavailable values are `null` and the causal violation remains mandatory. `report_serialization_invalid` is additionally mandatory when the matrix requires complete accounting after a reached boundary or when structure, type, order, format, contradiction, sentinel use, retention, pass validity, human/JSON agreement, staged-set validation, destination freshness, or atomic publication fails; a structurally valid source- or process-profile production failure does not add it merely because correct independent nulls exist. An authoritative passing report requires successful bounded cleanup and successful atomic set publication; all three source properties to be non-null, valid, and unchanged; the supplied reviewed SHA, independently materialized commit/root tree, and artifact metadata to match; the executable digest to match the coordinator-built complete exact securely bound single artifact launched; the build-input provenance, required publish shape, extraction integrity, launched-process image, runtime closure, and dynamic process profile to remain valid through committed cleanup-termination intent; the configuration name to equal `r0-005-25-target-acceptance/v1`; all nine flags to be `true`; no `null` acceptance field; and every existing availability, cap, measurement, identity, coverage, continuity, privacy, and violation rule to pass.

The following matrix is exhaustive for every boundary-dependent fixed property and for the fixed report-model constants in `caps`. “Null before boundary” means `null` is required when the controlling flag is false. Normal types apply only when non-null. Properties grouped in one row have identical rules; every grouped property is named explicitly.

| Property or exhaustive property group | Earliest producing boundary / controlling flag | Normal non-null type | Null rule | Later failure after production |
| --- | --- | --- | --- | --- |
| `source.commit_sha` | successful independent resolution/materialization/verification of the supplied reviewed commit and exact root tree during coordinator-controlled source capture; no measurement-availability flag | exact 40-character lowercase ASCII hexadecimal JSON string matching `[0-9a-f]{40}` | null when the exact reviewed commit/tree cannot be independently established; artifact metadata alone is insufficient and no empty, all-zero, branch, abbreviated, sentinel, current-checkout, or runtime-derived substitute is permitted | retain the exact independently proven value after artifact-production, hashing, metadata-cross-check, configuration, static host, process, readiness, steady-state, drain, snapshot, cleanup, or reporting failure |
| `source.configuration_name` | report-model/source-profile construction during source capture; no measurement-availability flag | exact JSON string `r0-005-25-target-acceptance/v1` | null only when report-model/source-profile construction itself cannot establish the fixed constant; arbitrary, empty, path-derived, machine-derived, operator-derived, or customer-derived values are forbidden | retain the exact constant through every later failure |
| `source.executable_sha256` | successful exact-tree provenance, coordinator-controlled restore/publish, frozen graph, one-file closure validation, complete-artifact digest, secure binding, and validation that launch/extraction/runtime-closure identity can be enforced; no measurement-availability flag | exact 64-character lowercase ASCII hexadecimal JSON string matching `[0-9a-f]{64}` for the complete single artifact | null when exact-tree provenance or controlled production did not succeed, the one-file private closure cannot be proved, the artifact cannot be selected/opened/completely read/retained/hashed, or launch/extraction/runtime-closure verification cannot be established; no prebuilt, empty, all-zero, partial, stale, adjacent-file, estimated, multi-file, framework-dependent, or substituted digest | retain the exact validated digest through embedded-metadata mismatch, process-start failure, and every later failure; a metadata, launched-image, extraction, or external-load defect adds `source_identity_invalid` without replacing independently proven values |
| `configuration.target_count`, `configuration.https_provider_count`, `configuration.logical_processor_count`, `configuration.sampling_interval_seconds`, `configuration.requested_steady_state_minutes` | `configuration_accepted` | nonnegative JSON integer | null before boundary | retain accepted value |
| `host_profile.profile_id`, `host_profile.os_product`, `host_profile.os_version`, `host_profile.os_architecture`, `host_profile.processor_vendor`, `host_profile.virtualization`, `host_profile.power_source`, `host_profile.power_scheme_guid` | `host_profile_complete` | bounded JSON string | null before boundary | retain completed static value |
| `host_profile.os_build`, `host_profile.os_revision`, `host_profile.logical_processor_count`, `host_profile.total_physical_memory_bytes`, `host_profile.processor_family`, `host_profile.processor_model`, `host_profile.processor_stepping`, `host_profile.idle_preflight_available_memory_bytes` | `host_profile_complete` | nonnegative JSON integer | null before boundary | retain completed value |
| `host_profile.battery_saver_enabled` | `host_profile_complete` | JSON Boolean | null before boundary; `false` is a measured value, never unavailable | retain completed static value |
| `host_profile.idle_preflight_average_cpu_percent` | `host_profile_complete` | finite JSON number | null before boundary | retain completed value |
| `host_profile.process_architecture`, `host_profile.framework_description`, `host_profile.runtime_version`, `host_profile.runtime_identifier`, `host_profile.gc_mode`, `host_profile.process_priority` | successful initial exact-PID process-profile capture after `process_started`; no additional availability flag | bounded JSON string from the exact process, with priority from the closed vocabulary | null before successful capture and independently null when that field cannot be obtained or validated; a readable bounded out-of-profile value is retained, not replaced by null | retain the initial captured value after readiness, scheduler, priority, debugger, profiler, PID, steady-state, drain, snapshot, or cleanup failure |
| `host_profile.debugger_attached`, `host_profile.profiler_attached` | successful initial exact-PID process-profile capture after `process_started`; no additional availability flag | JSON Boolean from the exact process | null before successful capture and independently null when state cannot be obtained or validated; `true` is an actual measured failure, never unavailable | retain the initial captured value after every later dynamic process-profile failure |
| every named `measurement_availability` property | report model creation | JSON Boolean | never null | retain derived value |
| `timing.process_start_utc` | `process_started` | ISO-8601 UTC string | null before boundary | retain actual timestamp |
| `timing.readiness_utc` | `readiness_reached` | ISO-8601 UTC string | null before boundary | retain actual timestamp |
| `timing.startup_elapsed_ms` | `process_started` | nonnegative JSON integer | null before boundary; after process start use actual elapsed time to readiness or the terminal startup failure endpoint | retain measured duration |
| `timing.steady_state_start_barrier_elapsed_ms` | `readiness_reached` | nonnegative JSON integer | null before boundary; after readiness use actual elapsed time to baseline commit or the bounded barrier failure endpoint | retain measured duration |
| `timing.steady_state_start_utc` | `steady_state_started` | ISO-8601 UTC string | null before boundary | retain actual commit timestamp |
| `timing.steady_state_end_utc` | `steady_state_ended` | ISO-8601 UTC string | null before boundary; a planned end is forbidden | retain actual endpoint timestamp |
| `timing.steady_state_end_drain_elapsed_ms` | `end_drain_finished` | nonnegative JSON integer | null before boundary and therefore always null unless `steady_state_ended` is also true | retain duration through closure or deadline |
| `timing.final_runtime_snapshot_elapsed_ms` | `final_snapshot_window_started` | nonnegative JSON integer | null before boundary | retain duration through commit or failing deadline |
| `timing.valid_sample_count`, `timing.missing_sample_count` | `process_started` | nonnegative JSON integer | null before boundary; zero requires initialized, closed sample accounting | retain completely reconciled count |
| `startup.cpu_observation_duration_ms`, `startup.total_cpu_ms`, `startup.maximum_normalized_cpu_percent`, `startup.maximum_working_set_bytes` | `process_started` | nonnegative integer except finite-number CPU percent | null before boundary | retain measured value through failure endpoint |
| `startup.attributed_disk_bytes`, `startup.attributed_network_bytes`, `startup.attempts_started`, `startup.attempts_completed`, `startup.attempts_succeeded`, `startup.attempts_failed`, `startup.attempts_timed_out`, `startup.attempts_canceled`, `startup.attempts_incomplete` | `steady_state_started` | nonnegative JSON integer | null before boundary; zero requires the startup domain to be sealed and completely reconciled | retain sealed startup value |
| `steady_state.average_normalized_cpu_percent`, `steady_state.maximum_working_set_bytes`, `steady_state.maximum_private_bytes`, `steady_state.maximum_handle_count`, `steady_state.maximum_thread_count` | `steady_state_ended` | nonnegative integer except finite-number CPU percent | null before boundary | retain full-interval value |
| `steady_state.attributed_network_bytes`, `steady_state.network_bytes_per_minute` | `end_drain_finished` | nonnegative integer / finite number | null before boundary | retain completely reconciled value |
| `steady_state.attributed_disk_bytes`, `steady_state.disk_mb_per_day` | `final_snapshot_committed` | nonnegative integer / finite number | null before boundary; no ambiguous snapshot value | retain committed-snapshot value |
| `growth.working_set.first_window_median`, `growth.working_set.final_window_median`, `growth.working_set.absolute_increase`, `growth.working_set.percent_increase`, `growth.working_set.result` | `steady_state_ended` | finite number or `pass`/`fail` string | null before boundary; `percent_increase` also uses the existing zero-to-positive mathematical-null rule | retain full-window result |
| `growth.private_bytes.first_window_median`, `growth.private_bytes.final_window_median`, `growth.private_bytes.absolute_increase`, `growth.private_bytes.percent_increase`, `growth.private_bytes.result` | `steady_state_ended` | finite number or `pass`/`fail` string | same rule as `working_set` | retain full-window result |
| `growth.handles.first_window_median`, `growth.handles.final_window_median`, `growth.handles.absolute_increase`, `growth.handles.percent_increase`, `growth.handles.result` | `steady_state_ended` | finite number or `pass`/`fail` string | same rule as `working_set` | retain full-window result |
| `growth.threads.first_window_median`, `growth.threads.final_window_median`, `growth.threads.absolute_increase`, `growth.threads.percent_increase`, `growth.threads.result` | `steady_state_ended` | finite number or `pass`/`fail` string | same rule as `working_set` | retain full-window result |
| `growth.working_set.noise_floor`, `growth.private_bytes.noise_floor`, `growth.handles.noise_floor`, `growth.threads.noise_floor`, `growth.working_set.unit`, `growth.private_bytes.unit`, `growth.handles.unit`, `growth.threads.unit` | report model creation | nonnegative number / bounded string | never null; these are contract constants, not measurements | retain constant |
| `attempts.by_probe_kind`, `attempts.by_outcome`, `attempts.started`, `attempts.completed`, `attempts.succeeded`, `attempts.failed`, `attempts.timed_out`, `attempts.canceled`, `attempts.incomplete` | `end_drain_finished` | ordered arrays / nonnegative JSON integers | null before boundary; zero arrays/counts require initialized and completely reconciled full steady-state accounting | retain closed accounting |
| every `attempts.by_probe_kind[*].code`, `attempts.by_outcome[*].code`, `attempts.by_probe_kind[*].count`, `attempts.by_outcome[*].count` | `end_drain_finished` | closed-vocabulary string / nonnegative JSON integer | entries exist only when their containing array is non-null; entry properties are never null | retain closed accounting |
| `workload_coverage.gateway_required`, `workload_coverage.dns_required`, `workload_coverage.https_providers_configured`, `workload_coverage.tcp_targets_configured` | `configuration_accepted` | JSON Boolean / nonnegative JSON integer | null before boundary; accepted-configuration counts are never fabricated | retain manifest-derived value |
| `workload_coverage.gateway_exercised`, `workload_coverage.dns_exercised`, `workload_coverage.https_providers_exercised`, `workload_coverage.tcp_targets_exercised`, `workload_coverage.all_required_exercised` | `end_drain_finished` | JSON Boolean / nonnegative JSON integer | null before boundary; `false` or zero means complete calculation found no exercise, not unavailable | retain reconciled value |
| `runtime_continuity.monitoring_ready_continuous`, `runtime_continuity.scheduler_active_continuous`, `runtime_continuity.scheduler_epoch_unchanged`, `runtime_continuity.all_due_rounds_reconciled` | `readiness_reached` | JSON Boolean | null before boundary; `false` means the continuity interval existed and the calculation failed | retain false after later recovery |
| `caps.readiness_ms`, `caps.working_set_bytes`, `caps.network_bytes_per_minute`, `caps.working_set_growth_noise_bytes`, `caps.private_bytes_growth_noise_bytes`, `caps.handle_growth_noise_count`, `caps.thread_growth_noise_count` | `report model creation; no availability flag` | nonnegative JSON integer | never null; contract constant, not a measurement | retain exact constant |
| `caps.average_cpu_percent`, `caps.disk_mb_per_day`, `caps.growth_percent` | `report model creation; no availability flag` | finite nonnegative JSON number | never null; contract constant, not a measurement | retain exact constant |

When any source property cannot be validly produced, including when exact reviewed-tree provenance, controlled restore/publish, the frozen input graph, one-file publish shape, complete private closure, digest, secure binding, or pre-process ability to enforce launch/extraction/runtime identity cannot be established, the sampler emits a **source-capture failure** before configuration acceptance, static host preflight, process creation, or scheduler activation. The fixed `source` object and all three properties remain present in exact order; each independently proven property is serialized normally, only an unavailable property is JSON `null`, and each null source value is `unavailable` in the human report. If the exact reviewed tree cannot be independently established, `commit_sha` and `executable_sha256` are `null` and no valid artifact may be claimed. If exact-tree provenance succeeds but artifact production or hashing fails, `commit_sha` may remain non-null while `executable_sha256` is `null`. If provenance and hashing succeed but embedded metadata disagrees, retain both independently proven non-null values as applicable, add `source_identity_invalid`, and begin no later stage. `configuration_name` remains independently available under its existing rule. All nine flags remain `false`, every later boundary-dependent value remains `null`, all ten fixed `caps` properties remain non-null, `source_identity_invalid` appears exactly once in ordinal order, `result` is `fail`, and the sampler exits nonzero. A structurally valid source-capture failure with correct independent nulls does not add `report_serialization_invalid` merely because production failed. After successful source capture, a later process-image, extraction, or external-private-code continuity failure retains all independently validated source values unchanged while failing acceptance with `source_identity_invalid`.

For example, this object fragment is the normative independent-null representation when the exact reviewed tree cannot be independently established but report-model/source-profile construction establishes the fixed identifier:

```json
"source": {
  "commit_sha": null,
  "configuration_name": "r0-005-25-target-acceptance/v1",
  "executable_sha256": null
}
```

`caps` is always present, and all ten cap properties remain present and non-null in every success and failure report. The report model creates these contract constants independently of configuration acceptance, host-profile completion, process start, readiness, steady-state boundaries, end-drain completion, and final-snapshot completion; no additional availability Boolean controls them. A failure at any of those boundaries retains every exact cap constant.

An available cap is not evidence that its associated measurement exists. A cap must never replace an unavailable measurement with zero, the cap value, or another sentinel. A cap comparison is performed only when the associated measurement is non-null and its producing boundary exists. A report with unavailable measurements still contains all exact cap constants and remains `fail`. Missing, `null`, malformed, negative, nonfinite, reordered, duplicated, or altered cap constants add `report_serialization_invalid`; the exact property order, JSON types, values, and units remain those defined by this matrix and the cap-value table below.

`configuration.logical_processor_count` must exactly equal `host_profile.logical_processor_count` when both are non-null; `configuration.target_count` must equal the manifest's distinct TCP-member count; and `configuration.https_provider_count` must equal its distinct HTTPS-provider count. The three boundary timing durations are nonnegative JSON integers in milliseconds when available. `timing.steady_state_start_utc` is the UTC correlation value for the exact monotonic baseline-bundle commit; `steady_state_end_utc` is the actual exact requested-duration endpoint, never a planned substitute. `steady_state_end_drain_elapsed_ms` ends at terminal attempt closure or its deadline. `final_runtime_snapshot_elapsed_ms` starts at end-drain completion, is zero only when the complete two-map protocol commits at the same observable monotonic instant, and on failure records elapsed time through the final-snapshot deadline. The disk integers retain the existing snapshot-pair meanings, and `steady_state.disk_mb_per_day` is available only from the stable final pair. Startup attempt values satisfy the same identities as top-level attempt scalars when non-null. Queued work canceled before transport start appears in neither set of counts.

These source/flag examples are normative; every fixed property remains present and follows the matrix. In every scenario, `caps` and every cap property remain present and non-null with the exact contract values; the constants do not establish availability of any associated measurement. Except for source-capture failure, all three independently proven source properties remain present, non-null, valid, and unchanged. Static host values retain their completed values after `host_profile_complete`; initial process-profile values retain their exact captured values after every later failure. A scheduler exists only after initial process-profile success and the existing readiness-controlled activation succeeds.

| Scenario | First false boundary and required effect | Required result/evidence |
| --- | --- | --- |
| Source capture fails | one or more source properties are independently unavailable and null; all nine flags are false and every later value is null | `fail`; `source_identity_invalid`; human `unavailable` for each null; no configuration, static preflight, process, or scheduler |
| Configuration rejected before process start | `configuration_accepted = false`; all nine flags are false; configuration, static/process profile, process, readiness, steady-state, attempt, coverage, and continuity values are null | `fail`; bounded configuration violation; no scheduler |
| Static host/preflight failure before process creation | `configuration_accepted = true`, `host_profile_complete = false`, and all later flags are false; every static and process-specific host field and later measurement is null | `fail`; `host_profile_unsupported` or `host_profile_preflight_failed`; no process or scheduler |
| Process creation fails after static profile success | `configuration_accepted = host_profile_complete = true`, `process_started = false`, and later flags are false; static host values remain, all process-specific and later values are null | `fail`; no process or scheduler and every independently applicable bounded violation |
| Process starts, initial exact-PID profile capture fails, and PID is live at arbitration | flags through `process_started = true`, `readiness_reached = false`, and later flags are false; static values remain; successfully captured process fields retain bounded actual values and independently unavailable fields are null | `fail`; `process_profile_invalid`; no accepted readiness, scheduler, or workload; no full final profile validation; commit exact-PID intent, issue graceful stop, treat post-intent exit as expected, and perform bounded cleanup |
| Process starts, initial exact-PID profile capture fails, and PID exits before intent | the same reached flags, retained values, and nulls as the live-PID initial-profile failure | `fail`; `process_profile_invalid` and `process_exited` each exactly once; no full final profile validation, retroactive intent, or stop; bounded remaining cleanup |
| Initial process profile succeeds but readiness times out | flags through `process_started = true`, `readiness_reached = false`, and later flags are false; all static and initial process-profile values remain non-null; readiness/later fields and continuity values are null | `fail`; `readiness_timeout`; no accepted scheduler workload |
| Dynamic process-profile violation during initialization | flags through `process_started = true`, `readiness_reached = false`, and later flags are false; all initial profile values remain unchanged and later values are null | `fail`; `process_profile_invalid`; no scheduler; terminate and perform bounded cleanup; restoration/detachment is not forgiven |
| Readiness reached but start barrier fails | through `readiness_reached = true`; `steady_state_started = false`; barrier duration is measured, baseline-dependent values are null | `fail`; applicable boundary/snapshot violations |
| Dynamic process-profile violation during start barrier | flags through `readiness_reached = true`, `steady_state_started = false`, and later flags are false; source/static/initial process values remain; barrier evidence is retained | `fail`; `process_profile_invalid`; scheduler admissions stop permanently and only bounded started-attempt closure, evidence, and cleanup continue |
| Steady state starts but fails before exact end | through `steady_state_started = true`; `steady_state_ended = false`; full-interval, attempt, coverage, and end/final values are null | `fail`; causal bounded violations |
| Dynamic process-profile violation during steady state | flags through `steady_state_started = true`, `steady_state_ended = false`, and later flags are false; initial process fields remain unchanged | `fail`; `process_profile_invalid`; no recovery forgiveness, new admissions stop, and only bounded closure/evidence/cleanup continue |
| End drain reaches its terminal deadline | through `end_drain_finished = true`; `final_snapshot_window_started = true`; attempt accounting includes actual `incomplete` values | `fail`; `attempt_accounting_incomplete` and `measurement_boundary_timeout` |
| Dynamic process-profile violation during end drain | for the isolated injected defect, all nine flags become true after bounded drain closure and stable final snapshot; all initial process fields remain unchanged | `fail`; `process_profile_invalid`; no new work/restart/reactivation and exact bounded final evidence/cleanup retained |
| Final snapshot fails to stabilize | through `final_snapshot_window_started = true`; `final_snapshot_committed = false`; final elapsed is measured and disk values are null | `fail`; `runtime_file_snapshot_incomplete` and `measurement_boundary_timeout` |
| Dynamic process-profile violation during final-snapshot stabilization | for the isolated injected defect, all nine flags become true when the stable snapshot still commits; all initial process fields and completed measurements remain unchanged | `fail`; `process_profile_invalid`; no restoration/detachment forgiveness and no post-endpoint scheduler/network work; bounded cleanup still occurs |
| Dynamic process-profile violation during pre-termination cleanup after an otherwise complete run | stable final snapshot is already committed; all nine flags remain true; every completed source, static-host, initial process-profile, timing, measurement, attempt, coverage, continuity, and cap value remains non-null and unchanged; no new work or network admission occurs | `fail`; `process_profile_invalid`; authorized termination and bounded cleanup continue; expected post-intent exit does not add `process_exited`; `acceptance_cleanup_failed` and/or `synthetic_listener_cleanup_failed` appear only when their separate obligations fail |
| Exit during final validation or before the arbiter liveness check | preserve the exact earlier flag/value/null pattern; intent has not committed | `fail`; pre-intent `process_exited`; add `process_profile_invalid` when successful initial capture established continuity; no retroactive intent or stop; bounded remaining cleanup |
| Retained handle is signaled while callback delivery is delayed | preserve the exact earlier flag/value/null pattern; the zero-time handle check commits pre-intent exit | `fail`; the handle-signaled decision wins regardless of later callback delivery; no intent or stop |
| Exit immediately after a nonsignaled arbiter check or callback delivered after intent | preserve the exact earlier flag/value/null pattern; intent is the next committed arbiter sequence | expected post-intent exact-PID exit; no independent `process_exited`, `pid_changed`, or `process_profile_invalid`; stop and cleanup failures remain independently applicable |
| Report publication fails after otherwise successful measurement and cleanup | all nine flags remain true and every measured value remains unchanged; staging is non-authoritative | `fail`; `report_serialization_invalid`; nonzero exit; no authoritative current-run report; `acceptance_cleanup_failed` only if staging cleanup independently fails |
| Report publication fails after an earlier acceptance failure | preserve the earlier exact flag/value/null pattern and every earlier causal violation | add `report_serialization_invalid`; nonzero exit; no authoritative current-run report; no prior or staged set is current evidence |
| Complete successful run | all flags are true and no acceptance field is null | `pass` only when every other rule passes and the complete JSON/human set publishes atomically to the fresh current-run destination |

If the same cleanup-phase process-profile defect occurs after an earlier acceptance failure, the report preserves the exact flag, value, and `null` pattern reached by that earlier failure and adds `process_profile_invalid` exactly once. Cleanup does not promote later flags, finish a failed producing boundary, or create a measurement that did not exist.

Every scenario after source-capture failure assumes exact-tree source capture completed successfully. Configuration rejection, static host failure, process-start failure, initial or dynamic process-profile failure, readiness timeout, start-barrier failure, early steady-state failure, end-drain failure, final-snapshot failure, cleanup failure, and complete success therefore retain all three source properties present, non-null, valid, and byte-for-byte unchanged. No later failure may erase or replace one with `null`, an empty value, a different SHA, the current checkout SHA, a path-derived label, or another sentinel.

The human report uses the identical source commit SHA, configuration identifier, complete single-artifact digest, initial process-profile values, null/`unavailable` decisions, profile validation results, violations, retention behavior, availability flags, boundary/null decisions, baseline-bundle timestamp, CPU baseline, stable snapshot pairs, phase membership, exact method-charge derivation, rolling-invariant result, coverage reconciliation, runtime-continuity derivation, timing values, and totals. It adds no human-only source/build path, filename, assembly/module identity, extraction location, process command line, debugger/profiler identity, diagnostic endpoint, event payload, checkout, branch, machine, account, or environment detail.

When the sealed startup-attempt domain exists, it satisfies:

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

When `host_profile_complete` is true, the static `host_profile.profile_id` is exactly `r0-005-windows11-x64/v1` and the remaining static values use the representations below. The same table also defines the normal representations for the eight process-specific properties after their separate successful initial exact-PID capture; those properties may still be `null` while `host_profile_complete = true` and before that post-launch boundary.

| Property | Required representation |
| --- | --- |
| `os_product` | `windows_11` |
| `os_version` | bounded invariant Windows version string |
| `os_build` | JSON integer, at least 26100 |
| `os_revision` | nonnegative JSON integer |
| `os_architecture` | `x64`; static |
| `process_architecture` | `x64`; initial exact-PID process profile |
| `framework_description` | bounded invariant .NET framework-description string from the exact process |
| `runtime_version` | bounded invariant exact-process runtime-version string whose major version is 10 |
| `runtime_identifier` | bounded invariant exact-process runtime identifier for Windows x64 |
| `gc_mode` | exact-process value `workstation` or `server` |
| `logical_processor_count` | JSON integer from 4 through 32 |
| `total_physical_memory_bytes` | JSON integer, at least 8,000,000,000 |
| `processor_vendor` | bounded invariant ASCII CPU-vendor class, at most 32 characters |
| `processor_family`, `processor_model`, `processor_stepping` | nonnegative JSON integers |
| `virtualization` | `physical`, `virtual`, or `unknown` |
| `power_source` | `ac` |
| `power_scheme_guid` | lowercase `381b4222-f694-41f0-9685-ff5bb260df2e` |
| `battery_saver_enabled` | JSON Boolean; must be `false` |
| `process_priority` | closed exact-PID vocabulary `idle`, `below_normal`, `normal`, `above_normal`, `high`, or `real_time`; only `normal` passes |
| `debugger_attached`, `profiler_attached` | exact-PID JSON Booleans; both must be `false` to pass |
| `idle_preflight_average_cpu_percent` | finite JSON number; must be less than 10.0 |
| `idle_preflight_available_memory_bytes` | JSON integer; must be at least 2,000,000,000 |

All non-null host-profile strings are derived from the listed bounded OS/runtime/hardware/exact-process sources, not from arbitrary environment variables or intended configuration. Missing, malformed, or out-of-profile static values fail under `host_profile_unsupported` or `host_profile_preflight_failed`; initial or later process defects fail under `process_profile_invalid`. Both groups use the section 8.8 matrix rather than fabricated replacements. A malformed non-null process value independently adds `report_serialization_invalid`; a structurally valid unavailable initial field remains `null` without that automatic serialization violation.

The following synthetic fragment is normative for the top-level placement of `source` immediately after `schema_version`, the exact source-property order, and the adjacency and property order of `configuration`, `host_profile`, `measurement_availability`, `timing`, and `startup`; its concrete provenance and measurement values are illustrative and in a real run require coordinator-controlled exact reviewed-tree materialization/build, three-way commit identity, separate complete-artifact digest/binding, and successful post-launch process-profile capture:

```json
"schema_version": "r0-005-resource-report/v1",
"source": {
  "commit_sha": "0123456789abcdef0123456789abcdef01234567",
  "configuration_name": "r0-005-25-target-acceptance/v1",
  "executable_sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
},
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
"measurement_availability": {
  "configuration_accepted": true,
  "host_profile_complete": true,
  "process_started": true,
  "readiness_reached": true,
  "steady_state_started": true,
  "steady_state_ended": true,
  "end_drain_finished": true,
  "final_snapshot_window_started": true,
  "final_snapshot_committed": true
},
"timing": {
  "process_start_utc": "2026-01-01T00:00:00.0000000Z",
  "readiness_utc": "2026-01-01T00:00:01.0000000Z",
  "startup_elapsed_ms": 1000,
  "steady_state_start_barrier_elapsed_ms": 9000,
  "steady_state_start_utc": "2026-01-01T00:00:10.0000000Z",
  "steady_state_end_utc": "2026-01-01T00:30:10.0000000Z",
  "steady_state_end_drain_elapsed_ms": 0,
  "final_runtime_snapshot_elapsed_ms": 0,
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

When non-null after `end_drain_finished`, `attempts.by_probe_kind` counts steady-state transport invocations that actually started and contains exactly five entries in this order:

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

When non-null after `end_drain_finished`, `attempts.by_outcome` contains exactly five entries in this order:

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

When non-null after `end_drain_finished`, the scalar attempt properties have these exact definitions:

| Property | Definition |
| --- | --- |
| `started` | Total steady-state transport invocations whose transport-start instant is in the half-open membership interval. |
| `completed` | `succeeded + failed + timed_out + canceled`. |
| `succeeded` | Identical to the `by_outcome` entry `succeeded`. |
| `failed` | Identical to `by_outcome.failed`; excludes timeouts, cancellations, and incomplete attempts. |
| `timed_out` | Identical to `by_outcome.timed_out`. |
| `canceled` | Identical to `by_outcome.canceled`. |
| `incomplete` | Identical to `by_outcome.incomplete`. |

When `attempts` is non-null after `end_drain_finished`, the report must satisfy these exact identities:

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

`workload_coverage` is derived from the frozen manifest and terminal steady-state attempt records, never independently assigned. When non-null, Gateway and DNS values are JSON Booleans and provider/target values are nonnegative JSON integers. Its properties appear exactly in the table order above. After `configuration_accepted`, `gateway_required` is `true`, `dns_required` reflects the accepted DNS-enabled value, and configured counts equal the manifest's distinct category counts; before that boundary those four fields are `null`. After `end_drain_finished`, the corresponding exercised Boolean is `true` only when that required member meets section 8.1, disabled DNS requires both DNS flags to be `false`, exercised counts deduplicate exact internal member identity, and `all_required_exercised` is `true` if and only if every equality in section 8.1 holds, the Gateway logical-round/ICMP rule holds, no manifest identity or configuration defect exists, and every required member is distinctly exercised. Before complete end-drain accounting those five exercised fields are `null`, not false or zero.

The required passing two-provider, DNS-enabled, 25-TCP-target run has this ordered object shape:

```json
"workload_coverage": {
  "gateway_required": true,
  "gateway_exercised": true,
  "dns_required": true,
  "dns_exercised": true,
  "https_providers_configured": 2,
  "https_providers_exercised": 2,
  "tcp_targets_configured": 25,
  "tcp_targets_exercised": 25,
  "all_required_exercised": true
},
"runtime_continuity": {
  "monitoring_ready_continuous": true,
  "scheduler_active_continuous": true,
  "scheduler_epoch_unchanged": true,
  "all_due_rounds_reconciled": true
}
```

`runtime_continuity` is derived from the subscribed readiness-transition history, sampled readiness/scheduler/PID levels, scheduler-epoch and workload-ownership checks, immutable configuration/manifest checks, and the internal due-round ledger. When the continuity interval exists, its four values are JSON Booleans and appear exactly in the table order above. A required run can pass only when all four are `true`. A later recovery cannot change a derived `false` back to `true`. If first readiness is never observed, the continuity interval never exists and all four values are `null`, `readiness_timeout` remains mandatory, and `monitoring_readiness_lost` does not replace that pre-continuity failure. Scheduler epoch, target/member/provider identity, endpoint, route/interface value, transition timestamp detail, and private host value never enter this object or the human report.

The complete report fails deterministic serialization when `source`, `host_profile`, `measurement_availability`, `workload_coverage`, `runtime_continuity`, or `caps` is missing, reordered, malformed, contains an additional property, uses an invalid type, violates the exhaustive matrix or implication chain, conflicts with exact-tree source evidence, configuration, initial process-profile evidence, internal manifest, readiness, scheduler, PID, epoch, due-ledger reconciliation, rolling-invariant evidence, or caps, or disagrees with the human calculation. Staging completeness, byte validation, human/JSON agreement, fresh-destination validation, and atomic set publication are additional fail-closed report-lifecycle gates; none changes the fixed schema. No endpoint/provider/target/host/port/operator identity, scheduler key/epoch, ledger entry, internal member identity, transition/event detail, source/build/package/project/artifact/executable/module/extraction/checkout/publication/staging path or filename, debugger/profiler identity, diagnostic endpoint, process token/security descriptor, environment value, or other private detail enters either report or bounded publication-failure output.

The following object fragment is the normative zero-attempt representation for the required two-provider, DNS-enabled, 25-TCP-target run:

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
},
"workload_coverage": {
  "gateway_required": true,
  "gateway_exercised": false,
  "dns_required": true,
  "dns_exercised": false,
  "https_providers_configured": 2,
  "https_providers_exercised": 0,
  "tcp_targets_configured": 25,
  "tcp_targets_exercised": 0,
  "all_required_exercised": false
},
"runtime_continuity": {
  "monitoring_ready_continuous": true,
  "scheduler_active_continuous": false,
  "scheduler_epoch_unchanged": true,
  "all_due_rounds_reconciled": false
}
```

This example is normative for property names, property order, array order, closed vocabularies, mandatory zero entries, integer count encoding, Boolean encoding, scalar representation, and the adjacency of workload and continuity evidence when the full accounting domains existed and closed. It is an object fragment from the complete `r0-005-resource-report/v1` report, not a standalone full report and not a substitute for `null` before the controlling boundary. Counts in other valid configurations reflect their accepted manifest. The zero-attempt representation remains a valid deterministic diagnostic only after steady-state attempt accounting closed; it cannot pass when the manifest is nonempty: the exercised values remain false or zero, `all_required_exercised` is `false`, and `acceptance_workload_incomplete` is mandatory. This representative idle-scheduler fragment also derives false scheduler and due-round continuity; another failing zero-attempt diagnostic must serialize the values from its actual bounded evidence and availability flags rather than copy these illustrative values.

`violations` is an ordinal-sorted array containing only these stable bounded codes when applicable:

- `acceptance_cleanup_failed`
- `acceptance_scheduler_inactive`
- `acceptance_workload_incomplete`
- `attempt_accounting_incomplete`
- `configuration_invalid`
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
- `monitoring_readiness_lost`
- `network_cap_exceeded`
- `pid_changed`
- `private_bytes_growth_exceeded`
- `process_exited`
- `process_profile_invalid`
- `readiness_timeout`
- `report_serialization_invalid`
- `rolling_attempt_budget_exceeded`
- `rolling_network_budget_exceeded`
- `runtime_file_disappeared`
- `runtime_file_scope_violation`
- `runtime_file_snapshot_incomplete`
- `runtime_file_snapshot_invalid`
- `sample_missing`
- `source_identity_invalid`
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
- the exact rolling transport-start/charge records are missing, duplicated, malformed, reordered nondeterministically, inconsistent with the attempt aggregates, or otherwise cannot prove both hard rolling admission invariants over activation and all later runtime intervals;
- `incomplete` is greater than zero.

Violation `acceptance_workload_incomplete` is mandatory when any required workload member is not exercised or workload identity cannot be reconciled. This includes zero steady-state attempts; missing Gateway coverage; missing enabled-DNS coverage; any unexercised configured HTTPS provider or enabled TCP target; a `configuration.target_count`/manifest mismatch; any configured/exercised count mismatch; duplicate identities masking a missing member; unknown, missing, duplicated, or unmanifested member attribution; accepted-configuration or manifest mutation; startup-only purported coverage; a coverage-calculation, derived-flag, or human/JSON identity failure; or malformed/missing/reordered `workload_coverage`. A started attempt without terminal accounting triggers the existing `attempt_accounting_incomplete` violation and cannot exercise a member, so both violations appear when both conditions apply. A run containing `acceptance_workload_incomplete` can never pass regardless of resource values.

Violation `monitoring_readiness_lost` is mandatory for any false, unavailable, missing, or internally contradictory readiness level or transition after the first successful ready observation and before the continuity endpoint. It remains mandatory after a later true observation.

Violation `acceptance_scheduler_inactive` is mandatory when the accepted scheduler is stopped, disabled, disposed, replaced, changes or loses epoch, loses accepted-workload ownership, silently loses any due round, emits a duplicate/wrong-member/late due-round reduction, or otherwise makes any of `scheduler_active_continuous`, `scheduler_epoch_unchanged`, or `all_due_rounds_reconciled` false. Accepted-configuration or manifest mutation independently adds `acceptance_workload_incomplete` as applicable. A run containing either continuity violation can never pass.

Coverage proves execution, not endpoint health. A completely accounted failed, timed-out, or post-start canceled attempt can exercise its exact member. No exact success count is required; the dynamically derived due-round count and its terminal-or-explicit-incomplete identity remain exact, while scheduler, cadence, rate, resource, and at-least-one-exercise rules remain independently binding.

Violation `source_identity_invalid` is mandatory when the supplied full reviewed SHA cannot be independently resolved to and matched with its exact commit/root tree; the materialized tree differs, is dirty, mutates, contains untracked/generated input, escapes its root, or uses a submodule/gitlink; a prebuilt, prior, developer-worktree, overlaid, alternate-project, or externally influenced artifact/input is used; the frozen SDK/package/build-input provenance cannot be proved; restore/publish changes the graph or writes the source root; any source value is unavailable, missing, malformed, the wrong length, not lowercase hexadecimal where required, not the fixed configuration identifier, mismatched in the reviewed-SHA/materialized-tree/embedded-metadata three-way identity, derived from a prohibited mutable/private source, or not securely bound to the complete exact single acceptance artifact; the publish shape or private closure is invalid; artifact bytes, retained handle, or launched image mismatch; native extraction integrity fails; or application-private managed/native code loads from outside the authenticated bundle. It appears exactly once whenever one or more conditions applies.

Violation `process_profile_invalid` is mandatory when any initial exact-PID process-profile field is unavailable, malformed, or out of profile; process architecture, .NET major version, runtime identifier, GC mode, or priority is invalid; process priority is not `normal`; a managed/native debugger or profiler is attached initially or later; an exact-PID profile query fails after successful initial capture, including the conditional final pre-termination query; process-profile state becomes unavailable; event and sampled state contradict; prevention or observation is lost; or continuity established by successful initial capture cannot be proved through cleanup arbitration and committed intent. It appears exactly once when one or more applicable conditions occur. Full final validation is inapplicable after failed initial capture and skipping it adds no second violation. It is not replaced by `host_profile_preflight_failed`, `host_profile_unsupported`, `pid_changed`, `monitoring_readiness_lost`, or a cleanup violation; every independently applicable violation still appears.

Violation `process_exited` is mandatory for exact-PID exit committed pre-intent by the serialized arbiter, including a retained handle found signaled/exited before intent even when callback delivery is delayed. It is not added for the expected exact-PID exit after intent under the arbiter's intent-wins tie-break. A pre-intent exit also adds `process_profile_invalid` when initial process-profile capture succeeded and live-process continuity can no longer be proved; after failed initial capture that existing profile violation still appears once. No retroactive intent or stop is permitted, and `acceptance_cleanup_failed` is added only when an independent acceptance-owned cleanup obligation cannot complete.

Violation `acceptance_cleanup_failed` is mandatory for a failed or timed-out acceptance-owned cleanup obligation other than the separately named synthetic-listener condition, including failure to issue the graceful stop after intent, a graceful service stop that misses the existing five-second bound, or failure to remove acceptance-owned staging/provisional material after publication failure. Violation `synthetic_listener_cleanup_failed` is mandatory for synthetic-listener cleanup failure. When independent conditions occur, each applicable code appears exactly once in the existing ordinal order. Neither code substitutes for a genuine profile/report defect, makes staging authoritative, or makes expected post-intent exit an unplanned `process_exited` failure. A cleanup failure after an otherwise successful measurement run changes final `result` to `fail` but leaves all nine flags and every completed measurement intact.

Violation `rolling_attempt_budget_exceeded` is mandatory exactly when the internal exact transport-start/charge records prove that any rolling monotonic 60-second window contains more than 60 starts. Violation `rolling_network_budget_exceeded` is mandatory exactly when those records prove that any such window contains fixed recognized-method charges totaling at least 100,000 bytes. Each applicable code appears exactly once in ordinal order, fails acceptance even when the 30-minute aggregate is lower, and is never substituted for missing or incomplete accounting; inability to prove either invariant independently adds `attempt_accounting_incomplete` and fails.

Violation `unknown_probe_method` is mandatory whenever `attempts.by_probe_kind["unknown"] > 0`. Both `attempt_accounting_incomplete` and `unknown_probe_method` appear when both conditions apply. Every applicable violation appears exactly once, and the `violations` array remains in ordinal code order.

Violation `report_serialization_invalid` is mandatory when the availability object or any report property violates section 8.8, including a missing, additional, reordered, non-Boolean, or contradictory availability flag; a missing/reordered `source` or `host_profile`; a missing, additional, reordered, or duplicated fixed property; a wrong JSON type; a malformed source or process value emitted non-null; an empty, all-zero, abbreviated, uppercase, sentinel, path, or other prohibited source value; source availability/retention contradicting exact-tree evidence; a pass report containing a null, malformed, mismatched, or otherwise invalid source/process value; JSON/human disagreement; a non-null value before its producing boundary; `null` after a boundary when complete accounting was required; zero, false, empty, copied/planned/epoch, or another sentinel used as unavailable; a missing/null/reordered/duplicated/malformed/negative/nonfinite/altered cap constant; a cap substituted for an unavailable measurement; or a pass report with any false availability flag or null acceptance field. It is also mandatory for serialization/staging write, flush, close, reread, deterministic-byte validation, human/JSON agreement, staged-set completeness, destination-collision, or atomic directory-publication failure. Correct independent JSON nulls in a structurally valid source-capture failure require `source_identity_invalid`, and correct independent process-field nulls in a structurally valid initial capture failure require `process_profile_invalid`, but neither case by itself requires `report_serialization_invalid`. Schema, property-order, array-order, type, staging, validation, or publication defects make `result = fail` and cause nonzero exit. Atomic publication failure leaves no authoritative current-run report; staging-cleanup failure independently adds `acceptance_cleanup_failed`. Every other independently applicable violation also appears exactly once in ordinal order.

Neither report contains a source value derived from or containing a machine/user/domain/property/tenant/target/provider/customer identity; source-root, worktree, Git-object-database, build-log, package-cache, intermediate/output, project, source-file, artifact, executable, module, extraction, runtime, cleanup, local-checkout, or other absolute/relative path or filename; tree listing or blob ID; analyzer/generator identity; branch name; arbitrary environment value; or exception text. The valid full independently proven commit SHA, fixed `r0-005-25-target-acceptance/v1` identifier, and exact complete single-artifact SHA-256 digest are the only permitted `source` values.

The reports otherwise contain no serial number, BIOS serial, device UUID, Windows product key, MAC/IP/disk/cloud instance identity, endpoint, target, provider/member identity, scheduler key/epoch, route/interface candidate value, `DisableDefaultRoutes` value, interface identity/type, VPN status, next hop, readiness/process transition timestamp detail, PID, process command line, termination method, executable/module path, debugger identity, profiler identity/CLSID, diagnostic endpoint, process token, security descriptor, environment-variable value, event-source payload, synthetic-listener endpoint, account/user identity, DNS answer, payload, header, body, credential, customer identifier, or DNS-correlation snapshot field. The human report contains the same source values, initial process-profile values, null/`unavailable` decisions, profile validation results, retention, timing, attempts, coverage, continuity, caps, result, and violations, with no alternate calculation or private detail.

### 8.9 Failure rules

The sampler exits nonzero when:

- exact-tree source capture cannot independently resolve/materialize/verify the supplied full reviewed commit and root tree, the source root is dirty/mutable/escaped, an unapproved build input or prebuilt artifact is used, controlled restore/publish or frozen graph provenance fails, the reviewed-SHA/materialized-tree/embedded-metadata identity mismatches, the one-file publish shape/private closure is invalid, artifact bytes or secure binding mismatch, native extraction integrity fails, the launched image differs, external application-private code loads, or any source identity/structure/retention/privacy rule fails;
- configuration is rejected, including under the scoped fail-closed/no-scheduler R0-005 startup rule;
- any acceptance-required availability flag is false, any required value is unavailable, or an availability implication/matrix rule fails;
- readiness exceeds 60 seconds;
- readiness is lost, unavailable, or contradictory after first readiness and before the continuity endpoint;
- the accepted scheduler is inactive, disabled, disposed, replaced, changes/loses epoch or workload ownership, or fails exact due-round reconciliation at any point in the continuity interval;
- any static acceptance-host requirement is unsupported, required host metadata is missing, or a prohibited shared/CPU-quota-limited host is used;
- the 60-second host preflight is missing a required sample, has average non-agent system CPU greater than or equal to 10.0%, or ends with less than 2,000,000,000 bytes available physical memory;
- any initial exact-PID process-profile field is unavailable, malformed, or out of profile; initial process architecture/runtime/GC/priority/debugger/profiler validation fails; readiness or scheduler activation precedes successful initial capture; process priority later leaves `normal`; a managed/native debugger or profiler attaches; exact-PID state becomes unavailable; event/sample evidence contradicts; prevention/observation is lost; the conditional final exact-PID validation after successful initial capture fails; or continuous proof established by initial success cannot be maintained through cleanup arbitration and committed intent; every such case adds `process_profile_invalid` exactly once, while failed initial capture requires no full final validation;
- any live started exact PID does not enter the one serialized exit/intent arbiter, the coordinator lacks the retained exact process handle/PID or pre-subscribed exit observation, a callback classifies exit independently, the retained-handle check is omitted, or arbiter sequence/tie-break semantics are violated;
- the arbiter commits pre-intent exact-PID exit, the PID changes before intent, or a replacement/restart occurs; pre-intent exit permits no retroactive intent or stop, while expected exact-PID exit under the post-intent tie-break does not add `process_exited` or `pid_changed`;
- the complete start barrier from admission pause through baseline-bundle commit, the end drain, or the separate final-snapshot stabilization window exceeds its 10-second deadline;
- an invocation starts at or after `steady_state_end`;
- a sample is missing;
- a required non-disk counter or network-accounting value is unavailable, negative, reset, or overflows;
- a required recursive runtime-file snapshot is incomplete, unstable within its bounded start or final window, unreadable, contains an invalid length, overflows during subtraction or summation, escapes the monitored directory, or loses a baseline file;
- working set reaches or exceeds 150,000,000 bytes;
- steady-state average CPU reaches or exceeds 2.0%;
- disk reaches or exceeds 100 decimal MB/day;
- network reaches or exceeds 100,000 bytes/minute;
- any rolling monotonic 60-second window exceeds 60 transport starts or reaches 100,000 fixed charged bytes, or complete exact records cannot prove both hard admission invariants over activation and all later runtime intervals;
- any growth rule fails;
- any required workload member is not distinctly exercised or manifest/configuration/member reconciliation fails under mandatory `acceptance_workload_incomplete` conditions in section 8.8;
- any required `runtime_continuity` value is false or its event/sample/epoch/due-ledger derivation fails;
- attempt accounting, exact recognized-method charge derivation, or configured-budget/acceptance table consistency is incomplete under any mandatory `attempt_accounting_incomplete` condition in section 8.8;
- an unknown probe method is encountered;
- report serialization, staging, flush, reread, validation, staged-set completeness, destination freshness, or atomic directory publication violates section 8.8, adding `report_serialization_invalid`; atomic publication failure leaves no authoritative current-run report and requires nonzero exit;
- graceful-stop issuance fails after committed intent, the stop misses its five-second deadline, staging/provisional cleanup fails, or another general acceptance cleanup obligation fails, adding `acceptance_cleanup_failed`;
- synthetic-listener cleanup fails, adding `synthetic_listener_cleanup_failed`; when both independent cleanup conditions fail, both codes appear exactly once.

Every representable failure model retains all fixed objects/properties in binding order, derives the nine flags reached before cleanup, writes JSON `null` for values whose producing boundaries never existed or whose independently permitted capture failed, retains all ten exact non-null caps, includes each causal bounded violation once, renders unavailable values as human `unavailable`, and sets `result = fail`. Source-capture failure independently nulls only source properties that could not be proven and starts no later stage. After successful source capture, every later failure retains all three source properties unchanged. After static profile completion, later failure retains static host values. After initial process-profile capture, later dynamic or cleanup failure retains the initial process values unchanged; it never rewrites them to current/restored/fabricated values or null. Cleanup and publication failure never clear or promote a flag. Final model derivation waits for the final cleanup endpoint, and report authority waits for successful atomic set publication. A publication failure may therefore have no authoritative current-run report; its bounded in-memory/status classification and nonzero exit remain mandatory without fabricating a report that proves its own failed publication. No failure may fabricate a zero, Boolean, timestamp, empty collection, cap, current-checkout identity, path-derived label, intended process value, host-derived substitute, or estimate.

## 9. Required verification for implementation

### 9.1 Unit tests

Use fake transports, an injected clock, deterministic jitter, and synthetic route/interface rows only to cover:

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
- eight starts at activation followed by exact one-per-second refill, with candidate 60 accepted and candidate 61 blocked inside the same rolling 60-second window, the complete first activation minute never exceeding 60 starts, and the first replacement accepted exactly when the oldest entry reaches age 60 seconds;
- deterministic rolling expiry and admission immediately before, exactly at, and immediately after the 60-second boundary, including the rule that an entry at exactly `now - 60 seconds` expires and same-timestamp candidates use stable fair/provider order;
- a fully refilled eight-token bucket after long elapsed time not bypassing either rolling cap, and activation, normal steady runtime, synchronized due work, release from backpressure, and long-elapsed refill all satisfying both hard invariants;
- rolling-budget blocking reserving no target/provider guard or global slot, consuming no token, creating no ledger/attempt/charge record, remaining cancelable, and producing scheduler-level incomplete rather than a started failure when its logical-round deadline expires;
- no rolling-ledger refund after success, failure, timeout, cancellation, or attempt-level incomplete; attempt and charged-byte entries surviving acceptance phase boundaries; and epoch degradation, shutdown, or restart disposing the complete ledger before replacement activation starts empty;
- exact rolling charged-byte admission immediately below and at 100,000 bytes, multiple HTTPS children each contributing 8,192 independently, and candidate acceptance requiring both count and charge relations simultaneously;
- the 11-fast/14-slow TCP example and every documented 25-target example remaining under both hard runtime caps after passing the scheduled-demand formulas, including zero-duration transports and minimum jitter;
- one exact injected-monotonic integer/fixed-point implementation with no culture, wall-clock, timer-tick, floating-point, iteration-order, sampling-average, or aligned-minute dependence;
- unknown methods always failing with the deterministic zero-sentinel/measured-byte representation, and missing, malformed, negative, nonintegral, supplied, overridden, or conflicting recognized-method charges failing deterministically;
- exact budget boundaries with DNS enabled and disabled, including a configuration that is valid only when DNS is disabled;
- rejection of 25 targets at 15 seconds, acceptance of a 25-target configuration with sufficiently long intervals, and the valid two-default-provider 25-target/60-second example;
- atomic acceptance-manifest capture from the accepted startup configuration before acceptance measurement, with exactly one Gateway member, conditional DNS membership, one member per configured HTTPS provider, and one member per enabled TCP target;
- immutable manifest/configuration and stable scheduler/member identity across the run, with duplicate, unknown, missing, mutated, or no-longer-configured identities rejected without using display names, positions, endpoints, logs, or aggregate counts;
- readiness-transition subscription preceding the first accepted ready observation, continuous true readiness through the final snapshot, and event-driven transitions reconciled with sampled readiness levels under the injected monotonic clock;
- readiness loss during the start barrier, after every workload member was initially exercised, during end drain, and during final-snapshot stabilization, with a transient false-to-true transition remaining a permanent `monitoring_readiness_lost` failure;
- scheduler stop, disablement, disposal, replacement, epoch change/loss, or accepted-workload ownership loss producing `acceptance_scheduler_inactive` even when readiness remains true;
- due-round ledger equality for Gateway, enabled DNS, HTTPS aggregate, and every enabled TCP member, with each due round receiving one terminal or explicit scheduler-level incomplete reduction before its deadline and silent, duplicate, wrong-member, or late reduction failing;
- acceptance barriers preserving the original epoch, token bucket, and rolling admission ledger, and continuity failure causing no same-run scheduler restart/reactivation or token/ledger reset;
- all provider children in valid two-, three-, and four-provider HTTPS rounds starting without serial five-second waits when both rolling budgets, the required slots, and tokens are available;
- three- and four-provider rounds in which every child times out at five seconds completing as one counted aggregate failure within the ten-second round deadline, and two consecutive such rounds advancing HTTPS state under the existing threshold;
- partial HTTPS success remaining aggregate success with `partial_endpoint_failure` when one provider succeeds and the other provider children fail;
- the process-wide concurrency cap never exceeding four, no provider having two concurrent attempts, and no two HTTPS aggregate rounds overlapping;
- stable provider-child order, independent child slot/token acquisition, one token and one attempt record per child, and separate complete provider counts, bytes, outcomes, and phase membership under concurrent starts;
- stable rotating fair-queue order, bounded HTTPS fan-out, pending-child/non-HTTPS alternation, no systematic Gateway/DNS/TCP starvation under sustained valid mixed demand, cancellation before token consumption, and a started timeout consuming one token;
- genuine capacity or token contention that prevents required provider completion still producing the existing non-counting incomplete aggregate result;
- shutdown and degradation cancellation;
- flapping detection, rolling-window eviction, clearing, restart reset, and degradation reset;
- configuration bounds, atomic rejection, duplicate IDs, 0/25/26 targets, interval/port limits, credential-like values, and URI/range rejection;
- malformed startup configuration activating no scheduler or partial valid subset, preserving no prior in-memory state or stale provider/target set across restart, leaving readiness unavailable/degraded with bounded value-free evidence, and corrected configuration plus restart permitting normal activation;
- a document-drift check proving the R0-005 no-durable-LKG exception remains narrow and consistent with `docs/security-guardrails.md`, while durable/updateable configuration outside this exception continues to preserve its safely stored accepted last-known-good value;
- default one-name DNS configuration, one valid trimmed override, atomic rejection of lists, arrays, comma- or whitespace-separated multiple names, fallback names, wildcard/search-domain expansion, URI/credential/customer syntax, and configuration order or representation never introducing another name;
- exactly one DNS transport attempt and one DNS method charge per logical round, unchanged attempt/network-budget examples, one aggregate DNS snapshot per completed round, failure affecting only DNS state under existing correlation language, and a drift check preventing silent fallback or second-name queries;
- missing or invalid `operator_id` and rejection when any provider pair shares a provider ID, declared operator, normalized endpoint host, or normalized registrable domain;
- valid two-, three-, and four-provider configurations whose provider IDs, declared operators, normalized hosts, and normalized registrable domains are all pairwise distinct, including the built-in Google/Cloudflare declarations;
- registrable-domain cases for `example.com`, a multi-label suffix such as `example.co.uk`, exact, wildcard, exception, and PRIVATE-section rules, one optional terminal dot, a host equal to a public suffix, IP-literal rejection, two subdomains sharing one registrable domain, and Unicode/IDNA-equivalent hosts normalizing to the same domain;
- byte-for-byte pinned suffix-data verification and deterministic registrable-domain behavior across restarts;
- provider-set count, pairwise provider/domain/host/declared-operator separation, configuration-order independence, atomic rejection with no partial scheduler activation, and a drift test proving every requirements statement uses `distinct_registrable_domain_count = N` for all 2–4 providers;
- HTTPS-only URLs; validation acceptance for otherwise valid expected statuses 200, 204, 206, and 299; atomic rejection for 100, 199, 300, 302, 304, 307, 308, 400, 500, and 599; configuration-order independence; and no partial provider subset activation;
- expected-body rejection with status 204 or 205, acceptance of 204 or 205 without a body, optional bounded exact UTF-8 body comparison for other accepted 2xx statuses, and unchanged Google-204/Cloudflare-200 defaults;
- HTTPS endpoint outcomes for 204, 200 status-only, wrong status, every actual 3xx as terminal failure, redirects disabled, TLS failure, timeout, cancellation, oversized body, body not retained, partial provider failure, all-provider failure, and two-provider independence;
- DNS snapshot correlation for DNS completion before HTTPS start, DNS completion after HTTPS start, concurrent completions in opposite task orders producing the same HTTPS result, the exact 60-second freshness boundary, stale snapshot, no snapshot, fresh success, fresh counted failure, fresh non-counting result, DNS disabled, and timestamp reversal;
- DNS-blocked HTTPS aggregation, `dns_correlation_unavailable`, DNS independence, and absence of snapshot fields from logs and evidence;
- one IPv4 default route, one IPv6 default route, simultaneous-family routes, lower effective metric winning across families, IPv4 winning only an exact effective-metric tie, and route/interface metric composition using checked addition from the same family-matching IP-interface row that supplies `Connected` and `DisableDefaultRoutes`;
- `DisableDefaultRoutes == false` allowing an otherwise eligible route and `true` excluding it before sorting, including a disabled lower-metric route losing to an enabled higher-metric route and every potential route disabled producing counted `missing_route`;
- `false` in A and `true` in B, plus `true` in A and `false` in B, making the pair unstable and retrying; one unstable pair followed by a stable pair committing only the stable result; and continuous toggling through the deadline producing non-counting `route_selection_unavailable`;
- IPv4 and IPv6 using their own family-matching IP-interface rows, API failure while obtaining a required row producing `route_selection_unavailable`, and candidate input order not altering the result;
- Ethernet, Wi-Fi, PPP, VPN, tunnel, and virtual interfaces all applying `DisableDefaultRoutes == false` with no interface-type preference, exact remaining tuple tie-breaks, identical-row deduplication, and a post-commit route or policy change affecting only the next Gateway round;
- filtering of `DisableDefaultRoutes == true`, interface down/disconnected/unauthenticated/paused/low-power states, and software-loopback, endpoint, and filter interfaces before sorting;
- stable zero-candidate `missing_route`, including every potential default route policy-disabled; route/interface API failure or incomplete row resolution; unstable A/B sets; invalid/contradictory metric data; checked overflow; unusable IPv4 or on-link next hop; and IPv6 link-local next hop with selected-interface scope, preserving the exact `missing_route`/`icmp_unavailable`/`route_selection_unavailable` distinctions;
- a policy-disabled route never satisfying Gateway workload coverage;
- no `DisableDefaultRoutes` value, VPN/interface identity or type, route/interface candidate field, address, next hop, metric, LUID, index, protocol, origin, or scheduler/endpoint identity entering evidence; and
- a document-drift test failing if any default-route eligibility list or summary omits the mandatory `DisableDefaultRoutes == false` requirement;
- evidence-code allowlisting and redaction.
- exact `source` top-level placement after `schema_version`, exact `commit_sha`, `configuration_name`, `executable_sha256` property order, and rejection of a missing, additional, reordered, duplicated, or wrong-typed source property;
- exact 40-character lowercase commit-SHA validation and rejection of abbreviated, uppercase, all-zero, whitespace-padded, prefixed, suffixed, symbolic-ref, branch, malformed, and reviewed-head-mismatched values, with proof that the value comes from coordinator-verified exact reviewed-tree provenance and is only cross-checked against embedded metadata, never self-proved by the artifact, developer checkout, environment, or symbolic ref;
- exact `r0-005-25-target-acceptance/v1` configuration identifier and rejection of arbitrary labels, filenames, paths, machine/user/property/tenant/customer/provider/target identifiers, environment-derived values, empty values, and changed constants;
- exact 64-character lowercase complete-single-artifact digest validation and rejection of uppercase, malformed, partial, all-zero, adjacent-file, stale-file, substituted-file, and mismatched digests, including hashing all bytes through the retained bound handle; mutation before binding; mutation after hashing but before process creation; and delete, rename, or replacement attempts;
- effective acceptance-publish validation for `win-x64`, self-contained, single-file, native-library inclusion, untrimmed, symbol-free output; a valid one-file artifact; and rejection of framework-dependent apphost, `SelfContained=false`, multi-file, separate main/referenced managed assembly, application-private native DLL, external `.deps.json`/`.runtimeconfig.json`, second executable, plug-in, probing path, mutable shared runtime, and any closure not proved complete;
- exact reviewed detached-tree materialization and coordinator build; dirty developer worktree isolation; modified/staged/untracked/ignored/generated source rejection; preexisting `bin`/`obj`/`publish`/artifact reuse and supplied prebuilt artifact rejection; different or dirty source embedding the expected SHA rejection; exact artifact-byte mutation changing the digest; stale/adjacent/copied/similarly named artifact rejection; embedded metadata mismatch; exact reviewed-SHA/materialized-tree/artifact-metadata three-way match; launched-image mismatch; fresh controlled native extraction; preexisting/substituted extraction rejection; unexpected external application-private module loading; and Windows system-module exclusion;
- source-root immutability at all four checks, external fresh intermediate/output roots, parent/user props/targets rejection, external project/import/compile/analyzer/generator/native input rejection, source/reparse escape rejection, package graph and content-hash capture, publish without implicit graph change, and independent exact-tree-provenance/artifact-digest null and retention behavior;
- independent source-property capture with only unavailable properties null, all nine availability flags false and no configuration/process/scheduler start after source-capture failure, `source_identity_invalid` for every source defect, and `report_serialization_invalid` for every independently applicable structural, type, format, ordering, contradiction, pass-with-null, or human/JSON disagreement defect;
- all three source values remaining non-null and unchanged through configuration rejection, host-profile failure, process-start failure, readiness timeout, start-barrier failure, early steady-state failure, end-drain failure, final-snapshot failure, and success, with passing reports requiring exact reviewed-head, fixed-profile, complete single-artifact binding, launched-image, extraction-integrity, and runtime-closure matches;
- exact `measurement_availability` placement and flag order, every implication, every boundary-to-null transition, actual zero versus unavailable, no fake or planned timestamps, every normative source/failure/success scenario, human `unavailable`/JSON-null agreement, byte-stable failure JSON, pass reports containing no null, and inconsistent null/value combinations adding `report_serialization_invalid`.
- `host_profile_complete` becoming true before process creation with all eight process fields null; the exact `process_started => configuration_accepted && host_profile_complete` implication; static failure; process-creation failure after static success; initial exact-PID process-profile success/failure; no readiness acceptance or scheduler activation before success; and an early readiness event remaining unaccepted until success;
- initial process architecture, .NET-major, runtime-identifier, framework/runtime availability, workstation/server/malformed GC, every closed priority class, managed/native debugger, managed/native profiler, unavailable debugger/profiler state, exact-PID query failure, independent process-field nulls, and retention of every valid initial field;
- `process_profile_invalid` without automatic `report_serialization_invalid` for structurally valid capture failure, malformed non-null process values adding both applicable violations, and exact human/JSON agreement;
- priority change during initialization; priority change/restoration during steady state remaining permanent; debugger/profiler attach-detach between ordinary samples being prevented or completely detected; transitions during start barrier/end drain/final stabilization/pre-termination cleanup; event/sample contradiction; loss of prevention/observation; no recovery forgiveness; admissions stopping with bounded started-attempt closure; and initial field retention;
- cleanup included in the dynamic process-profile phase matrix; an isolated cleanup-phase defect after otherwise complete measurement retaining all nine flags and every completed value; and the same defect after each earlier failure boundary preserving that boundary's exact flags, values, and nulls without promotion or clearing;
- universal deterministic cleanup disposition for every captured started PID; process creation failure producing no PID/intent/stop; initial profile failure with a live PID entering arbitration and authorized stop; initial profile failure with pre-intent exit; and full final profile validation occurring only after successful initial capture;
- one pre-subscribed exact-PID exit/intent arbiter with closed states and transitions; exit before entry; callback committed before intent; delayed callback with a signaled retained handle; exit during final validation; exit after validation but before the liveness check; intent winning immediately after a nonsignaled check; exit after intent before stop completion; exit after stop; delayed and duplicate callbacks; callback/handle contradiction; wrong-PID event; replacement after exit; and identical injected ordering producing identical violations and decisions;
- final exact-PID validation success immediately before arbiter liveness/intent/stop; unavailable and out-of-profile conditional final queries; final event/sample contradiction; serialized validation/arbitration/intent/stop ordering; stop-issuance failure retaining intent; expected post-intent exit; unplanned pre-intent exit; no post-intent profile query; and replacement/restart remaining prohibited;
- cleanup failure leaving initial process fields and every reached measurement unchanged; no recovery forgiveness; final in-memory model derivation only after cleanup outcome; and independently applicable profile, exit, general-cleanup, and synthetic-listener violations appearing exactly once in ordinal order;
- report lifecycle/authority tests for same-model JSON/human generation, fresh private current-run destination, same-volume two-file staging, complete write/flush/close/reread/validation, exact staged-set membership, one atomic directory publication, and authority beginning only after publication;
- JSON or human staging-write failure, flush failure, reread failure, deterministic-byte failure, human/JSON disagreement, extra or missing staged file, destination collision, atomic rename failure, failure before a final destination exists, and no partial or stale authoritative set;
- provisional success followed by cleanup failure publishing an authoritative fail set; prior historical sets never treated as current; exactly one corrected failure-set attempt and both its success/failure outcomes; staging cleanup success/failure; nonzero publication-failure exit; bounded `report_serialization_invalid`; no private publication detail; and identical injected failure producing identical bounded classification/exit behavior;
- document-drift tests failing when cleanup is missing from a dynamic-profile phase list, the normative scenario matrix, or the unit/native/resource coverage; when authorized intent is conditioned on initial profile success; when any started PID lacks cleanup disposition; when an exit/intent race lacks the retained-handle tie-break or relies on callback/wall-clock order; when expected post-intent exit is described as `process_exited`; when JSON and human files can become independently authoritative; when a provisional/staged or stale set can be current; when publication failure can omit `report_serialization_invalid`, return zero, or leave an authoritative pass; when authority is claimed before atomic set publication; or when cleanup/publication may alter an availability flag or initial process value;
- exact `caps` placement and ten-property order, complete matrix membership with stable integer/finite-number grouping, report-model-creation and never-null behavior through every normative scenario, exact JSON numeric types and values, no cap substitution for unavailable measurements, every invalid cap form adding `report_serialization_invalid`, identical human/JSON constants and units, and document-drift alignment among the fixed-property list, matrix, exact-value table, and normative examples.

### 9.2 Integration tests

Use disposable local endpoints or fakes only. Cover:

- local TCP accept, refuse, timeout, cancellation, and socket disposal;
- local HTTPS status/body/redirect/streaming behavior with test certificates as appropriate;
- local HTTPS validation/execution for the complete accepted/rejected status set, 204/205 body contradictions, actual 3xx never succeeding, redirects remaining disabled, provider-order independence, and preserved default-provider validity;
- two-, three-, and four-provider local HTTPS fan-out with concurrent all-timeout completion, partial success, global/per-provider limits, non-overlapping aggregate rounds, contention incompleteness, and sustained mixed-work fairness;
- two-, three-, and four-provider startup acceptance with pairwise-distinct IDs/operators/hosts/domains, plus atomic no-scheduler rejection for every pairwise duplicate and for IDNA-equivalent or configuration-order-permuted duplicates;
- system-resolver adapter success/failure through a controlled abstraction;
- controlled one-name DNS operation with exactly one resolver invocation, no fallback/search expansion, one snapshot result, and unchanged token/charged-byte accounting;
- scheduler activation after readiness creating one full process-local bucket plus an empty rolling admission ledger, exact monotonic lazy refill/rolling expiry during local fake/endpoint work, and prompt stop plus bucket/ledger discard on readiness degradation;
- restart rebinds static configuration, reconstructs enabled probes as `unknown`, moves them to `baselining` only through scheduler activation after readiness, and creates a new full bucket/anchor and empty rolling ledger without prior credit or entry;
- malformed restart-time configuration producing no scheduler or partial activation, reusing no prior process-memory or stale provider/target set, remaining visibly unavailable/degraded with value-free bounded evidence, and corrected configuration on a later healthy restart activating normally;
- an acceptance coordinator subscribed before readiness, continuous event/sample reconciliation, permanent failure after transient readiness loss, same-epoch ownership through both barriers/final stabilization, and no same-run scheduler restart after continuity failure;
- deterministic due-round reconciliation through success, failure, timeout, cancellation, rate/rolling-budget/capacity incomplete, admission pauses, and injected silent-round loss, with token-bucket and rolling-ledger continuity across acceptance barriers;
- an injected Windows route adapter exercising stable A/B current-compartment snapshots; same-pass, family-specific `Connected`/`Metric`/`DisableDefaultRoutes` lookup; false/true policy filtering and both toggle directions; exact candidate ordering/deduplication; enabled-higher-metric selection over a disabled lower-metric route; stable-all-disabled `missing_route`; API/incomplete-row `route_selection_unavailable`; workload exclusion; and no route/interface or VPN-policy value leakage;
- coordinator-controlled build from a freshly materialized exact reviewed tree, independent commit/root-tree verification, frozen build-input provenance, and three-way supplied-SHA/materialized-tree/embedded-metadata agreement, with no prebuilt artifact, developer-worktree, current-checkout, environment, branch, shortened-SHA, overlay, external import/source, or metadata-only fallback;
- validating the exact effective acceptance publish shape, digesting the complete one-file private closure through a retained bound handle, launching that same bound artifact, proving process-image identity, and preventing or deterministically detecting write/delete/rename replacement, adjacent-file selection, copying, mutation, or substitution between digest capture and process creation;
- framework-dependent, `SelfContained=false`, multi-file, separate managed/native dependency, external metadata, plug-in/probing, and mutable shared-runtime rejection before process creation; fresh controlled bundle-native extraction; substituted/preexisting extraction rejection; and continuous detection of application-private executable components loaded from outside the authenticated bundle;
- source-capture failure combinations proving independent nulls, byte-identical JSON for identical failed inputs, identical human `unavailable` rendering, all nine measurement-availability flags false, and no configuration acceptance, host preflight, process creation, or scheduler activation;
- static host completion before launch with process fields null; initial exact-PID process-profile capture before readiness acceptance/scheduler activation; exact process runtime/GC/priority/debugger/profiler validation; permanent dynamic failure on transition or loss of proof through committed cleanup-termination intent; and retained initial values in JSON and human evidence;
- process-profile prevention or complete observation remaining active through pre-termination cleanup and arbitration after initial capture success; no new scheduler or network work during cleanup; conditional final validation plus one retained-handle liveness check, intent commit, and stop issuance in one serialized operation;
- pre-subscribed exact-PID exit observation, retained handle/PID identity, initial-profile-failure stop path, delayed callback, every arbiter tie-break outcome, stop issuance after intent, expected post-intent exit producing no `process_exited`, pre-intent exit producing it, and replacement/restart remaining prohibited;
- fresh-destination selection, same-volume JSON/human staging from one model, complete validation, one atomic directory publication, corrected failure-set retry at most once, publication/staging-cleanup failure, exact command exit codes, no stale or partial authoritative set, and JSON/human consistency;
- fixed-shape success and failure reports retaining the identical ordered ten-property `caps` object, including configuration rejection, host-profile failure, no process start, readiness timeout, start-barrier failure, end-drain failure, and final-snapshot failure, while every unavailable associated measurement remains `null` rather than copying a cap value;
- no probe journal/outbox calls and no database/schema artifacts;
- no response body, DNS answer, endpoint, target, interface, or exception leakage.

Live Google, Cloudflare, Microsoft, DNS, SMS, camera, or customer-network access is not required or permitted as an automated test dependency.

### 9.3 Security and privacy tests

Verify:

- URI user information, credentials, credential-assignment syntax, query strings, fragments, and customer/device identifiers are rejected;
- HTTPS redirects and invalid TLS are rejected without fallback or weakened validation;
- the R0-005 startup-only configuration exception remains atomic, fail-closed, nonpersistent, and value-redacted, while no failed cloud-delivered or durable/updateable configuration replaces a safely stored accepted last-known-good value;
- source fields contain only the independently exact-tree-proven full commit SHA, fixed acceptance-profile identifier, and exact complete single-artifact digest, with no machine/user/domain/property/tenant/customer/target/provider value, source/worktree/Git-object/build/package/intermediate/project/source/artifact path or filename, tree/blob/analyzer/generator identity, executable/extraction filename, assembly/module identity, checkout, branch, arbitrary environment value, or exception text in JSON or human evidence;
- process evidence exposes no command line, executable/module path, debugger identity, profiler identity/CLSID, diagnostic endpoint, process token, security descriptor, environment-variable value, event-source payload, or account/user identity, and JSON/human reports agree on initial values, nulls, validation, violations, and retention;
- publication paths, staging paths, filenames, directory identities, handles, filesystem/operating-system errors, exception text, host/account identity, and private destination detail appear in neither report nor bounded console/status output, including destination-collision, rename, and staging-cleanup failures;
- endpoint URLs/hosts, DNS queries/answers, DNS-correlation snapshot fields, target IDs/labels/hosts/ports, response bodies, headers, `DisableDefaultRoutes` values, VPN status, interface identities/types, interface/route candidate data, IP addresses, next hops, metrics, LUIDs, indices, protocols/origins, exception text, machine paths, and other customer-controlled values do not appear in JSON or human reports, production logs, health output, support evidence, metrics, or other evidence;
- workload manifests, scheduler keys or epochs, rolling-ledger entries or timestamps, readiness-transition timestamp details, internal member identities, per-provider identities, and per-target identities do not appear in JSON or human acceptance reports, logs, health, support output, or metrics;
- response-body readers stop at the 256-byte bound plus one overflow-detection byte and retain no bytes;
- no production inbound listener, firewall mutation, packet capture, subnet/range expansion, broad port scan, or arbitrary gateway TCP probe exists;
- no probe journal/outbox call, schema artifact, identity fabrication, or raw LAN inventory is introduced.
- document-drift checks keep the requirements/source-precedence/startup/activation/failure/test language aligned with the narrow security-guardrail exception and do not generalize it.

### 9.4 Native Windows acceptance

Native acceptance must verify:

- exact-tree source capture occurs before configuration acceptance and static host preflight; the coordinator independently resolves/materializes/verifies the supplied full reviewed commit/root tree, builds from that immutable isolated tree with frozen approved inputs and fresh external outputs, requires supplied SHA/materialized tree/embedded metadata agreement, rejects supplied/preexisting/dirty/alternate/overlaid artifacts or source, records the fixed identifier, and separately hashes every byte of the securely bound complete single-file closure actually launched;
- effective publish-shape validation accepts only the specified `win-x64`, self-contained, single-file, native-library-inclusive, untrimmed, symbol-free runtime shape and rejects framework-dependent, `SelfContained=false`, multi-file, separate managed/native dependency or metadata, second-executable, plug-in/probing, and mutable shared-runtime shapes before process creation;
- the coordinator retains the bound artifact through launch, proves launched-process image identity, uses only fresh controlled native extraction from the authenticated bundle, rejects preexisting/substituted executable extraction content, detects any external application-private managed/native code load through the final endpoint, and excludes only Windows-owned system modules governed by the pinned host profile;
- injected missing, malformed, mismatched, substituted, or insecurely bound source inputs independently null only unavailable properties, add `source_identity_invalid`, start no later acceptance stage, process, or scheduler, and produce identical source values and `unavailable` decisions in JSON and human reports;
- the static host matches `r0-005-windows11-x64/v1`, records every required bounded static field, passes the 60-second idle preflight, and sets `host_profile_complete` before process creation while process-specific fields remain null;
- immediately after exact-PID creation, the initial process profile proves x64/.NET 10/Windows-x64 RID/workstation-or-server GC/normal priority/no managed-or-native debugger/no managed-or-native profiler before accepting readiness or activating a scheduler; transition evidence and exact process-exit observation are subscribed before or atomically with launch, the exact handle/PID are retained, and successful initial capture establishes dynamic priority/debugger/profiler continuity through cleanup arbitration and committed intent;
- the host is physical or a dedicated VM rather than a shared or CPU-quota-limited runner, with virtualization recorded;
- current-compartment IPv4/IPv6 default-route discovery through the required IP Helper rows; same-pass, family-specific `Connected`/`Metric`/`DisableDefaultRoutes` resolution; stable A/B candidate-set commit; exclusion before sorting when the policy value is true; both flag-toggle directions causing retry; family-specific effective-metric composition; exact tuple selection; and bounded `missing_route`/`icmp_unavailable`/`route_selection_unavailable` distinctions;
- Ethernet, Wi-Fi, PPP, VPN, tunnel, and virtual competition resolved uniformly by mandatory `DisableDefaultRoutes == false` plus the binding metric/tuple policy, with an enabled higher-metric route selected over a disabled lower-metric route and without policy value, VPN status, interface identity/type, address, next hop, metric, LUID, index, protocol, or origin disclosure;
- ICMP success when permitted and `icmp_unavailable` behavior when it is not;
- cancellation and service stop complete within five seconds;
- the 25-target resource sampler passes only after the Gateway, enabled DNS, both HTTPS providers, and all 25 distinct synthetic TCP members receive terminal steady-state exercise;
- exact internal start/charge evidence proves activation and every later rolling monotonic 60-second window contains at most 60 starts and less than 100,000 fixed charged bytes, with candidate order and exact-age expiry deterministic and JSON/human aggregates derived from the same started-attempt records;
- the start barrier commits one stable steady-state baseline bundle while admissions remain paused, with `steady_state_start` equal to that commit and no later pre-existing sampling-grid wait;
- the start and final snapshots use the same consecutive-map stable-snapshot protocol, the final snapshot commits within its separate ten-second post-drain window, and admissions remain paused with no transport start after `steady_state_end`;
- steady-state sampling is anchored at exact `steady_state_start + 5n seconds`, and startup/steady-state CPU and disk attribution share the committed boundary without an omitted or double-counted interval;
- the exact PID remains alive and unchanged and CPU, disk, and working-set enforcement continue through stable final-snapshot commit while attempt and network membership stop at exact `steady_state_end`;
- monitoring-ready is achieved within 60 seconds after network availability;
- injected configuration rejection, static-host failure, process-start failure, initial process-profile failure with the process still live, initial-profile failure followed by pre-intent exit, readiness timeout, start-barrier failure, dynamic process defect in initialization/start barrier/steady state/end drain/final stabilization/pre-termination cleanup, end-drain timeout, and final-snapshot failure each produce the exact flags, independent null/value behavior, source/static/initial-process retention, caps, scheduler presence/absence, violations, human `unavailable`, and JSON values required by section 8.8;
- injected process exit during final validation, between validation and liveness check, immediately after a nonsignaled check under the intent-wins tie-break, after intent with delayed callback, and after stop; final exact-PID query unavailability; transient priority/debugger/profiler defect during cleanup; stop-issuance failure; graceful-stop timeout; general acceptance cleanup failure; synthetic-listener cleanup failure; and both cleanup codes together when independently applicable;
- injected destination collision, JSON/human staging write or flush failure, atomic directory-publication failure, corrected failure-set success/failure, staging cleanup failure, and proof that publication failure leaves no current authoritative report;
- every cleanup/publication scenario asserts exact reached flags, retained values/nulls and measurements, source/static/initial-process retention, causal violations, expected/unplanned exit classification, report authority, final result, exit code, and absence of private cleanup, process, or publication detail;
- monitoring-ready remains continuously true from its first accepted observation through stable final-snapshot commit, the same active/enabled/undisposed scheduler epoch owns the unchanged accepted workload, and every due round before `steady_state_end` is terminally or explicitly incompletely reconciled;
- injected native acceptance failure proves readiness loss, scheduler/epoch loss, or silent due-round loss cannot be forgiven by recovery or a same-run scheduler restart;
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
- start/end admission pauses consuming no pre-start token or charge, issuing no refund, preserving the active bucket/anchor and rolling admission ledger, and allowing elapsed barrier time to refill lazily only to the eight-token cap and age entries only by exact monotonic duration before post-resume dispatch;
- no byte splitting, full bytes from a steady-state attempt that completes during end drain, startup attempts excluded from top-level attempts, and queued pre-start/end work excluded from transport-attempt arrays;
- full network-byte/outcome closure for steady-state attempts through end drain, disk attribution through the process-start/committed-stable-start/stable-final recursive snapshots, and growth windows excluding drain and final-snapshot samples;
- process CPU and working-set enforcement through stable final-snapshot commit, disk growth through that commit, and attempt/network membership ending at exact `steady_state_end` without a final-snapshot transport or token-bucket/rolling-ledger restart;
- CPU consumed before baseline-bundle commit counted startup-only, CPU consumed after commit counted steady-state-only, file growth before the committed snapshot counted startup-only, file growth after it counted steady-state-only, and no CPU or file-growth interval omitted, duplicated, or assigned to both phases;
- 80,000 process/network write bytes per minute with zero runtime-file growth producing zero `attributed_disk_bytes` and zero `disk_mb_per_day`, proving that aggregate process I/O counters are not disk inputs;
- new-file and existing-file growth summing correctly, unchanged and same-size rewritten files contributing zero, file shrinkage contributing zero, and a disappearing baseline file failing;
- startup and steady-state disk attribution using their separate pairs around the one committed snapshot, with timing, CPU, disk, attempts, network, and phase-boundary values/calculations identical in JSON and human reports;
- identical consecutive canonical maps committing the second map deterministically; differing path sets or file lengths discarding both maps and retrying; and one unstable pair followed by a stable pair committing only the stable second map;
- continuous mutation through either bounded window, unreadable or incomplete final enumeration, invalid lengths, arithmetic overflow, disappearing files, directory escape/reparse escape, and bounded retry/deadline exhaustion producing each applicable runtime-file and boundary violation without a passing disk result;
- one shared start/final canonicalization and equality implementation, same-size rewrites requiring no content detection, no wall-clock/hash/ETW/Process Monitor/driver dependency, and exact separate ten-second end-drain and final-snapshot deadlines;
- process exit, restart, or PID change during final stabilization failing; no transport start at or after `steady_state_end`; CPU and disk including use/growth through final commit while attempts and network remain bounded by the half-open interval;
- exact deterministic placement and nonnegative integer encoding of `final_runtime_snapshot_elapsed_ms`, including zero-at-the-same-observable-instant semantics, failure elapsed through the deadline, and identical human/JSON values;
- PID change, process exit, restart, negative/zero/reset/unavailable/overflowed non-disk counters, unknown methods, missing started-attempt accounting, and missing/incomplete exact rolling transport-start/charge evidence;
- decimal-byte, decimal-MB/day, bytes/minute, millisecond, percentage, and logical-processor normalization;
- every stable resource violation code and ordinal violation ordering;
- every static host-profile boundary, OS build below minimum, non-x64 OS rejection, insufficient processors/physical memory, battery/power-scheme failure, idle CPU boundary, and available-memory boundary;
- physical, virtual, and unknown virtualization values;
- exact `r0-005-resource-report/v1` top-level order with `source` immediately after `schema_version`, exact `source` three-property order, rejection of missing/additional/reordered/duplicated properties, and the remaining nested property names/order, invariant numeric and UTC serialization, no NaN/infinity, and identical values/calculations in the JSON and human report;
- exact 40-character lowercase commit-SHA format; rejection of abbreviated, uppercase, all-zero, whitespace-padded, prefixed, suffixed, symbolic-ref, branch, malformed, and mismatched values; and proof that the value comes from exact reviewed-tree provenance independently established by the coordinator rather than artifact self-declaration, the developer checkout, or an environment value;
- exact `r0-005-25-target-acceptance/v1` configuration value and rejection of arbitrary labels, filenames, paths, machine/user/property/tenant/customer/target/provider identifiers, environment-derived values, empty values, and changed constants;
- exact 64-character lowercase complete-single-artifact digest format; rejection of uppercase, malformed, partial, all-zero, adjacent-file, stale-file, copied-file, similarly named, substituted-file, and mismatched digests; hashing all artifact bytes through the retained bound handle; and prevention or detection of mutation, delete, rename, or replacement through process creation;
- exact effective `win-x64` self-contained/single-file/native-library-inclusive/untrimmed/symbol-free publish validation; valid complete one-file application-private closure; rejection of framework-dependent, `SelfContained=false`, multi-file, separate main/referenced managed assembly, application-private native DLL, external `.deps.json`/`.runtimeconfig.json`, second executable, plug-in/probing, unexpected executable-code file, or mutable shared-runtime shapes;
- exact reviewed detached-tree build passing; dirty developer worktree not contaminating it; modified tracked, staged, untracked, ignored/generated, or source-root-mutated input rejected at every required check; and current-checkout SHA, branch, or symbolic-ref substitution rejected;
- ignored or preexisting `bin`, `obj`, `publish`, generated source, package, staging, or artifact reuse rejected; user-supplied prebuilt artifact rejected; a different or dirty tree embedding the expected SHA rejected; and an exact artifact digest carrying false embedded commit metadata rejected;
- exact reviewed SHA/materialized commit-root-tree/artifact-metadata three-way match; metadata mismatch retaining independently proven source values as applicable; exact-tree provenance and artifact digest remaining separate; and independent null behavior for `commit_sha` and `executable_sha256`;
- source-root immutability across materialization/restore/publish, fresh external intermediate/output roots, parent/user `Directory.Build.props` or targets rejection, external project/import/compile item rejection, external analyzer/generator/native input rejection, and symlink/reparse/source escape rejection;
- exact SDK plus package IDs/versions/content hashes captured, restore from the exact tree using only the approved source, publish using the frozen graph without implicit change, and no package/SDK substitution after the boundary freezes;
- launched-process image match, fresh controlled native extraction, preexisting/substituted extracted executable rejection, continuous external application-private module-load rejection, Windows system-module exclusion, no private build/provenance path or identity leakage, byte-stable success/provenance-failure JSON, identical human/JSON provenance, and source-definition/matrix/scenario/failure/privacy/native/test drift alignment;
- no configuration, host preflight, process, or scheduler start after source-capture failure; independent source-property capture with only unavailable properties null; `source_identity_invalid` for every source-capture defect; and `report_serialization_invalid` for every independently applicable structural, type, format, ordering, contradiction, pass-with-null, retention, or human/JSON disagreement defect;
- source values remaining unchanged through every later failure scenario, all three present and valid in a passing report, byte-identical success JSON for identical report models, byte-identical source-capture failure JSON for identical failed inputs, and identical source values and `unavailable` rendering in human and JSON reports;
- document-drift tests proving alignment among the fixed-property declaration, exhaustive availability matrix with three explicit source rows and no unnamed catch-all, source-capture semantics, all failure/success scenarios, privacy rules, failure rules, closed violation vocabulary, normative examples, and required future tests;
- exact `measurement_availability` placement after `host_profile`, its nine-property order and Boolean encoding, every implication, the exhaustive per-property availability matrix, every normative source/failure/success scenario, no fabricated zero/false/empty/timestamp evidence, and `report_serialization_invalid` for every inconsistent flag/null/value combination;
- `host_profile_complete = true` before process creation while all eight process-specific fields remain null; the exact `process_started => configuration_accepted && host_profile_complete` implication; static host failure; process-creation failure after static success; initial exact-PID process-profile success; and readiness observed early but not accepted before profile success;
- process architecture mismatch, .NET major-version mismatch, runtime-identifier mismatch, unavailable framework/runtime, accepted workstation and server GC, malformed GC, initial non-normal priority, and every bounded non-normal priority class;
- initial managed/native debugger attachment, initial managed/native profiler attachment, unavailable debugger/profiler state, exact-PID query failure, independently null process fields, no scheduler activation before initial success, and retained valid values after partial capture failure;
- `process_profile_invalid` without automatic `report_serialization_invalid` for structurally valid capture failure, malformed non-null process values adding serialization failure, exact human/JSON agreement, and no private process/debugger/profiler detail leakage;
- priority change during initialization; priority change/restoration during steady state remaining permanent; debugger/profiler attach-detach between ordinary samples being prevented or completely detected; and transition during start barrier, end drain, final stabilization, and pre-termination cleanup;
- event/sample contradiction, loss of enforcement/observation, exact-PID state unavailability, no recovery forgiveness, initial field retention, admissions stopping with only bounded started-attempt closure, no process replacement/scheduler reactivation, and `process_profile_invalid` exactly once in ordinal order;
- acceptance measurement endpoint versus final cleanup and publication endpoints; universal termination arbitration for every captured started PID; no final full profile validation after initial capture failure; conditional final validation after success; exact closed arbiter sequence and retained-handle tie-break; committed intent separating pre-intent from expected post-intent exit; all-nine-true cleanup failure after otherwise complete measurement; and earlier-failure cleanup preserving reached boundaries;
- no process-profile requirement after authorized intent; pre-subscribed exact-PID exit observation; no unclassified started PID; no retroactive intent; stop-issuance failure retaining intent; duplicate/delayed/wrong-PID callbacks; replacement prohibition; and every independently applicable profile/process-exit/cleanup combination appearing once in ordinal order;
- one final model feeding transactional two-report staging/publication; fresh private destination; same-volume complete staged set; one atomic directory rename; report authority only after success; one corrected failure-set attempt; publication failure with bounded `report_serialization_invalid`, nonzero exit, and no authoritative current report; bounded staging cleanup; and preservation of all measurement evidence;
- document-drift tests keeping static host definition, process producing boundary, cleanup and publication endpoints, dynamic phase matrix, implication chain, scenarios, failure rules, privacy, native acceptance, and required future tests complete, disjoint, and aligned, while failing any independent JSON/human authority, stale pass, pre-publication authority claim, missing publication classification, or zero publication-failure exit;
- every one of the ten `caps` properties appearing in the exhaustive availability matrix;
- exact matrix ordering and grouping for the seven integer cap constants and three finite-number cap constants;
- integer cap constants serializing as JSON integers;
- finite-number cap constants serializing deterministically as finite JSON numbers;
- every cap remaining present and non-null when configuration is rejected;
- every cap remaining present and non-null when host-profile validation fails;
- every cap remaining present and non-null when the process never starts;
- every cap remaining present and non-null on readiness timeout;
- every cap remaining present and non-null on start-barrier failure;
- every cap remaining present and non-null on end-drain failure;
- every cap remaining present and non-null on final-snapshot failure;
- no cap value being used as a substitute for an unavailable measurement;
- missing, null, reordered, duplicated, malformed, negative, nonfinite, or changed cap values adding `report_serialization_invalid`;
- success and failure reports using identical cap values and property order;
- human and JSON reports using the same cap constants and units; and
- document-drift tests proving the fixed-property list, matrix rows, exact-value table, and normative examples remain aligned;
- exact `by_probe_kind` vocabulary and exact `dns`, `gateway_icmp`, `https`, `tcp`, `unknown` order;
- exact `by_outcome` vocabulary and exact `canceled`, `failed`, `incomplete`, `succeeded`, `timed_out` order;
- exact `{code,count}` property order, mandatory zero-count entries, and the normative zero-attempt representation;
- rejection of an omitted zero entry, duplicate code, unknown or extension code, additional entry property, negative count, and nonintegral count;
- equality between each outcome scalar and its corresponding array entry, plus every started/completed/outcome accounting identity;
- mutually exclusive probe-kind assignment and mutually exclusive outcome assignment for every invocation;
- every started invocation receiving exactly one outcome and rejection of an outcome without a corresponding started invocation;
- separate accounting for every HTTPS provider request;
- complete required workload passing only after the Gateway logical-round/terminal-ICMP rule, enabled DNS, every configured HTTPS provider, and all 25 TCP members are distinctly exercised during steady state, with terminal failed/timed-out attempts eligible and no exact success count required;
- a zero-attempt run, one missing TCP target, 24 distinct TCP targets plus duplicate work on another, one missing HTTPS provider, aggregate HTTPS counts masking an unexercised provider, missing Gateway coverage, and enabled-but-unexercised DNS each adding `acceptance_workload_incomplete` and failing;
- disabled DNS producing no required member and both DNS flags false; startup-only work and pre-start cancellation not satisfying coverage; and a started nonterminal attempt satisfying neither coverage nor existing attempt closure while adding both applicable violations;
- unknown, missing, duplicate, unmanifested, mutated, or no-longer-configured internal identities; `configuration.target_count`/manifest mismatch; configured/exercised mismatch; and configuration or manifest mutation all failing deterministic reconciliation;
- exact `workload_coverage` top-level placement and property order, JSON Boolean and nonnegative-integer encodings, derived `all_required_exercised`, agreement between human and JSON calculations, and rejection of missing, reordered, malformed, additional, or inconsistent fields;
- readiness remaining true from first observation through final snapshot; loss during the start barrier, after initial member coverage, during end drain, or during final stabilization; transient false-to-true loss not forgiven; and exact readiness-event/sampled-level reconciliation;
- scheduler disposal, disablement, replacement, epoch change/loss, or workload-ownership loss failing even when readiness remains true, with token-bucket and rolling-ledger continuity across barriers and no same-run restart/reactivation after continuity failure;
- every due Gateway, enabled-DNS, HTTPS-aggregate, and enabled-TCP round receiving exactly one terminal or explicit scheduler-level incomplete reduction before its deadline, with silent disappearance, duplicate/wrong-member/late reduction, and ledger-identity failure producing `acceptance_scheduler_inactive`;
- exact `runtime_continuity` placement and `monitoring_ready_continuous`, `scheduler_active_continuous`, `scheduler_epoch_unchanged`, `all_due_rounds_reconciled` property order, derived JSON Booleans, all-four-true pass requirement, and identical human-report values;
- privacy exclusion of target/provider identities, endpoints, hosts, ports, operator IDs, scheduler keys/epochs, rolling-ledger entries/timestamps, readiness-transition timestamp details, route/interface values, internal member identities, and private paths from workload/continuity evidence, with the deterministic zero-attempt fragment remaining valid diagnostic output but never a passing nonempty-manifest run;
- exact fixed charge derivation for every supported method, adapter charge input/override rejection, measured-below-table and measured-above-table attribution, and one shared configured-budget/acceptance table without drift;
- byte-identical totals and reports from different adapters with identical measured bytes, one exact 8,192-byte rolling charge per concurrent HTTPS provider, deterministic aggregate network totals immediately below, exactly at, and above the 100,000-byte/minute boundary, and exact rolling admission immediately below and at 100,000 fixed charged bytes;
- complete exact transport-start/charge records proving no rolling monotonic 60-second window over activation or later runtime has more than 60 starts or at least 100,000 fixed charged bytes; deterministic candidate order and exact-age expiry; `rolling_attempt_budget_exceeded`/`rolling_network_budget_exceeded` appearing exactly once and in ordinal order on actual violations; and `attempt_accounting_incomplete` when either invariant cannot be proved;
- route/interface discovery remaining outside token and transport-invocation counts, with same-pass family-specific `Connected`/`Metric`/`DisableDefaultRoutes` resolution, stable A/B membership changes for both flag-toggle directions, exact false-only eligibility and tuple selection, all-disabled `missing_route`, API/incomplete-row and persistent-instability `route_selection_unavailable`, disabled-route workload exclusion, route or policy changes after commit deferred to the next round, a drift check for the false requirement in every eligibility summary, and no policy value, VPN/interface identity or type, or other route/interface candidate field in either report;
- cancellation before transport invocation not counting as started and cancellation after start counting only as `canceled`;
- timeout counting only as `timed_out` and ordinary failure counting only as `failed`;
- attempt-level incomplete adding `attempt_accounting_incomplete` and failing acceptance;
- scheduler-level pre-start incomplete remaining absent from both attempt arrays and not incrementing `attempts.started`;
- an unknown method counting under `unknown`, using the deterministic zero-sentinel/measured-byte failing representation, adding `unknown_probe_method`, and never passing acceptance;
- missing, malformed, negative, nonintegral, adapter-supplied, overridden, and conflicting charged values for recognized methods adding `attempt_accounting_incomplete` while totals remain table-derived;
- both `attempt_accounting_incomplete` and `unknown_probe_method` appearing exactly once and in ordinal order when both apply;
- byte-for-byte stable JSON for identical report models;
- byte-for-byte stable JSON for identical failure models, including identical source-capture failures; pass reports with all three valid source values, all availability flags true, and no null acceptance field; and exact human `unavailable` rendering for every source or boundary-null value;
- identical attempt counts, exact method-charge-derived network totals, rolling-invariant results, timing boundaries, startup values, steady-state values, and disk values in the human and JSON reports, all derived from the same started-attempt records;
- privacy exclusion of machine names, usernames, domain membership, property/tenant/customer names or identifiers, serials, device UUIDs, product keys, MAC/IP addresses, disk serials, cloud instance IDs, arbitrary environment variables, absolute or relative paths, executable or extraction filenames, assembly names, module identities, checkout directories, branch names, endpoints, targets/providers, `DisableDefaultRoutes` values, VPN status, interface identities/types, next hops, other route/interface fields, DNS answers, DNS-correlation snapshot fields, payloads, headers, bodies, exception text, and credentials, including identical exclusions in JSON and human evidence and only the three approved provenance values in `source`;
- deterministic `host_profile` property order and rejection of missing or additional host-profile fields;
- synthetic-listener and general acceptance cleanup success/failure, separately and together, with final report outcome known only after bounded cleanup.

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

Pre-registration configuration and all operational evidence are intentionally volatile. Restart or readiness degradation loses counters, transitions, and flapping history. Restart reconstructs enabled targets as `unknown`; scheduler activation after readiness moves them to `baselining`. A malformed restart-time override can therefore stop probing until configuration is fixed and another restart succeeds; R0-005 accepts that visible availability loss rather than silently reusing stale targets/providers, and this scoped exception does not apply once an approved durable configuration owner exists. In addition, the inherited R0-004 limitation remains: simultaneous journal and lifecycle-marker failure can leave the durable marker stale until restart even though in-process readiness is false. R0-005 must stop on that in-process false state.

## 12. Delivery boundary

R0-005 is complete only when the implementation, deterministic tests, native evidence, and resource report satisfy this document. Completion does not authorize:

- registration or identity creation;
- persistence or cloud delivery;
- target-management surfaces;
- alerts, incidents, causal claims, or “protected” status;
- R0-006 or R0-013 work.

Any implementation need that conflicts with this boundary is a new requirements decision and must stop for review.
