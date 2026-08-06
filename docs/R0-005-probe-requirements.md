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
- Technical Feasibility section 7.2 proposes two stable DNS names to reduce authoritative-domain-specific false negatives. R0-005 explicitly supersedes that prototype heuristic for R0-005 only and defines one independently scheduled resolver-path probe with one normalized logical query identity, one terminal-root-dot absolute resolver name, one resolver invocation, one transport attempt, and one unambiguous reducer result per logical round. Pairwise-distinct multi-provider HTTPS supplies separate internet-path diversity, and the existing correlation rules prohibit one DNS signal from being presented as a broad property outage. A two-name DNS design would require reviewed aggregation, per-child token/charge, concurrency/fairness, coverage-identity, snapshot-publication, and budget semantics that this task does not silently invent. The accepted tradeoff is that failure of the configured authoritative domain can move only the DNS probe through its own `suspect` or `offline` state. Any future multi-name design requires a separately reviewed requirements change.
- Data Model/API supplies the broader persisted state vocabulary. R0-005 maps to it without changing contracts and defers durability until authenticated identity exists.
- MVP Backlog assigns target management to the later local-configuration task. R0-005 pulls forward only the narrow execution/evidence primitives listed in section 2.5.
- [Security and Privacy Guardrails](security-guardrails.md) generally require malformed durable or updateable configuration to preserve a safely stored accepted last-known-good value. R0-005 has no authenticated or durable configuration owner and no stored last-known-good probe configuration, so it uses the guardrail's documented, narrow pre-registration exception: atomic, visible fail-closed startup rejection with no scheduler and no stale substitution. This is an R0-005-only source reconciliation, not general product policy.
- R0-005 configurable hostname handling is intentionally narrower than general DNS, URI, resolver, TLS, or Public Suffix List capabilities. It accepts only the closed ASCII LDH grammar in section 2.3 and rejects direct Unicode plus every ASCII label beginning with `xn--`. Any internationalized-hostname or ACE/punycode support requires a separately reviewed future requirements change and cannot be inferred from a library or platform behavior.
- R0-005 also binds configuration preprocessing to exact UTF-16 code units, TCP literal classification to the closed grammars, HTTPS transport construction to the pre-`Uri` character parser, and suffix matching to the pinned-byte PSL parser in section 2.3. Platform parser permissiveness, replacement decoding, resolver search behavior, URI normalization, and library-default suffix data cannot broaden those contracts.

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

#### Shared configurable-hostname preprocessing and validation

R0-005 uses one private configurable-string preprocessing procedure and one private ASCII LDH label validator for the DNS logical query name, a TCP target DNS hostname, the complete built-in or custom HTTPS-provider URL and its raw authority-host slice, and every other configurable hostname input already governed by this section. TCP IPv4 and IPv6 literals use the closed classifiers and grammars below. PSL rule lines do not use configurable-string preprocessing. No second configurable-string, hostname, URL, TCP-literal, or PSL parser is permitted.

A .NET configuration value is processed as its ordered sequence of 16-bit UTF-16 code units. The procedure must not decode Unicode scalar values, enumerate `System.Text.Rune`, replace invalid input with U+FFFD, or let a decoder exception determine the result:

1. From the beginning, repeatedly remove the current first UTF-16 code unit only while its numeric value is exactly `0x0009`, `0x000A`, `0x000B`, `0x000C`, `0x000D`, or `0x0020`.
2. From the end, repeatedly remove the current last UTF-16 code unit only while its numeric value is exactly one of that same six-code-unit set.
3. Stop at the first nonmember at each edge. No other code unit is trimmed. This is not a call to or interpretation of configuration-provider behavior, Unicode whitespace categories, parameterless `String.Trim()`, `Char.IsWhiteSpace`, a decoder, or a scalar-value API.
4. Reject when no code unit remains; when any remaining code unit is `0x0000` through `0x001F` or `0x007F`; or when any remaining code unit is greater than `0x007F`. An interior `0x0009` through `0x000D` fails here; an interior `0x0020` remains ASCII at this stage and is rejected by each closed hostname or URL field grammar rather than trimmed.

The exact leading/trailing outcomes are normative:

| Outcome | UTF-16 code units |
| --- | --- |
| Trimmed only while leading or trailing | `0x0009`, `0x000A`, `0x000B`, `0x000C`, `0x000D`, `0x0020` |
| Never trimmed and therefore rejected | `0x0085`, `0x00A0`, `0x1680`, `0x180E`, `0x2000` through `0x200A`, `0x200B`, `0x2028`, `0x2029`, `0x202F`, `0x205F`, `0x3000`, `0xFEFF`, U+FFFD (`0xFFFD`), every surrogate code unit, and every other value greater than `0x007F` |

A valid surrogate pair, an unpaired high surrogate from `0xD800` through `0xDBFF`, an unpaired low surrogate from `0xDC00` through `0xDFFF`, and every other ill-formed UTF-16 sequence are invalid through the same bounded path. Direct Unicode and a literal U+FFFD are likewise invalid. No decoder exception, replacement character, scalar enumeration, culture, Unicode category, ICU/NLS behavior, CLDR data, operating-system globalization data, runtime servicing data, or configuration-provider behavior may affect the result.

The shared ASCII LDH validator accepts only a non-root hostname that has already passed the exact UTF-16 preprocessing where applicable and whose field wrapper has already applied its terminal-dot policy. The shared validator must not detect, permit, remove, or restore a terminal root dot. It operates directly on the remaining ASCII code units. Each label must be 1–63 ASCII octets, contain only `A-Z`, `a-z`, `0-9`, or `-`, and begin and end with an ASCII letter or digit. Interior hyphens are allowed. Underscore and every other punctuation character are forbidden. Empty labels, a leading or trailing dot, and consecutive dots are forbidden. A configurable non-root hostname must contain at least two labels and be no more than 253 ASCII octets. PSL rule bodies reuse only the per-label LDH and case-mapping rules and may contain one label.

Normalization maps each U+0041 through U+005A ASCII uppercase letter to the corresponding U+0061 through U+007A lowercase letter by adding hexadecimal `0x20`. Lowercase letters, digits, hyphens, and dots remain unchanged. The normalized result is compared using ordinal semantics only. The implementation must not call `String.ToLower()` with current culture, another current-culture or Unicode case conversion, Unicode normalization, `System.Globalization.IdnMapping`, `GetAscii`, `GetUnicode`, `Uri.IdnHost`, `Uri.DnsSafeHost`, or another globalization or IDN conversion API for validation or normalization.

After that exact ASCII mapping, every label whose normalized form begins with `xn--` is invalid, including a bare `xn--` label. The comparison is ordinal; ASCII case insensitivity results only from the exact mapping above. No ACE or Unicode decoding occurs and no attempt is made to determine whether an ACE label is canonical. Thus `xn--bcher-kva.example`, `XN--BCHER-KVA.example`, `xn--zca.example`, `xn--3xa.example`, any other label beginning with `xn--`, and direct Unicode such as `bücher.example` are invalid in every configurable hostname field. This is an intentional R0-005 scope restriction. Later IDN support requires a separate reviewed requirements task that binds its Unicode/IDNA profile, globalization data, runtime mode, dependencies, licensing, update lifecycle, and security review; it cannot be inferred from HTTP, DNS, URI, resolver, TLS, or public-suffix library behavior.

Any UTF-16 preprocessing, ASCII, control, surrogate, label, length, Unicode, ACE-prefix, URL, TCP-host, DNS-host, provider-host, or registrable-domain customer-configuration failure atomically rejects the complete startup configuration, contributes exactly one set-like `configuration_invalid`, activates no scheduler or valid subset, and creates no resolver or transport work. Multiple defects, including combinations with other configuration defects, still contribute only one `configuration_invalid`. Rejection exposes no original or preprocessed input, code unit, replacement value, exception, index, rejected label, Unicode value, ACE label, parser or URI-normalization detail, environment or globalization setting, ICU/NLS mode or version, or other customer-controlled value.

#### TCP target definition

Each statically configured TCP target contains:

| Field | Requirement |
| --- | --- |
| `id` | Required stable local identifier; trimmed; 1–128 characters; unique ignoring case; not a cloud identity. |
| `label` | Required operator label; trimmed; 1–160 characters; never logged or used as evidence. |
| `host` | Required single DNS hostname, strict IPv4 literal, or strict IPv6 literal; the complete value uses the exact UTF-16 preprocessing once and the closed classification below; no URI syntax. |
| `port` | Integer from 1 through 65535. |
| `enabled` | Boolean; only enabled entries are scheduled and counted toward the limit. |
| `interval` | Optional duration; default 60 seconds; minimum 15 seconds; maximum 15 minutes. |

Zero TCP targets is valid. At most 25 TCP targets may be enabled. All IDs, including disabled entries, must be unique ignoring case.

Apply the exact UTF-16 preprocessing once to the complete TCP `host`, then classify it in this exact order: (1) strict IPv6-literal candidate, (2) strict IPv4-literal candidate, and (3) DNS-hostname candidate. Once a value enters either literal-candidate branch, failure in that branch is invalid configuration and cannot fall through to hostname validation or resolution.

An IPv6-literal candidate is any value containing `:`, `[`, `]`, or `%`. Brackets and `%` are always invalid because the port is separate and R0-005 accepts no zone or scope text. After rejecting them, permit only ASCII hexadecimal digits and `:`; a dot is invalid, so embedded dotted-decimal IPv4 is never accepted. Each explicit hextet has 1–4 hexadecimal digits. Permit at most one `::`. Without `::`, require exactly eight hextets. With `::`, permit zero through seven explicit hextets total, require the compression to replace at least one all-zero hextet, expand to exactly eight hextets, and permit no other empty hextet. Parse in big-endian hextet order into exactly 16 address bytes. Reject `:::`, multiple `::`, wrong expanded counts, overlong or nonhexadecimal hextets, bracketed or scoped text, and dotted embedding before the final address predicate is evaluated.

After the strict textual grammar has decoded exactly 16 bytes `b[0]` through `b[15]`, a directly configured IPv6 literal is accepted if and only if one (and therefore exactly one) of these three disjoint fixed predicates is true:

```text
accepted_direct_ipv6_literal =
    exact_loopback
    OR r0_005_unique_local
    OR r0_005_direct_global

exact_loopback =
    b[0..14] == 0x00
    AND b[15] == 0x01

r0_005_unique_local =
    (b[0] & 0xFE) == 0xFC

r0_005_direct_global =
    (b[0] & 0xE0) == 0x20
```

`b[0..14] == 0x00` means every byte from `b[0]` through `b[14]` is exactly `0x00`. `r0_005_unique_local` is exactly `fc00::/7`, from `fc00::` through `fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff`. `r0_005_direct_global` is exactly `2000::/3`, from `2000::` through `3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff`. Reject every decoded address outside this union. The byte predicates are the sole source of direct-literal configuration validity; explanatory labels such as unique-local or global-unicast cannot add, remove, or reclassify an address.

This is an immutable R0-005 direct-literal policy, not a universal definition of IPv6 address types or reachability. Startup validity must not depend on `IPAddress` classification helpers, `IsIPv6LinkLocal`, `IsIPv6Multicast`, an operating-system address or routing classification, an IANA registry or mutable special-purpose-prefix list, a package or network source, actual reachability, DNS, interface assignment, source-address selection, culture, or globalization. Construction of a direct socket address from the already validated 16 bytes is permitted, but no platform result may alter the predicate outcome.

The fixed `2000::/3` predicate intentionally accepts every syntactically valid address in that range, including documentation and other special-purpose subranges. Thus `2001:db8::1` is valid startup configuration; acceptance does not assert public reachability or production suitability, and an inability to connect is ordinary runtime TCP evidence under the existing state machine. Conversely, a special-purpose address outside the fixed union is rejected even if a current registry or platform describes it as globally reachable. No current or future IANA assignment silently changes R0-005 validity. Expansion beyond exact loopback, `fc00::/7`, and `2000::/3` requires a separately reviewed requirements change.

The exact direct-literal prefix boundaries are binding: for `fc00::/7`, first byte `0xFB` rejects, `0xFC` accepts, `0xFD` accepts, and `0xFE` rejects under `(b[0] & 0xFE) == 0xFC`; for `2000::/3`, first byte `0x1F` rejects, `0x20` accepts, `0x3F` accepts, and `0x40` rejects under `(b[0] & 0xE0) == 0x20`. Every second and later byte may range independently from `0x00` through `0xFF`; only the stated first-byte bits determine membership.

Normative direct IPv6 startup-validation vectors are:

| Result | Exact configured literals |
| --- | --- |
| Accept: exact loopback | `::1` |
| Accept: `fc00::/7` | `fc00::`, `fc00::1`, `fd00::1`, `fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff` |
| Accept: `2000::/3` | `2000::`, `2000::1`, `2001:db8::1`, `3fff::1`, `3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff` |
| Reject: grammar failure or outside the fixed union | `::`, `::2`, `1::`, `100::1`, `64:ff9b::1`, `64:ff9b:1::1`, `1fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff`, `4000::`, `fbff:ffff:ffff:ffff:ffff:ffff:ffff:ffff`, `fe00::`, `fe80::1`, `ff02::1`, `::ffff`, `::ffff:ffff`, `::ffff:0:1`, `::ffff:192.0.2.1`, and every other decoded value outside exact loopback, `fc00::/7`, and `2000::/3` |

The compatible-form vectors have these exact binding outcomes:

| Input | Grammar | Final predicate | Startup result | Resolver |
| --- | --- | --- | --- | --- |
| `::2` | valid | false | `configuration_invalid` | never invoked |
| `::ffff` | valid | false | `configuration_invalid` | never invoked |
| `::ffff:ffff` | valid | false | `configuration_invalid` | never invoked |

Each of `::2`, `::ffff`, and `::ffff:ffff` is syntactically valid under the closed IPv6 hextet/compression grammar and decodes to exactly 16 bytes, but exact loopback, `fc00::/7`, and `2000::/3` each evaluate false. Each therefore fails the final fixed direct-literal predicate and is a literal-branch configuration failure outside the permitted union. Exact decoded bytes may be used privately only to evaluate that predicate. No platform semantic address classifier or mapped/compatible conversion may override the result, and none may fall through to hostname validation or resolution. Each atomically prevents creation of a scheduler, valid subset, resolver or transport work, token consumption, attempt accounting, retry, or fallback and contributes one set-like `configuration_invalid`, including when combined with another configuration defect. The literal text, decoded bytes, failed predicate, compatibility classification, and parser or platform exception details remain private.

The final predicate authoritatively and sufficiently rejects the all-zero unspecified address, multicast, link-local, IPv4-mapped IPv6 including the 80-zero-bit-plus-`ffff` prefix, and deprecated IPv4-compatible values whose first 96 bits are zero except exact loopback `::1`. Those descriptions explain results of the fixed union and do not create a second semantic acceptance rule. A rejected direct IPv6 literal remains a literal-branch configuration failure and cannot fall through to hostname validation or resolution. The canonical private identity and connection input for an accepted value are the exact decoded 16 bytes, never a platform-selected text rendering or hostname-level API.

Invalid direct IPv6 configuration atomically rejects the complete startup configuration, contributes exactly one set-like `configuration_invalid` even when combined with other defects, activates no scheduler or valid subset, and creates no resolver or transport work. It exposes no input text, address bytes, matched or failed predicate, prefix, registry classification, exception, platform result, or other private classification detail.

An IPv4-literal candidate is any value for which every character is an ASCII digit or dot. It is also any value whose dot-separated components are each either one or more ASCII decimal digits or `0x`/`0X` followed by one or more hexadecimal digits, with at least one hexadecimal component. The only accepted form is exactly four decimal components separated by exactly three dots. Each component contains 1–3 digits, has no leading zero when longer than one digit, and has value 0–255. Reject shorthand, empty or extra components, hexadecimal, octal-looking leading zeros, signs, brackets, a terminal root dot, remaining whitespace, and values over 255. Direct IPv4 unspecified `0.0.0.0`, multicast `224.0.0.0/4`, and limited broadcast `255.255.255.255` are invalid; loopback, private, link-local, and ordinary unicast literals remain valid. The canonical private identity and direct connection input are the four address bytes.

Only a value that entered neither literal-candidate branch may be a TCP hostname. Its field wrapper permits zero or one terminal root dot, removes it exactly once, rejects multiple terminal dots, applies the shared non-root ASCII LDH validator, requires at least two labels, and rejects direct Unicode and every normalized `xn--` label. It creates private `tcp_logical_host_name` as the normalized non-root hostname and exactly `tcp_absolute_resolver_name = tcp_logical_host_name + "."`. The sole Windows resolver argument is `tcp_absolute_resolver_name`; the adapter must preserve the terminal dot and must not pass the relative form, append suffixes, perform suffix search or devolution, iterate a search list, retry another hostname, or perform hidden alternate-name expansion. Dotted and undotted equivalent inputs produce the same logical identity, absolute resolver argument, candidate set, schedule, budget, and behavior.

Normative valid TCP hosts include `127.0.0.1`, `192.0.2.1`, `255.255.255.254`, every accepted direct IPv6 vector in the table above, `service.example`, and `service.example.`. Normative invalid hosts include `0.0.0.0`, `224.0.0.1`, `255.255.255.255`, `65535`, `20.2`, `20.65535`, `128.1.2`, `1.1.1.010`, `0300.0.0.1`, `0x2F`, `0xC0.0x00.0x02.0x01`, `192.0.2.1.`, `256.0.0.1`, malformed dotted decimal, every rejected direct IPv6 vector in the table above, bracketed or scoped IPv6, IPv4-embedded dotted IPv6, malformed compression, and any literal-like input failing its selected branch. `192.0.2.1` and every accepted directly configured IPv6 literal always select the direct-literal branch and never invoke a resolver.

The host field also rejects a scheme, user information, path, query, fragment, credential-assignment syntax, wildcard, subnet, address range, or multiple-host expression. Credentials are forbidden in every target field. One definition represents one explicit host/port service; no range expansion, discovery, scan, or automatic fallback is allowed. The one-resolver-snapshot, deterministic-address-selection, one-connect, and no-fallback behavior in section 3.4 remains unchanged.

Validation errors identify only the configuration section, bounded field name, and stable error code. They do not reproduce IDs, labels, hosts, values, exception messages, or provider payloads.

#### DNS definition

The DNS probe has one enabled flag and one logical query-name configuration. The default logical configuration is `example.com`. A custom override is one textual DNS name validated by the shared closed preprocessing and ASCII LDH algorithm above. R0-005 rejects direct Unicode and every label beginning with `xn--`, and permits either no terminal root dot or exactly one terminal root dot.

DNS applies the shared preprocessing first, then this closed field-specific order:

1. Permit either no terminal root dot or exactly one terminal root dot. Reject multiple terminal dots rather than repeatedly removing dots.
2. Remove at most one terminal root dot.
3. Apply the shared ASCII hostname-label validator. Reject the remaining non-root value if it is empty; begins or ends with a dot; contains consecutive dots or any empty label; contains fewer than two labels or no label-separating dot; or exceeds 253 ASCII octets. Because every accepted input character is ASCII, one character equals one octet for all label and complete-name bounds.

Validate and normalize each label independently in original left-to-right order with the one shared ASCII validator. No Unicode, ACE, IDN, IDNA, or globalization conversion path is permitted. Host language, DNS suffix settings, resolver behavior, and platform enumeration order cannot affect validation or normalization.

Join the accepted normalized lowercase ASCII labels using exactly one literal dot to produce `dns_logical_query_name`. Recheck that it has at least two labels, every label is 1–63 ASCII octets, and the complete non-root name is at most 253 ASCII octets. This is the private immutable configuration or workload identity where such identity is required and is never emitted. Derive exactly `dns_absolute_resolver_name = dns_logical_query_name + "."`; it contains exactly one terminal root dot and is the only name supplied to the approved system-resolver adapter.

The following outcomes are normative. Valid inputs include code unit `0x0020` + `Example.COM` + `0x0020` producing `example.com`; `0x0009` + `example.com.` + `0x000D` producing `example.com`; `example.com`; `example.com.`; `a.example`; `a-b.example`; a label of exactly 63 ASCII letters followed by `.example`; conforming complete non-root names of exactly 252 and 253 ASCII octets with every label at most 63 octets; an ordinary ASCII TCP hostname; an ordinary ASCII custom HTTPS-provider hostname; and the built-in Google and Cloudflare provider hosts. Invalid inputs include all-empty trim-set input; any trim-set code unit in an interior position; a valid surrogate pair around a hostname; an unpaired high or low surrogate; U+FFFD; embedded `0x0000`; `0x007F`; names wrapped in U+0085, U+00A0, U+1680, U+180E, every value `0x2000` through `0x200A`, U+200B, U+2028, U+2029, U+202F, U+205F, U+3000, or U+FEFF; direct Unicode such as `bücher.example`; `xn--bcher-kva.example`; `XN--BCHER-KVA.example`; `xn--zca.example`; `xn--3xa.example`; a bare `xn--` label; any other label beginning with `xn--` in any ASCII case; `printer`; `printer.`; `_service.example`; `service._tcp.example`; `-host.example`; `host-.example`; `host..example`; `.host.example`; `host.example..`; `example.com..`; `example.com...`; an empty label; a 64-ASCII-octet label; a 254-ASCII-octet complete non-root name; a Unicode or ACE TCP hostname; a Unicode or ACE HTTPS-provider host; and any canonical-URI correspondence failure. Field-wrapper vectors must prove no double removal for `example.com`, `example.com.`, `example.com..`, `example.com...`, trim-set-wrapped `example.com.`, and trim-set-wrapped `example.com..`. The existing prohibited URI, port, user-information, credential, wildcard, customer-identifier, list, array, multiple-name, fallback, search-expression, and search-domain syntax remains invalid.

The default produces logical name `example.com` and absolute resolver name `example.com.`. Equivalent ASCII case and optional-terminal-dot representations produce one identical `dns_logical_query_name`, `dns_absolute_resolver_name`, workload identity, resolver argument, report, evidence, schedule, budget, and behavior. No field records the input representation.

Every DNS preprocessing, ASCII, label, ACE-prefix, or complete-name validation failure follows the shared atomic-rejection rule: exactly one existing set-like `configuration_invalid`, no scheduler or valid subset, and no resolver invocation. It creates no DNS-specific error or violation code and exposes no rejected code unit, label, name, parser detail, environment setting, or field value. Multiple DNS defects, or a DNS defect combined with another configuration defect, still contribute only one `configuration_invalid`.

Each DNS logical round invokes the approved system-resolver adapter exactly once with exactly `dns_absolute_resolver_name`. The adapter must preserve the terminal dot and must not pass the relative/non-root form, enable or emulate DNS suffix search, append a primary or connection-specific suffix, perform devolution or search-list iteration, or attempt a fallback or alternate name. Host DNS suffix configuration cannot alter the resolver argument. A resolver API that cannot preserve this absolute-name contract is nonconforming and must not be used. The resolver may return its normal complete A/AAAA result for that one absolute name; existing result reduction remains one DNS transport attempt, one token, one DNS method charge, one logical outcome, and one published snapshot. Failure, timeout, or no valid address never authorizes a retry or alternate or suffix-expanded name. Returned addresses and both private DNS-name forms are never logged, persisted, or added to evidence.

#### HTTPS provider definition

The default configuration contains exactly two reviewed, separately operated providers:

| Stable ID | Declared `operator_id` | URL | Required result |
| --- | --- | --- | --- |
| `google-generate-204` | `google` | `https://www.google.com/generate_204` | GET with no request body; valid TLS and hostname; no redirect; HTTP 204; no required body. |
| `cloudflare-trace` | `cloudflare` | `https://www.cloudflare.com/cdn-cgi/trace` | GET with no request body; valid TLS and hostname; no redirect; HTTP 200; status-only validation with no expected-body rule. |

Every HTTPS provider child attempt, whether built in or supplied by a valid custom provider-set override, uses the uppercase invariant HTTP method token `GET`. The method is fixed by the R0-005 contract and is not a startup-configuration or provider-override field. `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`, `CONNECT`, `TRACE`, and every extension or custom method are prohibited. A transport adapter must not replace GET with HEAD as an optimization, including for a status-only provider. A provider definition cannot request or imply another method. If an implementation exposes an internal method parameter, that parameter is fixed to GET and any contradictory value is rejected as an implementation/configuration invariant before scheduler activation.

Every provider GET has no request body, uploaded payload, caller-supplied request content, authentication, cookie, redirect-following behavior, certificate exception, or HTTP fallback. Request content must not be derived from customer, device, property, tenant, target, endpoint, configuration, or other identity. The request does not emit `Content-Length` or another entity-body header unless the reviewed HTTP stack deterministically emits a zero-body representation that does not change semantics.

The Google default is supported by current Chromium source, which defines `https://www.google.com/generate_204` as its HTTPS-first default probe and treats HTTP 204 as validated:

- <https://chromium.googlesource.com/chromium/src/+/HEAD/chrome/android/java/src/org/chromium/chrome/browser/net/connectivitydetector/ConnectivityDetector.java>

Cloudflare's WARP documentation directs clients to `https://www.cloudflare.com/cdn-cgi/trace` to verify connectivity/routing:

- <https://developers.cloudflare.com/warp-client/get-started/linux/>

Cloudflare documents that the trace response contains connection metadata, including an apparent public IP and Cloudflare colo/location information:

- <https://developers.cloudflare.com/privacy-proxy/get-started/>

For that reason, the Cloudflare default is status-only. Its GET uses response-headers-first streaming; once status is known, the implementation disposes the response without opening or reading the content stream. It must not parse, log, persist, compare, expose as health evidence, or otherwise retain the body. A defensive reader exists only for a provider with an expected-body rule, must stop at 256 bytes plus one overflow-detection byte, and must still discard every byte.

`http://www.msftconnecttest.com/connecttest.txt` is explicitly not a default. Microsoft's NCSI documentation defines it as a plain-HTTP probe with HTTP 200 and expected text:

- <https://learn.microsoft.com/en-us/windows-server/networking/ncsi/ncsi-frequently-asked-questions>

Plain HTTP conflicts with this repository's outbound HTTPS-only rule. During requirements verification, the corresponding HTTPS request also failed strict Windows hostname validation. R0-005 must not disable certificate validation, fall back to HTTP, add a provider exception, or treat the Microsoft endpoint as required.

An operator may override the full HTTPS provider set at startup, but every accepted custom provider still uses GET with no request body and no method override. The provider-set override is subject to all of these rules:

- exactly 2–4 entries;
- stable IDs are required, 1–64 characters, and unique ignoring case;
- `operator_id` is required for every entry, trimmed, 1–64 ASCII characters, and unique ignoring case across the configured provider set;
- `operator_id` contains only lowercase ASCII letters, digits, and hyphens and begins and ends with an ASCII letter or digit;
- every URL satisfies the closed UTF-16, scheme, authority, port, host, and path grammar below before any URI parser runs;
- the canonical transport URL constructed by that grammar is the only URL supplied to the reviewed .NET HTTPS URI and transport stack;
- normalized endpoint hosts are pairwise distinct under ordinal comparison after the exact ASCII normalization below;
- across the complete configured set, every provider ID, declared `operator_id`, normalized endpoint host, and normalized registrable domain is pairwise distinct;
- redirects are disabled and rejected;
- cookies, authentication, uploads, custom authorization, and customer identifiers are forbidden;
- each entry contains an exact integer expected status from 200 through 299, inclusive; every 1xx, 3xx (including 300–308), 4xx, and 5xx value is invalid configuration;
- an optional exact expected body may be configured as UTF-8 text of at most 256 bytes;
- an expected body is invalid when expected status is 204 or 205; either status remains valid without an expected body;
- if no exact body is configured, the provider still uses GET, body content is not a health condition, and the content stream is not opened or read;
- if an exact body is configured, the provider uses GET and at most 256 bytes plus one overflow-detection byte are read, compared without normalization, and discarded; HEAD cannot substitute and a missing, truncated, oversized, unreadable, mismatched, or otherwise invalid body retains the existing terminal provider-failure classification;
- the fixed method is contract behavior rather than evidence: no HTTP request-method field or method string enters startup configuration, provider overrides, JSON or human reports, logs, persistence, probe evidence, health output, support output, or metrics;
- endpoint bodies, request bodies, headers, hostnames, URLs, DNS answers, and `operator_id` values never enter logs, persistence, probe evidence, health output, support output, or metrics.

Apply the exact UTF-16 preprocessing once to the complete configured URL string. The raw authority host is then an exact slice of that already-preprocessed ASCII string and is not independently trimmed or decoded. Reject any remaining control or non-ASCII code unit before any URI parser runs.

The scheme parser ASCII-folds only U+0041 through U+005A by the existing exact plus-`0x20` rule and requires the result to begin with exactly `https://`. Exactly two forward slashes follow the scheme colon. Another or missing scheme, one slash, three or more scheme slashes, or any backslash is invalid. The authority begins immediately after `https://` and ends at the first forward slash or at end of string. The authority must be nonempty. `?` or `#` anywhere and backslash anywhere in the complete URL are invalid.

The authority is parsed at the character level. It is invalid if it contains `@`, `[`, `]`, `%`, ASCII whitespace or a control, more than one colon, an empty host, an empty port, or any other unauthorized delimiter. Any `@` is invalid, so both one and multiple user-information separators fail. HTTPS-provider IPv4 and IPv6 literals remain invalid; a colon can only introduce the optional port. Permit no explicit port or exact ASCII `:443`. Reject nondecimal text, signs, `:0443`, `:0`, every non-443 port, an empty port, or multiple colons. Effective port is always 443, and the canonical authority omits it.

