# SCN-003 — Agent Process Stopped

## Preconditions

Agent and cloud are healthy with current heartbeats.

## Action A — Planned stop

Stop the service normally.

Expected: a local clean shutdown event is committed before exit when possible. Cloud eventually sees property silence, but the later timeline records the planned stop evidence.

## Action B — Forced termination

Kill the agent process without graceful shutdown.

Expected: cloud detects property silence. On restart, the agent records the previous session as unclean and uploads that evidence. The system must not claim who or what stopped it.

## Prohibited outcomes

- Silent restart with no lifecycle evidence.
- Duplicate property incidents.
