# SCN-001 — Full Internet Outage with LAN Still Active

## Preconditions

- Agent registered and protection active.
- Gateway reachable and at least one critical camera target healthy.
- Heartbeats current for five intervals.

## Action

Block the agent's WAN path while leaving the Windows host, LAN, gateway, and monitored camera powered.

## Expected local evidence

- Gateway remains up.
- Multiple external HTTPS checks fail across confirmation rounds.
- Local target probes continue.
- Internet state becomes unavailable.
- Material events queue in SQLite.

## Expected cloud behavior

- First missed interval becomes internal late state only.
- One property-unreachable incident opens after three misses plus 15-second grace.
- No duplicate incident during continued outage.

## Expected notification

One initial remote alert states that the property stopped communicating and that the cause is not yet confirmed.

## Restoration

Restore WAN. Agent sends newest heartbeat, then ordered event batches. Cloud enters recovering, merges evidence, may refine the incident to internet unavailable, and sends one linked restoration after policy confirmation.

## Prohibited outcomes

- Claiming that the line was cut.
- Multiple initial SMS messages.
- Loss or duplication of queued events.
- Replacing observed timestamps with receive timestamps.
