#Requires -Version 5.1
# Beacon: one-time setup of a clone. Enables the git hooks in .githooks/ (CLAUDE.md,
# "Git workflow"): commit subjects, protected branches and task branches. Git for Windows
# runs the hooks through its own sh.

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot

git -C $ProjectRoot config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw "git config failed (exit $LASTEXITCODE)" }

Write-Host "Git hooks enabled: core.hooksPath = $(git -C $ProjectRoot config core.hooksPath)" -ForegroundColor Green
