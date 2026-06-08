#!/usr/bin/env bash
# Beacon — Drop and recreate the database from EF Core migrations
# Usage: ./scripts/reset-db.sh [--force]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BACKEND_DIR="$PROJECT_ROOT/api/Beacon.Api"

RED='\033[0;31m'; CYAN='\033[0;36m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }
err()  { echo -e "\n${RED}[ERROR]${NC} $1" >&2; exit 1; }

FORCE=0
for arg in "$@"; do
    [[ "$arg" == "--force" ]] && FORCE=1
done

echo ""
echo -e "${YELLOW}Beacon — Database Reset${NC}"
echo -e "${YELLOW}This will DROP the Beacon database and recreate it from migrations.${NC}"
echo -e "${YELLOW}All data (statements, transactions, categories, rules) will be lost.${NC}"

if [[ "$FORCE" -eq 0 ]]; then
    read -rp $'\nType \'yes\' to continue: ' answer
    if [[ "$answer" != "yes" ]]; then
        echo "Aborted."
        exit 0
    fi
fi

step "Checking prerequisites"

command -v dotnet &>/dev/null || err "dotnet not found. Install .NET 8 SDK."
ok "dotnet found"

command -v dotnet-ef &>/dev/null || dotnet tool run dotnet-ef --version &>/dev/null 2>&1 || \
    dotnet ef --version &>/dev/null 2>&1 || err "dotnet-ef not found. Run: dotnet tool install --global dotnet-ef"
ok "dotnet-ef found"

step "Reading connection string"

APP_SETTINGS="$BACKEND_DIR/appsettings.json"
[[ -f "$APP_SETTINGS" ]] || err "appsettings.json not found at: $APP_SETTINGS\n    Copy api/Beacon.Api/appsettings.template.json to appsettings.json and fill in your connection string, API key, and Python script path."

CONN_STR="$(python3 -c "
import json, sys
cfg = json.load(open('$APP_SETTINGS'))
print(cfg['ConnectionStrings']['DefaultConnection'])
" 2>/dev/null)" || err "Could not parse appsettings.json"

[[ -n "$CONN_STR" ]] || err "ConnectionStrings.DefaultConnection is empty in appsettings.json"

DB_NAME="$(echo "$CONN_STR" | grep -oP '(?i)(?<=Database=)[^;]+')" \
    || err "Could not parse database name from connection string"

ok "Target database: $DB_NAME"

step "Building project"

( cd "$BACKEND_DIR" && dotnet build -c Release --nologo -v q ) \
    || err "Build failed — fix compilation errors before resetting the database"
ok "Build succeeded"

step "Dropping database '$DB_NAME'"

( cd "$BACKEND_DIR" && dotnet ef database drop --force --no-build ) \
    || err "dotnet ef database drop failed"
ok "Database dropped"

step "Applying migrations to fresh database"

( cd "$BACKEND_DIR" && dotnet ef database update --no-build ) \
    || err "dotnet ef database update failed"
ok "All migrations applied"

echo -e "\n${GREEN}Database reset complete.${NC}"
