# R0 Acceptance Criteria

| ID | Criterion | Pass condition |
|---|---|---|
| AC-001 | Property-silence incident uniqueness | 20 of 20 scripted tests open exactly one incident |
| AC-002 | Remote alert latency | At least 95% of property-silence SMS alerts arrive within 150 seconds of last successful heartbeat |
| AC-003 | Single-camera classification | 20 of 20 camera disconnect tests with healthy internet classify as target outage, not property outage |
| AC-004 | Multi-endpoint resilience | Failure of one external test endpoint never opens an internet-outage incident while other endpoints succeed |
| AC-005 | Offline evidence durability | Four-hour simulated outage preserves all state transitions and replays them without duplication |
| AC-006 | Notification deduplication | No more than one initial SMS per recipient and one linked restoration SMS per alerted incident |
| AC-007 | Restoration latency | At least 95% of tests message restoration within 120 seconds of stable recovery |
| AC-008 | Reboot recovery | Service resumes monitoring after 10 of 10 Windows reboots without manual intervention |
| AC-009 | Soak reliability | Seven-day soak creates zero false Critical alerts and zero unplanned service terminations |
| AC-010 | Secret redaction | Logs/support bundles contain no camera password, cloud secret, or full authorization header |
| AC-011 | Alert comprehension | A non-technical reviewer accurately explains five incident timelines without engineer assistance |

## Resource guardrails

- less than 150 MB steady-state working set while monitoring 25 targets;
- less than 2% average CPU on a typical always-on home PC, excluding short discovery bursts;
- less than 100 MB/day disk growth before retention cleanup;
- less than 100 KB/minute average agent network overhead excluding optional diagnostics;
- monitoring-ready within 60 seconds after network availability.

## Exit decision

- **GO:** all P0 criteria pass; no unresolved critical security issue; failure matrix is repeatable.
- **CONDITIONAL GO:** isolated misses have understood fixes and do not undermine the core promise.
- **NO-GO/REWORK:** duplicate alerts, frequent false Critical incidents, unreliable service recovery, lost outage evidence, insecure identity, or customer text that cannot represent uncertainty honestly.
