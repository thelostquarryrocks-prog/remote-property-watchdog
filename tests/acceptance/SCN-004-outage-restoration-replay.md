# SCN-004 — Extended Outage, Restoration, and Replay

## Preconditions

Agent has a healthy baseline and the outbox is empty.

## Action

Simulate a four-hour cloud/WAN outage while local device states change several times. Restart the agent once during the outage. Then restore connectivity.

## Expected behavior

- Every material event is durable locally.
- Agent sequence remains monotonic across restart.
- On reconnect, the newest heartbeat is sent first.
- Event batches resume from the last cloud acknowledgment.
- Duplicate retry is harmless.
- Cloud timeline preserves observed time and server receive time.
- One restoration notification is linked to the original incident.

## Pass condition

Cloud event/evidence counts match unique locally committed material events, with no logical duplicates or missing sequence.
