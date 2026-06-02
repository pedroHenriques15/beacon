#Requires -Version 5.1
param([switch]$Force)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BackendDir = Join-Path $ProjectRoot "api\FinanceHub.Api"

function Write-Step { param($msg) Write-Host "" ; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok { param($msg) Write-Host "    [OK] $msg" -ForegroundColor Green }
function Write-Fail { param($msg) Write-Host "" ; Write-Host "[ERROR] $msg" -ForegroundColor Red ; exit 1 }

Write-Host ""
Write-Host "Finance Hub - Database Reset" -ForegroundColor White
Write-Host "This will DROP the FinanceHub database and recreate it from migrations." -ForegroundColor Yellow
Write-Host "All data (statements, transactions, categories, rules) will be lost." -ForegroundColor Yellow

if (-not $Force) {
    $answer = Read-Host "`nType 'yes' to continue"
    if ($answer -ne "yes") {
        Write-Host "Aborted." -ForegroundColor Gray
        exit 0
    }
}

Write-Step "Checking prerequisites"

if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Fail "dotnet not found. Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8"
}
Write-Ok "dotnet found"

$null = dotnet ef --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "dotnet-ef not found. Run: dotnet tool install --global dotnet-ef"
}
Write-Ok "dotnet-ef found"

Write-Step "Reading connection string"

$appSettings = Join-Path $BackendDir "appsettings.json"
if (-not (Test-Path $appSettings)) {
    Write-Fail "appsettings.json not found.`n    Copy api\FinanceHub.Api\appsettings.template.json to appsettings.json and fill in your connection string, API key, and Python script path."
}

$config = Get-Content $appSettings -Raw | ConvertFrom-Json
$connStr = $config.ConnectionStrings.DefaultConnection

if ([string]::IsNullOrWhiteSpace($connStr)) {
    Write-Fail "ConnectionStrings.DefaultConnection is empty in appsettings.json"
}

if ($connStr -match '(?i)(?:Database|Initial\s+Catalog)=([^;]+)') {
    $dbName = $Matches[1].Trim()
}
else {
    Write-Fail "Could not parse database name from connection string"
}

Write-Ok "Target database: $dbName"

Write-Step "Building project"

Push-Location $BackendDir
try {
    $buildOutput = dotnet build -c Release --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $buildOutput
        Write-Fail "Build failed - fix compilation errors before resetting the database"
    }
    Write-Ok "Build succeeded"
}
finally {
    Pop-Location
}

Write-Step "Dropping database '$dbName'"

Push-Location $BackendDir
try {
    $prevDiag = [Environment]::GetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics', 'Process')
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics', 'Critical', 'Process')

    $dropOutput = dotnet ef database drop --force --no-build 2>&1
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics', $prevDiag, 'Process')
    if ($LASTEXITCODE -ne 0) {
        Write-Host $dropOutput -ForegroundColor Gray
        Write-Fail "dotnet ef database drop failed (exit $LASTEXITCODE)"
    }
    Write-Ok "Database dropped"
}
finally {
    Pop-Location
}

Write-Step "Applying migrations to fresh database"

Push-Location $BackendDir
try {
    $prevDiag = [Environment]::GetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics', 'Process')
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics', 'Critical', 'Process')

    $updateOutput = dotnet ef database update --no-build 2>&1
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics', $prevDiag, 'Process')
    if ($LASTEXITCODE -ne 0) {
        Write-Host $updateOutput -ForegroundColor Gray
        Write-Fail "dotnet ef database update failed (exit $LASTEXITCODE)"
    }
    Write-Ok "All migrations applied"
}
finally {
    Pop-Location
}