#!/bin/sh
# Beacon: one-time setup of a clone. Enables the git hooks in .githooks/ (CLAUDE.md,
# "Git workflow"): commit subjects, protected branches and task branches.
set -e

cd "$(dirname "$0")/.."
git config core.hooksPath .githooks
echo "Git hooks enabled: core.hooksPath = $(git config core.hooksPath)"
