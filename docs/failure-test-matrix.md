# Controlled Failure Matrix

| ID | Injected condition | Expected classification | Target outcome |
|---|---|---|---|
| CF-01 | Block agent internet; keep LAN active | Property unreachable; later enriched as internet unavailable | <=150 sec remote alert |
| CF-02 | Stop one camera power | Single critical device offline; upstream healthy | 30–90 sec in Away mode |
| CF-03 | Disconnect camera Ethernet cable | Single critical device offline; possible cable/power/device | 30–90 sec in Away mode |
| CF-04 | Stop recorder service but keep host online | Service unavailable on reachable host | <=180 sec |
| CF-05 | Power off recorder | Recorder offline; dependent recording at risk | <=180 sec |
| CF-06 | Disable router WAN only | Property unreachable; replay shows gateway remained up | <=150 sec plus enrichment |
| CF-07 | Power off router | Property unreachable; local evidence may show gateway loss before silence | <=150 sec |
| CF-08 | Stop Windows service cleanly | Monitoring stopped or planned-shutdown evidence | Policy dependent |
| CF-09 | Kill agent process | Cloud property silence; unclean session recorded after restart | <=150 sec |
| CF-10 | Put PC to sleep | Property silence; coverage-gap limitation visible after resume | <=150 sec |
| CF-11 | Fail one HTTPS test endpoint | No internet incident | No user alert |
| CF-12 | Fail DNS while another internet path remains usable | DNS degraded, not total outage | Medium/internal |
| CF-13 | Disable two cameras sequentially | Unusual sequential failure pattern | Pattern window plus one check |
| CF-14 | Disable shared switch | Multiple related devices/shared dependency failure | <=180 sec |
| CF-15 | Restore after four-hour outage | Replay, enriched timeline, single restoration | <=120 sec after stable recovery |
| CF-16 | Replay duplicate event batch | No duplicate event, incident, or notification | Immediate deduplication |
| CF-17 | Restart cloud worker during escalation | No duplicate SMS | One logical send |
| CF-18 | Change system clock materially | Timeline confidence flag; ordering preserved by sequence | No false incident |


## Execution record

For each run capture:

- environment and agent version;
- baseline confirmation time;
- physical or simulated action;
- expected incident and timing;
- actual local detection, cloud incident, message receipt, and restoration times;
- duplicate or false alerts;
- evidence/screenshot references;
- result and defect ID.
