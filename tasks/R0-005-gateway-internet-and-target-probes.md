# R0-005 — Gateway, internet, and target probes

Priority: P0  
Release: R0  
Depends on: R0-002, R0-003, R0-004

## Objective

Implement bounded, deterministic, method-aware local probes for the active default gateway, DNS, aggregate external HTTPS connectivity, and up to 25 statically configured TCP targets.

The implementation must follow [the binding R0-005 requirements](../docs/R0-005-probe-requirements.md) and [ADR-010](../adr/ADR-010-pre-registration-probe-boundary.md). This task does not authorize registration, durable probe persistence, cloud delivery, target management, alerts, incidents, or user-facing outage claims.

## Resolved planning blockers

1. **Probe states:** use `unknown`, `baselining`, `healthy`, `suspect`, `offline`, and `recovering` with the exact transition table and compatibility mappings in the requirements.
2. **Identity and persistence:** before R0-006, do not fabricate agent, property, tenant, enrollment, or cloud identity. Keep probe configuration and runtime state in memory and do not append probe samples or material events.
3. **Configuration:** bind and atomically validate static .NET startup configuration. A restart applies changes. No CRUD, UI, HTTP management API, SQLite configuration, or cloud configuration is in scope.
4. **External endpoints:** default to exactly Google `generate_204` and Cloudflare `cdn-cgi/trace`, with reviewed `google` and `cloudflare` operator declarations, strict HTTPS, hostname validation, no redirects, and endpoint-specific status rules. Overrides must satisfy the exact ID, host, registrable-domain, and declared-operator validation without claiming runtime proof of corporate ownership. Do not retain or inspect the Cloudflare response body.
5. **Scheduling and transitions:** use the exact intervals, timeouts, jitter, concurrency, 60-attempt/minute and charged-network startup budgets, rotating fair dispatch, token consumption, deadline, cancellation, thresholds, recovery, and flapping rules in the requirements.
6. **Resource acceptance:** the future implementation must supply the exact 25-target, 30-complete-steady-state-minute Windows sampler contract, formulas, attribution boundaries, deterministic `r0-005-resource-report/v1` output, and pass/failure rules.
7. **Backlog boundary:** pull forward only static TCP definitions, TCP connect execution, local operational states, and bounded flapping evidence. R0-013 retains target CRUD, persistent target configuration, richer selection, and UI.

## In scope

- discover the active default route, interface, and gateway each round;
- use ICMP gateway evidence when the platform permits it, without inventing a TCP fallback;
- resolve one bounded DNS name independently of HTTPS state;
- probe all configured HTTPS providers and aggregate them so one provider cannot declare total internet failure;
- perform TCP connect probes for up to 25 enabled, explicitly configured host/port targets;
- implement the required scheduler, state machine, bounded evidence codes, degradation behavior, and deterministic tests;
- add the required local resource-sampling script and ignored report location.

## Out of scope

- registration, enrollment, tenant/property association, or fabricated identity;
- probe journal/outbox writes, a schema version change, or new persistence tables;
- target CRUD, persistent management, UI, management APIs, cloud configuration, or hot reload;
- user incidents, notifications, cooldowns, alert suppression, or “protected” status;
- broad port scanning, subnet/range discovery, automatic port discovery, arbitrary TCP fallback, packet capture, deep inspection, or vendor-specific discovery;
- RTSP frame analysis, credentials, authenticated probes, uploads, cookies, redirects, or response-body retention;
- cloud delivery, long learned baselines, or a seven-day soak as this PR's merge gate.

## Acceptance criteria

- All implementation and tests conform to `docs/R0-005-probe-requirements.md`.
- Gateway, DNS, HTTPS aggregate, and TCP states transition deterministically under an injected clock and injected transports.
- DNS failure remains independently visible and cannot alone declare HTTPS aggregate offline.
- A single HTTPS endpoint failure produces bounded `partial_endpoint_failure` evidence without incrementing the aggregate failure counter or creating an incident signal.
- Probe work starts only after R0-004 readiness and stops promptly on cancellation or journal/service degradation, with no collection continuing silently.
- Invalid startup configuration prevents probe scheduling and reports only bounded, non-sensitive validation errors.
- HTTPS configuration proves only structural diversity across distinct declared operators and registrable domains; separately operated providers and truthful custom `operator_id` declarations remain operator responsibilities.
- Atomic startup validation rejects aggregate attempt demand above 60/minute or charged-network demand at or above 100,000 bytes/minute before scheduling.
- Due gateway, DNS, HTTPS aggregate, and TCP logical targets use deterministic rotating fair dispatch so no target or multi-provider HTTPS round monopolizes tokens or execution slots.
- The implementation neither creates identity nor calls `AppendProbeSampleAsync` or `AppendMaterialEventAsync`.
- Unit, local integration, security/privacy, lifecycle, and native Windows tests in the binding requirements pass.
- The 25-target sampler observes the exact PID for 30 complete steady-state minutes and passes every measurement, attribution, growth, accounting, JSON, privacy, and cleanup rule in the binding requirements.

## Required validation

Run:

```powershell
dotnet restore ./RemotePropertyWatchdog.sln
dotnet build ./RemotePropertyWatchdog.sln --configuration Release --no-restore
dotnet test ./RemotePropertyWatchdog.sln --configuration Release --no-build
pwsh ./scripts/test.ps1
dotnet list ./RemotePropertyWatchdog.sln package --vulnerable --include-transitive
pwsh ./scripts/r0-005-resource-sampling.ps1 `
  -Configuration Release `
  -TargetCount 25 `
  -DurationMinutes 30
```

Live public endpoints are not a CI dependency. Automated HTTPS tests must use fakes or disposable local endpoints.

## Required evidence

- deterministic state-transition and aggregate-outcome test results;
- declared-operator, aggregate rate/network-budget, fair-dispatch, configuration, and privacy/security test results;
- lifecycle/degradation and cancellation test results;
- native Windows gateway-method evidence;
- the resource sampler's bounded human summary and matching `r0-005-resource-report/v1` JSON report from ignored `artifacts/r0-005/`;
- exact validation commands, totals, warnings, errors, and limitations.

## Codex completion report

Use the completion report in `AGENTS.md`. Do not begin R0-006 or R0-013 work.
