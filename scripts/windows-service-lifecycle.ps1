param(
    [string]$Configuration = 'Release',
    [string]$ServiceName = 'RemotePropertyWatchdogAgent',
    [string]$DisplayName = 'Remote Property Watchdog Agent',
    [int]$RestartCount = 10,
    [switch]$PrepareRebootVerification,
    [switch]$VerifyRebootAndCleanup
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Assert-WindowsAdministrator {
    if (-not $IsWindows) {
        throw 'This validation script must be run on Windows.'
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This validation script must be run from an elevated PowerShell session.'
    }
}

function Wait-ServiceStatus {
    param(
        [string]$Name,
        [System.ServiceProcess.ServiceControllerStatus]$Status,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $service = Get-Service -Name $Name -ErrorAction Stop
        if ($service.Status -eq $Status) {
            return $service
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Service '$Name' did not reach status '$Status' within $TimeoutSeconds seconds."
}

function Read-LifecycleMarker {
    param([string]$Path)

    if (-not (Test-Path -Path $Path)) {
        throw "Lifecycle marker not found at $Path."
    }

    Get-Content -Path $Path -Raw | ConvertFrom-Json
}

function Assert-RunningMarker {
    param(
        [object]$Marker,
        [string]$Context,
        [string]$PreviousSessionId,
        [bool]$PreviousCleanExpected
    )

    if ([string]::IsNullOrWhiteSpace([string]$Marker.session_id)) {
        throw "$Context marker has an empty session_id."
    }
    if ($null -eq $Marker.ready_at_utc) {
        throw "$Context marker does not contain ready_at_utc."
    }
    if ($Marker.planned_shutdown -ne $false) {
        throw "$Context marker should have planned_shutdown=false while running."
    }
    if ($null -eq $Marker.readiness_elapsed_milliseconds -or [int64]$Marker.readiness_elapsed_milliseconds -gt 60000) {
        throw "$Context marker readiness elapsed is missing or greater than 60 seconds."
    }
    if ($PreviousSessionId -and ([string]$Marker.previous_session_id -ne $PreviousSessionId)) {
        throw "$Context marker previous_session_id mismatch. Expected $PreviousSessionId but found $($Marker.previous_session_id)."
    }
    if ($Marker.previous_session_ended_cleanly -ne $PreviousCleanExpected) {
        throw "$Context marker previous_session_ended_cleanly mismatch. Expected $PreviousCleanExpected but found $($Marker.previous_session_ended_cleanly)."
    }
}

function Assert-StoppedMarker {
    param([object]$Marker)

    if ($Marker.planned_shutdown -ne $true) {
        throw 'Planned stop marker did not record planned_shutdown=true.'
    }
    if ($null -eq $Marker.stopped_at_utc) {
        throw 'Planned stop marker did not record stopped_at_utc.'
    }
}

function Install-AgentService {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
        try { Wait-ServiceStatus -Name $ServiceName -Status Stopped -TimeoutSeconds 30 | Out-Null } catch { }
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

Assert-WindowsAdministrator

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$artifactRoot = Join-Path $repoRoot 'artifacts/r0-003'
$evidenceDir = Join-Path $artifactRoot $timestamp
$publishDir = Join-Path $artifactRoot 'windows-service'
$markerPath = Join-Path $env:ProgramData 'RemotePropertyWatchdog/Agent/r0-003-lifecycle-marker.json'
$transcriptPath = Join-Path $evidenceDir 'windows-service-lifecycle-transcript.txt'
$markerEvidencePath = Join-Path $evidenceDir 'markers.jsonl'
$projectPath = Join-Path $repoRoot 'src/Agent/RemotePropertyWatchdog.Agent.Service/RemotePropertyWatchdog.Agent.Service.csproj'

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $markerPath -Parent) | Out-Null

Start-Transcript -Path $transcriptPath -Force | Out-Null
try {
    dotnet publish $projectPath --configuration $Configuration --output $publishDir

    $exePath = Join-Path $publishDir 'RemotePropertyWatchdog.Agent.Service.exe'
    $binaryPath = '"{0}" --AgentLifecycle:MarkerPath "{1}"' -f $exePath, $markerPath

    if ($PrepareRebootVerification) {
        Install-AgentService
        Start-Service -Name $ServiceName
        Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 30 | Format-List Name,Status,StartType
        Read-LifecycleMarker -Path $markerPath | ConvertTo-Json -Depth 4 | Add-Content -Path $markerEvidencePath
        Write-Host 'Reboot verification phase 1 complete. Reboot the host, then run this script with -VerifyRebootAndCleanup before user login-dependent validation.'
        return
    }

    if ($VerifyRebootAndCleanup) {
        $service = Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 60
        $service | Format-List Name,Status,StartType
        $marker = Read-LifecycleMarker -Path $markerPath
        Assert-RunningMarker -Marker $marker -Context 'post-reboot' -PreviousSessionId '' -PreviousCleanExpected $true
        $marker | ConvertTo-Json -Depth 4 | Add-Content -Path $markerEvidencePath
        Stop-Service -Name $ServiceName
        Wait-ServiceStatus -Name $ServiceName -Status Stopped -TimeoutSeconds 30 | Out-Null
        sc.exe delete $ServiceName | Out-Null
        Write-Host 'Reboot verification phase 2 complete and service cleanup finished.'
        return
    }

    Install-AgentService
    Start-Service -Name $ServiceName
    Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 30 | Format-List Name,Status,StartType
    $marker = Read-LifecycleMarker -Path $markerPath
    Assert-RunningMarker -Marker $marker -Context 'initial-start' -PreviousSessionId '' -PreviousCleanExpected $true
    $marker | ConvertTo-Json -Depth 4 | Add-Content -Path $markerEvidencePath
    $previousSessionId = [string]$marker.session_id

    for ($i = 1; $i -le $RestartCount; $i++) {
        Restart-Service -Name $ServiceName -Force
        Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 30 | Out-Null
        $marker = Read-LifecycleMarker -Path $markerPath
        Assert-RunningMarker -Marker $marker -Context "restart-$i" -PreviousSessionId $previousSessionId -PreviousCleanExpected $true
        if ([string]$marker.session_id -eq $previousSessionId) {
            throw "restart-$i did not create a new session_id."
        }
        $marker | ConvertTo-Json -Depth 4 | Add-Content -Path $markerEvidencePath
        $previousSessionId = [string]$marker.session_id
        Write-Host "Restart $i/$RestartCount completed with new session $previousSessionId."
    }

    $processId = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").ProcessId
    if (-not $processId) {
        throw 'Unable to locate service process for forced termination validation.'
    }
    Stop-Process -Id $processId -Force
    Wait-ServiceStatus -Name $ServiceName -Status Stopped -TimeoutSeconds 30 | Out-Null
    Start-Service -Name $ServiceName
    Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 30 | Out-Null
    $marker = Read-LifecycleMarker -Path $markerPath
    Assert-RunningMarker -Marker $marker -Context 'forced-termination-restart' -PreviousSessionId $previousSessionId -PreviousCleanExpected $false
    if ([string]$marker.session_id -eq $previousSessionId) {
        throw 'Forced termination restart did not create a new session_id.'
    }
    $marker | ConvertTo-Json -Depth 4 | Add-Content -Path $markerEvidencePath

    Stop-Service -Name $ServiceName
    Wait-ServiceStatus -Name $ServiceName -Status Stopped -TimeoutSeconds 30 | Out-Null
    $marker = Read-LifecycleMarker -Path $markerPath
    Assert-StoppedMarker -Marker $marker
    $marker | ConvertTo-Json -Depth 4 | Add-Content -Path $markerEvidencePath
    sc.exe delete $ServiceName | Out-Null

    Write-Host "R0-003 Windows lifecycle validation passed. Transcript: $transcriptPath"
    Write-Host "Marker evidence: $markerEvidencePath"
}
finally {
    Stop-Transcript | Out-Null
}
