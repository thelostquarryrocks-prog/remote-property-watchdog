# ADR-008 — no video upload R0

Status: Accepted

## Context

The prototype can validate connectivity and service availability without creating unnecessary privacy and storage risk.

## Decision

Do not upload video, audio, frames, or snapshots in R0.

## Consequences

- RTSP work is limited to local handshake/health evidence.
- Any future media handling requires a new privacy/security ADR.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
