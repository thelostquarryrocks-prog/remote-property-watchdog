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
$ScheduledTaskName = 'RemotePropertyWatchdog-R0-003-RebootEvidence'

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
        return $null
    }

    $stream = $null
    $reader = $null
    try {
        $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
        $stream = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
        $content = $reader.ReadToEnd()
    }
    catch [System.IO.IOException] {
        return $null
    }
    catch [System.UnauthorizedAccessException] {
        return $null
    }
    finally {
        if ($reader) { $reader.Dispose() }
        elseif ($stream) { $stream.Dispose() }
    }

    if ([string]::IsNullOrWhiteSpace($content)) {
        return $null
    }

    try { $content | ConvertFrom-Json } catch { $null }
}

function Test-LifecycleMarker {
    param(
        [object]$Marker,
        [string]$PreviousSessionId,
        [bool]$PreviousCleanExpected,
        [bool]$RequireNewSession
    )

    if ($null -eq $Marker) { return $false }
    if ([string]::IsNullOrWhiteSpace([string]$Marker.session_id)) { return $false }
    if ($RequireNewSession -and ([string]$Marker.session_id -eq $PreviousSessionId)) { return $false }
    if ($null -eq $Marker.ready_at_utc) { return $false }
    if ($null -eq $Marker.readiness_elapsed_milliseconds) { return $false }
    if ([int64]$Marker.readiness_elapsed_milliseconds -gt 60000) { return $false }
    if ($Marker.planned_shutdown -ne $false) { return $false }
    if ($PreviousSessionId -and ([string]$Marker.previous_session_id -ne $PreviousSessionId)) { return $false }
    if ($Marker.previous_session_ended_cleanly -ne $PreviousCleanExpected) { return $false }
    return $true
}

function Wait-LifecycleMarker {
    param(
        [string]$Path,
        [string]$Context,
        [string]$PreviousSessionId = '',
        [bool]$PreviousCleanExpected = $true,
        [bool]$RequireNewSession = $false,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $marker = Read-LifecycleMarker -Path $Path
        if (Test-LifecycleMarker -Marker $marker -PreviousSessionId $PreviousSessionId -PreviousCleanExpected $PreviousCleanExpected -RequireNewSession $RequireNewSession) {
            return $marker
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    $lastMarker = Read-LifecycleMarker -Path $Path
    $lastJson = if ($null -eq $lastMarker) { '<missing-or-transiently-unreadable>' } else { $lastMarker | ConvertTo-Json -Depth 6 -Compress }
    throw "$Context lifecycle marker did not reach the expected state within $TimeoutSeconds seconds. Last marker: $lastJson"
}

function Assert-StoppedMarker {
    param([object]$Marker)

    if ($null -eq $Marker) { throw 'Planned stop marker was not found.' }
    if ($Marker.planned_shutdown -ne $true) { throw 'Planned stop marker did not record planned_shutdown=true.' }
    if ($null -eq $Marker.stopped_at_utc) { throw 'Planned stop marker did not record stopped_at_utc.' }
}

function Remove-AcceptanceScheduledTask {
    if (Get-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $ScheduledTaskName -Confirm:$false
    }
}

function Cleanup-TestArtifacts {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    try { Wait-ServiceStatus -Name $ServiceName -Status Stopped -TimeoutSeconds 30 | Out-Null } catch { }
    Remove-AcceptanceScheduledTask
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        sc.exe delete $ServiceName | Out-Null
    }
}

function Assert-TestArtifactsAbsent {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        throw "Expected test service '$ServiceName' to be absent before standard acceptance run. Run the documented cleanup command first."
    }
    if (Get-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction SilentlyContinue) {
        throw "Expected test scheduled task '$ScheduledTaskName' to be absent before standard acceptance run. Run the documented cleanup command first."
    }
}

function Archive-AndRemoveStaleMarker {
    if (Test-Path -Path $markerPath) {
        Copy-Item -Path $markerPath -Destination (Join-Path $evidenceDir 'pre-run-lifecycle-marker.json') -Force
        Remove-Item -Path $markerPath -Force
    }
}