The raw authority host wrapper permits zero or one terminal root dot, removes that dot exactly once, and rejects multiple terminal dots. It then applies the shared non-root ASCII LDH validator, requires at least two labels, and rejects Unicode, every normalized `xn--` label, every IP literal, underscore, edge hyphens, empty labels, and every other prohibited character. The result is the one private normalized non-root `https_transport_host`. The shared validator itself never removes a dot.

If the configured URL ends after the authority, `canonical_path` is `/`; otherwise it is the substring beginning with the first forward slash. The path permits only ASCII letters, digits, `-`, `.`, `_`, `~`, `!`, `$`, `&`, `'`, `(`, `)`, `*`, `+`, `,`, `;`, `=`, `:`, `@`, and `/`. Reject `%`, `?`, `#`, backslash, controls, whitespace, non-ASCII, and any path segment exactly `.` or `..`. Do not percent-decode, percent-encode as an input normalization, collapse slashes, remove dot segments, or otherwise normalize the path. Preserve permitted path-character case exactly.

Construct exactly `canonical_https_url = "https://" + https_transport_host + canonical_path`. It never contains the original terminal root dot, explicit `:443`, user information, query, or fragment. Only then may the implementation create the reviewed .NET HTTPS URI object. It must verify ordinally that the object's original canonical string is exactly `canonical_https_url`, scheme is HTTPS, host is exactly `https_transport_host`, effective port is 443, user information/query/fragment are empty, and path corresponds exactly to `canonical_path`; any parser-selected rewriting or alternate authority is invalid. The original configured URL is never passed through `Uri`. Validation and correspondence must not use `Uri.IdnHost`, `Uri.DnsSafeHost`, `IdnMapping`, Unicode normalization, IDN conversion, percent-decoded host data, or a parser-selected alternate host.

The actual HTTPS attempt uses only `canonical_https_url`. Dotted/undotted host inputs and absent-port/exact-`:443` inputs representing the same provider produce byte-for-byte identical normalized endpoint host, canonical request URI, DNS hostname, SNI hostname, certificate-name validation input, generated HTTP `Host` header authority, provider workload identity, registrable domain, scheduling, token and charge, evidence, and privacy behavior. DNS, SNI, certificate validation, and the generated `Host` header hostname are exactly non-root `https_transport_host`; no component may restore the original terminal dot, and no caller-supplied `Host` override is allowed. Strict TLS and ordinary hostname validation remain mandatory.

#### Registrable-domain normalization and pinned suffix data

HTTPS provider hosts are exactly the `https_transport_host` values constructed above. IPv4 and IPv6 literals are invalid even though eligible explicit TCP targets may use strict literals. Provider uniqueness, Public Suffix List matching, and registrable-domain comparison consume that one normalized non-root host; they must not reparse the original URL, restore a root dot or port, or introduce another character, label, case, Unicode, ACE, IDN, URI-host, or normalization algorithm.

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

Before parsing, read the exact vendored bytes and verify their complete SHA-256 against the binding value above. Hash the original bytes without normalizing line endings or removing a BOM. A mismatch fails closed and cannot become an empty, default, partial, or implicit-star-only list.

Decode only with strict UTF-8. Invalid byte sequences, overlong encodings, surrogate encodings, and replacement-character substitution are fatal. The pinned file must not contain a UTF-8 BOM; leading bytes `EF BB BF` are invalid after hash verification and are never silently consumed. Platform-default encoding is prohibited.

Split lines by treating LF as the terminator and removing one immediately preceding CR only as its CRLF pair. Accept LF, CRLF, and a final line with or without terminating LF. A lone CR anywhere else is invalid. Do not call a general whitespace-trimming operation. An empty line contains exactly zero characters.

A comment begins with exact ASCII `//` at column zero. Before ignoring other comment lines, recognize exactly one each of `// ===BEGIN ICANN DOMAINS===`, `// ===END ICANN DOMAINS===`, `// ===BEGIN PRIVATE DOMAINS===`, and `// ===END PRIVATE DOMAINS===`, in that order, with no overlap. The complete remainder of every other comment line is ignored. Inline comments on rule lines are invalid. Leading spaces or tabs before `//` do not form a comment and are invalid syntax. No rule may occur outside an active ICANN or PRIVATE section, although comments and blank lines may occur outside them.

A rule line has no leading, trailing, or interior ASCII space or tab and no control character; malformed whitespace is rejected rather than trimmed. Every nonblank, noncomment line is exactly an exact rule `labels`, wildcard rule `*.labels`, or exception rule `!labels`. The wildcard marker occurs only as exact leading `*.`, the exception marker only as exact leading `!`, no rule combines them, and no other `*` or `!` occurs. Reject an empty body, leading or trailing dot, consecutive dots, terminal root dot, or empty label. A PSL body may contain one label and does not inherit the configurable-host two-label requirement. `com`, `co.uk`, `*.ck`, and `!www.ck` are structurally valid examples. PSL lines receive neither configurable-string preprocessing nor terminal-dot removal.

After structural parsing, a rule is eligible only when every body label is ASCII, satisfies the shared per-label LDH grammar, and does not begin with normalized `xn--` after the exact plus-`0x20` ASCII mapping. A structurally valid rule containing direct Unicode or an `xn--` label is deterministically ineligible and ignored for R0-005 matching; it cannot participate in prevailing-rule selection. No `IdnMapping`, globalization, Unicode-normalization, or IDN API is permitted.

For every eligible rule create private key `(rule_kind, normalized_rule_body)`. The pinned data is invalid if that key occurs more than once, including duplicates differing only by ASCII case. Related bodies of different rule kinds remain distinct where PSL syntax permits them. Collection insertion order cannot determine duplicate behavior.

Public-suffix matching over the eligible rule set must implement the official Public Suffix List algorithm:

- process exact, wildcard, and exception rules;
- choose the prevailing rule according to the official algorithm;
- include both the ICANN and PRIVATE sections;
- apply the implicit `*` rule only as defined by that algorithm.

Exception precedence and wildcard behavior are deterministic. The implicit `*` exists only as algorithm behavior and is not inserted as a parsed file rule. Because the bytes and parser are fixed, ICU/NLS mode, operating-system globalization data, ICU/CLDR version, and runtime servicing cannot change the eligible rule set or matching result.

The registrable domain is the label immediately preceding the prevailing public suffix plus that suffix. A host that is itself a public suffix, or that has no registrable label, is invalid. For a configured provider count `N` from 2 through 4, all configured provider hosts must produce pairwise-distinct normalized registrable domains under ordinal comparison after the exact ASCII normalization:

```text
distinct_registrable_domain_count = N
```

Subdomains of one registrable domain never count as independent providers. For example, the three-provider set `a.example.com`, `b.example.com`, and `c.example.net` is invalid because the first two normalized hosts share registrable domain `example.com`. The pairwise rule applies to the complete configured set and is independent of configuration order.

This pinned snapshot remains binding for R0-005. Any suffix-data update requires a reviewed requirements change, a new full upstream commit SHA, a new file SHA-256, license review, security review, and full configuration-boundary regression tests. Existing deployed configuration must not be silently reinterpreted by a live list update.

Malformed pinned bytes, hash or strict-decoding failure, BOM, line/section failure, malformed rule syntax, or duplicate eligible rules fail closed at the existing acceptance source preflight with exactly `source_identity_invalid` before configuration acceptance, host preflight, process creation, or scheduling. This is source/artifact invalidity, not customer configuration, and it does not add `configuration_invalid`, a report field, availability flag, cap, or violation code. The parser must never continue with a partial rule set, only successfully parsed lines, the implicit `*`, or a library-default list.

This documentation correction authorizes no new production package, app-local ICU, ICU package, Unicode data file, second PSL file, or public contract. If implementation requires a parser dependency instead of a repository-owned parser, stop for explicit package, license, security, and vulnerability approval. No dependency may fetch suffix data at runtime.

Runtime validation deterministically enforces pairwise-distinct provider IDs, declared `operator_id` values, normalized endpoint hosts, and normalized registrable domains across the complete configured set. Any duplicate or cardinality mismatch rejects the entire provider configuration before scheduler activation with bounded `configuration_invalid`; no valid subset may activate. Configuration errors remain bounded and must not echo an operator identifier, endpoint, host, URL, registrable domain, or other customer-controlled value.

This validation is intentionally structural. Pairwise-distinct registrable domains combined with truthful pairwise-distinct operator declarations are required, but neither property proves real-world corporate independence and network probing cannot prove ownership. The configuration author is responsible for declaring `operator_id` accurately. The built-in Google and Cloudflare declarations satisfy the pairwise rule and remain reviewed defaults; a custom override that falsely declares ownership is configuration-author error that the agent cannot discover through probing. Separately operated providers remain an architectural and operator responsibility.

### 2.4 External endpoint verification record

Requirements resolution performed a bounded, read-only verification on 2026-07-29 using GET with no request body, system TLS validation, disabled redirects, response-headers-first completion, and no retained bodies:

| Endpoint | Observed status | Redirect | Requirements-only bounded first read |
| --- | ---: | --- | ---: |
| Google `generate_204` | 204 | no | 0 bytes |
| Cloudflare `cdn-cgi/trace` | 200 | no | 200 bytes, immediately discarded |

The Cloudflare read in this one-time requirements-resolution record is historical discovery evidence, not permitted runtime behavior. Every R0-005 Cloudflare runtime GET is status-only and disposes the response without opening or reading its content stream. This record is not a CI dependency or uptime guarantee; runtime behavior is governed by the aggregation rules below, and automated tests use fakes or disposable local endpoints.

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

DNS is a separate logical probe with its own state, counter, transition history, and evidence. It uses the approved system-resolver adapter only with the configured `dns_absolute_resolver_name`; `dns_logical_query_name` remains its private immutable configuration/workload identity.

R0-005 intentionally performs exactly one resolver invocation and one transport attempt against exactly one terminal-root-dot absolute resolver name per DNS logical round. The invocation receives exactly `dns_absolute_resolver_name`; neither the adapter nor its API may remove the terminal dot, pass `dns_logical_query_name`, or enable or emulate suffix search, primary-suffix or connection-specific-suffix appending, devolution, search-list iteration, fallback, alternate-name expansion, or an additional name attempt. The same accepted configuration must produce the same exact resolver argument regardless of host suffix settings. A normal complete A/AAAA result from that one invocation remains input to the existing single-attempt reducer; result multiplicity does not create another R0-005 attempt.

Technical Feasibility section 7.2's two-name prototype heuristic sought to reduce authoritative-domain-specific false negatives, but adopting it here would require new child aggregation, token/charge, concurrency/fairness, coverage-identity, snapshot-publication, and budget rules. Those semantics are not silently invented. Pairwise-distinct HTTPS providers separately supply internet-path diversity. The accepted one-name tradeoff is that failure of the configured authoritative domain can move this bounded DNS signal through `suspect` or `offline`; it must not by itself be labeled as broad property-outage evidence under the existing correlation rules. This decision leaves the existing DNS timeout, one-token/one-method-charge rule, attempt and charged-network formulas, threshold, one-snapshot publication, and HTTPS correlation model unchanged. Resolver failure, timeout, or no-address reduction cannot substitute a relative, suffix-expanded, fallback, or second-name query. Any future multi-name or retry design requires a separately reviewed requirements change.

Success requires a valid A or AAAA result within two seconds. Timeout, resolver failure, or no valid address is a counted DNS failure. The existing `dns_succeeded`, `dns_timeout`, `dns_no_address`, and `dns_resolution_failed` classifications, two-failure offline threshold, state transitions, scheduling/deadline rules, startup and rolling budgets, exact charged bytes, workload coverage, and HTTPS DNS-correlation rules remain unchanged. This absolute-name binding adds no report property, availability flag, cap, violation code, persistence behavior, dependency, or change to the exact 39-code vocabulary, `r0-005-resource-report/v1` schema, or property order.

A DNS-only failure cannot declare the HTTPS aggregate or total internet state offline. Consumers must be able to see DNS state independently.

After every completed DNS logical round, the DNS reducer atomically publishes one immutable process-local DNS-correlation snapshot. It contains only:

- a strictly increasing process-local DNS round sequence;
- the monotonic DNS round-completion timestamp, sampled at the serialized DNS logical-reduction and publication commit boundary rather than at an earlier socket callback;
- the DNS round classification: `success`, `counted_failure`, or `non_counting`;
- the resulting DNS operational state.

These fields are internal coordination data. They must not enter logs, health output, resource reports, durable evidence, support output, or metrics. When the DNS probe is disabled, no DNS-correlation snapshot is published or expected.

### 3.3 HTTPS aggregate

One logical HTTPS round attempts every provider in the atomically accepted pairwise-distinct set, subject to global limits and the logical-round deadline. Provider count `N` is therefore also the distinct provider-ID, operator-ID, normalized-host, and normalized-registrable-domain count; aggregation never collapses or substitutes providers by shared domain, host, operator, or ID. The HTTPS aggregate is one logical target and has at most one active logical round. Each configured provider invocation is a child transport attempt within that round, not another logical target. Within the one active round, at most one attempt per configured provider may be active, provider attempts may overlap, and no provider may be attempted more than once.

The HTTPS logical round is active from atomic due-ledger admission until it commits exactly one of section 4.2's three closure classes. Its private HTTPS logical-round ownership guard is acquired atomically by that admission, retained while the round has no committed closure, and released exactly once when terminal logical reduction, deadline-expired scheduler incomplete, or controlled-cancellation scheduler incomplete commits. A provider-child completion never releases the ownership guard by itself. The ownership guard is never reacquired for the same round, never remains held after a valid incomplete closure while waiting for a terminal aggregate result, and never permits a second closure.

A subsequent HTTPS logical round may be admitted only when the preceding round has committed exactly one section 4.2 closure and that closure's calculated `next_round_due` has arrived. Closure alone never authorizes an immediate replacement or catch-up round, and an incomplete closure does not permanently suppress later cadence merely because it produced no terminal aggregate result. The next round receives a new round identity, due-ledger entry, immutable `round_due`, immutable `round_deadline`, and provider-child attempt identities. No identity, provider outcome, token, charge, accounting record, DNS snapshot, or child completion from the preceding round may be reused, merged into, or attributed to the next round.

After logical closure, already-started provider children may remain temporarily open only for bounded attempt-level accounting under section 8.1. That accounting cannot reopen the logical round, change its closure class or operational result, delay or replace its closure-specific cadence anchor, produce a second logical closure, or retain or reopen the HTTPS logical-round ownership guard.

Every child transport invocation is exactly one GET with no request body. This binding is identical for built-in providers, custom providers, status-only providers, and providers with an expected-body rule. An adapter never sends HEAD or another method, even when only status is evaluated. A status-only GET disposes the response after headers without reading or exposing its body; an expected-body GET applies only the existing bounded exact-read rules. Method binding changes no timeout, deadline, fan-out, token, fixed 8,192-byte method charge, measured-byte attribution, attempt accounting, phase membership, workload coverage, aggregate reduction, or report shape.

HTTPS DNS-snapshot capture is one atomic logical-round admission/start action under the serialized scheduler coordinator in section 4.0. It captures exactly one immutable copy of the greatest published DNS snapshot ordered at or before the HTTPS capture key. A DNS publication with the same raw injected-monotonic timestamp but the earlier publication phase is included. A genuinely later publication is excluded and can never alter that HTTPS round. Callback arrival, task enqueue or completion order, thread scheduling, logging order, and dictionary order cannot change the captured snapshot. Snapshot age uses only the injected monotonic component of the capture key:

