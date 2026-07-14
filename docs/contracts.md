# Contract Baseline

## Identity and ordering

- IDs are opaque strings or UUID/ULID-compatible identifiers; consumers must not infer meaning from them.
- Agent event sequence is strictly increasing within an agent identity.
- `observed_at` is the agent's UTC wall-clock observation.
- `received_at` is assigned by the cloud.
- Ordering is primarily by agent sequence; wall-clock anomalies are recorded, not silently corrected.
- Every retryable write has a stable idempotency key.

## Heartbeat default

- Interval: 30 seconds.
- Internal late marker: first missed interval plus 10 seconds.
- Incident confirmation: three missed heartbeats plus 15 seconds grace.
- Evaluator frequency: every 15 seconds.
- Restoration: two current heartbeats plus local healthy summary when available.
- Target payload: less than 10 KB.

These values are prototype defaults and remain prototype-gated. Timing logic must use configuration and an injected clock.

## Material event requirements

Every material event contains:

- schema version;
- message/event ID;
- property and agent identity;
- agent sequence;
- session ID;
- event type;
- observed UTC timestamp;
- relevant target identity;
- state transition;
- bounded evidence codes;
- gateway/internet summary when relevant.

## Notification invariant

For one logical incident and recipient/channel/stage, there may be no more than one successful logical send. Provider retries do not create additional logical notification records.

## Incident-language data contract

An incident presentation must keep separate fields for:

- `confirmed_observation`;
- `interpretation`;
- `limitations`;
- `recommended_action`;
- `confidence`.

Do not concatenate uncertain interpretations into a confirmed headline.
