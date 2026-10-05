#Requires -Version 5.1
# Beacon — Start the .NET API against the demo database (local/beacon-demo.db)

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
    Write-Host "    (the database, uploads and backups are always local/beacon-demo.db," -ForegroundColor Gray
    Write-Host "    local/uploads-demo, local/backups-demo and local/logs-demo)" -ForegroundColor Gray
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

# The demo database references no stored PDF, so it must never share a folder with real
# uploads: the startup cleanup would treat them all as orphans. Whatever the environment file
# says, the demo keeps its database, uploads, backups and logs in files and folders of its own.
$env:ConnectionStrings__DefaultConnection = "Data Source=$(Join-Path $ProjectRoot 'local/beacon-demo.db')"
$env:Storage__Path = Join-Path $ProjectRoot 'local/uploads-demo'
$env:Backup__Path  = Join-Path $ProjectRoot 'local/backups-demo'
$env:Logs__Path    = Join-Path $ProjectRoot 'local/logs-demo'
New-Item -ItemType Directory -Force -Path $env:Storage__Path, $env:Backup__Path | Out-Null
Write-Ok "Environment loaded (ApiKey=$env:ApiKey)"
Write-Ok 'Demo files: local/beacon-demo.db, local/uploads-demo, local/backups-demo, local/logs-demo'

# ── Migrations ────────────────────────────────────────────────────────────────
Write-Step 'Running migrations'

Push-Location $BackendDir
try {
    # dotnet-ef is pinned in dotnet-tools.json at the repository root.
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (exit $LASTEXITCODE)" }

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
