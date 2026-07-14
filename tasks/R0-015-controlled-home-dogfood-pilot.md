# R0-015 — Controlled home dogfood pilot

Priority: P0  
Release: R0  
Depends on: R0-014

## Objective

Run the prototype on the founder’s property and measure real-world stability, alerts, and usability before private alpha.

## In scope

- install on an always-on Windows computer.
- monitor router/internet, DVR/NVR, and selected cameras.
- controlled unplug/reconnect tests.
- overnight and seven-day soak.
- Starlink/rural connectivity observation.
- non-technical alert comprehension review.

## Out of scope

- covert testing on third-party networks.
- unsafe interference with emergency/security systems.
- claims that the pilot detects all sabotage.

## Implementation notes

- Schedule tests, preserve personal safety, and avoid disabling all protection without a backup plan.
- Use synthetic labels in shared defect reports where practical.

## Acceptance criteria

- 10 of 10 reboot recoveries.
- Seven-day soak has zero false Critical alerts and no unplanned service termination.
- Controlled camera disconnect/restoration messages are correct and non-duplicative.
- Controlled WAN outage meets latency target in at least 95% of runs.
- Founder can understand incident text and identify required action.
- Go/conditional-go/no-go review is completed.

## Required tests

- Physical CF matrix runs.
- daily health review.
- resource measurements.
- alert comprehension review.

## Required evidence

- Dogfood test log.
- Completed R0 scorecard.
- Architecture/timing changes proposed through ADRs.
- R1 recommendation.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