function Install-AgentService {
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

function ConvertTo-JsonFile {
    param(
        [object]$InputObject,
        [string]$Path,
        [int]$Depth = 8
    )

    $InputObject | ConvertTo-Json -Depth $Depth | Set-Content -Path $Path -Encoding utf8
}

function Capture-FailureEvidence {
    param(
        [string]$Reason,
        [int]$FailedRestartNumber,
        [string]$PreviousSessionId,
        [string]$ReplacementSessionId,
        [string]$PollingDetails,
        [DateTime]$WindowStartUtc
    )

    $failureDir = Join-Path $evidenceDir 'failure'
    New-Item -ItemType Directory -Force -Path $failureDir | Out-Null
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    $win32Service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue |
        Select-Object State,ExitCode,ServiceSpecificExitCode,ProcessId,StartMode,PathName
    $marker = Read-LifecycleMarker -Path $markerPath
    $serviceProcessId = if ($win32Service) { $win32Service.ProcessId } else { $null }
    $systemEvents = Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Service Control Manager'; StartTime = $WindowStartUtc } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -like "*$ServiceName*" } |
        Select-Object -First 25 TimeCreated,ProviderName,Id,LevelDisplayName,Message
    $applicationEvents = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = @('.NET Runtime','Application Error'); StartTime = $WindowStartUtc } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -like '*RemotePropertyWatchdog.Agent.Service*' -or ($serviceProcessId -and $_.Message -like "*$serviceProcessId*") } |
        Select-Object -First 25 TimeCreated,ProviderName,Id,LevelDisplayName,Message

    $evidence = [ordered]@{
        captured_at_utc = (Get-Date).ToUniversalTime().ToString('o')
        reason = $Reason
        failed_restart_number = $FailedRestartNumber
        previous_session_id = $PreviousSessionId
        replacement_session_id = $ReplacementSessionId
        marker_polling_failure_details = $PollingDetails
        lifecycle_marker = $marker
        service = $service | Select-Object Name,Status,StartType,ServiceType
        win32_service = $win32Service
        service_control_manager_events = $systemEvents
        dotnet_runtime_and_application_error_events = $applicationEvents
        transcript_path = $transcriptPath
    }
    ConvertTo-JsonFile -InputObject $evidence -Path (Join-Path $failureDir 'failure-evidence.json') -Depth 10
    Write-Host "Captured failure evidence under $failureDir"
}

function Install-RebootEvidenceTask {
    $captureScriptPath = Join-Path $rebootEvidenceDir 'capture-reboot-evidence.ps1'
    $captureScript = @'
param(
    [string]$ServiceName,
    [string]$MarkerPath,
    [string]$EvidenceDir
)

$ErrorActionPreference = 'Stop'
function Read-MarkerShared {
    param([string]$Path)
    if (-not (Test-Path -Path $Path)) { return $null }
    $stream = $null
    $reader = $null
    try {
        $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
        $stream = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
        $content = $reader.ReadToEnd()
    }
    catch { return $null }
    finally {
        if ($reader) { $reader.Dispose() }
        elseif ($stream) { $stream.Dispose() }
    }
    if ([string]::IsNullOrWhiteSpace($content)) { return $null }
    try { $content | ConvertFrom-Json } catch { $null }
}
$deadline = (Get-Date).AddSeconds(120)
do {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        $marker = Read-MarkerShared -Path $MarkerPath
        if ($null -ne $marker -and $marker.session_id -and $marker.ready_at_utc -and $null -ne $marker.readiness_elapsed_milliseconds -and $marker.planned_shutdown -eq $false) { break }
    }
    Start-Sleep -Seconds 1
} while ((Get-Date) -lt $deadline)

