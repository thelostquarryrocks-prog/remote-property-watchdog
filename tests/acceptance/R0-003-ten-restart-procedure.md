# R0-003 Windows Service lifecycle acceptance procedure

This procedure records the required Windows-specific validation evidence for the native service shell. Run it on a Windows test host with administrator privileges; it is not expected to run on Linux CI.

## Preconditions

- .NET 10 SDK/runtime is installed.
- The repository has been restored and built from the current branch.
- PowerShell is running as Administrator.

## Commands

```powershell
pwsh ./scripts/windows-service-lifecycle.ps1 -RestartCount 10
```

## Evidence to capture

1. Screenshot or transcript showing the service status as `Running` with `StartupType` set to `Automatic`.
2. Lifecycle marker JSON after initial start showing a non-empty `session_id`, `started_at_utc`, `ready_at_utc`, and `planned_shutdown: false` while running.
3. Transcript lines for all ten successful restarts.
4. Lifecycle marker JSON after planned stop showing `planned_shutdown: true` and `stopped_at_utc`.
5. For forced termination validation, start the service, terminate the service process from Task Manager or `Stop-Process -Force`, then start the service again and verify the new start log or marker-derived state reports the previous session as unclean.

## Acceptance record template

```text
Date/time UTC:
Windows version:
.NET SDK version:
Service name:
Initial status evidence:
Ten restart result:
Forced termination result:
Final lifecycle marker excerpt:
Operator:
Notes/risks:
```
