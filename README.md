# Remote Property Watchdog — Codex-Ready R0 Kickoff

Version: 0.1  
Status: Implementation starter  
Purpose: Prove the core monitoring and remote-alert mechanism before building a commercial user experience.

## What R0 must prove

R0 is successful only when a Windows computer at a property can:

1. run an always-on monitoring service;
2. monitor the gateway, internet path, and selected critical devices;
3. preserve evidence locally during an outage;
4. send heartbeats to an off-site cloud service;
5. cause the cloud to open exactly one property-unreachable incident after confirmed missed heartbeats;
6. send one initial remote alert and one linked restoration alert;
7. replay local evidence after connectivity returns without duplication; and
8. explain confirmed observations, likely interpretations, and limitations without claiming that tampering is proven.

## Start here

Read these files in order:

1. [`AGENTS.md`](AGENTS.md)
2. [`docs/r0-scope.md`](docs/r0-scope.md)
3. [`docs/system-context.md`](docs/system-context.md)
4. [`docs/technology-stack.md`](docs/technology-stack.md)
5. [`docs/acceptance-criteria.md`](docs/acceptance-criteria.md)
6. [`tasks/README.md`](tasks/README.md)
7. [`tasks/R0-001-repository-bootstrap.md`](tasks/R0-001-repository-bootstrap.md)

## First Codex prompt

```text
Read AGENTS.md, docs/r0-scope.md, docs/technology-stack.md, docs/security-guardrails.md,
and tasks/R0-001-repository-bootstrap.md. Inspect the repository before editing.
Implement only R0-001. Add the required tests and validation commands, run them, and report:
1) files changed, 2) commands run and results, 3) acceptance criteria status,
4) assumptions, and 5) remaining risks. Do not begin R0-002.
```

## Delivery sequence

The tasks are intentionally ordered. R0-001 through R0-009 are primarily sequential because later interfaces depend on earlier contracts. R0-010 through R0-013 can be parallelized cautiously after contracts stabilize. R0-014 is the end-to-end gate. R0-015 is the controlled home dogfood pilot.

## Repository intent

This package is a planning and scaffold repository. It contains no production-ready implementation. Codex should create the solution and code under `src/` and `tests/` while preserving the documented boundaries.

## Core architectural promise

- Local LAN access stays local.
- Normal operation uses outbound HTTPS only.
- No router port forwarding or inbound remote-control tunnel.
- Camera credentials remain local by default.
- No video or audio upload in R0.
- The cloud can detect property silence even after the property loses internet.
- Alerts report evidence and uncertainty; they do not assert criminal intent.

## Package contents

- `docs/`: condensed product, architecture, security, acceptance, and test guidance.
- `adr/`: initial architecture decisions.
- `tasks/`: implementation-ready R0 tickets.
- `prompts/`: reusable Codex prompts.
- `schemas/`: initial JSON contracts.
- `tests/acceptance/`: executable scenario specifications.
- `scripts/`: bootstrap, test, and package validation helpers.
- `reference-documents/`: the approved v0.1 source documents used to derive this package.
