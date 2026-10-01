#Requires -Version 5.1
# Beacon — Seed the demo database (local/beacon-demo.db, as run-backend-demo.ps1 uses)
# Usage: ./scripts/seed-demo.ps1

$ErrorActionPreference = 'Stop'

$ProjectRoot  = Split-Path -Parent $PSScriptRoot
$DbFile       = Join-Path $ProjectRoot 'local/beacon-demo.db'
$SqlFile      = Join-Path $PSScriptRoot 'seed-demo.sql'
$RunnerDir    = Join-Path $PSScriptRoot 'SeedRunner'

function Write-Step { param($msg) Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok   { param($msg) Write-Host "    [OK] $msg" -ForegroundColor Green }
function Write-Fail { param($msg) Write-Host ""; Write-Host "[ERROR] $msg" -ForegroundColor Red; exit 1 }

Write-Host ''
Write-Host 'Beacon — Seed demo database' -ForegroundColor Yellow

$connStr = "Data Source=$DbFile"
$dbName  = 'local/beacon-demo.db'

# ── Drop and recreate database ────────────────────────────────────────────────
Write-Step "Resetting $dbName"
Write-Host "    This will DROP $dbName and reapply all migrations." -ForegroundColor Yellow

$BackendDir = Join-Path $ProjectRoot 'api/Beacon.Api'
Push-Location $BackendDir
try {
    # dotnet-ef is pinned in dotnet-tools.json at the repository root.
    dotnet tool restore 2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet tool restore failed" }

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
