$ErrorActionPreference = 'Stop'

Write-Host 'Remote Property Watchdog R0 bootstrap'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK is required. Install a supported .NET 10 SDK.'
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Warning 'Docker was not found. PostgreSQL integration tests will be unavailable.'
}

Write-Host (dotnet --version)
Write-Host 'Run Codex on tasks/R0-001-repository-bootstrap.md to create the solution and projects.'
