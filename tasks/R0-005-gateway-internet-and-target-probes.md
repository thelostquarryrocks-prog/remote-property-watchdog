# R0-005 — Gateway, internet, and target probes

Priority: P0  
Release: R0  
Depends on: R0-002, R0-003, R0-004

## Objective

Collect bounded, method-aware evidence for gateway, DNS, external internet, and selected TCP targets.

## In scope

- default-gateway discovery; ICMP or safe alternative evidence.
- multiple independent HTTPS targets.
- DNS probe separated from total internet state.
- TCP connect probe for selected device/service.
- scheduling, jitter, timeout, concurrency, and rate guardrails.
- suspect/offline/recovering evidence rounds.

## Out of scope

- broad port scanning.
- packet capture.
- vendor-specific device discovery.
- production RTSP frame analysis.

## Implementation notes

- One failed external endpoint cannot declare internet outage.
- Targets can declare historically successful methods.
- Probe results use bounded evidence codes, not raw sensitive payloads.

## Acceptance criteria

- Gateway/internet/device states transition deterministically.
- One failed HTTPS endpoint creates no user incident signal.
- DNS-only failure remains distinct from total outage.
- Cancellation stops probes promptly.
- 25-target steady-state stays within resource guardrails.

## Required tests

- Unit tests with fake probe transports and controllable clock.
- Integration tests against local disposable endpoints.
- timeout/cancellation tests.
- resource sampling script.

## Required evidence

- Probe matrix and measured resource report.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