$service = Get-Service -Name $ServiceName -ErrorAction Stop
$serviceCim = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
$process = if ($serviceCim.ProcessId) { Get-Process -Id $serviceCim.ProcessId -ErrorAction SilentlyContinue } else { $null }
$bootTime = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
$marker = Read-MarkerShared -Path $MarkerPath
$evidence = [ordered]@{
    generated_at_utc = (Get-Date).ToUniversalTime().ToString('o')
    os_boot_time_utc = $bootTime.ToUniversalTime().ToString('o')
    service_name = $ServiceName
    service_status = [string]$service.Status
    service_start_type = [string]$service.StartType
    service_process_id = $serviceCim.ProcessId
    service_process_start_time_utc = if ($process) { $process.StartTime.ToUniversalTime().ToString('o') } else { $null }
    marker = $marker
}
New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
$path = Join-Path $EvidenceDir ('reboot-evidence-{0}.json' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$evidence | ConvertTo-Json -Depth 8 | Set-Content -Path $path -Encoding utf8
'@
    Set-Content -Path $captureScriptPath -Value $captureScript -Encoding utf8
    $argument = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -ServiceName "{1}" -MarkerPath "{2}" -EvidenceDir "{3}"' -f $captureScriptPath, $ServiceName, $markerPath, $rebootEvidenceDir
    $action = New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument $argument
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
    Remove-AcceptanceScheduledTask
    Register-ScheduledTask -TaskName $ScheduledTaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null
}

function Assert-RebootEvidence {
    $evidenceFile = Get-ChildItem -Path $rebootEvidenceDir -Filter 'reboot-evidence-*.json' -ErrorAction Stop | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $evidenceFile) { throw "No reboot evidence file was found in $rebootEvidenceDir." }

    $evidence = Get-Content -Path $evidenceFile.FullName -Raw | ConvertFrom-Json
    $bootTime = [DateTimeOffset]::Parse([string]$evidence.os_boot_time_utc)
    $generatedAt = [DateTimeOffset]::Parse([string]$evidence.generated_at_utc)
    if ($generatedAt -lt $bootTime) { throw "Reboot evidence $($evidenceFile.FullName) was not generated during the current startup." }
    if ([string]$evidence.service_status -ne 'Running') { throw "Reboot evidence service_status is $($evidence.service_status), expected Running." }
    if ([string]$evidence.service_start_type -ne 'Automatic') { throw "Reboot evidence service_start_type is $($evidence.service_start_type), expected Automatic." }
    if ($null -eq $evidence.service_process_start_time_utc) { throw 'Reboot evidence did not include service process start time.' }
    if (-not (Test-LifecycleMarker -Marker $evidence.marker -PreviousSessionId '' -PreviousCleanExpected $true -RequireNewSession $false)) {
        throw "Reboot evidence marker is incomplete or not ready: $($evidence.marker | ConvertTo-Json -Depth 6 -Compress)"
    }

    Write-Host "Verified reboot evidence: $($evidenceFile.FullName)"
    return $evidenceFile.FullName
}

Assert-WindowsAdministrator

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$artifactRoot = Join-Path $repoRoot 'artifacts/r0-003'
$rebootEvidenceDir = Join-Path $artifactRoot 'reboot-evidence'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$evidenceDir = Join-Path $artifactRoot $timestamp
$publishDir = Join-Path $artifactRoot 'windows-service'
$markerPath = Join-Path $env:ProgramData 'RemotePropertyWatchdog/Agent/r0-003-lifecycle-marker.json'
$transcriptPath = Join-Path $evidenceDir 'windows-service-lifecycle-transcript.txt'
$markerEvidencePath = Join-Path $evidenceDir 'markers.jsonl'
$projectPath = Join-Path $repoRoot 'src/Agent/RemotePropertyWatchdog.Agent.Service/RemotePropertyWatchdog.Agent.Service.csproj'

New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
New-Item -ItemType Directory -Force -Path $rebootEvidenceDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $markerPath -Parent) | Out-Null

