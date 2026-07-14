# ADR-004 — cloud modular monolith

Status: Accepted

## Context

The small team needs clear boundaries without early microservice deployment complexity.

## Decision

Begin with a modular monolith plus background workers.

## Consequences

- Modules own their data access and domain behavior.
- Extract services only after measured scaling/ownership pressure.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