```text
dns_snapshot_age =
  https_capture_timestamp - dns_snapshot_publication_commit_timestamp
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
4. If every endpoint completes and all providers fail exclusively during DNS resolution, apply exactly one of these mutually exclusive reductions using the snapshot captured at the atomic HTTPS admission/start boundary:
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

Each round performs exactly one logical TCP attempt to the configured host and port. Classification and canonical private identity were frozen during atomic startup validation and are not repeated by a platform parser. The token-consuming transport invocation begins at one atomic transport-start boundary immediately before either the direct socket connect for a strict eligible IP literal or the hostname-resolution-and-selected-address-connect pipeline for a hostname. The complete hostname pipeline remains one `tcp` attempt with one token, one fixed 1,024-byte method charge, one attempt record, one outcome, one two-second operation timeout, and the round's one universal deadline. Resolution does not create an independent DNS-probe round, DNS attempt, or additional token.

For an IPv4 or IPv6 literal, the implementation constructs the connection address directly from the frozen four or 16 canonical address bytes, bypasses hostname resolution and hostname-level APIs, and connects once to that address and configured port. Direct IPv6 validity is exactly the three-predicate union in section 2.3; explanatory rejections of unspecified, multicast, link-local, mapped, compatible, bracketed, and scoped forms cannot add a second rule. One valid direct IPv6 literal retains one connect, one attempt, one token, one fixed 1,024-byte TCP charge, and the existing timeout, deadline, state-transition, and no-fallback rules. No alternate address, family substitution, retry, or fallback occurs. In particular, `192.0.2.1` and every accepted directly configured IPv6 literal never invoke the resolver.

For a hostname, the started TCP attempt invokes the approved Windows system resolver exactly once with exactly frozen `tcp_absolute_resolver_name` and captures one immutable complete result for that invocation. The adapter preserves the terminal dot; it never removes it, passes `tcp_logical_host_name`, appends a suffix, performs suffix search or devolution, iterates a search list, retries another hostname, or performs hidden alternate-name expansion. Direct Unicode, every normalized `xn--` label, and any alternate or unnormalized hostname were rejected before scheduling.

Each raw resolver item is exactly one of these typed forms:

- IPv4: address family `InterNetwork`, exactly four network-order bytes from `IPAddress.GetAddressBytes()`, and no source scope field;
- IPv6: address family `InterNetworkV6`, exactly 16 network-order bytes from `IPAddress.GetAddressBytes()`, and the signed 64-bit integer returned by `IPAddress.ScopeId` as its source scope field.

Another family, a wrong byte count, a missing IPv6 source scope, an IPv4 source scope, or an IPv6 source scope less than `0` or greater than `0x00000000FFFFFFFF` is malformed or contradictory adapter data and therefore an implementation invariant failure. A valid IPv6 source scope is normalized by checked numeric conversion to one unsigned 32-bit integer with the same mathematical value. Sign reinterpretation, truncation, modulo reduction, text parsing, interface-name parsing, and platform-dependent conversion are prohibited. IPv4 receives normalized scope `0`; an IPv4-mapped IPv6 item is range-validated as IPv6 first and receives normalized scope `0` only after the mapped conversion below.

Resolver-result classification uses only the following exact network-order byte predicates, in the stated order:

1. IPv4-mapped IPv6 is exactly `b[0..9] == 0x00` and `b[10] == 0xFF` and `b[11] == 0xFF`. Replace it with IPv4 bytes `b[12..15]`, discard the source IPv6 scope, assign IPv4 scope `0`, and then apply every IPv4 exclusion below to the converted bytes.
2. IPv4 unspecified is exactly `b[0..3] == 0x00`; IPv4 multicast is exactly `(b[0] & 0xF0) == 0xE0`; and IPv4 limited broadcast is exactly `b[0..3] == 0xFF`. Each is ineligible.
3. IPv6 unspecified is exactly all 16 bytes equal to `0x00`; IPv6 multicast is exactly `b[0] == 0xFF`; and IPv6 link-local is exactly `b[0] == 0xFE` and `(b[1] & 0xC0) == 0x80`, the complete `fe80::/10` range. Unspecified and multicast are ineligible. Link-local proceeds to the scope-usability rule below.
4. Every remaining IPv4 or IPv6 item is eligible. This deliberately retains IPv4 loopback, RFC1918 private, and `169.254.0.0/16` link-local results, plus IPv6 loopback, `fc00::/7`, `1::`, `100::1`, `64:ff9b::1`, and other nonexcluded IPv6 results. No direct-literal range predicate, mutable address registry, or platform address-classification helper can add or remove a resolver candidate.

When the immutable resolver result contains at least one IPv6 `fe80::/10` item, the attempt captures exactly one complete immutable Windows IPv6-interface snapshot after resolver capture and before candidate eligibility, deduplication, or sorting. The approved adapter derives each row from the same successful IP Helper `GetAdaptersAddresses` snapshot and exposes only unsigned 32-bit `Ipv6IfIndex`, `Ipv6Enabled`, and `OperStatus`. A normalized link-local scope is usable exactly when it is nonzero and the snapshot has exactly one row whose nonzero `Ipv6IfIndex` equals that scope, whose `Ipv6Enabled` is true, and whose `OperStatus` is exactly `IfOperStatusUp`. Zero, no matching row, a disabled IPv6 row, or any status other than `IfOperStatusUp` is not usable. Duplicate rows for one `Ipv6IfIndex` or structurally invalid or contradictory `Ipv6IfIndex`, `Ipv6Enabled`, or `OperStatus` data are implementation invariant failures.

Snapshot capture and resolver/interface normalization occur after `transport_start` and remain part of that same single TCP invocation, token, fixed 1,024-byte charge, attempt record, two-second operation timeout, universal deadline, and logical round. They create no additional attempt, token, charge, retry, fallback, alternate snapshot, guessed snapshot, candidate fallback, or hidden transport. The snapshot is never refreshed during the attempt. Later interface changes cannot retroactively alter eligibility. No interface name, address assignment, route lookup, source-address selection, reachability test, or fallback is part of usability. The adapter must convert an ordinary IP Helper/API acquisition failure into its bounded acquisition-failure result and must detect malformed or contradictory returned data inside the adapter boundary. No return code, operating-system error, exception, row, scope, status, or failure reason may escape that boundary or leave a started attempt or admitted due-ledger entry without exactly one bounded attempt outcome and exactly one logical closure.

An ordinary bounded snapshot-acquisition failure returned before scheduler-shutdown cancellation, the two-second operation timeout, or the logical-round deadline selects and connects no candidate. It classifies the started invocation as `failed`, increments `attempts.by_outcome["failed"]` exactly once, commits one terminal logical reduction, makes the TCP round one counted failure with evidence `tcp_connect_failed`, and advances the TCP failure streak and operational state through the existing threshold and transition table. The exact failure-reduction commit timestamp is the terminal-closure cadence anchor, and the due-ledger entry closes exactly once. This operational probe failure does not by itself add an acceptance `violations` entry. When it occurs during steady state and bounded accounting closes normally, that completed failed attempt exercises its exact required TCP workload member. No retry, alternate snapshot, guessed snapshot, partial snapshot, candidate fallback, or socket connect occurs.

When the existing TCP two-second operation timeout becomes due while snapshot acquisition remains unfinished and that timeout classification is permitted by the universal-deadline ordering, no candidate is selected or connected. The invocation becomes `timed_out`, increments `attempts.by_outcome["timed_out"]` exactly once, commits one terminal logical reduction, and makes the round one counted TCP failure with evidence `tcp_connect_timeout`. The existing TCP failure streak and state rules apply, the exact timeout-reduction commit timestamp is the terminal cadence anchor, and the due-ledger entry closes exactly once. Snapshot work cannot extend or re-anchor the universal deadline. If the universal deadline wins before the operation timeout is eligible, the existing deadline-expired scheduler-incomplete and independent started-attempt accounting rules apply; no snapshot-specific deadline class exists.

Snapshot capture creates no independent cancellation category and no exception to section 8.1's shared barrier-pause procedure. For Gateway, DNS, and TCP, a start or end barrier may commit controlled-cancellation scheduler incomplete only when the round's one required transport invocation did **not** commit `transport_start` before the serialized barrier pause. The pause permanently prohibits the missing start, creates no attempt record or outcome, consumes no token, fixed charge, slot, transport-attempt guard, measured bytes, or rolling-ledger entry, leaves operational counters and state unchanged, retains the original immutable deadline as the cadence anchor, closes the due-ledger entry exactly once, uses only the existing controlled-barrier evidence behavior, and admits no replacement start for that canceled round. Because no invocation began, this unstarted path does not contribute `canceled` to attempt accounting.

Once the TCP invocation commits `transport_start`, neither a start barrier nor an end barrier may cancel the invocation, commit controlled-cancellation logical closure for that round, assign attempt outcome `canceled`, use common evidence `cancelled`, or suppress a later ordinary success, failure, timeout, malformed-data invariant result, or exact-deadline closure. The started resolver, interface-snapshot, normalization, selection, and connect pipeline drains under the existing bounded barrier procedure. It may commit exactly one ordinary terminal logical reduction at or before its immutable deadline; otherwise ordinary deadline-expired scheduler incomplete commits at that deadline. A snapshot callback delivered after the pause remains eligible for ordinary classification. Attempt accounting may close later under the existing bounded rule but cannot rewrite a deadline-expired logical closure.

Scheduler shutdown retains separate authority. Before TCP `transport_start`, shutdown commits its existing controlled-cancellation scheduler-incomplete closure with private reason `shutdown`, creates no attempt record, leaves TCP state unchanged, retains the original deadline only as the private conceptual cadence anchor, and creates no replacement work. After TCP `transport_start`, shutdown may commit controlled-cancellation scheduler incomplete and actively cancel the invocation. The logical result remains non-counting, TCP state and counters remain unchanged, the due-ledger entry closes exactly once, and the original deadline remains the private cadence anchor. Bounded accounting that observes cancellation assigns `canceled` exactly once and uses common evidence `cancelled`; if it closes without a terminal canceled outcome, it assigns attempt-level `incomplete` and adds `attempt_accounting_incomplete`. No later API result, timeout, malformed-data detection, snapshot completion, or socket callback may rewrite the committed shutdown closure. Normal planned shutdown cancellation adds no violation solely because shutdown occurred; independently failed accounting, continuity, cleanup, or shutdown obligations remain additive.

Invalid resolver family or byte count; missing or prohibited scope; scope outside `0` through `0x00000000FFFFFFFF`; truncation, reinterpretation, or contradictory scope normalization; malformed interface rows; duplicate rows for one `Ipv6IfIndex`; structurally contradictory `Ipv6Enabled`, `OperStatus`, or interface-index data; and every other resolver/interface adapter value expressly classified as malformed or contradictory in this section remain internal invariant defects. They cannot throw or escape past the adapter boundary. At the exact serialized invariant-detection commit, the implementation selects and connects no candidate, classifies the started invocation as `incomplete`, increments `attempts.by_outcome["incomplete"]` exactly once, does not increment `attempts.completed`, and commits one immediate terminal logical reduction through the existing non-counting terminal-result path with common evidence `round_incomplete`. It uses no TCP success, failure, or timeout evidence code; leaves TCP failure/recovery counters and operational state unchanged; closes the due-ledger entry exactly once; and uses that invariant-detection reduction commit timestamp as the terminal cadence anchor. Later work is scheduled only by the existing fixed-delay/jitter formula; the coordinator does not wait for the immutable deadline merely to close a known invariant result. It must not retry, substitute a candidate, discard only the malformed row, continue with a partial snapshot, or create a new evidence or violation code.

During acceptance, every such malformed/contradictory-data `incomplete` outcome makes `attempt_accounting_incomplete` mandatory exactly once and cannot exercise the required TCP workload member. `acceptance_workload_incomplete` is therefore also mandatory when no separate complete qualifying attempt exercises that member. Every independently applicable continuity, boundary, or cleanup violation remains additive; `result` is `fail`, exit is nonzero, and a passing report is impossible. The closed violation vocabulary remains exactly 39 codes.

Snapshot processing uses this exact precedence under the serialized coordinator:

1. A terminal classification or logical reduction already committed before a barrier or shutdown pause remains terminal.
2. Every immutable deadline at or before the pause timestamp receives ordinary section 4.3 processing before controlled cancellation.
3. At a pre-deadline start or end barrier, TCP `transport_start` committed before the serialized pause means started and must drain; a start that loses that boundary means unstarted and is barrier-canceled without an attempt record.
4. For a started invocation that is draining across a start or end barrier, a due operation timeout precedes a completed ordinary snapshot-acquisition failure; ordinary failure precedes malformed/contradictory-data detection; malformed-data detection precedes valid-snapshot candidate processing; and a successful selected-address connect uses its ordinary success classification.
5. At shutdown, section 4.6's order applies: prior terminal classifications and reductions, deadlines at or before the shutdown pause, shutdown controlled cancellation of every remaining open admitted round, then cancellation and bounded accounting of active invocations.
6. An already committed deadline-expired or shutdown-controlled logical closure wins and cannot be rewritten by a later callback.

Transport start and barrier pause at the same injected timestamp use their serialized order. API failure and barrier pause, operation timeout and end barrier, or malformed-data detection and barrier pause at the same timestamp use the same coordinator phases and private sequence, never callback delivery or task order. Callback order, task order, task completion, exception timing, interface enumeration order, and logging order cannot change the result. One path receives exactly one attempt outcome and one logical closure.

The following snapshot/candidate scenarios and their exact acceptance effects are normative:

| Scenario | Logical closure | Attempt outcome | Evidence | State effect | Cadence anchor | Acceptance violations |
| --- | --- | --- | --- | --- | --- | --- |
| start barrier wins before TCP `transport_start` | controlled-cancellation scheduler incomplete | no attempt | existing controlled-barrier evidence | unchanged | original deadline | none solely from the controlled barrier |
| end barrier wins before TCP `transport_start` | controlled-cancellation scheduler incomplete | no attempt | existing controlled-barrier evidence | unchanged | original deadline | none solely from the controlled barrier |
| start barrier occurs after snapshot invocation started; snapshot API then fails | terminal counted failure | `failed` | `tcp_connect_failed` | increment TCP failure streak | failure-reduction commit | none solely from this condition |
| end barrier occurs after snapshot invocation started; timeout then occurs before deadline | terminal counted failure | `timed_out` | `tcp_connect_timeout` | increment TCP failure streak | timeout-reduction commit | none solely from this condition |
| start or end barrier occurs after start; successful selected-address connect completes | terminal counted success | `succeeded` | `tcp_connect_succeeded` | apply ordinary TCP success/recovery rules | success-reduction commit | none solely from this condition |
| start or end barrier occurs after start; malformed or contradictory data is detected | terminal non-counting invariant result | `incomplete` | `round_incomplete` | unchanged | invariant-detection commit | `attempt_accounting_incomplete`; plus `acceptance_workload_incomplete` when the member remains unexercised |
| start or end barrier occurs after start; valid snapshot has no eligible candidate | terminal counted failure | `failed` | `tcp_connect_failed` | increment TCP failure streak | failure-reduction commit | none solely from this condition |
| start or end barrier occurs after start; immutable deadline arrives first | deadline-expired scheduler incomplete | separately bounded started-attempt outcome | existing deadline/incomplete evidence | unchanged | original deadline | `attempt_accounting_incomplete` only when bounded attempt closure is incomplete; other independent violations remain additive |
| shutdown before TCP `transport_start` | shutdown controlled cancellation | no attempt | existing shutdown-controlled evidence | unchanged | original deadline | none solely from normal planned shutdown |
| shutdown after TCP `transport_start`; cancellation observed | shutdown controlled cancellation | `canceled` | `cancelled` | unchanged | original deadline | none solely from normal planned shutdown |
| shutdown after TCP `transport_start`; accounting fails to close terminally | shutdown controlled cancellation | `incomplete` | logical cancellation evidence plus existing accounting-failure representation | unchanged | original deadline | `attempt_accounting_incomplete`; other independent shutdown/cleanup violations remain additive |
| duplicate `Ipv6IfIndex` row is detected without a barrier or shutdown winning | terminal non-counting invariant result | `incomplete` | `round_incomplete` | unchanged | invariant-detection commit | `attempt_accounting_incomplete`; plus `acceptance_workload_incomplete` when the member remains unexercised |
| resolver scope is `-1` or `4294967296` without a barrier or shutdown winning | terminal non-counting invariant result | `incomplete` | `round_incomplete` | unchanged | invariant-detection commit | same invariant violations |
| wrong resolver family/byte count or malformed interface row is detected without a barrier or shutdown winning | terminal non-counting invariant result | `incomplete` | `round_incomplete` | unchanged | invariant-detection commit | same invariant violations |

After mapped conversion, exclusions, and link-local scope eligibility, candidates are deduplicated by canonical family, canonical address bytes, and normalized scope. Mapped and native IPv4 forms with the same four bytes collapse to the same IPv4 candidate. Same-byte IPv6 candidates with different normalized scopes remain distinct. Resolver failure or a completed result with no eligible candidate is one bounded failed TCP attempt with the same exact `failed`/counted/`tcp_connect_failed` reduction as ordinary snapshot-acquisition failure. Malformed or contradictory resolver or interface-adapter data uses the immediate `incomplete` invariant reduction above and cannot throw, escape, or be replaced with a guessed address.

These resolver-result vectors are normative; byte ranges are inclusive and the synthetic interface rows are the complete captured interface snapshot for that vector:

| Raw resolver item | Complete interface rows | Required normalization/result |
| --- | --- | --- |
| IPv4 `0.0.0.0` | none | ineligible: exact IPv4 unspecified |
| IPv4 `224.0.0.1` | none | ineligible: exact IPv4 multicast boundary |
| IPv4 `239.255.255.255` | none | ineligible: exact IPv4 multicast upper boundary |
| IPv4 `240.0.0.1` | none | eligible IPv4, scope `0`; outside multicast |
| IPv4 `255.255.255.255` | none | ineligible: exact limited broadcast |
| IPv6 `::ffff:192.0.2.9`, source scope `7` | none | eligible IPv4 `192.0.2.9`, scope `0` |
| IPv6 `::ffff:0.0.0.0`, source scope `0` | none | mapped, then ineligible IPv4 unspecified |
| IPv6 `::ffff:224.0.0.1`, source scope `0` | none | mapped, then ineligible IPv4 multicast |
| IPv6 `::ffff:255.255.255.255`, source scope `0` | none | mapped, then ineligible limited broadcast |
| IPv6 `::`, source scope `0` | none | ineligible: exact IPv6 unspecified |
| IPv6 `ff02::1`, source scope `7` | none | ineligible: exact IPv6 multicast; no interface snapshot required for this item |
| IPv6 `fe7f::1`, source scope `0` | none | eligible IPv6, scope `0`; below `fe80::/10` |
| IPv6 `fe80::1`, source scope `0` | `(7, true, IfOperStatusUp)` | ineligible: link-local zero scope |
| IPv6 `fe80::1`, source scope `7` | `(7, true, IfOperStatusUp)` | eligible IPv6, scope `7` |
| IPv6 `fe80::1`, source scope `7` | `(7, true, IfOperStatusDown)` | ineligible: matching interface is not up |
| IPv6 `fe80::1`, source scope `7` | `(8, true, IfOperStatusUp)` | ineligible: no matching interface index |
| IPv6 `febf:ffff::1`, source scope `4294967295` | `(4294967295, true, IfOperStatusUp)` | eligible IPv6, scope `4294967295`; upper `fe80::/10` boundary |
| IPv6 `fec0::1`, source scope `0` | none | eligible IPv6, scope `0`; above `fe80::/10` |
| IPv6 `1::`, source scope `-1` | none | immediate `incomplete` invariant reduction with `round_incomplete`; no truncation or unsigned reinterpretation |
| IPv6 `1::`, source scope `4294967296` | none | immediate `incomplete` invariant reduction with `round_incomplete`; outside unsigned 32-bit range |
| IPv6 `fe80::1`, source scope `7` | duplicate rows `(7, true, IfOperStatusUp)`, `(7, true, IfOperStatusUp)` | immediate `incomplete` invariant reduction with `round_incomplete`; no row-order tie-break or partial snapshot |

For one immutable raw set containing native IPv4 `192.0.2.2`, mapped IPv6 `::ffff:192.0.2.2` with source scope `9`, IPv6 `fe80::1` with source scopes `7` and `8`, and IPv6 `1::` with source scope `0`, a complete interface snapshot of `(7, true, IfOperStatusUp)` and `(8, true, IfOperStatusDown)` produces exactly three deduplicated eligible candidates in sorted order: `(IPv4, 192.0.2.2 bytes, 0)`, `(IPv6, 1:: bytes, 0)`, then `(IPv6, fe80::1 bytes, 7)`. The mapped/native IPv4 pair collapses and the scope-`8` link-local item is removed. If the raw result instead contains only IPv6 `fe80::1` with source scope `7` and the complete interface snapshot has no matching up IPv6-enabled row, the eligible set is empty and the result is one bounded failed TCP attempt. If both scope `7` and scope `8` rows are IPv6-enabled and up, same-byte link-local candidates with scopes `7` and `8` both remain and sort by unsigned numeric scope, `7` before `8`.

Resolver-returned candidate eligibility is this separate closed contract and must not apply the direct-literal exact-loopback/`fc00::/7`/`2000::/3` union. The direct-literal correction changes no resolver-result filtering, mapped normalization, link-local scope handling, deduplication, sorting, first-candidate selection, one-connect behavior, or no-fallback behavior.

The complete deduplicated candidate set is sorted ascending by exactly:

```text
(
  address_family_rank,
  canonical_address_bytes,
  ipv6_scope_id
)
```

IPv4 rank is `0`, IPv6 rank is `1`, canonical bytes compare lexicographically as unsigned bytes, IPv4 uses scope ID `0`, and IPv6 compares its normalized unsigned 32-bit scope identifier numerically. The first candidate is selected. Resolver order, DNS answer order, dictionary or runtime enumeration order, interface-snapshot row order, adapter order, locale, and thread scheduling cannot change selection.

The implementation resolves first and invokes socket connect against only the selected address. It must not call a hostname-level socket API that can perform hidden implementation-defined iteration, Happy Eyeballs, fallback, or family racing. Refused, timed out, unreachable, resolver, connect, cancellation, or other failure never causes another returned address, family, port, hostname, or protocol to be tried. A selected address failing while another candidate would have succeeded remains one counted TCP failure. Any future fallback or multi-address aggregate design requires separate architecture review because it changes token, attempt, deadline, reduction, fairness, traffic-budget, and workload-evidence semantics.

Successful connection is success. Refused, timed out, unreachable, DNS/connect error, ordinary snapshot-acquisition failure, a valid snapshot with no eligible candidate, or another bounded transport failure is a counted failure under the exact outcome/evidence mapping above. Malformed or contradictory resolver/interface data is the specified non-counting terminal invariant result, not an outage. The socket is disposed immediately; no application protocol, payload, banner, authentication, TLS substitution, retry, alternate address/port, or discovery occurs. The complete resolver-snapshot-normalization-connect pipeline uses the existing two-second operation timeout from TCP transport start and cannot extend the universal logical-round deadline or its equal-timestamp ordering.

Resolver answers, selected address, address family, candidate count, scope identifier, sort tuple, literal bytes, `tcp_logical_host_name`, `tcp_absolute_resolver_name`, hostname, and port remain private. They never enter JSON, human reports, logs, metrics, health, persistence, or support output.

The TCP offline threshold is two consecutive counted failures.

## 4. Scheduler contract

### 4.0 Serialized coordination order

One private serialized scheduler coordinator owns transport terminal classifications, logical reductions, DNS snapshot publication, ordinary deadline closure, controlled cancellation, due-ledger admission, HTTPS DNS-snapshot capture, and transport-start admission. Every coordinated event has one private total-order key equivalent to:

```text
(
  injected_monotonic_timestamp,
  scheduler_phase_rank,
  stable_logical_target_key,
  private_same_timestamp_sequence
)
```

The private sequence disambiguates repeated raw clock values but cannot override phase rank. The complete key, every component, and comparison results are process-local coordination data and never enter output, logs, metrics, health, support evidence, persistence, or report JSON.

At one injected-monotonic timestamp, the coordinator processes exactly:

1. already-available terminal transport classifications and operation timeouts;
2. complete logical reductions, including atomic DNS snapshot publication;
3. ordinary deadline-expired closures;
4. controlled-cancellation closures required by an acceptance barrier or scheduler shutdown;
5. due-ledger admission and logical-round start actions, including atomic HTTPS DNS-snapshot capture; and
6. transport-start candidates.

Therefore, a DNS logical reduction/publication and HTTPS admission/capture at the same raw monotonic timestamp always commit in that order, so HTTPS captures the newly published snapshot. Reversing callback, task-enqueue, or task-completion order cannot change correlation. A terminal callback that arrives after the coordinator has passed the applicable terminal-classification or reduction phase cannot be backdated to an earlier coordination key and cannot retroactively alter an HTTPS capture. The DNS round-completion timestamp is the serialized DNS reduction/publication commit timestamp, never an earlier unsynchronized socket callback timestamp. The stable logical-target key and comparison are defined in section 4.5.

Within the terminal-classification phase, TCP snapshot processing uses section 3.4's fixed sub-order. A terminal classification or logical reduction committed before a barrier/shutdown pause remains final, and ordinary deadlines at or before the pause run before controlled cancellation. At a pre-deadline start/end pause, a committed TCP `transport_start` makes the round started and ineligible for barrier cancellation; a start that loses the serialized pause boundary is unstarted and eligible for barrier cancellation. While a started invocation drains, an eligible due operation timeout precedes a completed ordinary acquisition failure; ordinary acquisition failure precedes malformed/contradictory-data detection; and malformed/contradictory-data detection precedes valid-snapshot candidate processing. Only shutdown may controlled-cancel and actively cancel an already-started TCP invocation. The coordinator commits at most one classification and one logical closure for the path. Callback, task, exception, interface-enumeration, and logging order cannot override this sub-order.

### 4.1 Activation and degradation

The scheduler is never constructed or activated when atomic startup configuration validation fails. No prior process-memory configuration or partial provider/target subset may survive that failure. Probe monitoring-ready remains false or visibly degraded with bounded local validation evidence until corrected configuration is supplied and the process is restarted. This is the scoped R0-005 exception to the general last-known-good rule in [Security and Privacy Guardrails](security-guardrails.md); it adds no persistence and does not weaken the rule for configuration that has a safely stored accepted last-known-good value.

The scheduler may activate only after R0-004 reports both journal readiness and formal service readiness. A configured logical target is constructed as `unknown`. At the exact injected-monotonic timestamp of successful scheduler activation, activation creates one stable process-local scheduler epoch, marks it active and enabled, moves every enabled target from `unknown` to `baselining`, clears every failure streak, recovery progress, and flapping history, and atomically admits every enabled target's first due-ledger entry. Every initial Gateway, enabled DNS, HTTPS aggregate, and enabled TCP round has `round_due` equal to that exact activation timestamp and `round_deadline` equal to activation plus exactly ten monotonic seconds. The synchronized initial wave remains subject to the same fairness, concurrency, token, and rolling-budget controls as later work without re-anchoring either timestamp. Activation also creates the epoch's one process-local token bucket with exactly eight token-seconds of credit and that activation timestamp as its refill anchor, and creates the epoch's empty rolling transport-start admission ledger defined in section 4.4. The rolling ledger applies to the initial due wave immediately. No transport round may be dispatched or reduced while a target remains `unknown`; a round result observed for an `unknown` target is an implementation invariant failure, not an operational transition.

If journal health or formal service readiness becomes false:

- stop admitting new rounds;
- reconcile every admitted round and cancel queued and active attempts promptly through section 4.6's controlled scheduler-shutdown procedure;
- mark the scheduler epoch inactive and disabled, then clear schedules, discard the complete token-bucket credit, refill anchor, and rolling transport-start admission ledger, dispose scheduler resources, and clear counters, recovery progress, and flapping history only after reconciliation;
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

Every admitted logical round commits exactly one of three private internal closure classes:

1. **Terminal logical reduction.** Success, counted failure, or an existing non-counting terminal result commits when the complete logical result becomes available before or at the immutable `round_deadline`. Its scheduling anchor is the exact injected-monotonic terminal logical-reduction commit timestamp. TCP ordinary snapshot-acquisition failure and snapshot operation timeout use the counted-failure path; malformed or contradictory resolver/interface data uses the immediate non-counting terminal-result path. Each closes the admitted due-ledger entry exactly once at that reduction commit.
2. **Deadline-expired scheduler incomplete.** When required starts or outcomes remain missing at `round_deadline`, this class commits through section 4.3's ordinary exact-deadline ordering. Its scheduling anchor is the immutable expired `round_deadline`.
3. **Controlled-cancellation scheduler incomplete.** This class commits only through the acceptance start barrier, acceptance end barrier, or scheduler shutdown. Its private cancellation reason is exactly `start_barrier`, `end_barrier`, or `shutdown`; the reason is not a report field. At a start or end barrier it may commit for Gateway, DNS, or TCP only when that round's required invocation did not commit `transport_start` before the serialized pause; the existing partially started HTTPS aggregate rule remains unchanged. Shutdown may commit it for any remaining open admitted round, including one with an active invocation. It may commit before `round_deadline`, commits immediately for reconciliation, is non-counting, and retains the original immutable `round_deadline` as its cadence anchor rather than the earlier cancellation commit timestamp. It cannot later become terminal or deadline-expired and releases any HTTPS logical-round ownership guard.

These classes, their discriminator, their timestamps, and their scheduling anchor are private scheduler concepts. Their names or values never enter report JSON, human reports, logs, metrics, health, persistence, or support output. The existing due ledger may retain one fixed internal closure discriminator, but it remains non-identifying and private.

For a started TCP snapshot path, an ordinary acquisition failure, an eligible operation timeout, malformed/contradictory-data detection, or valid-snapshot completion supplies at most one terminal classification to this closure model. A start or end barrier cannot supply controlled cancellation after `transport_start`; the invocation drains to ordinary terminal reduction or ordinary deadline expiry. Only a shutdown cancellation that commits for the still-open started round supplies the sole controlled-cancellation closure and prevents later snapshot completion from rewriting it. No exception or adapter callback can bypass these classes, defer a known invariant result until the deadline, create a fourth class, or leave the invocation outcome or due-ledger closure absent.

For the HTTPS aggregate, committing any one of these three classes releases the HTTPS logical-round ownership guard exactly once. Deadline-expired and controlled-cancellation scheduler incomplete are complete logical closures even when already-started provider children still require bounded attempt-level accounting. Such child accounting cannot retain or reacquire the ownership guard, rewrite the closure or operational result, alter the scheduling anchor, or be attributed to a later round.

After a logical round commits its one closure, its next due time uses exactly one scheduling-anchor formula:

```text
next_round_schedule_anchor =
  terminal_reduction_commit_timestamp
    for a terminal logical reduction

  original_round_deadline
    for a deadline-expired scheduler incomplete

  original_round_deadline
    for a controlled-cancellation scheduler incomplete

next_round_due =
  next_round_schedule_anchor
  + uniform random duration in
      [max(15 seconds, 90% of base interval),
       110% of base interval]
