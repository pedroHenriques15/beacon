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
SQLCMD="$(command -v sqlcmd 2>/dev/null || echo /opt/mssql-tools18/bin/sqlcmd)"
[[ -x "$SQLCMD" ]] || err "sqlcmd not found. Install mssql-tools18: https://learn.microsoft.com/sql/linux/sql-server-linux-setup-tools"
[[ -f "$SQL_FILE" ]] || err "seed-demo.sql not found at: $SQL_FILE"
[[ -f "$ENV_FILE" ]] || err "Environment file not found at: $ENV_FILE"

# ── Parse connection string ────────────────────────────────────────────────────
step "Reading connection string"

CONN_STR="$(sudo grep -oP '(?<=ConnectionStrings__DefaultConnection=).+' "$ENV_FILE")" \
    || err "Could not read ConnectionStrings__DefaultConnection from $ENV_FILE"

DB_SERVER="$(echo "$CONN_STR" | grep -oP '(?i)(?<=Server=)[^;]+')"   || err "Could not parse Server from connection string"
DB_NAME="$(echo "$CONN_STR"   | grep -oP '(?i)(?<=Database=)[^;]+')" || err "Could not parse Database from connection string"
DB_USER="$(echo "$CONN_STR"   | grep -oP '(?i)(?<=User Id=)[^;]+')"  || err "Could not parse User Id from connection string"
DB_PASS="$(echo "$CONN_STR"   | grep -oP '(?i)(?<=Password=)[^;]+')" || err "Could not parse Password from connection string"

ok "Target: $DB_NAME on $DB_SERVER"

# ── Run seed ──────────────────────────────────────────────────────────────────
step "Running seed-demo.sql"

"$SQLCMD" -S "$DB_SERVER" -d "$DB_NAME" -U "$DB_USER" -P "$DB_PASS" -C -i "$SQL_FILE" -b \
    || err "Seed failed — check the errors above"

echo -e "\n${GREEN}Demo data seeded successfully.${NC}"
