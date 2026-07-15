# R0-002 Contract Matrix

| DTO | Schema | Purpose |
|---|---|---|
| `HeartbeatDto` | `schemas/heartbeat.schema.json` | Agent liveness and bounded local health summary. |
| `MaterialEventDto` | `schemas/material-event.schema.json` | Durable material observation with sequence, UTC timestamp, and bounded evidence codes. |
| `EventBatchDto` | `schemas/event-batch.schema.json` | Idempotent replay/upload batch for material events. |
| `IncidentSummaryDto` | `schemas/incident-summary.schema.json` | Incident presentation fields with observation, interpretation, limitation, action, and confidence separated. |

## Supporting transport contracts without schemas

| DTO | Purpose |
|---|---|
| `ErrorEnvelopeDto` | Stable machine-readable error code envelope for API responses. |
| `AgentConfigurationDto` | Versioned minimal agent configuration transport shape for selected critical targets and heartbeat cadence. |

## Conventions

- Current schema version is `0.1`.
- JSON uses deterministic snake_case property names and string enums.
- DTO validation rejects unsupported enum values, malformed identifiers, non-positive sequences, non-UTC timestamps, and oversized heartbeat payloads.
- Deserialization ignores additive optional fields so older readers can safely accept newer payloads when required fields remain compatible.
