# ADR-003 — outbound HTTPS only

Status: Accepted

## Context

No router port forwarding or inbound tunnel is required for monitoring and alerts.

## Decision

Normal property-to-cloud communication uses outbound HTTPS only.

## Consequences

- Reduces attack surface and setup burden.
- Precludes general remote administration in R0.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
