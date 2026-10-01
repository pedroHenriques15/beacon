#Requires -Version 5.1
# Beacon — Start the Angular dev server

$ErrorActionPreference = 'Stop'

$ProjectRoot  = Split-Path -Parent $PSScriptRoot
$FrontendDir  = Join-Path $ProjectRoot 'web'

function Write-Step { param($msg) Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok   { param($msg) Write-Host "    [OK] $msg" -ForegroundColor Green }

Write-Host ''
Write-Host 'Beacon — Frontend (development)'

# ── Wait for backend ──────────────────────────────────────────────────────────
Write-Step 'Waiting for backend (port 5098)'

while ($true) {
    $tcp = New-Object System.Net.Sockets.TcpClient
    try {
        $tcp.Connect('127.0.0.1', 5098)
        $tcp.Close()
        break
    } catch {
        Write-Host -NoNewline '.'
        Start-Sleep -Seconds 1
    }
}
Write-Host ''
Write-Ok 'Backend is ready'

# ── Start Angular ─────────────────────────────────────────────────────────────
Write-Step 'Starting Angular dev server'
Write-Host '    App: http://localhost:4200'
Write-Host '    API proxied to http://localhost:5098'
Write-Host ''

Push-Location $FrontendDir
npx ng serve
Pop-Location
