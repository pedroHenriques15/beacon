#Requires -Version 5.1
# Beacon — Seed the BeaconDemo database
# Usage: ./scripts/seed-demo.ps1

$ErrorActionPreference = 'Stop'

$ProjectRoot  = Split-Path -Parent $PSScriptRoot
$EnvFile      = Join-Path $ProjectRoot 'local/environment.demo'
$SqlFile      = Join-Path $PSScriptRoot 'seed-demo.sql'
$RunnerDir    = Join-Path $PSScriptRoot 'SeedRunner'

function Write-Step { param($msg) Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok   { param($msg) Write-Host "    [OK] $msg" -ForegroundColor Green }
function Write-Fail { param($msg) Write-Host ""; Write-Host "[ERROR] $msg" -ForegroundColor Red; exit 1 }

Write-Host ''
Write-Host 'Beacon — Seed demo database' -ForegroundColor Yellow

# ── Read connection string from env file ──────────────────────────────────────
Write-Step 'Reading connection string'

if (-not (Test-Path $EnvFile)) {
    Write-Fail "Missing $EnvFile`n    Create it by copying local/environment.dev and changing Database=Beacon to Database=BeaconDemo"
}

$connStr = $null
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#')) {
        $idx = $line.IndexOf('=')
        if ($idx -gt 0 -and $line.Substring(0, $idx) -eq 'ConnectionStrings__DefaultConnection') {
            $connStr = $line.Substring($idx + 1)
        }
    }
}

if (-not $connStr) { Write-Fail "ConnectionStrings__DefaultConnection not found in $EnvFile" }

$dbName = if ($connStr -match '(?i)Database=([^;]+)') { $Matches[1] } else { '(unknown)' }
Write-Ok "Target database: $dbName"

# ── Drop and recreate database ────────────────────────────────────────────────
Write-Step "Resetting $dbName"
Write-Host "    This will DROP $dbName and reapply all migrations." -ForegroundColor Yellow

$profileHome = if ($env:USERPROFILE) { $env:USERPROFILE } else { $env:HOME }
$tools = Join-Path $profileHome '.dotnet/tools'
if ($env:PATH -notlike "*$tools*") { $env:PATH = "${tools}:$env:PATH" }

$BackendDir = Join-Path $ProjectRoot 'api/Beacon.Api'
Push-Location $BackendDir
try {
    $prevConn = $env:ConnectionStrings__DefaultConnection
    $env:ConnectionStrings__DefaultConnection = $connStr
    $prevEnv  = $env:ASPNETCORE_ENVIRONMENT
    $env:ASPNETCORE_ENVIRONMENT = 'Demo'

    dotnet ef database drop --force 2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet ef database drop failed" }

    dotnet ef database update 2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet ef database update failed" }

    $env:ConnectionStrings__DefaultConnection = $prevConn
    $env:ASPNETCORE_ENVIRONMENT = $prevEnv
} finally {
    Pop-Location
}
Write-Ok 'Database reset and migrations applied'

# ── Build seed runner ─────────────────────────────────────────────────────────
Write-Step 'Building seed runner'

Push-Location $RunnerDir
try {
    dotnet build -c Release --nologo -v q 2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) { Write-Fail "SeedRunner build failed" }
} finally {
    Pop-Location
}
Write-Ok 'Build succeeded'

# ── Run seed ──────────────────────────────────────────────────────────────────
Write-Step "Seeding $dbName"
Write-Host "    SQL file: $SqlFile" -ForegroundColor Gray

Push-Location $RunnerDir
try {
    dotnet run -c Release --no-build -- $connStr $SqlFile
    if ($LASTEXITCODE -ne 0) { Write-Fail "Seed failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

Write-Host ''
Write-Host "Done. $dbName is ready." -ForegroundColor Green