Start-Transcript -Path $transcriptPath -Force | Out-Null
$acceptanceStartUtc = (Get-Date).ToUniversalTime()
try {
    if ($VerifyRebootAndCleanup) {
        $service = Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 60
        $service | Format-List Name,Status,StartType
        $rebootEvidencePath = Assert-RebootEvidence
        $marker = Wait-LifecycleMarker -Path $markerPath -Context 'post-reboot' -PreviousCleanExpected $true -TimeoutSeconds 60
        $marker | ConvertTo-Json -Depth 6 | Add-Content -Path $markerEvidencePath
        Stop-Service -Name $ServiceName
        Wait-ServiceStatus -Name $ServiceName -Status Stopped -TimeoutSeconds 30 | Out-Null
        Remove-AcceptanceScheduledTask
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Reboot verification phase 2 complete. Evidence: $rebootEvidencePath"
        return
    }

    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
    dotnet publish $projectPath --configuration $Configuration --output $publishDir

    $exePath = Join-Path $publishDir 'RemotePropertyWatchdog.Agent.Service.exe'
    $binaryPath = '"{0}" --AgentLifecycle:MarkerPath "{1}"' -f $exePath, $markerPath

    if ($PrepareRebootVerification) {
        Install-AgentService
        Install-RebootEvidenceTask
        Start-Service -Name $ServiceName
        Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 30 | Format-List Name,Status,StartType
        $marker = Wait-LifecycleMarker -Path $markerPath -Context 'prepare-reboot' -PreviousCleanExpected $true
        $marker | ConvertTo-Json -Depth 6 | Add-Content -Path $markerEvidencePath
        Write-Host 'Reboot verification phase 1 complete. Reboot the host, then run this script with -VerifyRebootAndCleanup. Manual cleanup if interrupted:'
        Write-Host "Unregister-ScheduledTask -TaskName '$ScheduledTaskName' -Confirm:`$false; Stop-Service -Name '$ServiceName' -ErrorAction SilentlyContinue; sc.exe delete '$ServiceName'"
        return
    }

    Assert-TestArtifactsAbsent
    Archive-AndRemoveStaleMarker
    Install-AgentService
    Start-Service -Name $ServiceName
    Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 30 | Format-List Name,Status,StartType
    $marker = Wait-LifecycleMarker -Path $markerPath -Context 'initial-start' -PreviousCleanExpected $true
    $marker | ConvertTo-Json -Depth 6 | Add-Content -Path $markerEvidencePath
    $previousSessionId = [string]$marker.session_id

    for ($i = 1; $i -le $RestartCount; $i++) {
        try {
            Restart-Service -Name $ServiceName -Force
            Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 30 | Out-Null
            $marker = Wait-LifecycleMarker -Path $markerPath -Context "restart-$i" -PreviousSessionId $previousSessionId -PreviousCleanExpected $true -RequireNewSession $true
            $marker | ConvertTo-Json -Depth 6 | Add-Content -Path $markerEvidencePath
            $previousSessionId = [string]$marker.session_id
            Write-Host "Restart $i/$RestartCount completed with new session $previousSessionId."
        }
        catch {
            $replacement = Read-LifecycleMarker -Path $markerPath
            Capture-FailureEvidence -Reason 'planned-restart-failed-before-readiness' -FailedRestartNumber $i -PreviousSessionId $previousSessionId -ReplacementSessionId ([string]$replacement.session_id) -PollingDetails $_.Exception.Message -WindowStartUtc $acceptanceStartUtc
            Cleanup-TestArtifacts
            throw
        }
    }

    $processId = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").ProcessId
    if (-not $processId) { throw 'Unable to locate service process for forced termination validation.' }
    Stop-Process -Id $processId -Force
    Wait-ServiceStatus -Name $ServiceName -Status Stopped -TimeoutSeconds 30 | Out-Null
    Start-Service -Name $ServiceName
    Wait-ServiceStatus -Name $ServiceName -Status Running -TimeoutSeconds 30 | Out-Null
    $marker = Wait-LifecycleMarker -Path $markerPath -Context 'forced-termination-restart' -PreviousSessionId $previousSessionId -PreviousCleanExpected $false -RequireNewSession $true
    $marker | ConvertTo-Json -Depth 6 | Add-Content -Path $markerEvidencePath

    Stop-Service -Name $ServiceName
    Wait-ServiceStatus -Name $ServiceName -Status Stopped -TimeoutSeconds 30 | Out-Null
    $marker = Read-LifecycleMarker -Path $markerPath
    Assert-StoppedMarker -Marker $marker
    $marker | ConvertTo-Json -Depth 6 | Add-Content -Path $markerEvidencePath
    Remove-AcceptanceScheduledTask
    sc.exe delete $ServiceName | Out-Null

    Write-Host "R0-003 Windows lifecycle validation passed. Transcript: $transcriptPath"
    Write-Host "Marker evidence: $markerEvidencePath"
}
finally {
    Stop-Transcript | Out-Null
}
