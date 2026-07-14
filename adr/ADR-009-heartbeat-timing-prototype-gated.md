# ADR-009 — heartbeat timing prototype gated

Status: Prototype-gated

## Context

This targets a roughly 105–120 second detection window while allowing delayed packets.

## Decision

Start with 30-second heartbeat, three misses plus 15-second grace, and 15-second evaluator cadence.

## Consequences

- All timing is configurable and tested with an injected clock.
- Retain or revise only after controlled and soak-test evidence.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
