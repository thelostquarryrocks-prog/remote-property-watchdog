param(
    [string]$Configuration = 'Release',
    [string]$ServiceName = 'RemotePropertyWatchdogAgentR0003',
    [string]$DisplayName = 'Remote Property Watchdog Agent R0-003',
    [int]$RestartCount = 10
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if (-not $IsWindows) {
    Write-Error 'This validation script must be run on Windows with administrative privileges.'
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot 'src/Agent/RemotePropertyWatchdog.Agent.Service/RemotePropertyWatchdog.Agent.Service.csproj'
$publishDir = Join-Path $repoRoot 'artifacts/r0-003/windows-service'
$markerPath = Join-Path $env:ProgramData 'RemotePropertyWatchdog/Agent/r0-003-lifecycle-marker.json'

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $markerPath -Parent) | Out-Null

dotnet publish $projectPath --configuration $Configuration --output $publishDir

$exePath = Join-Path $publishDir 'RemotePropertyWatchdog.Agent.Service.exe'
$binaryPath = '"{0}" --AgentLifecycle:MarkerPath "{1}"' -f $exePath, $markerPath

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName -StartupType Automatic | Out-Null
Start-Service -Name $ServiceName
Start-Sleep -Seconds 5
Get-Service -Name $ServiceName | Format-List Name,Status,StartType
Get-Content -Path $markerPath -Raw

for ($i = 1; $i -le $RestartCount; $i++) {
    Restart-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 3
    $service = Get-Service -Name $ServiceName
    if ($service.Status -ne 'Running') {
        throw "Service did not return to Running during restart $i."
    }
    Write-Host "Restart $i/$RestartCount completed."
}

Stop-Service -Name $ServiceName
Start-Sleep -Seconds 3
Get-Content -Path $markerPath -Raw
sc.exe delete $ServiceName | Out-Null
