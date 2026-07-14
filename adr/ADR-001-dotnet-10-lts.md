# ADR-001 — .NET 10 LTS

Status: Accepted

## Context

A single supported LTS runtime provides Windows Service support, ASP.NET Core, shared contracts, and an operationally simple prototype.

## Decision

Use .NET 10 LTS for the Windows agent and cloud services.

## Consequences

- Use the latest supported patches.
- Avoid preview language/runtime features unless approved.
- Reassess before .NET 10 support ends or if a critical platform limitation appears.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
