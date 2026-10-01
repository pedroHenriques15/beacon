#Requires -Version 5.1
# Beacon — Start the .NET API in development mode

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BackendDir  = Join-Path $ProjectRoot 'api/Beacon.Api'
$EnvFile     = Join-Path $ProjectRoot 'local/environment.dev'

function Write-Step { param($msg) Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok   { param($msg) Write-Host "    [OK] $msg" -ForegroundColor Green }

Write-Host ''
Write-Host 'Beacon — Backend (development)'

# ── Load environment ──────────────────────────────────────────────────────────
Write-Step 'Loading environment'

if (-not (Test-Path $EnvFile)) {
    Write-Host "    [ERROR] Missing $EnvFile" -ForegroundColor Red
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
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Write-Ok "Environment loaded (ApiKey=$env:ApiKey)"

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
Write-Step 'Starting .NET API'
Write-Host '    API:     http://localhost:5098'
Write-Host '    Swagger: http://localhost:5098/swagger'
Write-Host ''

Push-Location $BackendDir
dotnet run
Pop-Location
