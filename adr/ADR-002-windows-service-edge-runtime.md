# ADR-002 — windows service edge runtime

Status: Accepted

## Context

LAN probing requires local execution that survives user logout and restarts after reboot/crash.

## Decision

Use a native Windows Service as the initial edge runtime.

## Consequences

- Provide visible readiness and sleep-risk status.
- Do not claim protection when the service is stopped or the computer is sleeping.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
