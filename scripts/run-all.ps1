<#
Run both Engine (elevated) and UI (normal) and prepare frontend build.
Usage: Open PowerShell and run: .\scripts\run-all.ps1
#>

# Resolve paths
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$setup = Join-Path $root "setup-dev.ps1"
$runEngine = Join-Path $root "run-engine-elevated.ps1"

Write-Host "Running frontend build and copying dist..."
if (Test-Path $setup) {
    & $setup
} else {
    Write-Warning "setup-dev.ps1 not found. Please build frontend manually (cd GotchaDNS.UI/frontend; npm install; npm run build)"
}

# Start engine elevated
if (Test-Path $runEngine) {
    Write-Host "Starting Engine (elevated)..."
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -NoExit -Command `"cd '$PWD'; .\\scripts\\run-engine-elevated.ps1`""
} else {
    Write-Warning "run-engine-elevated.ps1 not found. Start engine manually with admin privileges: dotnet run --project GotchaDNS.Engine"
}

Start-Sleep -Seconds 2

# Start UI (normal)
Write-Host "Starting UI (non-elevated)..."
Start-Process powershell -ArgumentList "-NoProfile -NoExit -Command `"cd '$PWD'; dotnet run --project GotchaDNS.UI`""

Write-Host "Done. Engine will run in an elevated window. UI will run in another window."
