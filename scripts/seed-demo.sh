#!/usr/bin/env bash
# Beacon — Seed the database with demo data from seed-demo.sql
# Usage: ./scripts/seed-demo.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
SQL_FILE="$SCRIPT_DIR/seed-demo.sql"

RED='\033[0;31m'; CYAN='\033[0;36m'; GREEN='\033[0;32m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }
err()  { echo -e "\n${RED}[ERROR]${NC} $1" >&2; exit 1; }

ENV_FILE="/etc/beacon/environment"

# ── Prerequisites ─────────────────────────────────────────────────────────────
command -v dotnet &>/dev/null || err "dotnet not found. Install the .NET 10 SDK."
[[ -f "$SQL_FILE" ]] || err "seed-demo.sql not found at: $SQL_FILE"
[[ -f "$ENV_FILE" ]] || err "Environment file not found at: $ENV_FILE"

# ── Read connection string ────────────────────────────────────────────────────
step "Reading connection string"

CONN_STR="$(sudo grep -oP '(?<=ConnectionStrings__DefaultConnection=).+' "$ENV_FILE")" \
    || err "Could not read ConnectionStrings__DefaultConnection from $ENV_FILE"

ok "Target: $CONN_STR"

# ── Run seed ──────────────────────────────────────────────────────────────────
step "Running seed-demo.sql"

( cd "$SCRIPT_DIR/SeedRunner" && dotnet run -c Release -- "$CONN_STR" "$SQL_FILE" ) \
    || err "Seed failed — check the errors above"

echo -e "\n${GREEN}Demo data seeded successfully.${NC}"
