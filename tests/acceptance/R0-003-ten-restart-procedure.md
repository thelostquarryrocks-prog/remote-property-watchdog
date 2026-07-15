# R0-003 Windows Service lifecycle acceptance procedure

This procedure captures the required Windows-specific evidence for the native service shell. R0-003 readiness means the service lifecycle shell has initialized, recorded a session, and is ready for later monitoring components; it does **not** mean network probes, heartbeats, registration, ingestion, incidents, notifications, installers, updates, tunnels, or inbound LAN listeners exist.

## Standard lifecycle and ten-restart command

Run from an elevated PowerShell session on a Windows test host:

```powershell
pwsh ./scripts/windows-service-lifecycle.ps1 -RestartCount 10
```

The script writes a timestamped transcript and JSONL lifecycle marker evidence under `artifacts/r0-003/<timestamp>/`. It polls the lifecycle marker after SCM reaches `Running` and does not treat SCM state alone as readiness evidence.

## Two-phase reboot verification

Phase 1 installs the Automatic service, creates a one-time startup evidence scheduled task named `RemotePropertyWatchdog-R0-003-RebootEvidence` running as SYSTEM, and leaves the service installed for reboot:

```powershell
pwsh ./scripts/windows-service-lifecycle.ps1 -PrepareRebootVerification
```

Reboot the Windows host. The startup task writes service status, startup type, process start time, OS boot time, and lifecycle marker evidence under `artifacts/r0-003/reboot-evidence`. After the host comes back, run phase 2 from an elevated PowerShell session to verify that evidence was generated during startup and then clean up:

```powershell
pwsh ./scripts/windows-service-lifecycle.ps1 -VerifyRebootAndCleanup
```


## Manual cleanup for interrupted runs

If a run is interrupted after phase 1 or before automatic cleanup, run from an elevated PowerShell session:

```powershell
Unregister-ScheduledTask -TaskName 'RemotePropertyWatchdog-R0-003-RebootEvidence' -Confirm:$false -ErrorAction SilentlyContinue
Stop-Service -Name 'RemotePropertyWatchdogAgent' -ErrorAction SilentlyContinue
sc.exe delete 'RemotePropertyWatchdogAgent'
```

## Pass/fail acceptance record

| Criterion | Required evidence | Pass/Fail | Evidence path or excerpt | Notes |
|---|---|---|---|---|
| Service starts after reboot without user login | Phase 2 transcript shows `Status: Running`/`StartType: Automatic`, and `artifacts/r0-003/reboot-evidence/reboot-evidence-*.json` shows evidence generated after `os_boot_time_utc`. |  |  |  |
| Monitoring-ready state within 60 seconds | Marker evidence has non-empty `ready_at_utc` and `readiness_elapsed_milliseconds <= 60000`. |  |  |  |
| Planned stop records clean lifecycle event | Final marker evidence has `planned_shutdown: true` and non-empty `stopped_at_utc`. |  |  |  |
| Planned restarts classify previous session as clean | Every planned restart marker has a new non-empty `session_id`, `previous_session_id` equal to the prior session, and `previous_session_ended_cleanly: true`. |  |  |  |
| Forced termination classifies previous session as unclean | Forced-termination restart marker has a new non-empty `session_id`, `previous_session_id` equal to the killed session, and `previous_session_ended_cleanly: false`. |  |  |  |
| Logs contain no secrets | Transcript and marker evidence contain only service state, session IDs, lifecycle timestamps, timeout values, and marker paths. |  |  |  |

## Operator record

```text
Date/time UTC:
Windows version:
.NET SDK version:
Commit SHA:
Service name: RemotePropertyWatchdogAgent
Display name: Remote Property Watchdog Agent
Standard lifecycle transcript path:
Standard marker evidence path:
Reboot phase 1 transcript path:
Reboot phase 2 transcript path:
Screenshots/log excerpts captured:
Acceptance criteria passed:
Acceptance criteria failed or blocked:
Operator:
Known limitations or follow-up risks:
```
