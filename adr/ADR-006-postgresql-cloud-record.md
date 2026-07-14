# ADR-006 — PostgreSQL cloud record

Status: Accepted

## Context

Users, properties, agents, incidents, notifications, audit, and subscriptions are relational and transactional.

## Decision

Use managed PostgreSQL as the cloud system of record.

## Consequences

- Use migrations and backups.
- Enforce tenant scope and uniqueness constraints in the database where practical.

## Alternatives

Documented in the approved architecture blueprint; revisit only with measured evidence.

## Evidence

R0 controlled-failure tests, soak tests, and dogfood results.

## Review trigger

A failed acceptance gate, a material security issue, unsupported platform lifecycle, or commercial requirement that cannot be met within this boundary.
