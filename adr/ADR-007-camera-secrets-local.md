# ADR-007 — camera secrets local

Status: Accepted

## Context

The cloud does not need surveillance credentials to perform the core remote-alert function.

## Decision

Keep camera and recorder credentials local by default.

## Consequences

- Protect with Windows-bound secure storage.
- Never include credentials in logs/support bundles.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