```

This is the only next-due formula. Controlled cancellation never accelerates cadence by using its earlier commit time. No dispatcher wake, barrier or shutdown timestamp, cancellation callback, drain completion, stable-snapshot commit, resume timestamp, fair selection, eligibility, or transport-start timestamp may replace the required anchor. Except for the end-barrier endpoint and shutdown rules, the next round is atomically admitted at its calculated `next_round_due`, and its deadline is always `next_round_due + 10 seconds`. A dispatcher that wakes or observes the due round late retains the original scheduled due and deadline. Shutdown-controlled cancellation retains the original deadline only as the conceptual closure-specific anchor: it calculates or retains no executable replacement work, admits no later round, and discards the in-memory schedule only with the scheduler epoch after reconciliation.

For HTTPS, the next-round admission boundary requires both the prior round's committed closure and arrival of that calculated due. A terminal logical reduction uses its actual commit timestamp as the anchor; both scheduler-incomplete closures use the original immutable deadline. Each later HTTPS round is independent and receives fresh round, due-ledger, due/deadline, and provider-child identities; no prior child, outcome, token, charge, DNS snapshot, or accounting entry carries forward.

Jitter comes from an injected deterministic source in tests. Scheduling uses fixed delay from the required closure-specific scheduling anchor, does not overlap a Gateway, DNS, or TCP logical target's transport attempt, does not overlap HTTPS aggregate logical rounds, and does not catch up or burst after delay, sleep, or backpressure. Concurrent provider children inside one HTTPS aggregate round are the sole permitted within-target transport overlap.

Nominal threshold × base-interval calculations are not elapsed-time guarantees or service-level agreements because anchor-based delay, jitter, admission delay, transport timeouts, and the logical-round deadline also contribute.

### 4.3 Universal logical-round due time and deadline

| Operation | Timeout |
| --- | ---: |
| Gateway ICMP | 1.5 seconds |
| DNS resolution | 2 seconds |
| TCP connect | 2 seconds |
| Each HTTPS endpoint | 5 seconds |
| Any logical round, including capacity/rate waiting | 10 seconds |

Every Gateway, DNS, HTTPS aggregate, and TCP logical round has exactly one immutable injected-monotonic `round_due` and exactly one immutable deadline:

```text
round_deadline = round_due + 10 seconds
```

Logical-round admission is the atomic creation of the due-ledger entry at the round's scheduled `round_due`. It is not fair-queue selection, dispatcher wake-up, transport-attempt guard or global-slot acquisition, token or rolling-budget availability, route discovery, DNS completion, TCP address selection, provider fan-out, or transport start. For HTTPS only, the same admission atomically acquires the separate logical-round ownership guard and performs the round-start DNS-snapshot capture under section 4.0. The private immutable entry contains round identity, member identity, `round_due`, and `round_deadline`; none of those values enters reports, logs, metrics, health, support output, or persistence. Checked deterministic monotonic arithmetic is mandatory. Overflow, reversal, malformed time, or inability to calculate a deadline is an implementation invariant failure with no UTC or wall-clock fallback.

The ten-second interval includes fair-dispatch waiting, transport-attempt-guard and concurrency-slot waiting, token waiting, both rolling-budget waits, route discovery, hostname resolution, TCP interface-snapshot capture and normalization, provider fan-out preparation, transport execution, and terminal logical reduction. A delayed candidate never gains another ten seconds. A transport may start only when `transport_start < round_deadline`; one that has not started may not start at or after the deadline. At `now == round_deadline`, deadline processing occurs before any new start or dispatcher selection for that round, and work that loses at this boundary consumes no token, slot, transport-attempt guard, or rolling-ledger entry.

At one injected-monotonic timestamp equal to the deadline, the scheduler applies this exact order identically to all four logical-round types:

1. Apply every terminal classification for an already-started invocation whose classification timestamp is at or before the deadline.
2. Apply an operation timeout occurring exactly at the deadline before testing logical-round completeness.
3. If all required started/provider outcomes now permit one complete logical reduction, commit that reduction.
4. Otherwise commit deadline-expired scheduler-level `incomplete`.
5. Only after that deadline outcome commits may unrelated later dispatcher work at the same timestamp proceed.
6. No not-yet-started attempt from the expired round may start.

A started timeout exactly at the deadline is therefore a classified timeout and may contribute to a counted logical failure. An outcome after the deadline cannot retroactively change the committed logical result. Missing required starts or outcomes at the boundary produce deadline-expired scheduler incomplete, which does not advance operational counters or state, while every actually started invocation remains independently accountable. For HTTPS, this incomplete closure releases the logical-round ownership guard at commit even when every provider child started but one or more attempt outcomes remain open. No later child outcome can replace it with terminal reduction, and one subsequent round remains schedulable only at the original-deadline-anchored `next_round_due`.

The same ordering binds TCP snapshot ties. A snapshot operation timeout due exactly at the universal deadline is classified in step 2 and may permit the counted `tcp_connect_timeout` terminal reduction in step 3. If the universal deadline closure has already committed, later timeout, acquisition-failure, malformed-data, or successful-snapshot callbacks cannot replace it. At the same pre-deadline coordination timestamp, a due timeout precedes ordinary acquisition failure, ordinary acquisition failure precedes malformed-data reduction, and each precedes valid-snapshot candidate selection. An already committed shutdown-controlled cancellation remains final; a start/end barrier cannot create such a closure after TCP `transport_start`. Exact callback, task, exception, interface-row, and logging order cannot alter the serialized result.

Before `round_deadline`, controlled-cancellation scheduler incomplete is the sole permitted scheduler-incomplete closure and is available only through section 8.1's serialized barrier-pause procedure or section 4.6's atomic scheduler-shutdown reconciliation. Start/end barriers use it only for missing-start work in Gateway, DNS, and TCP and for the existing missing-provider-start HTTPS cases; they never use it to cancel an already-started Gateway, DNS, or TCP invocation. Shutdown may use it for every remaining open admitted round. Ordinary capacity, token, rolling-budget, dispatcher, cancellation, and late-wake paths continue waiting or cancelably waiting until terminal reduction or exact-deadline processing; they do not invent another pre-deadline closure. A round committed through controlled cancellation cannot later commit a terminal or deadline-expired logical reduction, although already-started invocations remain independently accountable under the bounded partial-HTTPS or shutdown rules.

There is no retry within a round. The Gateway ICMP, DNS, TCP, and individual HTTPS operation timeouts begin at their actual operation or transport-start boundaries and remain 1.5, 2, 2, and 5 seconds respectively. An operation timeout can never extend the logical-round deadline. Token availability is evaluated by section 4.4's continuous lazy-refill rules at exact injected-monotonic instants; neither a coarse tick nor operating-system timer cadence may delay credit that exists. A required transport that never starts does not enter attempt accounting or consume a token; any invocation that did start remains subject to section 8.8 attempt closure and never receives a refund.

### 4.4 Capacity, configured budgets, and token consumption

Two guard classes are distinct and must never be represented by one ambiguous target, round, or provider guard:

1. The **HTTPS logical-round ownership guard** is one private guard for the HTTPS aggregate logical target. It is acquired atomically with HTTPS due-ledger admission, is held until exactly one logical-round closure commits, and prevents admission of a second HTTPS logical round. It carries no token, charge, global slot, transport capacity, rolling-budget reservation, or attempt record and does not count against global concurrency. It is the sole explicit exception to this section's no-reservation-while-waiting rule: an admitted HTTPS round retains this ownership guard while provider children wait, solely to prevent a second logical round.
2. A **transport-attempt guard** is capacity-bearing and is acquired only at the atomic transport-start boundary. Gateway, DNS, and each TCP logical target use their applicable transport-attempt guard; each HTTPS provider child uses its own provider-child transport-attempt guard. The guard is acquired together with rolling checks, one global slot, token subtraction, ledger append, start-timestamp recording, and immediate invocation. It is never retained while waiting for a token, rolling budget, or global slot.

An admitted HTTPS round waiting to start provider children therefore holds its logical-round ownership guard while holding zero transport-attempt guards, reserving no transport capacity, consuming no token or charge, reserving no global slot, and creating no attempt record. That ownership does not violate fair dispatch. Started child transport-attempt guards close only under attempt-level rules and cannot retain, reacquire, or reopen the logical-round ownership guard. All three logical closure classes release the ownership guard exactly once, independently of any still-accounting child transport-attempt guard.

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

Configuration is valid only when `worst_case_attempts_per_minute <= 60`. Excess scheduled demand, where `worst_case_attempts_per_minute > 60`, rejects the entire configuration before scheduling with bounded code `configuration_invalid`; passing this formula does not authorize any runtime burst outside the hard rolling cap below.

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

Configuration is valid only when `worst_case_charged_network_bytes_per_minute < 100,000`. A value at or above the approved decimal cap, where `worst_case_charged_network_bytes_per_minute >= 100000`, rejects the entire configuration before scheduling with bounded code `configuration_invalid`; passing this formula does not authorize any runtime burst outside the hard rolling charged-byte cap below. This exact charged-byte model is deterministic conservative accounting for configuration and acceptance; it is not packet capture or a claim of exact physical-wire traffic.

Validation may privately derive `attempt_demand_limit_failed = (worst_case_attempts_per_minute > 60)` and `charged_network_demand_limit_failed = (worst_case_charged_network_bytes_per_minute >= 100000)` only for deterministic validation control flow, internal test injection/assertions, and proof that the correct mathematical predicate was evaluated. Those private predicate results are not evidence or report violation codes, report fields, availability flags, log or metric dimensions, health or support output, persisted values, or user-facing error categories. The observable result never identifies which predicate failed.

If either or both startup-budget predicates fail, the complete configuration is rejected atomically and `configuration_invalid` is emitted exactly once. Multiple simultaneous configuration defects do not duplicate that set-like violation, activate a scheduler, schedule a valid subset, create probe workload, or expose calculated demand or any customer-controlled configuration value.

A disabled DNS probe creates no DNS scheduler target, consumes no DNS token or network-attempt slot, and contributes zero DNS demand to both startup budgets. HTTPS provider DNS resolution remains charged as part of each HTTPS provider attempt; disabling the independent DNS probe does not remove or reduce HTTPS demand. Configuration validation remains atomic, and its common bounded rejection code may not echo target IDs, intervals, hosts, ports, provider details, operator identifiers, endpoints, calculated customer-specific values, or other sensitive configuration.

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

The exact recognized-method charge is known before admission. Lazy token refill, rolling-window expiry, both rolling-budget checks, applicable transport-attempt-guard acquisition, global-slot acquisition, subtraction of one token-second, rolling-ledger append, `transport_start = now` recording, and immediate transport invocation form one deterministic atomic transport-start boundary serialized under the same `now`. Same-timestamp candidates follow section 4.5's stable fair-dispatch/provider order; every accepted candidate immediately affects the next same-timestamp candidate's attempt and charged-byte checks.

Waiting for either rolling budget reserves no transport-attempt guard or global slot, consumes no token, creates no rolling-ledger or attempt entry, remains cancelable, and consumes the round's immutable `round_due`-anchored interval. The HTTPS logical-round ownership guard is the sole non-capacity exception described above. If admission cannot become available while `transport_start < round_deadline`, the transport never starts and the logical work follows the scheduler-level incomplete rules. Selection or later eligibility never re-anchors the deadline. The unstarted work enters neither attempt accounting nor charged accounting. After a transport starts, its rolling-ledger entry is never refunded or removed because of success, failure, timeout, cancellation, or attempt-level incomplete; it expires only by exact monotonic age.

The rolling ledger remains continuous through activation, initialization, readiness barriers, steady state, end drain, and final-snapshot handling wherever starts are permitted. Acceptance phase boundaries never reset it. Degradation, shutdown, or restart discards it with the scheduler epoch, and replacement activation creates an empty ledger. Startup validation rejects configurations whose minimum-interval scheduled demand exceeds either formula, while runtime admission prevents activation, synchronization, sleep/refill, backpressure release, and other timing patterns from exceeding either rolling limit. Fair dispatch and no-starvation rules remain applicable only among work eligible under both runtime budgets.

After a transport attempt becomes eligible through the fair-dispatch rules, it may start only when both rolling budgets, its applicable transport-attempt guard, one global execution slot, and at least one exact token-second of credit are available. The dispatcher must not hold a transport-attempt guard or global slot while waiting for token credit or a rolling budget and must not subtract credit or append a ledger entry while waiting for a slot. Cancellation before the atomic boundary consumes no token or charge and creates no attempt record. Once transport invocation starts, its token and ledger charge are never refunded. Each HTTPS provider child performs this admission and accounting independently even when multiple children start at the same timestamp in one dispatch cycle; every child contributes one 8,192-byte ledger entry, same-timestamp children receive no intervening refill, and they follow the captured stable provider/fair-dispatch order. Acceptance phase membership uses each child's own transport-start timestamp, never fair-queue admission or aggregate-round admission.

### 4.5 Deterministic fair dispatch

The dispatcher uses a deterministic rotating fair queue over due logical targets. Every enabled logical target receives one immutable private key:

```text
(
  logical_target_kind_rank,
  logical_target_member_key
)
```

Kind ranks are exactly Gateway `0`, enabled DNS `1`, HTTPS aggregate `2`, and enabled TCP `3`. Disabled DNS and disabled TCP entries create no scheduler key. Singleton Gateway, DNS, and HTTPS keys use the empty member key. Each enabled TCP member key is its accepted trimmed stable `id`. TCP IDs compare first with `.NET StringComparer.OrdinalIgnoreCase` and then, as a deterministic defensive tie-break, with `.NET StringComparer.Ordinal`; configuration remains invalid when two IDs compare equal ignoring case. Order is never derived from configuration array position, host, port, label, interval, mutable state, generated GUID, hash-table order, discovery order, locale, or customer data other than the already accepted stable local ID.

At scheduler activation, the dispatcher constructs one immutable ring sorted by that key. Its initial rotating cursor points to the first key: Gateway, then enabled DNS, then HTTPS, then enabled TCP targets in the defined ID order. At each logical-target dispatch turn it scans circularly from the current cursor, skips disabled, canceled, closed, or not-due work, chooses the first due logical target, offers that target under the existing transport-capacity rules, and advances the cursor to the immediate successor of the selected logical-target key whether or not a transport starts. If no due target exists, the cursor remains unchanged. Multiple same-timestamp turns use the cursor produced by the preceding turn, so capacity waiting cannot let one target monopolize the cursor.

The same logical-target key governs dispatch, due-ledger identity, acceptance workload-manifest identity, same-timestamp target order, barrier cancellation order, shutdown reconciliation order, and acceptance continuity reconciliation. Within one scheduler epoch, each configured HTTPS provider child separately has one stable internal member identity. Provider-child order remains independent and uses stable provider ID with `.NET StringComparer.Ordinal`. Keys, identities, and comparison results are never logged, reported, derived from display names or endpoint strings, or exposed through health, support, persistence, or metrics output.

- Gateway, DNS, and each due TCP logical target contribute at most one attempt per pass.
- When an HTTPS logical round is admitted, its provider children are sorted by stable provider ID using `.NET StringComparer.Ordinal`. The captured order is immutable for that round and is never logged or reported.
- On the HTTPS target's dispatch turn, the dispatcher offers every not-yet-started provider child in that stable order during one bounded fan-out. Every child for which both rolling budgets, a slot, and at least one exact token-second of credit are available starts without waiting for an earlier provider to complete and without waiting for a complete pass over all other logical targets. Children evaluated at the same timestamp consume available credit and rolling capacity in that stable order with no intervening refill.
- If the initial fan-out leaves provider children pending because a rolling budget, slot, or token credit was unavailable, subsequent dispatch opportunities alternate one eligible pending child with one next eligible due non-HTTPS logical target, beginning with the pending child and skipping an empty or rolling-ineligible lane. Waiting for credit or a rolling budget never reserves a transport-attempt guard or global slot, and waiting for a slot never reserves or subtracts credit or charge. Pending provider children therefore remain bound to the same round and deadline but are never moved behind a complete pass over all other eligible logical targets.
- Child completion order does not affect aggregation. The round completes only after every configured provider has exactly one terminal child result; reduction reads results in the captured stable provider order.
- Cancellation stops new child starts and promptly cancels active children. Deadline expiry follows section 4.3's universal equal-timestamp order: it applies every eligible terminal child classification and exact-deadline timeout before reduction, then commits either the complete aggregate result or scheduler-level `incomplete`; no not-yet-started child may start at or after the deadline.
- Canceled, disabled, or no-longer-due work is skipped.
- A provider fan-out is bounded by the configured maximum of four children, by the process-wide cap of four, and by one active HTTPS round. After that bounded fan-out, the rotating non-HTTPS cursor continues from its prior position; no second HTTPS round can consume slots or tokens until the preceding round has committed one section 4.2 closure and closure-specific fixed-delay scheduling makes the next round due.
- Bounded HTTPS occupancy must not cause systematic starvation of Gateway, DNS, or TCP work under sustainable valid mixed demand eligible under both hard rolling budgets.
- Transient backpressure can still make a round incomplete under the ten-second deadline.

Fair-queue selection establishes candidate order but never changes `round_due` or `round_deadline`. Section 4.3 deadline processing wins over selection or start at an equal timestamp. Runtime-budget admission, transport-attempt-guard/global-slot acquisition, token consumption, rolling-ledger append, start recording, and invocation then follow the one atomic boundary in section 4.4 only while `transport_start < round_deadline`.

### 4.6 Shutdown

At scheduler shutdown, sample exactly one injected-monotonic `shutdown_pause_timestamp` and perform one atomic reconciliation under the section 4.0 coordinator:

1. stop all new logical-round and transport admissions;
2. process terminal classifications and complete logical reductions ordered at or before the pause;
3. process ordinary deadlines at or before the pause;
4. in immutable logical-target key order, commit controlled-cancellation scheduler incomplete with private reason `shutdown` for every remaining open admitted logical round;
5. permanently prohibit every missing transport start;
6. cancel active transport attempts and close their attempt-level accounting under the existing bounded shutdown rules;
7. prove every HTTPS logical-round ownership guard was released exactly once by its logical closure, independently from attempt-level guard closure;
8. prove exactly one due-ledger closure for every admitted round;
9. discard token credit, its refill anchor, the rolling transport-start admission ledger, and the in-memory schedule only after no further start is possible and reconciliation is complete; and
10. dispose transports and scheduler resources within the unchanged five-second shutdown limit.

Shutdown never waits for a future immutable round deadline. A terminal reduction already committed before shutdown cancellation wins, ordinary deadline processing wins when `shutdown_pause_timestamp == round_deadline`, and no round can receive both terminal/deadline closure and shutdown controlled cancellation. A shutdown-controlled closure is non-counting, retains the original deadline only as its conceptual cadence anchor, creates no executable replacement work, admits no later round, and permits the schedule to be discarded only with the reconciled scheduler epoch. Normal planned shutdown cancellation is not itself a report violation. Failure to reconcile an admitted due round remains an implementation invariant and, where acceptance evidence applies, uses the existing continuity or cleanup violations rather than a new code.

For TCP specifically, shutdown reconciliation reaching an admitted round before `transport_start` creates no attempt record or outcome and only commits the shutdown-controlled logical closure. Reaching it after `transport_start` commits the same logical closure, actively cancels the resolver/interface-snapshot/normalization/connect invocation, and closes bounded attempt accounting as `canceled` with common evidence `cancelled` when cancellation is observed or as attempt-level `incomplete` with `attempt_accounting_incomplete` when no terminal canceled outcome is obtained. The already committed shutdown logical closure, unchanged TCP state, original-deadline conceptual anchor, exact-once due-ledger closure, and no-replacement rule cannot be rewritten by a later callback.

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

For TCP snapshot processing, an ordinary bounded acquisition failure and an eligible snapshot operation timeout are counted TCP failures and therefore advance the TCP consecutive-failure streak and operational state under these rules. Malformed or contradictory resolver/interface data is an immediate non-counting terminal result. Start/end barrier cancellation applies only before TCP `transport_start`; shutdown cancellation may also apply after start. Both controlled-cancellation paths leave TCP failure/recovery counters and operational state unchanged, while a TCP invocation already started at a start/end pause instead drains and applies its eventual ordinary success, counted-failure, invariant, or deadline-expired state effect. A valid complete snapshot with no eligible candidate remains a counted TCP failure.

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

A logical target is flapping when at least four state transitions among `healthy`, `suspect`, `offline`, and `recovering` occur in the authoritative rolling ten-minute window. The existing transition-count threshold and qualifying-state set do not change.

Each qualifying material state transition receives exactly one internal timestamp from the same injected monotonic clock used for deterministic elapsed-time behavior elsewhere in R0-005. The timestamp is sampled at the exact atomic state-transition commit boundary and is the committed transition instant. Probe admission, transport start or completion, callback delivery, log write, UTC sampling, report generation, event-log time, and file time are not transition timestamps. Entry into `unknown` or `baselining` is not qualifying. Repeated rounds that leave state unchanged are not transitions and receive no flapping-history entry. In particular, repeated success in `healthy`, below-threshold failure in `suspect`, and counted failure in `offline` neither add a transition nor restart the clear interval.

For evaluation at one injected monotonic time `now`, the authoritative ten-minute history is:

```text
now - 10 minutes < transition_timestamp <= now
```

Before threshold evaluation, evict every timestamp satisfying `transition_timestamp <= now - 10 minutes`. A transition exactly ten monotonic minutes old is expired, a transition one monotonic tick newer remains, and a transition committed at `now` is included. Evaluation and a newly committed transition use one deterministic injected-monotonic `now` at the transition boundary; aligned wall-clock buckets and calendar minutes are prohibited.

Once active, flapping clears only when the injected monotonic age since the latest qualifying transition satisfies:

```text
now - latest_transition_timestamp >= 20 minutes
```

Clearing does not occur one monotonic tick before the boundary and occurs at exactly 20 monotonic minutes. Any new qualifying transition before that boundary becomes the latest transition and restarts the 20-minute age. Self-transitions and other nonqualifying observations do not restart it.

Flapping transition ordering, window membership, eviction, threshold evaluation, activation, clearing, and elapsed-duration calculation use only injected monotonic time with checked deterministic arithmetic. UTC or local wall time, timezone, DST, NTP adjustment, manual clock correction, leap-second representation, event-log timestamps, operating-system file timestamps, timer ticks, and calendar buckets cannot affect the result. Existing bounded UTC correlation may continue where separately permitted, but UTC never participates in flapping behavior. A monotonic reversal, malformed value, overflow, or inability to evaluate an interval is an implementation invariant failure and cannot fall back to UTC or wall time.

History is bounded to timestamps needed for the current ten-minute window plus the latest qualifying transition. It remains process-local internal state: no flapping timestamp or HTTP method is added to reports, persistence, logs, metrics, health, or support output. Restart or degradation discards the history, enabled probes resume through the existing initial/baselining lifecycle, and prior-process monotonic values are never compared with the new process. Flapping does not replace operational state, change cadence or thresholds, create a cloud/user incident, or suppress notifications.

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

TCP snapshot outcomes use no new evidence code. Ordinary snapshot-acquisition failure and a valid complete snapshot with no eligible candidate use only `tcp_connect_failed`; snapshot operation timeout uses only `tcp_connect_timeout`; malformed or contradictory resolver/interface data uses only common `round_incomplete`; and only an unstarted barrier/shutdown-controlled round or a shutdown-canceled started invocation uses the existing common cancellation evidence behavior. A start/end barrier after TCP `transport_start` cannot use `cancelled` and cannot suppress the ordinary later TCP evidence. No snapshot path may emit more than one logical evidence classification, and an already committed deadline or shutdown-controlled closure cannot be relabeled by a later adapter result.

`configuration_invalid` is the only stable bounded evidence code for atomic R0-005 customer startup-configuration rejection. It covers syntax or binding failure; missing required configuration; every UTF-16 trim, control, surrogate, ASCII, label, length, Unicode, ACE-prefix, URL/authority/port/path, TCP classification/literal/hostname, DNS-host, provider-host, or registrable-domain validation failure; invalid field types or values, targets, providers, counts, uniqueness, provider diversity, intervals, or combinations; either or both failed startup-budget predicates; and any other customer startup validation failure already covered by this contract. Malformed pinned PSL source bytes or rules instead use the existing `source_identity_invalid` preflight rule. Validation may privately distinguish the failed predicate for deterministic control flow and tests, but that distinction cannot be emitted. No target, provider, interval, host, endpoint, rejected value/code unit/label, replacement value, exception, index, Unicode or ACE value, parser or URI-normalization detail, environment or globalization setting, ICU/NLS mode or version, calculated demand, cap difference, or customer-controlled value enters evidence, reports, logs, metrics, health, support output, or persistence.

Codes are not user-facing causal claims. Production logs and metrics may include the probe kind, code, aggregate counts, state names, and bounded duration. They must not contain:

- exception text, stack traces from expected transport failures, API return values, operating-system error/status codes or strings, or snapshot failure reasons/stages;
- IDs, labels, hostnames, IP addresses, ports, URLs, query names, `dns_logical_query_name`, `dns_absolute_resolver_name`, `tcp_logical_host_name`, `tcp_absolute_resolver_name`, `https_transport_host`, `canonical_https_url`, literal or candidate address bytes, raw or normalized scope, DNS suffix/search/devolution configuration, DNS answers, SNI or certificate inputs, generated or supplied `Host` values, headers, bodies, adapter identity, snapshot presence, duplicate or malformed rows, selected/rejected predicates, interface identities or types, `Ipv6IfIndex`, `Ipv6Enabled`, `OperStatus`, VPN status, `DisableDefaultRoutes` values, interface descriptions, route/interface candidate fields, next hops, metrics, indices, LUIDs, protocols, origins, or machine paths;
- payloads, credentials, authorization, user information, cookies, customer identifiers, or provider trace metadata;
- DNS-correlation sequence, completion timestamp, round classification, or captured resulting state;
- raw configuration or entire serialized outcomes.

Neither private DNS/TCP/HTTPS name or URL form, literal bytes, the input's terminal-dot or explicit-port representation, original or preprocessed rejected input, rejected code unit or label, replacement value, exception, index, Unicode value, ACE label, parser or URI-normalization detail, PSL rule/line detail, environment/globalization setting, ICU/NLS mode or version, exception text, nor host DNS suffix/search/devolution configuration may appear in reports, evidence, logs, metrics, health, support output, persisted values, report properties, or violation details. They remain private control inputs only.

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

A required member is `exercised` only by attributable work during the half-open steady-state interval whose transport or required logical-round operation starts and reaches one terminal, completely accounted result under the attempt-closure rules. A member counts once as distinctly exercised regardless of later attempts. Terminal failure, timeout, or an authorized post-start cancellation such as scheduler shutdown or a started child of a partially started HTTPS round can exercise the member; endpoint success is not required. A start/end barrier never converts an already-started Gateway, DNS, or TCP invocation into cancellation. Work canceled before transport start, startup-only work, and a started attempt that never reaches terminal accounting do not exercise a member.

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
- After exact-tree source capture, configuration acceptance, and static host-profile/preflight success, `process_creation_failed` is mandatory exactly once when the coordinator attempts to create the bound service process but process creation fails before a valid exact PID and retained process handle are established. This includes a bounded operating-system process-creation API failure before process identity exists. It is not `source_identity_invalid`, which classifies source/artifact invalidity; `process_exited`, which requires an actual process that existed and then exited; or `process_profile_invalid`, which requires an exact PID whose profile cannot be accepted. If a partial operating-system operation creates an actual process/PID, the coordinator uses the existing exact-PID, source-image, profile, exit, intent, and cleanup rules rather than pretending no process existed.
- A pre-PID process-creation failure retains the valid source, accepted configuration, completed static host values, and all ten cap constants. It sets `configuration_accepted = true`, `host_profile_complete = true`, `process_started = false`, and every later availability flag to false; all eight process-specific and every later value are `null`. No readiness or scheduler exists, `result` is `fail`, command exit is nonzero, and `process_creation_failed` appears exactly once. Every independently applicable cleanup failure remains separately reportable. Executable path, working directory, command line, operating-system status or error code, exception text, account identity, token or security descriptor, and environment contents enter neither report nor bounded output.
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

Continuous readiness and epoch ownership are combined with deterministic due-work reconciliation. The scheduler maintains one internal due-round ledger under sections 4.2 and 4.3. Logical-round admission atomically creates each entry at its scheduled `round_due`. Every entry uses the same immutable private round identity, member identity, `round_due`, `round_deadline = round_due + 10 seconds`, one closure discriminator, reduction commit time, and scheduling anchor as dispatch. For every Gateway, enabled DNS, HTTPS aggregate, and enabled TCP logical round whose calculated `round_due` occurs before `steady_state_end`, the stable logical-target identity must receive exactly one of the three section 4.2 closure classes:

- one terminal logical reduction, including a valid success, counted failure, or existing non-counting terminal result, committed at or before its deadline;
- one deadline-expired scheduler incomplete committed through ordinary exact-deadline processing; or
- one controlled-cancellation scheduler incomplete committed through an acceptance barrier or scheduler shutdown under the only permitted private reason for that path.

A due round may not silently disappear during a barrier, capacity/token contention, degradation, cancellation, or any other scheduler path. For each required logical target, the ledger proves:

```text
due_rounds =
    terminal_rounds
  + deadline_expired_scheduler_incomplete_rounds
  + controlled_cancellation_scheduler_incomplete_rounds

explicit_scheduler_incomplete_rounds =
    deadline_expired_scheduler_incomplete_rounds
  + controlled_cancellation_scheduler_incomplete_rounds
