$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true


python ./scripts/validate-kickoff.py
python ./scripts/scan-secrets.py

if (Test-Path './RemotePropertyWatchdog.sln') {
    dotnet restore ./RemotePropertyWatchdog.sln
    dotnet build ./RemotePropertyWatchdog.sln --configuration Release --no-restore
    dotnet test ./RemotePropertyWatchdog.sln --configuration Release --no-build
} elseif (Test-Path './RemotePropertyWatchdog.slnx') {
    dotnet restore ./RemotePropertyWatchdog.slnx
    dotnet build ./RemotePropertyWatchdog.slnx --configuration Release --no-restore
    dotnet test ./RemotePropertyWatchdog.slnx --configuration Release --no-build
} else {
    Write-Host 'No .NET solution exists yet. Kickoff package validation passed; R0-001 must create the solution.'
}
