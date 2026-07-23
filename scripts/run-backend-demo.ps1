#Requires -Version 5.1
# Beacon — Start the .NET API against the demo database (BeaconDemo)

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BackendDir  = Join-Path $ProjectRoot 'api/Beacon.Api'
$EnvFile     = Join-Path $ProjectRoot 'local/environment.demo'

function Write-Step { param($msg) Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok   { param($msg) Write-Host "    [OK] $msg" -ForegroundColor Green }

Write-Host ''
Write-Host 'Beacon — Backend (demo database)' -ForegroundColor Yellow

# ── Load environment ──────────────────────────────────────────────────────────
Write-Step 'Loading environment'

if (-not (Test-Path $EnvFile)) {
    Write-Host "    [ERROR] Missing $EnvFile" -ForegroundColor Red
    Write-Host "    Create local/environment.demo by copying local/environment.dev" -ForegroundColor Gray
    Write-Host "    and changing Database=Beacon to Database=BeaconDemo" -ForegroundColor Gray
    exit 1
}

Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#')) {
        $idx = $line.IndexOf('=')
        if ($idx -gt 0) {
            [System.Environment]::SetEnvironmentVariable(
                $line.Substring(0, $idx),
                $line.Substring($idx + 1),
                'Process'
            )
        }
    }
}
if (-not $env:ApiKey) { $env:ApiKey = 'dev-only-key' }
$env:ASPNETCORE_ENVIRONMENT = 'Demo'
Write-Ok "Environment loaded (ApiKey=$env:ApiKey, DB=BeaconDemo)"

# ── Migrations ────────────────────────────────────────────────────────────────
Write-Step 'Running migrations'

Push-Location $BackendDir
try {
    $profileHome = if ($env:USERPROFILE) { $env:USERPROFILE } else { $env:HOME }
    $tools = Join-Path $profileHome '.dotnet/tools'
    if ($env:PATH -notlike "*$tools*") { $env:PATH = "${tools}:$env:PATH" }

    if (-not (Get-Command dotnet-ef -ErrorAction SilentlyContinue)) {
        dotnet tool install --global dotnet-ef -v quiet
    }

    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed (exit $LASTEXITCODE)" }
    dotnet ef database update
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update failed (exit $LASTEXITCODE)" }
    Write-Ok 'Migrations applied'
} finally {
    Pop-Location
}

# ── Start API ─────────────────────────────────────────────────────────────────
Write-Step 'Starting .NET API (demo database)'
Write-Host '    API:     http://localhost:5098'
Write-Host '    Swagger: http://localhost:5098/swagger'
Write-Host ''

Push-Location $BackendDir
dotnet run
Pop-Location