```

Both incomplete classes count exactly once under the existing `explicit_scheduler_incomplete_rounds`; the distinction adds no report field, availability flag, violation, cap, or persistence shape. The ledger uses the same immutable logical-target key as dispatch and manifest reconciliation. Dispatcher wake, fair selection, transport-attempt-guard/slot/token eligibility, rolling-budget eligibility, route discovery, hostname resolution, provider fan-out, transport start, a barrier or shutdown timestamp, cancellation callback, drain completion, stable-snapshot commit, or resume cannot replace the original due, deadline, or required scheduling anchor.

A due round with no closure, duplicate closure, controlled cancellation followed by terminal or deadline-expired logical reduction, terminal reduction followed by cancellation, wrong-member closure, missing or mismatched original deadline, closure after its permitted boundary, a barrier/shutdown-time cadence anchor, or next-due calculation from the wrong anchor makes due-work reconciliation false and adds the existing `acceptance_scheduler_inactive`. The same violation is mandatory if a start/end barrier controlled-cancels an already-started Gateway, DNS, or TCP invocation, assigns it `canceled`, emits `cancelled` for it, or discards its later ordinary classification merely because the barrier began. For HTTPS, the same violation is mandatory if the logical-round ownership guard releases before closure or remains held after closure, an incomplete closure suppresses its calculated subsequent due, a second round is admitted before prior closure or before calculated due, a prior closure is rewritten, a prior child is attributed to the next round, or duplicate closure or duplicate next-round admission occurs. After-deadline or post-shutdown-cancellation started-attempt accounting may close only as bounded attempt-level evidence and cannot change the already committed logical outcome. Ledger identities, closure discriminators, timestamps, scheduling anchors, counts, and target/provider relationships remain internal and never enter reports, logs, metrics, health, support output, or persistence. This rule requires deterministic round closure, not endpoint success or an exact success count; valid timeout, failure, shutdown-authorized post-start cancellation, and scheduler-level incomplete semantics remain unchanged.

The acceptance coordinator also retains internal exact transport-start/charge records from the scheduler epoch's rolling admission ledger sufficient to prove over activation and every later runtime interval that no rolling monotonic 60-second window contains more than 60 starts or reaches 100,000 fixed charged bytes. The proof uses the same started-attempt records that feed JSON and human aggregate accounting, includes each HTTPS child independently, preserves deterministic candidate order and exact-boundary expiry, and spans initialization, both acceptance barriers, steady state, drain, and final-snapshot handling without a phase-boundary reset. A lower 30-minute average cannot hide a hard rolling-budget violation. The internal proof exposes no ledger entry, timestamp, identity, path, endpoint, or configuration value in either report.

The transport-start instant is the monotonic timestamp captured at the same point the token is consumed, immediately before invoking the transport. Admission time, queue time, slot-wait time, and token-wait time do not determine resource-phase membership.

Both acceptance barriers use one identical serialized **barrier-pause procedure**. Each barrier samples exactly one injected-monotonic `barrier_pause_timestamp` at its atomic admission-pause boundary. That same private timestamp stops new admissions, separates already-due deadlines from pre-deadline rounds, selects eligible cancellations, and commits controlled cancellations in immutable logical-target key order. It is not reported. The procedure is:

1. Atomically pause every new transport admission at `barrier_pause_timestamp`.
2. Reconcile every due-ledger entry whose immutable `round_deadline <= barrier_pause_timestamp` through ordinary section 4.3 deadline processing.
3. Do not classify a round at its deadline through the pre-deadline barrier rule.
4. For each remaining due round with `barrier_pause_timestamp < round_deadline`, inspect whether every transport invocation required to permit a complete logical reduction has already started.
5. If one or more required invocations have not started, permanently prohibit those missing starts for that round, cancel queued or unstarted work, and commit exactly one controlled-cancellation scheduler-incomplete reduction immediately at `barrier_pause_timestamp` with private reason `start_barrier` or `end_barrier` as applicable, in immutable logical-target key order. Retain the original immutable `round_deadline` as the next-due scheduling anchor. Unstarted work consumes no token, slot, transport-attempt guard, fixed charge, rolling-ledger entry, or attempt record and has no measured bytes or attempt outcome.
6. If every required invocation has already started, do not barrier-cancel the logical round. Allow its started work to drain and permit one terminal logical reduction at or before its immutable deadline; otherwise apply ordinary deadline-expired scheduler incomplete at that deadline.
7. Started-attempt accounting may finish only under the existing bounded barrier/drain rules and cannot rewrite a committed barrier-canceled or deadline-expired logical outcome.

For Gateway, DNS, and TCP, the one required invocation either committed its start before the pause and drains under step 6, or it is unstarted and the round closes immediately under step 5. No second start, retry, or replacement is allowed. For HTTPS, no started child means immediate barrier cancellation. Some but not all required provider children started means the missing provider starts are permanently prohibited and the aggregate is immediately barrier-canceled; that closure releases the HTTPS logical-round ownership guard, while already-started children continue only for bounded attempt accounting and cannot convert the committed logical incomplete into a terminal aggregate result. When every configured provider child committed its start before the pause, the aggregate drains and reduces normally at or before its deadline or expires through ordinary deadline processing; deadline-expired closure releases the ownership guard and requires no later terminal aggregation. Every started child retains its token, fixed charge, phase membership, and exactly one attempt outcome without refund or duplication.

TCP snapshot capture is part of the already-started TCP invocation and does not change this split. A TCP `transport_start` before the pause makes the complete resolver/interface-snapshot/normalization/selection/connect pipeline drain-eligible and barrier-cancellation-ineligible even when snapshot work has not begun, is unfinished, or later fails. A TCP start losing the pause boundary is unstarted and creates no attempt. Callback delivery, task completion, interface enumeration, logging order, or thread scheduling cannot change which side of that serialized boundary applies.

Once `round_due <= barrier_pause_timestamp`, the due-ledger entry must already exist. A due time with no entry is a due-work reconciliation defect that adds `acceptance_scheduler_inactive`; the barrier does not silently repair it. A future round with `barrier_pause_timestamp < round_due` is not barrier-canceled.

Exact timestamp ties use the serialized scheduler and transport-start/admission boundaries. When `barrier_pause_timestamp < round_deadline`, pre-deadline cancellation is permitted only for missing-start work. When `barrier_pause_timestamp == round_deadline`, ordinary deadline processing runs first and exclusively. When a complete terminal result and barrier pause have the same pre-deadline timestamp, a terminal reduction already committed before the serialized pause operation remains terminal; otherwise the pause inspects the still-incomplete round and barrier-cancels only missing-start work. A transport start committed before the pause at the same timestamp remains started; the pause prevents every later start at that timestamp, and a start that loses the serialized boundary consumes no token, slot, transport-attempt guard, charge, or rolling-ledger entry. An API failure, timeout, malformed-data detection, or success ordered after the pause for a previously started TCP invocation remains eligible for ordinary reduction; opposite callback and task-delivery orders with the same serialized keys must produce identical closure, evidence, state, cadence, and accounting.

Controlled-cancellation scheduler incomplete is non-counting: it does not advance a failure streak or recovery progress, change operational state, create a probe incident, or create a report violation merely because a barrier canceled unstarted work or normal planned shutdown intentionally canceled work. It remains one private due-ledger closure and counts once under `explicit_scheduler_incomplete_rounds`. An independently applicable acceptance failure remains reportable. At the start barrier, invocations started before the pause remain startup attempts and their complete bytes, fixed charge, outcome, and workload attribution remain startup-only. At the end barrier, invocations started before `steady_state_end` remain steady-state attempts and retain their complete bytes, fixed charge, outcome, and workload attribution even if their accounting finishes during drain. Barrier timing never changes original transport-start membership.

Every round considered by a barrier was due at or before the pause, and therefore:

```text
round_deadline = round_due + 10 seconds
round_deadline <= barrier_pause_timestamp + 10 seconds
```

Consequently, a round whose complete required transport set already started can terminally reduce or ordinarily expire no later than the barrier's existing ten-second outer deadline, while an unstarted or partially started round closes immediately through barrier cancellation. A barrier never waits for an immutable round deadline later than its own outer deadline. This proof adds no allowance: stable-snapshot work still shares the existing start-barrier ten seconds, the end drain remains ten seconds, and final-snapshot stabilization retains its separate ten seconds.

After monitoring-ready, the acceptance coordinator establishes a clean steady-state start barrier:

1. Execute the shared barrier-pause procedure, with its one `barrier_pause_timestamp` starting the existing start-barrier allowance.
2. Complete bounded accounting for every invocation that started before the pause. No unstarted work creates an attempt record.
3. After no started invocation remains and while admissions remain paused, establish one steady-state baseline bundle containing the complete consistent recursive runtime-file snapshot, the exact process-CPU baseline, the acceptance-only attempt/network-accounting reset, and one injected-monotonic boundary timestamp. The reset seals the separately reported startup attempt/network totals and initializes the steady-state attempt/network accumulators to zero; it does not erase startup evidence.
4. Capture the process-CPU baseline immediately before the monotonic bundle-commit timestamp. Commit the bundle atomically; `steady_state_start` is exactly that successful commit timestamp. There is no wait for a later pre-existing sampling-grid boundary.
5. Resume admissions only after the baseline bundle and `steady_state_start` exist. Begin the requested 30 complete steady-state minutes at that exact timestamp.

For a round controlled-canceled at the start barrier, commit the incomplete result immediately, release any HTTPS logical-round ownership guard, and calculate its next due from the original deadline plus the existing deterministic jitter. Do not admit that next round before its retained calculated due, and keep admissions paused until the baseline bundle commits. After a successful barrier, resume the scheduler with that retained next-due schedule. Started HTTPS children from the canceled round remain startup attempt evidence only and cannot rewrite its closure or supply any identity, outcome, or accounting to the later round. If the barrier fails, admit no later round and use no later work to recover the run. The existing jitter lower bound and ten-second barrier bound make the retained next due occur after a successful barrier, but correctness comes from the explicit schedule rather than that timing assumption. No catch-up, immediate replacement, retry, or barrier-time re-anchoring is permitted.

A Gateway, DNS, or TCP invocation already started before the start-barrier pause is not a round controlled-canceled by that barrier. It drains within the existing single ten-second start-barrier allowance. Terminal reduction at or before its immutable deadline uses the ordinary terminal commit as cadence anchor; if the deadline arrives first, ordinary deadline-expired scheduler incomplete uses the immutable deadline. Its attempt accounting closes independently under the existing bounded rule, and the steady-state baseline is not established until the shared drain and baseline obligations are satisfied. The round is never rewritten as controlled-canceled.

The complete start-barrier operation, from the admission pause through successful baseline-bundle commit, has one ten-monotonic-second deadline. Exceeding it adds `measurement_boundary_timeout` and fails the run; drain completion does not start a new allowance. The runtime-file component uses the shared consecutive-map stable-snapshot protocol in section 8.6. If the monitored tree changes during a pair, the sampler may retry only within the same bounded barrier. If no stable complete snapshot can be committed before the deadline, the sampler fails with `runtime_file_snapshot_incomplete` and `measurement_boundary_timeout` as independently applicable rather than accepting an ambiguous baseline.

The acceptance pause does not initialize, reset, discard, or refund scheduler token-bucket credit or the rolling transport-start admission ledger. While admissions are paused, exact elapsed monotonic duration may lazily refill the active bucket only up to eight token-seconds and may age existing rolling-ledger entries only by exact monotonic duration under section 4.4. The next eligible starts after resume use that existing bucket, anchor, and retained rolling window. Only scheduler activation, readiness degradation, restart, or shutdown has the epoch-state lifecycle effects defined in sections 4.1 and 4.6.

`steady_state_start_barrier_elapsed_ms` measures from the admission pause immediately after readiness through the baseline-bundle commit at `steady_state_start`. For report attribution, `startup` spans process start up to that commit and therefore includes both initialization and the complete start barrier. All process CPU consumed before the committed CPU baseline, all runtime-file growth present in the committed snapshot, and every transport invocation whose transport-start instant precedes `steady_state_start` are startup-only. Steady-state CPU begins from the same committed CPU baseline, steady-state disk begins from the same committed runtime-file snapshot, and steady-state attempts/network begin at the same monotonic boundary. No CPU or disk interval is omitted, double-counted, or assigned to both phases.

Steady-state membership is the half-open interval:

```text
steady_state_start <= transport_start < steady_state_end
```

An invocation started before `steady_state_start` is startup work. An invocation started within the half-open interval is steady-state work. Each started invocation belongs to exactly one phase, and its bytes and outcome are never split across phases.

At exact `steady_state_end`, the acceptance coordinator establishes the end barrier:

1. Execute the shared barrier-pause procedure with `barrier_pause_timestamp` exactly equal to `steady_state_end`, before another token can be consumed.
2. Allow attempts started before the endpoint to finish bounded accounting. They remain steady-state attempts and contribute their complete sent, received, and charged bytes plus exactly one complete outcome even when completion occurs during the drain. A started Gateway, DNS, or TCP round drains to ordinary terminal reduction or deadline expiry and therefore has no end-barrier-canceled logical result to rewrite; only already-canceled partial-HTTPS logical results retain the existing no-rewrite rule.
3. Fail the run if the end drain exceeds 10 monotonic seconds. A started invocation without a terminal attempt outcome at that deadline becomes attempt-level `incomplete`, adds `attempt_accounting_incomplete`, and fails the run.
4. Keep admissions paused after the drain and immediately begin one separate final-runtime-snapshot stabilization window.
5. Commit the final snapshot through the shared consecutive-map stable-snapshot protocol in section 8.6 within exactly 10 monotonic seconds from end-drain completion.
6. Resume no work after `steady_state_end`; the final-snapshot protocol must not start a transport, restart acceptance accounting, or reset or reactivate the token bucket or rolling transport-start admission ledger.

For a round controlled-canceled at the end barrier, commit the incomplete result immediately, release any HTTPS logical-round ownership guard for reconciliation, and retain its original deadline as the conceptual scheduling anchor for deterministic ledger reconciliation. Do not create, admit, dispatch, or execute a subsequent round because no work may start after `steady_state_end`. The absent post-end round is not a missing-closure, missing-schedule, or continuity defect; the acceptance continuity interval closes under this existing endpoint rule.

A Gateway, DNS, or TCP invocation already started before `steady_state_end` is not controlled-canceled by the end barrier. It drains under the existing ten-second end-drain procedure, retains steady-state membership, and contributes its full bytes and ordinary terminal outcome. If its immutable deadline arrives first, ordinary deadline-expired scheduler incomplete commits; a later callback may close attempt accounting but cannot rewrite that logical closure. Failure to obtain a terminal attempt outcome by the end-drain accounting deadline uses the existing attempt-level `incomplete`, `attempt_accounting_incomplete`, and boundary rules.

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

The accepted configuration and every hostname-normalization result must remain identical under ICU mode, NLS mode, different system ICU versions where test infrastructure permits, CLDR or Windows globalization updates, `DOTNET_SYSTEM_GLOBALIZATION_USENLS=0`, `DOTNET_SYSTEM_GLOBALIZATION_USENLS=1`, `DOTNET_SYSTEM_GLOBALIZATION_APPLOCALICU`, globalization-invariant mode, changed current culture or UI culture, changed system language, and changed Unicode-normalization data. The corrected hostname algorithm satisfies this invariant by not calling globalization-dependent hostname APIs. These environment and globalization values are private test inputs only: acceptance must not add them to either report, log, persistence, metrics, health, support output, or another evidence surface.

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

R0-005 intentionally persists no probe evidence. Production behavior that attempts or completes any durable write of probe-derived information triggers the sticky bounded violation `probe_persistence_detected` exactly once and fails the run, independently of disk growth. Probe-derived information includes Gateway, DNS, HTTPS, or TCP results; target/provider health state; failure or recovery streaks; transition or flapping history; route/DNS correlation; attempt or workload-member evidence; probe counters; response-derived data; probe-result files; and probe-specific database, journal, outbox, registry, cache, or durable-queue entries. Later deletion, rollback, truncation, cleanup, or write failure does not forgive the detected attempt.

Attribution occurs at the durable-write boundary through one private fixed ownership/category mechanism equivalent to `probe_derived_persistence`. It must not infer ownership from filename text, a path substring, extension, content inspection, customer data, heuristic timing, or aggregate disk growth. The private mechanism retains no path, filename, table name, registry key, payload, target, provider, endpoint, address, customer identity, or exception text. The JSON and human reports represent the condition only through `probe_persistence_detected`; no artifact-name, category, path, persistence object, persisted-byte, target, or provider property is added.

The violation does not apply merely to approved preexisting R0-004 journal/lifecycle behavior containing no R0-005 probe-derived observation; the acceptance harness's ignored JSON/human staging and transactional final publication; source-build, extraction, or validation artifacts owned by the harness; or ordinary runtime files unrelated to probe-derived persistence. Those items remain subject to every independently applicable resource, source, cleanup, and publication rule.

Disk attribution and persistence policy are independent. A probe-derived artifact below `disk_mb_per_day < 100`, or an attempted durable write denied before any bytes exist, still requires `probe_persistence_detected`, `result = fail`, and nonzero exit without fabricating disk bytes or adding `disk_cap_exceeded`. A probe-derived artifact at or above the disk cap requires both `disk_cap_exceeded` and `probe_persistence_detected`, each exactly once in ordinal order. Unknown runtime-file growth without deterministic probe-derived ownership follows the normal disk/snapshot rules and does not invent the persistence violation. Every reached availability flag and every valid measurement remains retained in all of these scenarios.

Any run containing `probe_persistence_detected` fails regardless of disk or network caps, endpoint health, complete workload coverage, cleanup success, or later artifact deletion. This preserves the pre-registration, in-memory, no-probe-persistence architecture boundary without adding a persistent schema, report field, availability flag, cap, or dependency.

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
| `source.executable_sha256` | successful exact-tree provenance, coordinator-controlled restore/publish, frozen graph, one-file closure validation, complete-artifact digest, secure binding, and validation that launch/extraction/runtime-closure identity can be enforced; no measurement-availability flag | exact 64-character lowercase ASCII hexadecimal JSON string matching `[0-9a-f]{64}` for the complete single artifact | null when exact-tree provenance or controlled production did not succeed, the one-file private closure cannot be proved, the artifact cannot be selected/opened/completely read/retained/hashed, or launch/extraction/runtime-closure verification cannot be established; no prebuilt, empty, all-zero, partial, stale, adjacent-file, estimated, multi-file, framework-dependent, or substituted digest | retain the exact validated digest through embedded-metadata mismatch, process-creation failure, and every later failure; a metadata, launched-image, extraction, or external-load defect adds `source_identity_invalid` without replacing independently proven values |
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

When any source property or mandatory source preflight cannot be validly produced, including when exact reviewed-tree provenance, controlled restore/publish, the frozen input graph, one-file publish shape, complete private closure, digest, secure binding, exact pinned PSL byte/hash/strict-parser validity, or pre-process ability to enforce launch/extraction/runtime identity cannot be established, the sampler emits a **source-capture failure** before configuration acceptance, static host preflight, process creation, or scheduler activation. The fixed `source` object and all three properties remain present in exact order; each independently proven property is serialized normally, only an unavailable property is JSON `null`, and each null source value is `unavailable` in the human report. If the exact reviewed tree cannot be independently established, `commit_sha` and `executable_sha256` are `null` and no valid artifact may be claimed. If exact-tree provenance succeeds but artifact production, hashing, or pinned-PSL source preflight fails, `commit_sha` may remain non-null while `executable_sha256` follows its independently proven status. If provenance and hashing succeed but embedded metadata or the mandatory pinned source disagrees, retain independently proven non-null values as applicable, add `source_identity_invalid`, and begin no later stage. `configuration_name` remains independently available under its existing rule. All nine flags remain `false`, every later boundary-dependent value remains `null`, all ten fixed `caps` properties remain non-null, `source_identity_invalid` appears exactly once in ordinal order, `result` is `fail`, and the sampler exits nonzero. A structurally valid source-capture failure with correct independent nulls does not add `report_serialization_invalid` merely because production failed. After successful source capture, a later process-image, extraction, or external-private-code continuity failure retains all independently validated source values unchanged while failing acceptance with `source_identity_invalid`.

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
| Source capture or pinned-PSL source preflight fails | one or more source properties follow their independent availability rules; all nine flags are false and every later value is null | `fail`; `source_identity_invalid` exactly once; human `unavailable` for each null; no configuration, static preflight, process, or scheduler; no PSL byte, line, rule, parser, duplicate, or section detail escapes |
| Configuration rejected before process start | successful source-capture values remain retained; `configuration_accepted = false`; all nine flags are false; configuration, static/process profile, process, readiness, steady-state, attempt, coverage, and continuity values are null; all ten caps remain present and non-null | `fail`; `configuration_invalid` exactly once; no process, scheduler, valid subset, resolver or transport invocation, or probe workload; nonzero exit; no calculated budget value, original/preprocessed input, rejected code unit/label, replacement value, exception, index, Unicode/ACE value, parser/URI-normalization detail, environment/globalization setting, ICU/NLS mode/version, or private configuration detail in either report; identical behavior for attempt-only, charged-network-only, dual-budget, hostname/URL/TCP-literal validation, another customer startup defect, or multiple simultaneous defects |
| Static host/preflight failure before process creation | `configuration_accepted = true`, `host_profile_complete = false`, and all later flags are false; every static and process-specific host field and later measurement is null | `fail`; `host_profile_unsupported` or `host_profile_preflight_failed`; no process or scheduler |
| Process creation fails after static profile success before a valid PID/handle exists | `configuration_accepted = host_profile_complete = true`, `process_started = false`, and later flags are false; source, accepted configuration, static host values, and caps remain, while all process-specific and later values are null | `fail`; `process_creation_failed` exactly once; no process, readiness, or scheduler; nonzero exit; every independent cleanup violation retained |
| Process starts, initial exact-PID profile capture fails, and PID is live at arbitration | flags through `process_started = true`, `readiness_reached = false`, and later flags are false; static values remain; successfully captured process fields retain bounded actual values and independently unavailable fields are null | `fail`; `process_profile_invalid`; no accepted readiness, scheduler, or workload; no full final profile validation; commit exact-PID intent, issue graceful stop, treat post-intent exit as expected, and perform bounded cleanup |
| Process starts, initial exact-PID profile capture fails, and PID exits before intent | the same reached flags, retained values, and nulls as the live-PID initial-profile failure | `fail`; `process_profile_invalid` and `process_exited` each exactly once; no full final profile validation, retroactive intent, or stop; bounded remaining cleanup |
| Initial process profile succeeds but readiness times out | flags through `process_started = true`, `readiness_reached = false`, and later flags are false; all static and initial process-profile values remain non-null; readiness/later fields and continuity values are null | `fail`; `readiness_timeout`; no accepted scheduler workload |
| Dynamic process-profile violation during initialization | flags through `process_started = true`, `readiness_reached = false`, and later flags are false; all initial profile values remain unchanged and later values are null | `fail`; `process_profile_invalid`; no scheduler; terminate and perform bounded cleanup; restoration/detachment is not forgiven |
| Readiness reached but start barrier fails | through `readiness_reached = true`; `steady_state_started = false`; barrier duration is measured, baseline-dependent values are null | `fail`; applicable boundary/snapshot violations |
| Dynamic process-profile violation during start barrier | flags through `readiness_reached = true`, `steady_state_started = false`, and later flags are false; source/static/initial process values remain; barrier evidence is retained | `fail`; `process_profile_invalid`; scheduler admissions stop permanently and only bounded started-attempt closure, evidence, and cleanup continue |
| Steady state starts but fails before exact end | through `steady_state_started = true`; `steady_state_ended = false`; full-interval, attempt, coverage, and end/final values are null | `fail`; causal bounded violations |
| TCP interface-snapshot API returns ordinary bounded failure during an otherwise valid steady-state attempt | no acceptance boundary becomes false solely from this operational outcome; normal reached flags and accounting remain | one `failed` outcome, one completed attempt, counted TCP failure, `tcp_connect_failed`, normal TCP state reduction, one due-ledger closure, and no acceptance violation solely from this condition; the complete attempt exercises its member |
| TCP interface-snapshot acquisition reaches its two-second operation timeout | no acceptance boundary becomes false solely from this operational outcome; normal reached flags and accounting remain | one `timed_out` outcome, one completed attempt, counted TCP failure, `tcp_connect_timeout`, normal TCP state reduction, one due-ledger closure, and no acceptance violation solely from this condition; the complete attempt exercises its member |
| Start or end barrier wins before TCP `transport_start` | preserve the exact boundary reached by the governing barrier scenario | controlled-cancellation scheduler incomplete; no attempt record or outcome, token, charge, slot, guard, rolling-ledger entry, or bytes; existing controlled-barrier evidence; unchanged TCP state; original-deadline cadence anchor; one due-ledger closure; no replacement start |
| Start or end barrier occurs after TCP interface-snapshot invocation started | preserve the exact boundary reached by the governing barrier scenario and the invocation's original startup/steady-state membership | no barrier cancellation; drain the complete pipeline to ordinary `succeeded`, `failed`, `timed_out`, or malformed-data `incomplete` terminal reduction at or before the immutable deadline, otherwise ordinary deadline-expired scheduler incomplete; full original-phase attempt/byte/charge/workload accounting; one due-ledger closure; no `canceled` or `cancelled` from the barrier |
| Shutdown wins before TCP `transport_start` | preserve the exact boundary reached by shutdown | shutdown-controlled scheduler incomplete; no attempt record or outcome; unchanged TCP state; original deadline as private cadence anchor; one due-ledger closure; no replacement work |
| Shutdown wins after TCP interface-snapshot invocation started | preserve the exact boundary reached by shutdown | shutdown-controlled scheduler incomplete; active cancellation; one `canceled`/`cancelled` when observed or attempt-level `incomplete` plus `attempt_accounting_incomplete` when bounded accounting cannot obtain terminal cancellation; unchanged TCP state; original-deadline private anchor; one due-ledger closure; no later rewrite |
| TCP resolver/interface data is malformed or contradictory after transport start | preserve the other reached flags and valid values; attempt accounting closes with one `incomplete`; the required TCP member remains unexercised unless another complete qualifying attempt exists | `fail`; `round_incomplete`; `attempt_accounting_incomplete` exactly once; `acceptance_workload_incomplete` also exactly once when the member remains unexercised; nonzero exit; every independent continuity/boundary/cleanup violation retained; no private resolver/interface/error detail |
| Dynamic process-profile violation during steady state | flags through `steady_state_started = true`, `steady_state_ended = false`, and later flags are false; initial process fields remain unchanged | `fail`; `process_profile_invalid`; no recovery forgiveness, new admissions stop, and only bounded closure/evidence/cleanup continue |
| End drain reaches its terminal deadline | through `end_drain_finished = true`; `final_snapshot_window_started = true`; attempt accounting includes actual `incomplete` values | `fail`; `attempt_accounting_incomplete` and `measurement_boundary_timeout` |
| Dynamic process-profile violation during end drain | for the isolated injected defect, all nine flags become true after bounded drain closure and stable final snapshot; all initial process fields remain unchanged | `fail`; `process_profile_invalid`; no new work/restart/reactivation and exact bounded final evidence/cleanup retained |
| Final snapshot fails to stabilize | through `final_snapshot_window_started = true`; `final_snapshot_committed = false`; final elapsed is measured and disk values are null | `fail`; `runtime_file_snapshot_incomplete` and `measurement_boundary_timeout` |
| Dynamic process-profile violation during final-snapshot stabilization | for the isolated injected defect, all nine flags become true when the stable snapshot still commits; all initial process fields and completed measurements remain unchanged | `fail`; `process_profile_invalid`; no restoration/detachment forgiveness and no post-endpoint scheduler/network work; bounded cleanup still occurs |
| Dynamic process-profile violation during pre-termination cleanup after an otherwise complete run | stable final snapshot is already committed; all nine flags remain true; every completed source, static-host, initial process-profile, timing, measurement, attempt, coverage, continuity, and cap value remains non-null and unchanged; no new work or network admission occurs | `fail`; `process_profile_invalid`; authorized termination and bounded cleanup continue; expected post-intent exit does not add `process_exited`; `acceptance_cleanup_failed` and/or `synthetic_listener_cleanup_failed` appear only when their separate obligations fail |
| Exit during final validation or before the arbiter liveness check | preserve the exact earlier flag/value/null pattern; intent has not committed | `fail`; pre-intent `process_exited`; add `process_profile_invalid` when successful initial capture established continuity; no retroactive intent or stop; bounded remaining cleanup |
| Retained handle is signaled while callback delivery is delayed | preserve the exact earlier flag/value/null pattern; the zero-time handle check commits pre-intent exit | `fail`; the handle-signaled decision wins regardless of later callback delivery; no intent or stop |
| Exit immediately after a nonsignaled arbiter check or callback delivered after intent | preserve the exact earlier flag/value/null pattern; intent is the next committed arbiter sequence | expected post-intent exact-PID exit; no independent `process_exited`, `pid_changed`, or `process_profile_invalid`; stop and cleanup failures remain independently applicable |
| Otherwise complete run attempts or completes a small probe-derived durable write below the disk cap | retain all nine reached flags and every valid measurement; disk remains below cap | `fail`; `probe_persistence_detected` exactly once; no `disk_cap_exceeded`; no artifact name, path, category, or persisted-byte field |
| Probe-derived durable write is attempted but denied before bytes are written | retain the exact reached flags and values; fabricate no disk bytes | `fail`; sticky `probe_persistence_detected`; no disk-cap violation unless independently measured |
| Probe-derived artifact reaches or exceeds the disk cap | retain all reached flags and valid measurements | `fail`; both `disk_cap_exceeded` and `probe_persistence_detected` exactly once in ordinal order |
| Probe-derived persistence occurs after an earlier acceptance failure | preserve the earlier exact flag/value/null pattern and every earlier violation | add sticky `probe_persistence_detected` exactly once; retain every independently applicable prior and disk violation |
| Probe-derived artifact is later deleted, rolled back, truncated, or cleaned up | retain the original persistence detection and all independently reached evidence | `fail`; `probe_persistence_detected` remains sticky and deletion does not forgive it |
| Approved R0-004 journal/lifecycle write, acceptance report publication, harness-owned source/validation artifact, or unrelated runtime growth with no probe-derived ownership | apply ordinary journal, publication, source, cleanup, disk, and snapshot rules | no `probe_persistence_detected` merely from that write or growth; every independent violation remains applicable |
| Report publication fails after otherwise successful measurement and cleanup | all nine flags remain true and every measured value remains unchanged; staging is non-authoritative | `fail`; `report_serialization_invalid`; nonzero exit; no authoritative current-run report; `acceptance_cleanup_failed` only if staging cleanup independently fails |
| Report publication fails after an earlier acceptance failure | preserve the earlier exact flag/value/null pattern and every earlier causal violation | add `report_serialization_invalid`; nonzero exit; no authoritative current-run report; no prior or staged set is current evidence |
| Complete successful run | all flags are true and no acceptance field is null | `pass` only when every other rule passes and the complete JSON/human set publishes atomically to the fresh current-run destination |

If the same cleanup-phase process-profile defect occurs after an earlier acceptance failure, the report preserves the exact flag, value, and `null` pattern reached by that earlier failure and adds `process_profile_invalid` exactly once. Cleanup does not promote later flags, finish a failed producing boundary, or create a measurement that did not exist.

Every scenario after source-capture failure assumes exact-tree source capture completed successfully. Configuration rejection, static host failure, process-creation failure, initial or dynamic process-profile failure, readiness timeout, start-barrier failure, early steady-state failure, end-drain failure, final-snapshot failure, cleanup failure, and complete success therefore retain all three source properties present, non-null, valid, and byte-for-byte unchanged. No later failure may erase or replace one with `null`, an empty value, a different SHA, the current checkout SHA, a path-derived label, or another sentinel.

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
| `dns` | One admitted approved system-resolver transport invocation receiving exactly the one terminal-dot `dns_absolute_resolver_name`. |
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

Snapshot capture and normalization do not alter the `tcp` probe-kind entry, fixed charge, or scalar identities. An ordinary bounded snapshot-acquisition failure contributes exactly one `failed` outcome and increments `completed`; a snapshot operation timeout contributes exactly one `timed_out` outcome and increments `completed`; a started invocation canceled by scheduler shutdown contributes exactly one `canceled` outcome and increments `completed` when cancellation is observed; and malformed or contradictory resolver/interface data contributes exactly one `incomplete` outcome and does not increment `completed`. A start/end barrier before TCP `transport_start` creates no attempt entry or outcome; after `transport_start` it cannot create `canceled` and the eventual ordinary outcome applies. Each started path retains exactly one `tcp` probe-kind count, one fixed 1,024-byte charge, original phase membership, and one outcome. An already committed deadline or shutdown-controlled logical closure cannot be replaced by a later adapter result, but bounded attempt accounting still assigns the started invocation exactly one outcome under the existing closure rules.

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

`workload_coverage` is derived from the frozen manifest and terminal steady-state attempt records, never independently assigned. When non-null, Gateway and DNS values are JSON Booleans and provider/target values are nonnegative JSON integers. Its properties appear exactly in the table order above. After `configuration_accepted`, `gateway_required` is `true`, `dns_required` reflects the accepted DNS-enabled value, and configured counts equal the manifest's distinct category counts; before that boundary those four fields are `null`. After `end_drain_finished`, the corresponding exercised Boolean is `true` only when that required member meets section 8.1, disabled DNS requires both DNS flags to be `false`, exercised counts deduplicate exact internal member identity, and `all_required_exercised` is `true` if and only if every equality in section 8.1 holds, the Gateway logical-round/ICMP rule holds, no manifest identity or configuration defect exists, and every required member is distinctly exercised. Before complete end-drain accounting those five exercised fields are `null`, not false or zero. A completed TCP snapshot-acquisition `failed` or `timed_out` attempt qualifies to exercise its exact member; a snapshot invariant `incomplete` attempt cannot. A post-start `canceled` attempt retains the existing general eligibility rule only when bounded terminal cancellation accounting completes.

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

`violations` is an ordinal-sorted array containing only the following closed vocabulary of exactly 39 stable bounded codes when applicable. This replaces the prior 38-code vocabulary by adding only `process_creation_failed`; no extension code is permitted:

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
- `probe_persistence_detected`
- `process_creation_failed`
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

A malformed or contradictory resolver/interface snapshot result therefore makes `attempt_accounting_incomplete` mandatory exactly once through the final bullet. It does not add a snapshot-specific violation, convert the invariant into a counted TCP outage, or remove any independently applicable scheduler, boundary, continuity, workload, or cleanup violation.

Violation `acceptance_workload_incomplete` is mandatory when any required workload member is not exercised or workload identity cannot be reconciled. This includes zero steady-state attempts; missing Gateway coverage; missing enabled-DNS coverage; any unexercised configured HTTPS provider or enabled TCP target; a `configuration.target_count`/manifest mismatch; any configured/exercised count mismatch; duplicate identities masking a missing member; unknown, missing, duplicated, or unmanifested member attribution; accepted-configuration or manifest mutation; startup-only purported coverage; a coverage-calculation, derived-flag, or human/JSON identity failure; or malformed/missing/reordered `workload_coverage`. A started attempt without terminal accounting, including an immediate snapshot invariant `incomplete`, triggers the existing `attempt_accounting_incomplete` violation and cannot exercise a member, so both violations appear when no separate complete qualifying attempt exercises that member. A run containing `acceptance_workload_incomplete` can never pass regardless of resource values.

Violation `monitoring_readiness_lost` is mandatory for any false, unavailable, missing, or internally contradictory readiness level or transition after the first successful ready observation and before the continuity endpoint. It remains mandatory after a later true observation.

Violation `acceptance_scheduler_inactive` is mandatory when the accepted scheduler is stopped, disabled, disposed, replaced, changes or loses epoch, loses accepted-workload ownership, lacks an entry for a due round, silently loses a due round, commits no closure or more than one closure, commits a terminal or deadline-expired logical result after controlled cancellation, commits cancellation after terminal reduction, uses a wrong logical-target key or missing/mismatched original deadline, commits outside the closure's permitted boundary, uses a barrier or shutdown timestamp as cadence anchor, discards due-ledger or epoch state before shutdown reconciliation, calculates next due from the wrong anchor, or otherwise makes any of `scheduler_active_continuous`, `scheduler_epoch_unchanged`, or `all_due_rounds_reconciled` false. Accepted-configuration or manifest mutation independently adds `acceptance_workload_incomplete` as applicable. A run containing either continuity violation can never pass.

Coverage proves execution, not endpoint health. A completely accounted failed, timed-out, or post-start canceled attempt can exercise its exact member. No exact success count is required; the dynamically derived due-round count and its exact terminal, deadline-expired-incomplete, or barrier-canceled-incomplete identity remain private and exact, while scheduler, cadence, rate, resource, and at-least-one-exercise rules remain independently binding.

Violation `source_identity_invalid` is mandatory when the supplied full reviewed SHA cannot be independently resolved to and matched with its exact commit/root tree; the materialized tree differs, is dirty, mutates, contains untracked/generated input, escapes its root, or uses a submodule/gitlink; a prebuilt, prior, developer-worktree, overlaid, alternate-project, or externally influenced artifact/input is used; the frozen SDK/package/build-input provenance cannot be proved; restore/publish changes the graph or writes the source root; any source value is unavailable, missing, malformed, the wrong length, not lowercase hexadecimal where required, not the fixed configuration identifier, mismatched in the reviewed-SHA/materialized-tree/embedded-metadata three-way identity, derived from a prohibited mutable/private source, or not securely bound to the complete exact single acceptance artifact; the publish shape or private closure is invalid; artifact bytes, retained handle, or launched image mismatch; native extraction integrity fails; or application-private managed/native code loads from outside the authenticated bundle. It appears exactly once whenever one or more conditions applies.

Violation `process_profile_invalid` is mandatory when any initial exact-PID process-profile field is unavailable, malformed, or out of profile; process architecture, .NET major version, runtime identifier, GC mode, or priority is invalid; process priority is not `normal`; a managed/native debugger or profiler is attached initially or later; an exact-PID profile query fails after successful initial capture, including the conditional final pre-termination query; process-profile state becomes unavailable; event and sampled state contradict; prevention or observation is lost; or continuity established by successful initial capture cannot be proved through cleanup arbitration and committed intent. It appears exactly once when one or more applicable conditions occur. Full final validation is inapplicable after failed initial capture and skipping it adds no second violation. It is not replaced by `host_profile_preflight_failed`, `host_profile_unsupported`, `pid_changed`, `monitoring_readiness_lost`, or a cleanup violation; every independently applicable violation still appears.

Violation `process_creation_failed` is mandatory exactly once at the pre-PID boundary defined in section 8.1 when the coordinator attempted process creation after successful source, configuration, and static-host stages but established no valid exact PID and retained process handle. It is not emitted for source/artifact invalidity, a process that existed and exited, initial exact-PID profile failure, PID replacement, or a partial creation that produced an actual PID. Its report scenario retains all earlier valid values and caps, leaves all process-specific and later values null, creates no readiness or scheduler, fails the run, and exits nonzero without exposing any private process-creation detail. Every independent cleanup failure remains applicable.

Violation `process_exited` is mandatory for exact-PID exit committed pre-intent by the serialized arbiter, including a retained handle found signaled/exited before intent even when callback delivery is delayed. It is not added for the expected exact-PID exit after intent under the arbiter's intent-wins tie-break. A pre-intent exit also adds `process_profile_invalid` when initial process-profile capture succeeded and live-process continuity can no longer be proved; after failed initial capture that existing profile violation still appears once. No retroactive intent or stop is permitted, and `acceptance_cleanup_failed` is added only when an independent acceptance-owned cleanup obligation cannot complete.

Violation `probe_persistence_detected` is mandatory exactly once when production R0-005 behavior attempts or completes durable persistence of probe-derived information under section 8.6. Detection occurs at the private fixed ownership/category boundary and is sticky for the run: denial before bytes are written, later write failure, rollback, truncation, deletion, or cleanup does not remove it. It fails acceptance independently of disk arithmetic. Below-cap or zero-byte attempted persistence requires this code without `disk_cap_exceeded`; cap-reaching persistence requires both codes. The report representation is the violation alone and never includes an artifact name, path, category, persisted-byte value, target, provider, payload, or other private detail. Approved non-probe R0-004 journal/lifecycle behavior, acceptance-report publication, harness-owned source/validation artifacts, and unrelated runtime growth do not trigger it without deterministic probe-derived ownership.

Violation `acceptance_cleanup_failed` is mandatory for a failed or timed-out acceptance-owned cleanup obligation other than the separately named synthetic-listener condition, including failure to issue the graceful stop after intent, a graceful service stop that misses the existing five-second bound, or failure to remove acceptance-owned staging/provisional material after publication failure. Violation `synthetic_listener_cleanup_failed` is mandatory for synthetic-listener cleanup failure. When independent conditions occur, each applicable code appears exactly once in the existing ordinal order. Neither code substitutes for a genuine profile/report defect, makes staging authoritative, or makes expected post-intent exit an unplanned `process_exited` failure. A cleanup failure after an otherwise successful measurement run changes final `result` to `fail` but leaves all nine flags and every completed measurement intact.

Violation `rolling_attempt_budget_exceeded` is mandatory exactly when the internal exact transport-start/charge records prove that any rolling monotonic 60-second window contains more than 60 starts. Violation `rolling_network_budget_exceeded` is mandatory exactly when those records prove that any such window contains fixed recognized-method charges totaling at least 100,000 bytes. Each applicable code appears exactly once in ordinal order, fails acceptance even when the 30-minute aggregate is lower, and is never substituted for missing or incomplete accounting; inability to prove either invariant independently adds `attempt_accounting_incomplete` and fails.

Violation `unknown_probe_method` is mandatory whenever `attempts.by_probe_kind["unknown"] > 0`. Both `attempt_accounting_incomplete` and `unknown_probe_method` appear when both conditions apply. Every applicable violation appears exactly once, and the `violations` array remains in ordinal code order.

Violation `report_serialization_invalid` is mandatory when the availability object or any report property violates section 8.8, including a missing, additional, reordered, non-Boolean, or contradictory availability flag; a missing/reordered `source` or `host_profile`; a missing, additional, reordered, or duplicated fixed property; a wrong JSON type; a malformed source or process value emitted non-null; an empty, all-zero, abbreviated, uppercase, sentinel, path, or other prohibited source value; source availability/retention contradicting exact-tree evidence; a pass report containing a null, malformed, mismatched, or otherwise invalid source/process value; JSON/human disagreement; a non-null value before its producing boundary; `null` after a boundary when complete accounting was required; zero, false, empty, copied/planned/epoch, or another sentinel used as unavailable; a missing/null/reordered/duplicated/malformed/negative/nonfinite/altered cap constant; a cap substituted for an unavailable measurement; or a pass report with any false availability flag or null acceptance field. It is also mandatory for serialization/staging write, flush, close, reread, deterministic-byte validation, human/JSON agreement, staged-set completeness, destination-collision, or atomic directory-publication failure. Correct independent JSON nulls in a structurally valid source-capture failure require `source_identity_invalid`, and correct independent process-field nulls in a structurally valid initial capture failure require `process_profile_invalid`, but neither case by itself requires `report_serialization_invalid`. Schema, property-order, array-order, type, staging, validation, or publication defects make `result = fail` and cause nonzero exit. Atomic publication failure leaves no authoritative current-run report; staging-cleanup failure independently adds `acceptance_cleanup_failed`. Every other independently applicable violation also appears exactly once in ordinal order.

Neither report contains a source value derived from or containing a machine/user/domain/property/tenant/target/provider/customer identity; source-root, worktree, Git-object-database, build-log, package-cache, intermediate/output, project, source-file, artifact, executable, module, extraction, runtime, cleanup, local-checkout, or other absolute/relative path or filename; tree listing or blob ID; analyzer/generator identity; branch name; arbitrary environment value; or exception text. The valid full independently proven commit SHA, fixed `r0-005-25-target-acceptance/v1` identifier, and exact complete single-artifact SHA-256 digest are the only permitted `source` values.

The reports otherwise contain no serial number, BIOS serial, device UUID, Windows product key, MAC/IP/disk/cloud instance identity, endpoint, target, provider/member identity, scheduler key/epoch, route/interface candidate value, `DisableDefaultRoutes` value, interface identity/type, VPN status, next hop, readiness/process transition timestamp detail, PID, process command line, termination method, executable/module path, debugger identity, profiler identity/CLSID, diagnostic endpoint, process token, security descriptor, environment-variable value, event-source payload, synthetic-listener endpoint, account/user identity, DNS query name, `dns_logical_query_name`, `dns_absolute_resolver_name`, `tcp_logical_host_name`, `tcp_absolute_resolver_name`, `https_transport_host`, `canonical_https_url`, literal address bytes, SNI/certificate/`Host` inputs, PSL bytes/lines/rules/duplicates, DNS suffix/search/devolution configuration, DNS answer, payload, header, body, credential, customer identifier, or DNS-correlation snapshot field. The human report contains the same source values, initial process-profile values, null/`unavailable` decisions, profile validation results, retention, timing, attempts, coverage, continuity, caps, result, and violations, with no alternate calculation or private detail.

### 8.9 Failure rules

The sampler exits nonzero when:

- exact-tree source capture cannot independently resolve/materialize/verify the supplied full reviewed commit and root tree, the source root is dirty/mutable/escaped, an unapproved build input or prebuilt artifact is used, controlled restore/publish or frozen graph provenance fails, the reviewed-SHA/materialized-tree/embedded-metadata identity mismatches, the one-file publish shape/private closure is invalid, pinned PSL bytes/hash/strict decoding/line/section/rule/duplicate preflight is invalid, artifact bytes or secure binding mismatch, native extraction integrity fails, the launched image differs, external application-private code loads, or any source identity/structure/retention/privacy rule fails;
- configuration is rejected, including under the scoped fail-closed/no-scheduler R0-005 startup rule;
- any acceptance-required availability flag is false, any required value is unavailable, or an availability implication/matrix rule fails;
- readiness exceeds 60 seconds;
- readiness is lost, unavailable, or contradictory after first readiness and before the continuity endpoint;
- the accepted scheduler is inactive, disabled, disposed, replaced, changes/loses epoch or workload ownership, or fails exact due-round reconciliation at any point in the continuity interval;
- any static acceptance-host requirement is unsupported, required host metadata is missing, or a prohibited shared/CPU-quota-limited host is used;
- the 60-second host preflight is missing a required sample, has average non-agent system CPU greater than or equal to 10.0%, or ends with less than 2,000,000,000 bytes available physical memory;
- the coordinator attempts process creation after successful source, configuration, and static-host stages but establishes no valid exact PID and retained process handle, adding `process_creation_failed` exactly once and preserving the required pre-PID report/null/privacy behavior;
- any initial exact-PID process-profile field is unavailable, malformed, or out of profile; initial process architecture/runtime/GC/priority/debugger/profiler validation fails; readiness or scheduler activation precedes successful initial capture; process priority later leaves `normal`; a managed/native debugger or profiler attaches; exact-PID state becomes unavailable; event/sample evidence contradicts; prevention/observation is lost; the conditional final exact-PID validation after successful initial capture fails; or continuous proof established by initial success cannot be maintained through cleanup arbitration and committed intent; every such case adds `process_profile_invalid` exactly once, while failed initial capture requires no full final validation;
- any live started exact PID does not enter the one serialized exit/intent arbiter, the coordinator lacks the retained exact process handle/PID or pre-subscribed exit observation, a callback classifies exit independently, the retained-handle check is omitted, or arbiter sequence/tie-break semantics are violated;
- the arbiter commits pre-intent exact-PID exit, the PID changes before intent, or a replacement/restart occurs; pre-intent exit permits no retroactive intent or stop, while expected exact-PID exit under the post-intent tie-break does not add `process_exited` or `pid_changed`;
- the complete start barrier from admission pause through baseline-bundle commit, the end drain, or the separate final-snapshot stabilization window exceeds its 10-second deadline;
- a start/end barrier controlled-cancels an already-started Gateway, DNS, or TCP round, actively cancels that invocation, assigns it `canceled`, uses `cancelled`, discards an eligible post-pause callback, changes its original phase membership, or suppresses its ordinary terminal/deadline reduction; this is failed due-work reconciliation and adds the existing `acceptance_scheduler_inactive` without a new code;
- an invocation starts at or after `steady_state_end`;
- a sample is missing;
- a required non-disk counter or network-accounting value is unavailable, negative, reset, or overflows;
- a required recursive runtime-file snapshot is incomplete, unstable within its bounded start or final window, unreadable, contains an invalid length, overflows during subtraction or summation, escapes the monitored directory, or loses a baseline file;
- working set reaches or exceeds 150,000,000 bytes;
- steady-state average CPU reaches or exceeds 2.0%;
- disk reaches or exceeds 100 decimal MB/day;
- production R0-005 behavior attempts or completes any probe-derived durable write, even when denied before bytes are written, later removed, or below every resource cap; `probe_persistence_detected` is mandatory and every independent disk violation remains applicable;
- network reaches or exceeds 100,000 bytes/minute;
- any rolling monotonic 60-second window exceeds 60 transport starts or reaches 100,000 fixed charged bytes, or complete exact records cannot prove both hard admission invariants over activation and all later runtime intervals;
- any growth rule fails;
- any required workload member is not distinctly exercised or manifest/configuration/member reconciliation fails under mandatory `acceptance_workload_incomplete` conditions in section 8.8;
- any required `runtime_continuity` value is false or its event/sample/epoch/due-ledger derivation fails;
- attempt accounting, exact recognized-method charge derivation, or configured-budget/acceptance table consistency is incomplete under any mandatory `attempt_accounting_incomplete` condition in section 8.8;
- malformed or contradictory resolver/interface data produces a started TCP `incomplete` outcome; this uses `round_incomplete`, adds `attempt_accounting_incomplete` exactly once, also adds `acceptance_workload_incomplete` when no separate complete qualifying attempt exercises that member, preserves every independent continuity/boundary/cleanup violation, and can never pass;
- an unknown probe method is encountered;
- report serialization, staging, flush, reread, validation, staged-set completeness, destination freshness, or atomic directory publication violates section 8.8, adding `report_serialization_invalid`; atomic publication failure leaves no authoritative current-run report and requires nonzero exit;
- graceful-stop issuance fails after committed intent, the stop misses its five-second deadline, staging/provisional cleanup fails, or another general acceptance cleanup obligation fails, adding `acceptance_cleanup_failed`;
- synthetic-listener cleanup fails, adding `synthetic_listener_cleanup_failed`; when both independent cleanup conditions fail, both codes appear exactly once.

`configuration_invalid` is mandatory exactly once whenever atomic R0-005 customer startup configuration is rejected, including malformed binding; any exact UTF-16 preprocessing, ASCII/control/surrogate, LDH-label, complete-name, Unicode, `xn--`, closed HTTPS URL/authority/port/path/canonical-correspondence, TCP classification/literal/hostname, DNS logical-name, or registrable-domain failure; a single-label or malformed-root-dot configurable name; missing or invalid target/provider fields; uniqueness, cardinality, diversity, or interval failure; an unsupported combination; attempt demand exceeding 60; charged-network demand reaching or exceeding 100,000; either budget predicate alone; both budget predicates; or multiple simultaneous startup defects. Customer validation failure exposes no original or preprocessed input, rejected code unit or label, address bytes, matched or failed direct-literal predicate, prefix, registry or platform classification, replacement value, exception, index, Unicode or ACE value, parser/URI-normalization detail, environment/globalization setting, ICU/NLS mode or version, or exception text and creates no scheduler, resolver, or transport work. It applies before scheduler activation to rejected configured demand and is not replaced by a hostname-specific configuration code, `rolling_attempt_budget_exceeded`, `rolling_network_budget_exceeded`, `acceptance_scheduler_inactive`, `acceptance_workload_incomplete`, or `report_serialization_invalid`. Malformed pinned PSL source is instead `source_identity_invalid` before configuration acceptance. The two rolling-budget violations apply only when exact transport-start accounting proves a runtime rolling-window violation after transport starts exist; configuration rejected before activation has no transport start and cannot emit either runtime rolling-budget violation. Every independently applicable serialization, source, or cleanup violation remains applicable, but any number of customer configuration defects still contributes only one `configuration_invalid`.

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
- one universal deadline table covering all four logical-round types: initial `round_due` exactly at activation; subsequent due from the section 4.2 scheduling anchor plus deterministic jitter; terminal reductions anchored to their exact commit; both scheduler-incomplete classes anchored to the original deadline; deadline exactly due plus ten checked monotonic seconds; late dispatcher wake without re-anchoring; and fair-queue/slot/token/rolling-budget delay all consuming the original interval;
- transport start one tick before deadline, rejection exactly at deadline, timeout one tick before, timeout exactly at deadline classified before round expiry, timeout one tick after unable to change the logical result, complete reduction exactly at deadline, and a missing required start at deadline producing scheduler-level incomplete without token/slot/transport-attempt-guard/rolling-ledger consumption;
- the one serialized coordinator and exact six-phase same-timestamp order, including DNS callback enqueued first, HTTPS due work enqueued first, reversed task completion, DNS success/counted-failure/non-counting publication, no previous snapshot, one tick before and after, no retroactive capture, and identical correlation across opposite enqueue/callback orders;
- the private total-order key using injected monotonic timestamp, phase rank, stable logical-target key, and same-timestamp sequence, with sequence unable to override phase rank, late callbacks unable to backdate, DNS completion timestamped only at reduction/publication commit, and document drift failing when equal-time phase ordering is absent;
- multi-provider HTTPS exact-deadline ordering, identical monotonic sequences producing identical outcomes for Gateway/DNS/HTTPS/TCP, checked monotonic overflow/reversal failure without wall-time fallback, and document drift failing if any round type or due-ledger path lacks the universal due/deadline anchor;
- start-barrier and end-barrier cancellation before the round deadline only for missing-start work; cancellation one monotonic tick before the deadline; pause exactly at the deadline using ordinary deadline processing; a terminal reduction committed before a same-timestamp pause winning; transport start and pause at the same timestamp in both serialized orders; and a pause winning before a potential start creating an unstarted round with no attempt;
- barrier cancellation for unstarted Gateway, DNS, and TCP rounds; HTTPS with no child started, some but not all children started, and every child started; a fully started Gateway/DNS/TCP round draining to terminal reduction before its deadline or expiring at its original deadline; and start/end barriers unable to cancel or assign `canceled`/`cancelled` to those started invocations;
- immediate barrier-cancellation commit, original-deadline cadence anchoring, rejection of barrier-time anchoring, exactly one of the three closure classes, exact due-ledger member identity, no failure-streak/recovery/state change, one `explicit_scheduler_incomplete_rounds` count, and no new violation or report field;
- unstarted barrier-canceled work producing no token, slot, transport-attempt guard, rolling-ledger entry, charge, bytes, record, or outcome; already-started Gateway/DNS/TCP work retaining ordinary logical reduction, original phase, token, charge, full bytes, and exactly one outcome; partial-HTTPS and shutdown-canceled started work retaining existing bounded accounting without refund or logical-result rewrite;
- HTTPS logical-round ownership-guard acquisition at atomic due-ledger admission and exact-once release on terminal, deadline-expired incomplete, and controlled-cancellation incomplete closure; no release from one child completion, no retention after incomplete closure, no reacquisition for the same round, and no second closure;
- a due HTTPS round waiting with its ownership guard held and zero transport-attempt guards, no token/charge/slot/concurrency reservation or attempt record from that ownership, rejection of second-round admission, each child start acquiring only its provider-child transport-attempt guard, partial fan-out, closure while started children remain accounting-open, and ownership-guard release independent of attempt-guard closure;
- document-drift failure if the two guard names, the ownership guard's sole no-reservation exception, its no-capacity/no-token semantics, or transport-attempt acquisition only at the atomic start boundary is absent or ambiguous;
- no second HTTPS round before the prior closure or before its calculated `next_round_due`; one later round after terminal closure, deadline-expired incomplete, and successful start-barrier-canceled incomplete; no later round after end-barrier-canceled incomplete; and exact one-closure/one-next-due behavior without immediate replacement or catch-up;
- partially started HTTPS child completion unable to reopen or rewrite a committed closure, fully started deadline-expired HTTPS requiring no later terminal aggregation, and terminal-commit versus original-deadline cadence anchors remaining unchanged;
- fresh logical-round, due-ledger, due/deadline, and provider-child identities for each subsequent HTTPS round, with no provider outcome, token, charge, accounting record, DNS snapshot, or child completion reused or merged;
- successful activation creating exactly eight available tokens and an activation-time refill anchor, with the ninth otherwise eligible same-timestamp attempt unable to start;
- exact lazy monotonic refill with zero credit before one full second when starting empty, one token available at exactly one second, exact fractional-credit retention, same-timestamp evaluations receiving no refill, and long elapsed duration capping at eight tokens;
- one started attempt consuming exactly one token with no refund for any outcome, pre-start cancellation consuming none, slot waiting reserving no credit, token waiting reserving no slot, and monotonic reversal failing as an implementation invariant;
- degradation and shutdown reconciling every admitted due-ledger round before discarding token-bucket/schedule state, healthy restart/activation creating a new full bucket and anchor, and no prior credit surviving;
- four concurrent HTTPS children using four initial tokens while respecting global concurrency four, stable same-timestamp provider order, deterministic starvation-free sustainable mixed demand under an injected clock, and coarse operating-system timer cadence having no effect;
- worst-case attempt-demand and exact charged-network formulas, DNS enabled/disabled behavior, provider-count effects, and double-precision boundaries immediately below, exactly at, and immediately above 60 attempts/minute and 100,000 charged bytes/minute as applicable;
- startup validation cases below, exactly at, and above each formula boundary; attempt-only, charged-network-only, and dual-budget failures; a budget failure combined with another configuration defect; atomic no-subset rejection; no scheduler activation; and exactly one observable `configuration_invalid` with no retired budget-specific code, private-predicate distinction, calculated demand, or customer value escaping into evidence or output;
- exact fixed charge for each supported method, a shared configured-budget/acceptance table with no drift, adapter charge overrides rejected, measured bytes below the exact charge using the table value, measured bytes above it using measured bytes, and integer/culture/platform-stable arithmetic;
- two adapters with identical measured sent/received counts producing byte-identical totals and reports, each concurrent HTTPS provider receiving its own exact 8,192-byte charge, and deterministic totals immediately below, exactly at, and above 100,000 bytes/minute;
- eight starts at activation followed by exact one-per-second refill, with candidate 60 accepted and candidate 61 blocked inside the same rolling 60-second window, the complete first activation minute never exceeding 60 starts, and the first replacement accepted exactly when the oldest entry reaches age 60 seconds;
- deterministic rolling expiry and admission immediately before, exactly at, and immediately after the 60-second boundary, including the rule that an entry at exactly `now - 60 seconds` expires and same-timestamp candidates use stable fair/provider order;
- a fully refilled eight-token bucket after long elapsed time not bypassing either rolling cap, and activation, normal steady runtime, synchronized due work, release from backpressure, and long-elapsed refill all satisfying both hard invariants;
- rolling-budget blocking reserving no transport-attempt guard or global slot, consuming no token, creating no ledger/attempt/charge record, remaining cancelable, and producing scheduler-level incomplete rather than a started failure when its logical-round deadline expires;
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
- due-round ledger equality for Gateway, enabled DNS, HTTPS aggregate, and every enabled TCP member, with every due time atomically creating its entry and each round receiving exactly one terminal, deadline-expired scheduler-incomplete, or controlled-cancellation scheduler-incomplete closure; silent, duplicate, wrong-member, missing-deadline, late, post-cancellation, or wrong-anchor reduction fails;
- document-drift tests failing if section 3.3 requires terminal aggregation before a later HTTPS round; any incomplete closure fails to release the HTTPS logical-round ownership guard; a later round is blocked solely by prior incompleteness; a second round begins before prior closure or calculated due; child accounting rewrites a barrier-canceled result or retains the ownership guard; an end-barrier closure creates post-end work; or closure terminology and cadence anchors differ among sections 3.3, 4.2, 4.3, 4.5, and 8.1;
- acceptance barriers preserving the original epoch, token bucket, and rolling admission ledger, and continuity failure causing no same-run scheduler restart/reactivation or token/ledger reset;
- all provider children in valid two-, three-, and four-provider HTTPS rounds starting without serial five-second waits when both rolling budgets, the required slots, and tokens are available;
- three- and four-provider rounds in which every child times out at five seconds completing as one counted aggregate failure within the ten-second round deadline, and two consecutive such rounds advancing HTTPS state under the existing threshold;
- partial HTTPS success remaining aggregate success with `partial_endpoint_failure` when one provider succeeds and the other provider children fail;
- the process-wide concurrency cap never exceeding four, no provider having two concurrent attempts, and no two HTTPS aggregate rounds overlapping;
- stable provider-child order, independent child slot/token acquisition, one token and one attempt record per child, and separate complete provider counts, bytes, outcomes, and phase membership under concurrent starts;
- the immutable logical-target key with exact Gateway/DNS/HTTPS/TCP kind ranks, empty singleton member keys, enabled TCP accepted trimmed IDs, `.NET StringComparer.OrdinalIgnoreCase` followed by `.NET StringComparer.Ordinal`, invalid case-insensitive duplicates, and private comparison results;
- one immutable sorted ring with the exact initial cursor, DNS enabled/disabled, zero through 25 TCP targets, configuration-order permutations, ASCII and permitted non-ASCII IDs, same-timestamp repeated turns, cursor advance after both successful start and capacity-blocked offer, unchanged cursor when no target is due, provider-child order remaining independent, deterministic incomplete members under constrained initial tokens, and drift failure for any missing category/comparison/tie-break/cursor rule;
- the same logical-target key governing dispatch, due-ledger and workload identity, same-timestamp order, barrier and shutdown reconciliation, and continuity; stable rotating fair-queue order, bounded HTTPS fan-out, pending-child/non-HTTPS alternation, no systematic Gateway/DNS/TCP starvation under sustained valid mixed demand, cancellation before token consumption, and a started timeout consuming one token;
- genuine capacity or token contention that prevents required provider completion still producing the existing non-counting incomplete aggregate result;
- shutdown-controlled cancellation for queued Gateway, DNS, HTTPS, and TCP rounds; partially and fully started HTTPS; TCP shutdown before start creating no attempt and TCP shutdown after start actively canceling to bounded `canceled` or attempt-level `incomplete`; shutdown one tick before deadline; shutdown exactly at deadline with ordinary deadline winning; terminal result versus shutdown at the same timestamp; exactly one closure per admitted round; ownership-guard release; started-attempt accounting; no token refund, replacement round, or due-ledger discard before reconciliation; unchanged acceptance-barrier behavior; and completion within five seconds;
- exactly three closure classes with controlled cancellation restricted to `start_barrier`, `end_barrier`, and `shutdown`, private reason, immediate non-counting closure, original-deadline conceptual anchor, no later terminal/deadline closure, and no shutdown replacement schedule;
- flapping activation at the existing fourth-transition threshold, with each qualifying transition timestamped once at the atomic state-transition commit boundary and self-transitions/nonqualifying observations receiving no history entry;
- the injected-monotonic ten-minute window including a current transition and one exactly inside the boundary, evicting a transition exactly ten minutes old and one tick older, and retaining one tick newer;
- the injected-monotonic clear interval remaining active one tick before 20 minutes, clearing exactly at 20 minutes, and restarting from a new qualifying transition without changing transition eligibility;
- forward/backward UTC jumps, constant UTC with advancing monotonic time, timezone/DST changes, and identical monotonic sequences with different UTC sequences producing identical flapping results; monotonic reversal, malformed arithmetic, and overflow fail without wall-time fallback; restart and degradation discard history without persistence;
- document-drift tests failing if flapping uses an unspecified injected clock rather than injected monotonic time, permits UTC or wall time in calculations, changes the exact ten-minute or 20-minute boundary, broadens transition eligibility, or implies persisted flapping timestamps;
- configuration bounds, atomic rejection, duplicate IDs, 0/25/26 targets, interval/port limits, credential-like values, and URI/range rejection;
- malformed startup configuration activating no scheduler or partial valid subset, preserving no prior in-memory state or stale provider/target set across restart, leaving readiness unavailable/degraded with bounded value-free evidence, and corrected configuration plus restart permitting normal activation;
- document-drift tests failing if either startup formula maps to anything other than `configuration_invalid`; Section 7 permits another stable startup-budget evidence code; the configuration-rejection scenario omits exactly-one emission; Section 8.9 omits either startup-budget predicate or maps pre-activation rejection to a runtime rolling-budget violation; a retired budget-specific name appears; the closed violation count differs from 39; or report schema/property order changes;
- a document-drift check proving the R0-005 no-durable-LKG exception remains narrow and consistent with `docs/security-guardrails.md`, while durable/updateable configuration outside this exception continues to preserve its safely stored accepted last-known-good value;
- the exact six-UTF-16-code-unit trim set `0x0009`, `0x000A`, `0x000B`, `0x000C`, `0x000D`, and `0x0020`, including each member, repeated mixed members at both edges, all-empty rejection, every member rejected in an interior position, ordinary ASCII, nontrimmed Unicode outside a trimmed code unit, a trimmed code unit outside nontrimmed Unicode, and proof that no decoder, scalar enumeration, `Rune`, replacement behavior, configuration-provider convention, Unicode category, parameterless `String.Trim()`, or `Char.IsWhiteSpace` affects the result;
- rejection for a valid surrogate pair surrounding a hostname, unpaired high and low surrogates, U+FFFD, embedded `0x0000`, `0x007F`, U+0085, U+00A0, U+1680, U+180E, each value `0x2000` through `0x200A`, U+200B, U+2028, U+2029, U+202F, U+205F, U+3000, and U+FEFF, including wrapped inputs never normalizing to the interior hostname and every failure using only set-like `configuration_invalid`;
- the one shared non-root validator across DNS, TCP-hostname, HTTPS-provider-host, and registrable-domain paths; direct ASCII-code-unit operation; exact U+0041-through-U+005A plus-`0x20` lowercase mapping; ordinal comparison; no current-culture `String.ToLower()` or Unicode case conversion; LDH-only labels; interior-hyphen acceptance; underscore, edge-hyphen, empty-label, consecutive-dot, other-punctuation, control, and non-ASCII rejection; field wrappers owning zero-or-one terminal-dot policy exactly once; the shared validator removing no dot; PSL rules receiving no configurable preprocessing and permitting one label; and no second hostname algorithm;
- the exact label and complete-name boundaries: a 0-octet label is invalid, 1-, 62-, and 63-octet labels are valid when otherwise conforming, and a 64-octet label is invalid; 252- and 253-octet complete non-root names are valid when otherwise conforming and a 254-octet name is invalid; a leading dot is invalid; and a configurable trailing dot is absent only after the applicable field wrapper removes at most the one permitted root dot;
- direct Unicode rejection and rejection of every normalized label beginning with `xn--` in every ASCII case, including `xn--bcher-kva.example`, `XN--BCHER-KVA.example`, `xn--zca.example`, `xn--3xa.example`, a bare `xn--` label, every other `xn--` prefix, and `bücher.example`, with no ACE or Unicode decode and no canonicality test;
- the normative valid DNS set: `0x0020` + `Example.COM` + `0x0020`, `0x0009` + `example.com.` + `0x000D`, `example.com`, `example.com.`, `Example.COM`, `Example.COM.`, `a.example`, `a-b.example`, a 63-ASCII-letter label plus `.example`, and conforming 252- and 253-octet complete names; equivalent case and dotted/undotted forms produce one identical private logical name, absolute resolver name, workload identity, resolver argument, and behavior;
- field-wrapper no-double-removal vectors for `example.com`, `example.com.`, `example.com..`, `example.com...`, trim-set-wrapped `example.com.`, and trim-set-wrapped `example.com..`; plus rejection of `printer`, `printer.`, `_service.example`, `service._tcp.example`, `-host.example`, `host-.example`, `host..example`, `.host.example`, `host.example..`, multiple terminal dots, an empty label, a 64-octet label, a 254-octet complete name, every direct-Unicode or ACE case above, and every existing prohibited list, array, multiple-name, fallback-name, wildcard, search-expression/search-domain, URI, credential, or customer syntax;
- invalid customer hostname configuration producing no scheduler, valid subset, resolver invocation, or transport work; multiple defects still emitting exactly one `configuration_invalid`; and no original/preprocessed input, rejected code unit/label, replacement value, exception, index, Unicode/ACE value, parser/URI-normalization detail, environment/globalization setting, ICU/NLS mode/version, private name form, or input-representation detail entering evidence or output;
- exactly one resolver invocation receiving the terminal-dot `dns_absolute_resolver_name`, with adapters that remove the terminal dot, pass a relative form, perform hidden suffix expansion, append primary or connection-specific suffixes, apply devolution, iterate a search list, try fallback or alternate names, or use an API unable to preserve the absolute argument rejected as nonconforming;
- synthetic host DNS suffix lists, primary suffixes, connection-specific suffixes, and devolution settings having no effect on the exact resolver argument, with different synthetic host suffix inputs producing identical invocation behavior;
- exactly one DNS transport attempt, token, DNS method charge, logical outcome, and aggregate DNS snapshot per logical round; unchanged timeout/no-address/resolution-failure reduction, scheduling, attempt/network-budget examples, HTTPS correlation, A/AAAA result handling, and no retry or additional name after failure;
- unit-level document drift failing if UTF-16 code-unit preprocessing, ill-formed-surrogate rejection, field-owned terminal-dot handling, the TCP literal-first classification/no-fallthrough rules, strict literal grammars and direct bytes, the sole exact-loopback/`fc00::/7`/`2000::/3` direct-IPv6 union, absolute DNS/TCP resolver arguments, closed pre-`Uri` HTTPS parser/canonical non-root transport, DNS/SNI/certificate/generated-`Host` equivalence, or strict PSL bytes/UTF-8/lines/comments/sections/syntax/eligibility/duplicates/matching changes; and if any second parser, address-classification rule, name attempt, transport attempt, code, report field, flag, cap, data file, or dependency appears;
- exact TCP classification order IPv6 candidate, IPv4 candidate, then hostname; literal-branch failure never falling through; `192.0.2.1` and every accepted direct IPv6 literal always using exact decoded bytes without resolver invocation; accepted direct vectors `::1`, `fc00::`, `fc00::1`, `fd00::1`, `fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff`, `2000::`, `2000::1`, `2001:db8::1`, `3fff::1`, and `3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff`; and rejected direct vectors `::`, `::2`, `1::`, `100::1`, `64:ff9b::1`, `64:ff9b:1::1`, `1fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff`, `4000::`, `fbff:ffff:ffff:ffff:ffff:ffff:ffff:ffff`, `fe00::`, `fe80::1`, `ff02::1`, `::ffff`, `::ffff:ffff`, `::ffff:0:1`, and `::ffff:192.0.2.1` with exact expected startup results;
- byte-level direct-IPv6 tests proving exact loopback bytes; `(b[0] & 0xFE) == 0xFC` with first-byte boundaries `0xFB` reject, `0xFC` accept, `0xFD` accept, and `0xFE` reject; `(b[0] & 0xE0) == 0x20` with first-byte boundaries `0x1F` reject, `0x20` accept, `0x3F` accept, and `0x40` reject; and second/later bytes at both `0x00` and `0xFF` proving that only the stated first-byte bits determine range membership;
- byte-identical direct-IPv6 acceptance under different platform address helpers, IANA-registry or mutable-prefix inputs, network-interface assignments, routing/source-selection state, actual reachability, DNS state, culture, and globalization environments, with interception proving none is consulted and `2001:db8::1` remains accepted while `1::`, `100::1`, and `64:ff9b::1` remain rejected;
- explicit unit vectors `::2`, `::ffff`, and `::ffff:ffff`, each proving strict text parsing and exact 16-byte decoding succeed; exact loopback, `fc00::/7`, and `2000::/3` all evaluate false; startup rejects the atomically configured set with one set-like `configuration_invalid`; hostname validation and the resolver adapter are not called; and no platform mapped/compatible classifier or conversion is consulted;
- TCP strict IPv4 requiring four decimal components, three dots, 1–3 digits, no multi-digit leading zero, and range 0–255; strict IPv6 hextet/compression expansion into 16 big-endian bytes before the sole three-predicate union; every rejected direct literal creating no resolver/transport work and never falling through; every accepted direct literal bypassing resolution while retaining one connect/attempt/token/fixed charge; hostname `tcp_logical_host_name` producing the sole `tcp_absolute_resolver_name` argument; and equivalent dotted/undotted hostnames having identical identity, candidate set, schedule, budget, and behavior;
- byte-level resolver normalization covering exact four-/16-byte and family requirements; signed 64-bit IPv6 source scopes at `-1`, `0`, `1`, `4294967295`, and `4294967296`; checked unsigned 32-bit normalization; exact mapped `00` x 10 + `FF FF` predicate and post-map IPv4 exclusions; exact IPv4 all-zero, `224.0.0.0/4`, and all-`FF` predicates; exact IPv6 all-zero, leading-`FF`, and `fe80::/10` predicates; the complete normative vector table; and malformed family/length/scope/interface records producing the required invariant failure without truncation, reinterpretation, guessing, or selection;
- deterministic IPv4-first hostname selection from the complete immutable resolver snapshot; one complete synthetic interface snapshot per result containing link-local items; exact nonzero/matching/IPv6-enabled/`IfOperStatusUp` usability; down, disabled, missing, zero, duplicate, snapshot-failure, and `fe7f`/`fe80`/`febf`/`fec0` boundary cases; mapped/native IPv4 deduplication; same-byte/different-scope IPv6 retention and unsigned numeric scope ordering; the direct-literal union not filtering resolver results, including resolver-returned `1::`, `100::1`, and `64:ff9b::1` remaining eligible under the closed resolver rules; selected IPv4 or IPv6 failure with no alternate/family fallback; resolver failure and an empty eligible set; hidden hostname-level iteration/Happy-Eyeballs APIs prohibited; one token/attempt/fixed charge/two-second pipeline; identical candidate and interface sets selecting identically across enumeration orders; no resolver/interface/address or direct-predicate detail in output; and document drift failing if selection or fallback becomes unspecified;
- table-driven TCP snapshot outcomes proving ordinary acquisition failure is exactly `failed`/counted/`tcp_connect_failed`, snapshot timeout is exactly `timed_out`/counted/`tcp_connect_timeout`, malformed or contradictory resolver/interface data is immediate `incomplete`/non-counting/`round_incomplete`, and a valid complete no-candidate snapshot is exactly `failed`/counted/`tcp_connect_failed`; start/end cancellation before start has no attempt, start/end pause after start preserves ordinary reduction, shutdown before start has no attempt, and shutdown after start is controlled-cancellation incomplete plus bounded `canceled`/`cancelled` or attempt-level `incomplete`; every started case has one TCP invocation/token/1,024-byte charge/outcome, every admitted case has one logical and due-ledger closure, and each has the exact state, cadence, workload, and violation effects;
- injected TCP snapshot ties covering transport start and barrier pause at the same timestamp in both serialized orders; API failure and barrier pause at the same timestamp; timeout and end barrier at the same timestamp; malformed-data detection after start-barrier pause; deadline equal to barrier pause; late callback after deadline closure; late callback after shutdown cancellation; prior terminal versus shutdown; and opposite callback/task/exception/row/logging orders producing identical outcomes with no escaped exception, missing/duplicate attempt outcome, missing/duplicate due-ledger closure, retry, partial snapshot, or hidden transport;
- missing or invalid `operator_id` and rejection when any provider pair shares a provider ID, declared operator, normalized endpoint host, or normalized registrable domain;
- valid two-, three-, and four-provider configurations whose provider IDs, declared operators, normalized hosts, and normalized registrable domains are all pairwise distinct, including the built-in Google/Cloudflare declarations;
- registrable-domain cases for `example.com`, a multi-label suffix such as `example.co.uk`, eligible `com`, `co.uk`, `*.ck`, and `!www.ck`, both ICANN and PRIVATE sections, a host equal to a public suffix, IP-literal rejection, two subdomains sharing one registrable domain, implicit-star result, and prevailing exact/wildcard/exception cases;
- exact pinned byte/hash verification; strict valid and invalid UTF-8; BOM; LF, CRLF, final line without LF, lone CR, blank line, column-zero comment, leading-space pseudo-comment, inline comment, surrounding whitespace, exact section count/order, rule outside section, malformed wildcard/exception, empty/consecutive/terminal-dot labels, Unicode and `xn--` ineligibility, exact and ASCII-case duplicates, related different-kind rules, and built-in Google/Cloudflare domains; malformed source producing only `source_identity_invalid` before configuration acceptance with no partial/default/implicit-star-only continuation;
- exact/wildcard/exception matching only against eligible normalized ASCII rules; a single-label PSL rule never inheriting configurable-host two-label rejection; no configurable preprocessing or terminal-dot removal for rules; no second PSL file, platform-default or replacement decoding, line trimming, collection-order duplicate behavior, `IdnMapping`, globalization/IDN API, or runtime download; and deterministic registrable-domain behavior across restarts;
- byte-identical validation and normalization under ICU and NLS modes, different ICU versions where infrastructure permits, `DOTNET_SYSTEM_GLOBALIZATION_USENLS=0` and `=1`, `DOTNET_SYSTEM_GLOBALIZATION_APPLOCALICU`, globalization-invariant mode, changed current culture/UI culture/system language, and changed Unicode-normalization data, with no app-local ICU dependency and no hidden `IdnMapping`, IDN, Unicode-normalization, `Uri.IdnHost`, or `Uri.DnsSafeHost` path;
- provider-set count, pairwise provider/domain/host/declared-operator separation, configuration-order independence, atomic rejection with no partial scheduler activation, and a drift test proving every requirements statement uses `distinct_registrable_domain_count = N` for all 2–4 providers;
- closed HTTPS URL vectors accepting uppercase scheme/host, host with or without one root dot, absent port/exact `:443`, empty path becoming `/`, both built-in paths, and every permitted path character; rejecting non-HTTPS or missing scheme, one/three scheme slashes, user information, multiple `@`, `%` or percent-encoded authority characters, raw Unicode/`xn--`/IPv4/bracketed-IPv6 host, empty authority/port, `:0443`, non-443 port, multiple colons, query, fragment, backslash or mixed boundary, path percent escape, and `.`/`..` path segment before any `Uri` creation;
- dotted/undotted and absent-port/`:443` equivalents producing byte-identical `canonical_https_url` plus identical `https_transport_host` for DNS, SNI, certificate validation, generated `Host`, provider workload identity, registrable domain, schedule, and evidence; no original dot or port restoration, caller `Host` override, original-URL `Uri` parsing, percent decoding, IDN conversion, or path normalization;
- HTTPS-only canonical transport; every built-in and custom provider producing uppercase GET with no request body; no method override; HEAD rejected for both status-only and expected-body providers; and POST/PUT/PATCH/DELETE/OPTIONS/CONNECT/TRACE/extension methods rejected before scheduler activation;
- validation acceptance for otherwise valid expected statuses 200, 204, 206, and 299; atomic rejection for 100, 199, 300, 302, 304, 307, 308, 400, 500, and 599; configuration-order independence; and no partial provider subset activation;
- expected-body rejection with status 204 or 205, acceptance of 204 or 205 without a body, optional bounded exact UTF-8 body comparison over GET for other accepted 2xx statuses, and unchanged Google-204/Cloudflare-200 defaults;
- HTTPS endpoint outcomes for GET 204, GET 200 status-only without body inspection, GET expected-body success, wrong status, every actual 3xx as terminal failure, redirects disabled, TLS failure, timeout, cancellation, oversized or unreadable body, body not retained, partial provider failure, all-provider failure, and two-provider independence;
- method binding leaving tokens, fixed 8,192-byte charge, measured bytes, attempts, concurrency, deadline, phase membership, workload coverage, aggregate reduction, and report shape unchanged, with document-drift tests failing if any provider path omits GET, permits another method or request content, or adds an HTTP method field to evidence;
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
- evidence-code allowlisting and redaction;
- exact spelling and ordinal position of `process_creation_failed` after `probe_persistence_detected` and before `process_exited`, exactly 39 closed violation codes, and no other added code;
- process API failure before PID producing `process_creation_failed` exactly once with no false `process_exited`, `process_profile_invalid`, or `source_identity_invalid`; partial creation with a real PID using the existing PID/profile/exit rules; exact retained values/null matrix; nonzero exit; independent cleanup failures; and no process-creation privacy detail;
- exact spelling and ordinal position of `probe_persistence_detected`, below-cap and cap-reaching probe artifacts, attempted persistence with zero bytes, sticky behavior after deletion/rollback, exact multi-violation order, and no added report property;
- no persistence false positive for approved R0-004 lifecycle/journal writes, acceptance report publication, harness-owned source/validation artifacts, or unrelated runtime growth, with document drift failing when persistence is prohibited but lacks the code, the code count remains 37, the failure/scenario matrices omit the code, deletion forgives it, disk-cap violation substitutes for it, or the report exposes an artifact name/path/category.
- exact `source` top-level placement after `schema_version`, exact `commit_sha`, `configuration_name`, `executable_sha256` property order, and rejection of a missing, additional, reordered, duplicated, or wrong-typed source property;
- exact 40-character lowercase commit-SHA validation and rejection of abbreviated, uppercase, all-zero, whitespace-padded, prefixed, suffixed, symbolic-ref, branch, malformed, and reviewed-head-mismatched values, with proof that the value comes from coordinator-verified exact reviewed-tree provenance and is only cross-checked against embedded metadata, never self-proved by the artifact, developer checkout, environment, or symbolic ref;
- exact `r0-005-25-target-acceptance/v1` configuration identifier and rejection of arbitrary labels, filenames, paths, machine/user/property/tenant/customer/provider/target identifiers, environment-derived values, empty values, and changed constants;
- exact 64-character lowercase complete-single-artifact digest validation and rejection of uppercase, malformed, partial, all-zero, adjacent-file, stale-file, substituted-file, and mismatched digests, including hashing all bytes through the retained bound handle; mutation before binding; mutation after hashing but before process creation; and delete, rename, or replacement attempts;
- effective acceptance-publish validation for `win-x64`, self-contained, single-file, native-library inclusion, untrimmed, symbol-free output; a valid one-file artifact; and rejection of framework-dependent apphost, `SelfContained=false`, multi-file, separate main/referenced managed assembly, application-private native DLL, external `.deps.json`/`.runtimeconfig.json`, second executable, plug-in, probing path, mutable shared runtime, and any closure not proved complete;
- exact reviewed detached-tree materialization and coordinator build; dirty developer worktree isolation; modified/staged/untracked/ignored/generated source rejection; preexisting `bin`/`obj`/`publish`/artifact reuse and supplied prebuilt artifact rejection; different or dirty source embedding the expected SHA rejection; exact artifact-byte mutation changing the digest; stale/adjacent/copied/similarly named artifact rejection; embedded metadata mismatch; exact reviewed-SHA/materialized-tree/artifact-metadata three-way match; launched-image mismatch; fresh controlled native extraction; preexisting/substituted extraction rejection; unexpected external application-private module loading; and Windows system-module exclusion;
- source-root immutability at all four checks, external fresh intermediate/output roots, parent/user props/targets rejection, external project/import/compile/analyzer/generator/native input rejection, source/reparse escape rejection, package graph and content-hash capture, publish without implicit graph change, and independent exact-tree-provenance/artifact-digest null and retention behavior;
- independent source-property capture with only unavailable properties null, all nine availability flags false and no configuration/process/scheduler start after source-capture failure, `source_identity_invalid` for every source defect, and `report_serialization_invalid` for every independently applicable structural, type, format, ordering, contradiction, pass-with-null, or human/JSON disagreement defect;
- all three source values remaining non-null and unchanged through configuration rejection, host-profile failure, pre-PID process-creation failure, readiness timeout, start-barrier failure, early steady-state failure, end-drain failure, final-snapshot failure, and success, with passing reports requiring exact reviewed-head, fixed-profile, complete single-artifact binding, launched-image, extraction-integrity, and runtime-closure matches;
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

- local TCP accept, refuse, timeout, cancellation, and socket disposal for IPv4/IPv6 literals and hostname results, including mixed-family resolver permutations, every normative mapped/excluded/link-local/scope vector, complete controlled interface snapshots, mapped/native IPv4 and same-byte/different-scope IPv6 deduplication behavior, canonical filtering and unsigned-scope sorting, one selected address with its exact normalized scope, selected-address failure with another candidate available, no hidden iteration or fallback, and one token/attempt/charge/timeout pipeline;
- local HTTPS GET status/body/redirect/streaming behavior with test certificates as appropriate, proving Google-shaped 204 and Cloudflare-shaped 200 status-only behavior, no request body, and no Cloudflare-style or custom status-only response-body read, retention, or exposure;
- local HTTPS GET expected-body success and a controlled endpoint where HEAD would differ but is never invoked; interception proving POST/PUT/PATCH/DELETE/OPTIONS/CONNECT/TRACE/extension methods are never sent; and exact per-provider attempt/token/charge accounting unchanged;
- local HTTPS validation/execution for the complete accepted/rejected status set, 204/205 body contradictions, actual 3xx never succeeding, redirects remaining disabled, provider-order independence, and preserved default-provider validity;
- two-, three-, and four-provider local HTTPS fan-out with concurrent all-timeout completion, partial success, global/per-provider limits, non-overlapping aggregate rounds, contention incompleteness, and sustained mixed-work fairness;
- injected ordinary-success and counted-failure HTTPS rounds each followed by their calculated next round, plus deadline-expired incomplete followed by one later admitted round without requiring terminal aggregation;
- injected start-barrier-canceled HTTPS rounds with no child and with partial children, each retaining its original-deadline-derived schedule after successful resume; end-barrier cancellation creating no post-end admission; and no immediate replacement or catch-up in any case;
- started HTTPS child accounting closing after logical cancellation without blocking future cadence, with no overlap, identity reuse, prior-child attribution, closure rewrite, duplicate closure, or duplicate admission across rounds;
- two-, three-, and four-provider startup acceptance with pairwise-distinct IDs/operators/ordinary ASCII hosts/domains, preserved built-in Google/Cloudflare validity, and atomic no-scheduler rejection for every pairwise duplicate, every Unicode or `xn--` host, and every configuration-order-permuted duplicate;
- closed custom-HTTPS character parsing before `Uri`: exact UTF-16 preprocessing once, scheme/authority boundary, user-information and delimiter rejection, zero-or-one root-dot host wrapper, exact absent/`:443` port equivalence, closed path characters/segments, canonical non-root construction, and post-construction ordinal URI correspondence; parser interception proving the original URL is never passed to `Uri` and no Unicode/ACE conversion, percent-decoding, alternate authority, path normalization, `IdnMapping`, `Uri.IdnHost`, or `Uri.DnsSafeHost` path exists;
- controlled system-resolver adapter success, timeout, no-address, and resolution failure through an abstraction that receives the exact terminal-dot absolute name once;
- default `example.com`; every normative valid and invalid DNS/whitespace/format vector; invalid 0- and 64-octet labels, valid conforming 1-, 62-, and 63-octet labels, valid conforming 252- and 253-octet complete names, and an invalid 254-octet complete name; exact trim set; ASCII LDH/hyphen/underscore rules; direct-Unicode and every `xn--` rejection; exact ASCII plus-`0x20` case normalization; optional-one-dot behavior; no globalization/IDN path; and differing synthetic host suffix/search/devolution settings, all producing the same expected `dns_logical_query_name` and exact `dns_absolute_resolver_name` where equivalent;
- controlled one-name DNS operation with exactly one resolver invocation, one transport attempt, one token, one method charge, one outcome, and one snapshot result; unchanged scheduling, timeout, reducer, charged-byte, budget, and HTTPS-correlation behavior; and no relative-form pass-through, terminal-dot removal, suffix expansion/appending, devolution, search-list iteration, hidden API attempt, fallback, retry, or alternate name;
- startup rejection for every malformed UTF-16, DNS, TCP classification/literal/hostname, HTTPS URL/authority/port/path/canonical-host, or registrable-domain customer input, no scheduler, valid subset, resolver work, or transport work, exactly one `configuration_invalid` for combined defects, no rejected input/code-unit/label/address-byte/direct-predicate/prefix/registry/platform/replacement/exception/index/Unicode/ACE/parser/environment detail entering output, and nonconforming DNS/TCP adapters or APIs that cannot preserve the exact absolute resolver arguments rejected before use;
- integration interception proving all normative direct-IPv6 accepted/rejected vectors and both exact masks/boundaries produce their specified startup results; accepted direct literals use the exact 16 bytes and bypass resolution with one connect/attempt/token/fixed charge; rejected direct literals never fall through and create no resolver/transport work; `2001:db8::1` remains accepted without a reachability claim; and platform/IANA/interface/routing/globalization variation cannot affect validation;
- an atomically configured startup scenario for each of `::2`, `::ffff`, and `::ffff:ffff`, proving one bounded `configuration_invalid`, no scheduler activation or partial target activation, no resolver call or socket connect, no token consumption or TCP attempt record, and no fallback or retry;
- integration interception proving hostname resolution receives exactly `tcp_absolute_resolver_name`; raw `IPAddress` family, exact bytes, and signed `ScopeId` cross the adapter once; interface metadata comes from one complete controlled `Ipv6IfIndex`/`Ipv6Enabled`/`OperStatus` snapshot only when needed; ordinary snapshot failure yields exactly one completed `failed` counted attempt with `tcp_connect_failed`, while malformed resolver/interface data yields exactly one non-counting `incomplete` attempt with `round_incomplete` and the required acceptance failure; the direct-literal range union is never applied to resolver-returned candidates including `1::`, `100::1`, and `64:ff9b::1`; dotted/undotted hostname equivalents yield identical immutable candidates and one-connect behavior; post-snapshot host changes cannot alter selection; no extra token/attempt/charge or refresh occurs; plus HTTPS interception proving canonical dotted/undotted and absent-port/`:443` inputs yield byte-identical request URI and identical DNS, SNI, certificate, generated `Host`, and workload-host inputs;
- integration interception for bounded snapshot API failure, unfinished capture through the two-second timeout, start/end pause before and after `transport_start`, shutdown before and after `transport_start`, duplicate `Ipv6IfIndex`, scope `-1` and `4294967296`, wrong resolver family/byte count, malformed interface rows, and valid complete no-candidate snapshots, asserting the exact attempt/round/evidence/state/closure/cadence/phase/workload/acceptance matrix; started TCP work is never start/end-barrier-canceled, shutdown retains active-cancellation authority, all injected same-time and opposite callback/task-delivery permutations reduce identically, every exception/status is bounded at the adapter, and no path connects, retries, refreshes, falls back, or leaves attempt/due-ledger accounting open except the existing bounded post-deadline or post-shutdown attempt-accounting cases;
- pinned PSL source-preflight integration covering exact hash, strict UTF-8/BOM/line/section/rule/duplicate handling, eligible/ineligible rules, official matching, and `source_identity_invalid` with no configuration/host/process/scheduler start or partial/default rule set on malformed source;
- controlled DNS publication and HTTPS capture at equal injected-monotonic timestamps across both enqueue orders, reversed completion orders, every DNS classification, absent prior snapshot, adjacent ticks, and late callback delivery, proving the section 4.0 coordinator makes capture byte-identical and non-retroactive;
- scheduler activation builds the exact immutable logical-target ring and initial cursor across DNS enabled/disabled, zero through 25 TCP targets, configuration permutations, case variants, and permitted non-ASCII IDs; capacity-blocked offers advance the cursor; provider-child ordering remains independent;
- scheduler activation after readiness creating one full process-local bucket plus an empty rolling admission ledger, exact monotonic lazy refill/rolling expiry during local fake/endpoint work, and prompt stop plus bucket/ledger discard on readiness degradation;
- an injected dual-clock abstraction where monotonic time advances while UTC jumps forward, jumps backward, or remains constant, proving flapping transition ordering, ten-minute eviction, threshold evaluation, and exact 20-minute clearing depend only on monotonic input; restart clears history and no flapping timestamp is reported or persisted;
- restart rebinds static configuration, reconstructs enabled probes as `unknown`, moves them to `baselining` only through scheduler activation after readiness, and creates a new full bucket/anchor and empty rolling ledger without prior credit or entry;
- malformed restart-time configuration producing no scheduler or partial activation, reusing no prior process-memory or stale provider/target set, remaining visibly unavailable/degraded with value-free bounded evidence, and corrected configuration on a later healthy restart activating normally;
- attempt-only, charged-network-only, dual-budget, and budget-plus-other-defect startup rejection before scheduler construction, each producing the identical single bounded `configuration_invalid`, no probe work, token bucket, rolling ledger, due-ledger, accepted manifest, or partial target/provider subset, and no private predicate, calculated demand, configuration value, or retired budget-specific name entering logs, health, metrics, persistence, or support output; a later corrected configuration activates only after a healthy process restart under the startup-only model;
- an acceptance coordinator subscribed before readiness, continuous event/sample reconciliation, permanent failure after transient readiness loss, same-epoch ownership through both barriers/final stabilization, and no same-run scheduler restart after continuity failure;
- deterministic due-round reconciliation through success, failure, timeout, controlled cancellation, late wake, fair/slot/token/rolling-budget delay, exact-deadline equality, admission pauses, shutdown, and injected silent-round loss, with one entry already present for every due round, exactly one closure and closure-derived anchor after all three closure classes, no missing-entry repair path, missing entry adding `acceptance_scheduler_inactive`, and the same immutable due/deadline fields and token-bucket/rolling-ledger continuity across acceptance barriers;
- atomic start/end admission pause with one private `barrier_pause_timestamp`, immutable logical-target-key cancellation order, no token/slot/transport-attempt-guard/charge for unstarted work, started Gateway/DNS/TCP drain to ordinary terminal/deadline closure without active cancellation or `canceled`/`cancelled`, complete separate accounting for partially started HTTPS children, and no started completion rewriting an already deadline-, partial-HTTPS-, or shutdown-canceled logical result;
- atomic scheduler shutdown with one private `shutdown_pause_timestamp`, terminal and ordinary-deadline precedence, immutable logical-target-key cancellation order for every remaining admitted round, queued and partially/fully started cases including active TCP snapshot cancellation, bounded `canceled` versus attempt-level `incomplete`, exact due-ledger reconciliation before epoch-state discard, no replacement schedule, and five-second disposal;
- an admitted HTTPS round retaining only its logical-round ownership guard while children wait, zero transport-attempt guards until child start, no concurrency/token/charge reservation from ownership, second-round rejection, and independent logical/attempt guard closure through partial fan-out and shutdown;
- start-barrier resume retaining the original-deadline-anchored next due without catch-up or immediate replacement, end-barrier reconciliation creating no post-end round, and both barriers completing under their existing outer limits without extending stable-snapshot allowances;
- an injected Windows route adapter exercising stable A/B current-compartment snapshots; same-pass, family-specific `Connected`/`Metric`/`DisableDefaultRoutes` lookup; false/true policy filtering and both toggle directions; exact candidate ordering/deduplication; enabled-higher-metric selection over a disabled lower-metric route; stable-all-disabled `missing_route`; API/incomplete-row `route_selection_unavailable`; workload exclusion; and no route/interface or VPN-policy value leakage;
- coordinator-controlled build from a freshly materialized exact reviewed tree, independent commit/root-tree verification, frozen build-input provenance, and three-way supplied-SHA/materialized-tree/embedded-metadata agreement, with no prebuilt artifact, developer-worktree, current-checkout, environment, branch, shortened-SHA, overlay, external import/source, or metadata-only fallback;
- validating the exact effective acceptance publish shape, digesting the complete one-file private closure through a retained bound handle, launching that same bound artifact, proving process-image identity, and preventing or deterministically detecting write/delete/rename replacement, adjacent-file selection, copying, mutation, or substitution between digest capture and process creation;
- framework-dependent, `SelfContained=false`, multi-file, separate managed/native dependency, external metadata, plug-in/probing, and mutable shared-runtime rejection before process creation; fresh controlled bundle-native extraction; substituted/preexisting extraction rejection; and continuous detection of application-private executable components loaded from outside the authenticated bundle;
- source-capture failure combinations proving independent nulls, byte-identical JSON for identical failed inputs, identical human `unavailable` rendering, all nine measurement-availability flags false, and no configuration acceptance, host preflight, process creation, or scheduler activation;
- static host completion before launch with process fields null; initial exact-PID process-profile capture before readiness acceptance/scheduler activation; exact process runtime/GC/priority/debugger/profiler validation; permanent dynamic failure on transition or loss of proof through committed cleanup-termination intent; and retained initial values in JSON and human evidence;
- process-profile prevention or complete observation remaining active through pre-termination cleanup and arbitration after initial capture success; no new scheduler or network work during cleanup; conditional final validation plus one retained-handle liveness check, intent commit, and stop issuance in one serialized operation;
- pre-subscribed exact-PID exit observation, retained handle/PID identity, initial-profile-failure stop path, delayed callback, every arbiter tie-break outcome, stop issuance after intent, expected post-intent exit producing no `process_exited`, pre-intent exit producing it, and replacement/restart remaining prohibited;
- fresh-destination selection, same-volume JSON/human staging from one model, complete validation, one atomic directory publication, corrected failure-set retry at most once, publication/staging-cleanup failure, exact command exit codes, no stale or partial authoritative set, and JSON/human consistency;
- fixed-shape success and failure reports retaining the identical ordered ten-property `caps` object, including configuration rejection, host-profile failure, `process_creation_failed`, readiness timeout, start-barrier failure, end-drain failure, and final-snapshot failure, while every unavailable associated measurement remains `null` rather than copying a cap value;
- no probe journal/outbox calls and no database/schema artifacts;
- controlled private persistence-boundary injections for filesystem, SQLite/journal/outbox, and applicable durable cache/registry abstractions, covering denied writes, successful small and cap-reaching writes, later deletion, fixed non-identifying ownership attribution, exact `probe_persistence_detected` behavior, and no path/content leakage or false positive for approved harness/R0-004 writes;
- no response body, DNS answer, endpoint, target, interface, or exception leakage.

Live Google, Cloudflare, Microsoft, DNS, SMS, camera, or customer-network access is not required or permitted as an automated test dependency.

### 9.3 Security and privacy tests

Verify:

- URI user information, credentials, credential-assignment syntax, query strings, fragments, and customer/device identifiers are rejected;
- HTTPS redirects and invalid TLS are rejected without fallback or weakened validation;
- the R0-005 startup-only configuration exception remains atomic, fail-closed, nonpersistent, and value-redacted, while no failed cloud-delivered or durable/updateable configuration replaces a safely stored accepted last-known-good value;
- source fields contain only the independently exact-tree-proven full commit SHA, fixed acceptance-profile identifier, and exact complete single-artifact digest, with no machine/user/domain/property/tenant/customer/target/provider value, source/worktree/Git-object/build/package/intermediate/project/source/artifact path or filename, tree/blob/analyzer/generator identity, executable/extraction filename, assembly/module identity, checkout, branch, arbitrary environment value, or exception text in JSON or human evidence;
- process evidence exposes no executable path, working directory, command line, operating-system status/error code, exception text, executable/module path, debugger identity, profiler identity/CLSID, diagnostic endpoint, process token, security descriptor, environment content, event-source payload, or account/user identity, including the pre-PID `process_creation_failed` scenario; JSON/human reports agree on initial values, nulls, validation, violations, and retention;
- publication paths, staging paths, filenames, directory identities, handles, filesystem/operating-system errors, exception text, host/account identity, and private destination detail appear in neither report nor bounded console/status output, including destination-collision, rename, and staging-cleanup failures;
- endpoint URLs/hosts, HTTP method strings or method-specific details, request/response bodies, headers, DNS queries/answers, private DNS/TCP/HTTPS logical, absolute, canonical, SNI, certificate, `Host`, literal bytes, matched or failed direct-literal predicate, prefix, registry or platform classification, PSL-line/rule/duplicate inputs, original terminal-dot/explicit-port representation, host suffix/search/devolution configuration, DNS-correlation snapshot fields, target IDs/labels/hosts/ports, interface/route data, IP addresses, exception text, machine paths, environment/globalization settings, ICU/NLS mode or version, and other customer-controlled values do not appear in JSON or human reports, production logs, health output, support evidence, metrics, persistence, or other evidence;
- original or preprocessed rejected input, rejected UTF-16 code units or labels, address bytes, direct-literal predicate or classification details, replacement values, exceptions, indices, Unicode values, ACE labels, parser or URI-normalization details, environment/globalization settings, and field values remain absent from every output while invalid customer DNS, TCP, HTTPS, or registrable-domain input plus any other startup defect still produces only one value-free `configuration_invalid` and no scheduler, resolver, or transport work; malformed pinned PSL source instead produces only the existing value-free `source_identity_invalid` preflight result and starts no later stage;
- every normative accepted and rejected direct-IPv6 vector, exact-loopback byte check, `0xFB`/`0xFC`/`0xFD`/`0xFE` `fc00::/7` boundaries, and `0x1F`/`0x20`/`0x3F`/`0x40` `2000::/3` boundaries producing the same exact startup result across platform, registry, interface, routing, network, and globalization variation; accepted values expose no address bytes or predicate and rejected values expose only one set-like `configuration_invalid`, including when combined with another defect;
- rejection of each exact input `::2`, `::ffff`, and `::ffff:ffff` exposes no input literal, decoded bytes, compatible or mapped classification, predicate result, prefix, parser or platform exception, resolver detail, or transport detail; combined with another configuration defect, each still contributes only one set-like `configuration_invalid`;
- static analysis and controlled interception prove there is no scalar/`Rune`/replacement preprocessing, parameterless `String.Trim()`, `Char.IsWhiteSpace`, `IdnMapping`, IDN, Unicode-normalization, `Uri.IdnHost`, `Uri.DnsSafeHost`, app-local ICU, second hostname/TCP/URL/PSL parser, platform-permissive literal parser, `IPAddress`/`IsIPv6LinkLocal`/`IsIPv6Multicast` classification path, operating-system classification or routing decision, IANA-registry or mutable-prefix source, reachability/DNS/interface/source-selection input, original-URL `Uri` path, default/replacement PSL decoder, or URL parser that can silently broaden the accepted grammar or alter direct-IPv6 validity;
- every HTTPS provider request is GET with no caller-supplied content, upload, authentication, cookie, or unauthorized entity-body header, and status-only responses are disposed without content-stream reads;
- workload manifests, scheduler keys or epochs, rolling-ledger entries or timestamps, readiness-transition timestamp details, internal member identities, per-provider identities, and per-target identities do not appear in JSON or human acceptance reports, logs, health, support output, or metrics;
- coordinator total-order keys/sequences/phases, transport-start/pause ordering, callback or task-delivery order, logical-target keys/comparison results, due-ledger round identities, member identities, closure discriminators/reasons, `round_due`, `round_deadline`, reduction commit times, private cadence timestamps/anchors, `barrier_pause_timestamp`, `shutdown_pause_timestamp`, TCP resolver answers, selected address/family, candidate count, raw or normalized scope identifier, mapped/exclusion/link-local predicate, interface-snapshot presence/rows/index/IPv6-enabled/operational status, sort tuple, hostname, and port do not appear in JSON/human reports, logs, metrics, health, persistence, or support output;
- ordinary, timed-out, shutdown-canceled, malformed, and barrier-drained TCP snapshot scenarios expose only their bounded evidence code, ordinary state names, aggregate attempt counts, bounded duration, existing fixed violations, and existing report fields; barrier/shutdown cancellation reason, snapshot stage, callback order, API return/OS error code, exception, raw or normalized scope, interface index/status/IPv6-enabled value, duplicate or malformed row, candidate bytes/address, selected or rejected predicate, adapter identity, scheduler sequence, transport-start/pause ordering, private cadence timestamp, and failure stage remain absent from every output;
- persistence reporting consists only of `probe_persistence_detected`; the private ownership category, artifact path/name, table/registry key, payload, persisted bytes, target/provider/endpoint/address/customer identity, and exception text are absent from all output;
- response-body readers stop at the 256-byte bound plus one overflow-detection byte and retain no bytes;
- no production inbound listener, firewall mutation, packet capture, subnet/range expansion, broad port scan, or arbitrary gateway TCP probe exists;
- no probe journal/outbox call, schema artifact, identity fabrication, or raw LAN inventory is introduced;
- approved R0-004 lifecycle/journal behavior and acceptance report publication do not create persistence false positives, while a probe-derived durable-write attempt fails even before bytes are written.
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
- controlled configuration and adapter interception covers every normative UTF-16 code-unit, DNS, strict TCP literal/classification/hostname, and closed HTTPS URL/authority/port/path/canonical-transport vector; direct IPv6 covers exact loopback bytes, the exact `fc00::/7` and `2000::/3` masks, all four first-byte boundaries for each mask, later-byte minima/maxima, and every accepted/rejected vector; invalid configuration creates no scheduler, resolver, or transport work and one combined `configuration_invalid`; equivalent DNS/TCP hostname forms invoke exactly one unchanged terminal-dot absolute resolver name; `192.0.2.1` and accepted direct IPv6 literals bypass the resolver while rejected literals never fall through; resolver-returned candidate eligibility remains unchanged and does not apply the direct union; equivalent HTTPS dotted/undotted and absent-port/`:443` forms produce byte-identical canonical URL plus identical DNS/SNI/certificate/generated-`Host` input; platform/IANA/interface/routing/globalization variation cannot alter direct validation; and no rejected code unit/label/address-byte/predicate/prefix/classification/replacement/exception/index/value or private parser/transport detail enters acceptance output;
- native Windows configuration-boundary validation for `::2`, `::ffff`, and `::ffff:ffff`, proving runtime or `IPAddress` parsing cannot broaden acceptance; none is converted into an accepted mapped, compatible, loopback, or hostname form; resolver and socket transport are never invoked; and the configuration-acceptance result is identical across every supported Windows/runtime environment;
- all four initial logical-round types receiving activation-anchored due/deadline values, subsequent rounds using the closure-specific scheduling anchor plus jitter, late dispatcher observation preserving those values, and injected exact-boundary starts/timeouts/completions following the universal deterministic order;
- equal-timestamp DNS publication and HTTPS capture across opposite callback/enqueue/completion orders, every DNS classification, absent/stale/adjacent snapshots, and late callback delivery, proving publication phase always precedes capture and cannot retroactively change a round;
- the complete logical-target key/ring/cursor algorithm across the initial full due wave, enabled/disabled DNS, zero through 25 TCP targets, configuration permutations, case/non-ASCII IDs, capacity-blocked offers, same-timestamp turns, barrier/shutdown ordering, and independent provider-child ordering;
- controlled HTTPS scenarios proving scheduling resumes after deadline-expired incomplete and after start-barrier-canceled incomplete, partially started-child accounting does not suppress the next calculated round, no immediate replacement or catch-up occurs, and no post-end round starts, all without modifying the host clock;
- controlled native-Windows TCP hostname resolution proves exactly one `tcp_absolute_resolver_name` invocation with terminal dot preserved; `IPAddress.GetAddressBytes()` and signed 64-bit `ScopeId` are normalized by the exact predicates/range; mapped IPv6 collapses with native IPv4; `fe7f::1`, `fe80::1`, `febf:ffff::1`, and `fec0::1` prove the exact `fe80::/10` boundaries; synthetic or controlled IP Helper rows prove zero/missing/down/disabled/up and unsigned-maximum scope behavior; identical resolver/interface sets in opposite orders select identically; one selected-address connect receives the normalized scope; a selected link-local failure never falls back; interface changes after capture do not alter the attempt; no suffix search/fallback/Happy Eyeballs/hostname-level iteration occurs; and no resolver, scope, interface, or address detail enters acceptance output;
- controlled native-Windows snapshot acquisition and adapter injection prove ordinary IP Helper failure, two-second timeout, start/end pause before and after `transport_start`, shutdown before and after `transport_start`, duplicate `Ipv6IfIndex`, invalid signed scopes, wrong family/byte count, malformed rows, and valid no-candidate snapshots produce the exact normative attempt/round/evidence/state/violation/workload/cadence/phase matrix; start/end barriers never cancel a started TCP invocation, shutdown may, exact-deadline and same-timestamp ties are invariant to callback/task/exception/enumeration order, every admitted due entry closes once, and no native return code, exception, interface/scope/candidate value, adapter identity, or failure stage escapes;
- controlled internal interception proves every built-in and custom HTTPS provider child uses GET with no request body, without exposing endpoints, headers, payloads, response bodies, or method details in reports;
- controlled source/startup interception proves exact UTF-16 preprocessing and the shared non-root validator govern each configured path; strict TCP branches and the closed pre-`Uri` HTTPS parser are unique; ordinary ASCII hosts and built-ins remain valid; direct Unicode, surrogates, U+FFFD, and every `xn--` label are rejected; the pinned PSL parser covers strict bytes/UTF-8/BOM/lines/comments/sections/syntax/eligibility/duplicates/matching and fails malformed source with `source_identity_invalid`; and behavior is identical under ICU/NLS/culture/environment variations without reporting those private inputs;
- controlled wall-clock-correction simulation through the injected dual-clock abstraction proves flapping is driven only by monotonic elapsed time; acceptance does not change the host clock, timezone, NTP state, or Windows time-service configuration;
- shutdown reconciliation covers queued Gateway, DNS, HTTPS, and TCP rounds plus partially and fully started HTTPS and TCP, with terminal/deadline precedence, one controlled-cancellation closure for every remaining admitted round, active TCP cancellation only at shutdown, ownership-guard release, exact started-attempt accounting, no token refund/replacement work, and service stop within five seconds;
- the 25-target resource sampler passes only after the Gateway, enabled DNS, both HTTPS providers, and all 25 distinct synthetic TCP members receive terminal steady-state exercise;
- exact internal start/charge evidence proves activation and every later rolling monotonic 60-second window contains at most 60 starts and less than 100,000 fixed charged bytes, with candidate order and exact-age expiry deterministic and JSON/human aggregates derived from the same started-attempt records;
- injected attempt-only, charged-network-only, and dual-budget startup rejection plus exact 60-attempt passing and exact 100,000-byte failing boundaries, each asserting one deterministic `configuration_invalid`, the required all-nine-false report null matrix with retained source and all ten caps, no process or scheduler under the harness, nonzero exit, and no private configuration or calculated-demand leakage;
- the start barrier commits one stable steady-state baseline bundle while admissions remain paused, with `steady_state_start` equal to that commit and no later pre-existing sampling-grid wait;
- the start and final snapshots use the same consecutive-map stable-snapshot protocol, the final snapshot commits within its separate ten-second post-drain window, and admissions remain paused with no transport start after `steady_state_end`;
- injected queued Gateway and DNS rounds at the start barrier, queued TCP at the end barrier, started TCP snapshot work at both barriers, HTTPS with no child started, partially started HTTPS, and HTTPS with every child started, proving identical shared barrier ordering, started Gateway/DNS/TCP drain without barrier cancellation, exact separate attempt accounting, logical ownership-guard release on each logical closure, and no terminal-aggregation requirement after a valid partial-HTTPS incomplete closure;
- injected pause exactly at the round deadline, pause one tick before the deadline, terminal-versus-pause and transport-start-versus-pause same-timestamp ordering in both serialized orders, API-failure/pause and timeout/end-pause ties, malformed data after pause, late callback after deadline or shutdown closure, immediate cancellation only for unstarted work with original-deadline cadence anchoring, retained start-barrier next due, and no extra post-end work;
- steady-state sampling is anchored at exact `steady_state_start + 5n seconds`, and startup/steady-state CPU and disk attribution share the committed boundary without an omitted or double-counted interval;
- the exact PID remains alive and unchanged and CPU, disk, and working-set enforcement continue through stable final-snapshot commit while attempt and network membership stop at exact `steady_state_end`;
- monitoring-ready is achieved within 60 seconds after network availability;
- injected configuration rejection, static-host failure, native process-creation API failure before PID, partial creation with a real PID, initial process-profile failure with the process still live, initial-profile failure followed by pre-intent exit, readiness timeout, start-barrier failure, dynamic process defect in initialization/start barrier/steady state/end drain/final stabilization/pre-termination cleanup, end-drain timeout, and final-snapshot failure each produce the exact flags, independent null/value behavior, source/static/initial-process retention, caps, scheduler presence/absence, violations, human `unavailable`, JSON values, exit code, and privacy behavior required by section 8.8;
- injected process exit during final validation, between validation and liveness check, immediately after a nonsignaled check under the intent-wins tie-break, after intent with delayed callback, and after stop; final exact-PID query unavailability; transient priority/debugger/profiler defect during cleanup; stop-issuance failure; graceful-stop timeout; general acceptance cleanup failure; synthetic-listener cleanup failure; and both cleanup codes together when independently applicable;
- injected destination collision, JSON/human staging write or flush failure, atomic directory-publication failure, corrected failure-set success/failure, staging cleanup failure, and proof that publication failure leaves no current authoritative report;
- an injected probe-derived file below the disk cap fails with `probe_persistence_detected` and no `disk_cap_exceeded`, retains all completed measurements, and leaks no path/name; a cap-reaching probe artifact produces both codes in ordinal order; denied and later-deleted writes remain sticky;
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
- all three closure classes satisfying the prior-round-closed condition, the HTTPS logical-round ownership guard releasing exactly once on closure independently of transport-attempt guards, immediate ledger reconciliation retaining the closure-specific cadence anchor, incomplete closure never permanently suppressing workload coverage, started attempt accounting never retaining the ownership guard, and the report shape remaining unchanged with exactly the authorized 39-code vocabulary;
- start/end cancellation semantics with no started Gateway/DNS/TCP cancellation, full ordinary started-TCP drain and original phase attribution, partial-HTTPS started-attempt accounting after logical cancellation, deadline equality using ordinary processing, shutdown-only active TCP cancellation, start-barrier retained scheduling at calculated due, end-barrier no-post-end scheduling, no immediate replacement or catch-up, and the existing barrier/drain/final-snapshot bounds remaining unchanged;
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
- every stable resource violation code and ordinal violation ordering, including exactly 39 codes with `process_creation_failed` after `probe_persistence_detected` and before `process_exited`;
- the configuration-rejected scenario retaining successful source values and all ten caps, leaving all nine flags false and every later boundary-dependent value null, emitting `configuration_invalid` exactly once with no runtime rolling-budget or retired budget-specific code, producing identical JSON/human classification, and preserving exactly 39 violation codes;
- acceptance configuration vectors exercising ordinary ASCII, the exact six-code-unit trim set singly and mixed at both edges, interior trim units, nontrimmed Unicode inside/outside trimmed units, valid/unpaired surrogates, U+FFFD, `0x0000`, `0x007F`, direct Unicode and `xn--` rejection across configured DNS/TCP/HTTPS paths, strict TCP and closed HTTPS vectors, combined-defect set-like rejection, and no scheduler/resolver/transport work; direct IPv6 vectors cover exact loopback bytes, exact `(b[0] & 0xFE) == 0xFC` and `(b[0] & 0xE0) == 0x20` masks, all four prefix boundaries for each, later-byte minima/maxima, every normative accepted/rejected literal, direct resolver bypass, rejected-literal no-fallthrough, and unchanged resolver-returned candidate eligibility; separate pinned-PSL source vectors cover bytes through matching and fail with `source_identity_invalid`; identical acceptance decisions/report bytes under required platform/IANA/interface/routing/ICU/NLS/culture/environment variations; and no rejected value, address byte, predicate, prefix, classification, globalization input, dependency, resolver invocation, retry, fallback, new report field, flag, cap, or violation code;
- harness-level configuration rejection for each of `::2`, `::ffff`, and `::ffff:ffff` before source-workload activation, probe-process scheduling, attempt accounting, or resource-measurement workload execution, producing one set-like `configuration_invalid` and proving that accidental acceptance cannot yield a passing zero-work or partial-work resource report;
- persistence policy independent of disk arithmetic: below-cap persistence, cap-reaching persistence, attempted persistence with zero bytes, sticky detection after cleanup, report/measurement retention, exact ordinal multi-code output, deterministic JSON, and no added property or artifact detail;
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
- document-drift tests failing when either startup formula maps to a code other than `configuration_invalid`, Section 7 permits another startup-budget code, the configuration-rejection scenario does not require exactly one common code, Section 8.9 omits either predicate or maps startup rejection to a runtime rolling-budget violation, a retired budget-specific name appears, the violation vocabulary differs from 39, or the report schema/property order changes;
- document-drift tests failing if `unique-local` appears as a direct-literal rule without exact `(b[0] & 0xFE) == 0xFC`; `global-unicast` or a replacement direct-global label appears without exact `(b[0] & 0xE0) == 0x20`; any direct-IPv6 validity depends on a platform helper, operating-system classification/routing decision, IANA registry, mutable prefix list, package, network/reachability/DNS/interface/source-selection input, culture, or globalization; any decoded address outside exact loopback, `fc00::/7`, and `2000::/3` becomes directly valid; `2001:db8::1` becomes invalid; `1::`, `100::1`, or `64:ff9b::1` becomes directly valid; a rejected direct literal may fall through to hostname validation/resolution; or the direct union is applied to resolver-returned candidates;
- document-drift tests failing if `::2`, `::ffff`, or `::ffff:ffff` is removed from the normative rejected vectors; any becomes accepted, reaches hostname validation, or invokes the resolver; any platform mapped/compatible conversion may alter the fixed predicate result; or unit, integration, security/privacy, native Windows, or resource-sampler contract tests omit any of the three exact vectors `::2`, `::ffff`, and `::ffff:ffff`;
- document-drift tests failing if resolver normalization omits or changes the typed family/byte/source-scope boundary; signed-64 to unsigned-32 range and checked numeric conversion; exact mapped, unspecified, multicast, limited-broadcast, or `fe80::/10` byte predicate; post-map IPv4 exclusion order; complete one-snapshot `Ipv6IfIndex`/`Ipv6Enabled`/`IfOperStatusUp` usability test; zero/missing/down/disabled behavior; mapped/native IPv4 or same-byte/different-scope deduplication; unsigned scope sorting; any normative resolver/snapshot vector or exact expected result; the no-refresh/no-interface-name/no-route/no-reachability rule; or the matching unit, integration, security/privacy, native-Windows, and resource-contract coverage; and specifically failing if snapshot failure is described only as a bounded failure, malformed/invariant data may throw or escape, a started path lacks exactly one outcome, an admitted round lacks exactly one due-ledger closure, ordinary acquisition failure becomes non-counting, malformed data becomes a counted TCP outage or omits `attempt_accounting_incomplete`, an incomplete required member satisfies workload coverage, a snapshot-specific evidence or violation code is introduced, or private snapshot/interface/error detail becomes observable;
- document-drift tests failing if a start or end barrier can cancel an already-started Gateway, DNS, or TCP invocation; an already-started TCP snapshot round can receive controlled-cancellation closure for `start_barrier` or `end_barrier`; such a barrier can assign `canceled` or `cancelled` to that invocation; an unstarted barrier-canceled TCP round creates an attempt record; shutdown loses authority to cancel an already-started invocation; a post-pause callback is discarded merely because the barrier began; identical injected transport-start/pause ordering yields different closure, evidence, state, cadence, phase, or accounting; a late callback rewrites deadline-expired or shutdown-controlled logical closure; or phase membership changes from the original transport-start timestamp;
- document-drift tests failing if configurable preprocessing returns to unspecified code-point, scalar, decoder, replacement, or `Rune` behavior; invalid UTF-16 handling is omitted; the six-code-unit trim set changes; the shared validator removes a root dot or a field removes more than one; TCP IPv6/IPv4/hostname order changes, literal failure falls through, historical shorthand/octal/hexadecimal becomes valid, or bracketed/scoped/mapped/prohibited-family IPv6 becomes valid; the HTTPS raw parser becomes library-defined, user information/percent escape/non-443 port can alter authority, or canonical transport can restore the original dot/port or disagree across DNS/SNI/certificate/`Host`; the PSL parser uses default/replacement decoding, accepts BOM/malformed UTF-8, trims malformed lines, rejects single-label rules through configurable-host logic, makes duplicates order-dependent, or changes wildcard/exception/implicit-star/Unicode-ACE eligibility; a second parser/file/dependency is added; another observable code, field, flag, cap, name, resolver invocation, retry, fallback, or transport attempt appears; or the exact 39-code/schema/property-order/nine-flag/ten-cap and unchanged TCP transport/resolver, DNS, HTTPS, PSL, scheduler, budget, persistence, dependency, and privacy contracts change;
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
- separate accounting for every HTTPS provider GET request, fixed GET behavior for every provider member, identical scheduling/accounting for expected-body and status-only providers, method-independent fixed charge and report shape, and no HTTP request-method field in JSON or human output;
- complete required workload passing only after the Gateway logical-round/terminal-ICMP rule, enabled DNS, every configured HTTPS provider, and all 25 TCP members are distinctly exercised during steady state, with terminal failed/timed-out attempts eligible and no exact success count required;
- a zero-attempt run, one missing TCP target, 24 distinct TCP targets plus duplicate work on another, one missing HTTPS provider, aggregate HTTPS counts masking an unexercised provider, missing Gateway coverage, and enabled-but-unexercised DNS each adding `acceptance_workload_incomplete` and failing;
- disabled DNS producing no required member and both DNS flags false; startup-only work and pre-start cancellation not satisfying coverage; and a started nonterminal attempt satisfying neither coverage nor existing attempt closure while adding both applicable violations;
- unknown, missing, duplicate, unmanifested, mutated, or no-longer-configured internal identities; `configuration.target_count`/manifest mismatch; configured/exercised mismatch; and configuration or manifest mutation all failing deterministic reconciliation;
- exact `workload_coverage` top-level placement and property order, JSON Boolean and nonnegative-integer encodings, derived `all_required_exercised`, agreement between human and JSON calculations, and rejection of missing, reordered, malformed, additional, or inconsistent fields;
- readiness remaining true from first observation through final snapshot; loss during the start barrier, after initial member coverage, during end drain, or during final stabilization; transient false-to-true loss not forgiven; and exact readiness-event/sampled-level reconciliation;
- scheduler disposal, disablement, replacement, epoch change/loss, or workload-ownership loss failing even when readiness remains true, with token-bucket and rolling-ledger continuity across barriers and no same-run restart/reactivation after continuity failure;
- every due Gateway, enabled-DNS, HTTPS-aggregate, and enabled-TCP round receiving one immutable admitted due/deadline pair and exactly one terminal, deadline-expired scheduler-incomplete, or controlled-cancellation scheduler-incomplete closure, including activation, exact-once logical ownership-guard release, closure-anchor-plus-jitter, late wake, every waiting mode, both barrier pauses, shutdown, and exact-deadline equality cases, with silent disappearance, guard retention or early release, later-round suppression, duplicate/wrong-member/timestamp/late/post-cancellation closure, premature or duplicate next-round admission, re-anchoring, missing original deadline, identity reuse, prior-child attribution, or ledger-identity failure producing `acceptance_scheduler_inactive`;
- document-drift tests failing if section 3.3 requires terminal aggregation before the next HTTPS round; any closure class fails the prior-round-closed condition; a barrier or shutdown requires early reduction outside the one controlled-cancellation class; cancellation uses its commit, barrier, or shutdown timestamp as the cadence anchor; barrier cancellation is permitted at or after the deadline; a due round can lack its ledger entry; a second round can begin before prior closure or calculated due; an incomplete closure suppresses later cadence; partially started HTTPS behavior is unspecified; started attempt completion can retain the logical ownership guard or rewrite a canceled logical result; shutdown waits for a future deadline, omits reconciliation, or creates replacement work; the end barrier schedules post-end work; sections 3.3, 4.0, 4.2, 4.3, 4.4, 4.5, 4.6, and 8.1 diverge; the closed violation vocabulary differs from 39; or a report field is added for closure state or cancellation reason;
- TCP literal and hostname attempts retaining one `tcp` token/charge/outcome domain under exact preprocessing/classification, canonical literal bytes or one absolute resolver argument, deterministic typed resolver-byte/mapped/exclusion/scope/interface-snapshot normalization and selection, and no fallback, with interface capture adding no token/attempt/network charge and with no candidate, literal, address, raw or normalized scope, interface row/status/index, logical-name, or resolver-name information in either report;
- resource-sampler contract cases for every normative TCP snapshot scenario and tie, proving ordinary failure and timeout remain completed member-exercising counted failures without a violation solely from that condition; start/end pause before start creates no attempt, start/end pause after start preserves ordinary reduction and original phase, shutdown before start creates no attempt, and shutdown after start retains bounded `canceled` or attempt-level `incomplete` accounting; malformed/contradictory data yields one `incomplete`, no `completed` increment, `attempt_accounting_incomplete`, and conditional `acceptance_workload_incomplete`; every started path retains one `tcp` kind count and 1,024-byte charge, every admitted path closes its due entry once, the exact 39-code vocabulary/schema/nine flags/ten caps remain unchanged, and no private adapter/error/snapshot/interface/scope/candidate/barrier-order detail leaks;
- exact `runtime_continuity` placement and `monitoring_ready_continuous`, `scheduler_active_continuous`, `scheduler_epoch_unchanged`, `all_due_rounds_reconciled` property order, derived JSON Booleans, all-four-true pass requirement, and identical human-report values;
- privacy exclusion of target/provider identities, endpoints, hosts, ports, operator IDs, scheduler coordination keys/phases/sequences, logical-target keys/comparisons, scheduler epochs, rolling-ledger entries/timestamps, cancellation reasons, readiness-transition timestamp details, route/interface values, internal member identities, and private paths from workload/continuity evidence, with the deterministic zero-attempt fragment remaining valid diagnostic output but never a passing nonempty-manifest run;
- exact fixed charge derivation for every supported method, adapter charge input/override rejection, measured-below-table and measured-above-table attribution, and one shared configured-budget/acceptance table without drift;
- byte-identical totals and reports from different adapters with identical measured bytes, one exact 8,192-byte rolling charge per concurrent HTTPS provider, deterministic aggregate network totals immediately below, exactly at, and above the 100,000-byte/minute boundary, and exact rolling admission immediately below and at 100,000 fixed charged bytes;
- complete exact transport-start/charge records proving no rolling monotonic 60-second window over activation or later runtime has more than 60 starts or at least 100,000 fixed charged bytes; deterministic candidate order and exact-age expiry; `rolling_attempt_budget_exceeded`/`rolling_network_budget_exceeded` appearing exactly once and in ordinal order on actual violations; and `attempt_accounting_incomplete` when either invariant cannot be proved;
- route/interface discovery remaining outside token and transport-invocation counts, with same-pass family-specific `Connected`/`Metric`/`DisableDefaultRoutes` resolution, stable A/B membership changes for both flag-toggle directions, exact false-only eligibility and tuple selection, all-disabled `missing_route`, API/incomplete-row and persistent-instability `route_selection_unavailable`, disabled-route workload exclusion, route or policy changes after commit deferred to the next round, a drift check for the false requirement in every eligibility summary, and no policy value, VPN/interface identity or type, or other route/interface candidate field in either report;
- cancellation before transport invocation not counting as started; start/end barriers after Gateway/DNS/TCP start never producing cancellation; and scheduler-shutdown cancellation after start counting only as `canceled` when terminal cancellation is observed, otherwise as existing attempt-level `incomplete`;
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

Pre-registration configuration and all operational evidence are intentionally volatile. Restart or readiness degradation loses counters, transitions, and flapping history. Restart reconstructs enabled targets as `unknown`; scheduler activation after readiness moves them to `baselining`. A malformed restart-time override can therefore stop probing until configuration is fixed and another restart succeeds; R0-005 accepts that visible availability loss rather than silently reusing stale targets/providers, and this scoped exception does not apply once an approved durable configuration owner exists. R0-005 intentionally accepts only ASCII configurable names, the closed TCP literal forms, canonical HTTPS provider URLs, and the pinned ASCII-eligible PSL subset; direct IPv6 configuration is limited to exact `::1`, `fc00::/7`, and `2000::/3`, while the separately defined resolver-returned candidate contract is unchanged. It does not accept internationalized/ACE hostnames, platform-permissive numeric literals, scoped/link-local direct IPv6, direct literals outside that fixed union, user-authored URI normalization, or runtime suffix-data changes. Future direct-literal expansion beyond the fixed union requires a separately reviewed requirements change; other later support requires separately reviewed requirements, globalization/data, dependency, licensing, update-lifecycle, transport, and security work. In addition, the inherited R0-004 limitation remains: simultaneous journal and lifecycle-marker failure can leave the durable marker stale until restart even though in-process readiness is false. R0-005 must stop on that in-process false state.

## 12. Delivery boundary

R0-005 is complete only when the implementation, deterministic tests, native evidence, and resource report satisfy this document. Completion does not authorize:

- registration or identity creation;
- persistence or cloud delivery;
- target-management surfaces;
- alerts, incidents, causal claims, or “protected” status;
- R0-006 or R0-013 work.

Any implementation need that conflicts with this boundary is a new requirements decision and must stop for review.
