#!/usr/bin/env bash
# Beacon — Build and run the app
# Usage:
#   ./scripts/deploy.sh                  # development (default)
#   ./scripts/deploy.sh --development    # development
#   ./scripts/deploy.sh --production     # production

set -euo pipefail

# ── Mode flag ─────────────────────────────────────────────────────────────────
MODE="development"
for arg in "$@"; do
    case "$arg" in
        --production)  MODE="production"  ;;
        --development) MODE="development" ;;
        *) echo "Unknown flag: $arg"; exit 1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BACKEND_DIR="$PROJECT_ROOT/api/FinanceHub.Api"
FRONTEND_DIR="$PROJECT_ROOT/web"
LOCAL_DIR="$PROJECT_ROOT/local"
if [[ "$MODE" == "development" ]]; then
    ENV_FILE="$LOCAL_DIR/environment.dev"
else
    ENV_FILE="$LOCAL_DIR/environment"
fi
INSTALL_DIR="/opt/financehub"
BUILD_DIR="$PROJECT_ROOT/.build"

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }
err()  { echo -e "    ${RED}[ERROR]${NC} $1" >&2; exit 1; }

echo -e "\nFinance Hub — ${MODE^}\n"

# ── Preflight ─────────────────────────────────────────────────────────────────
step "Preflight checks"
[[ -f "$ENV_FILE" ]] || err "Missing $ENV_FILE"
API_KEY=$(grep  '^ApiKey='                                   "$ENV_FILE" | cut -d'=' -f2-)
CONN_STR=$(grep '^ConnectionStrings__DefaultConnection='     "$ENV_FILE" | cut -d'=' -f2-)

if [[ "$MODE" == "production" ]]; then
    [[ -n "$API_KEY"  && "$API_KEY"  != *REPLACE* ]] || err "ApiKey not set in $ENV_FILE"
fi
[[ -n "$CONN_STR" && "$CONN_STR" != *REPLACE* ]] || err "ConnectionStrings not set in $ENV_FILE"

command -v dotnet          &>/dev/null || err "dotnet not found"
command -v node            &>/dev/null || err "node not found — install via nvm"
command -v gnome-terminal  &>/dev/null || err "gnome-terminal not found"

if [[ "$MODE" == "production" ]]; then
    command -v nginx &>/dev/null || err "nginx not found"
fi
ok "All checks passed"

# ── Production-only: permissions, stop running instances, systemd ─────────────
if [[ "$MODE" == "production" ]]; then
    step "Setting local/ permissions"
    sudo chown -R "$(whoami):financehub" "$LOCAL_DIR"
    sudo chmod 750 "$LOCAL_DIR"
    sudo chmod 640 "$ENV_FILE"
    sudo chmod 770 "$LOCAL_DIR/Backups" "$LOCAL_DIR/Statements"
    ok "Permissions set"

    step "Stopping any running instance"
    sudo systemctl stop financehub 2>/dev/null || true
    sudo nginx -s stop            2>/dev/null || true
    sudo mkdir -p "$INSTALL_DIR/wwwroot"
    ok "Cleared"

    sudo sed -i "s|EnvironmentFile=.*|EnvironmentFile=$ENV_FILE|" \
        /etc/systemd/system/financehub.service
    sudo systemctl daemon-reload
fi

# ── Backend terminal script ───────────────────────────────────────────────────
BACKEND_SCRIPT=$(mktemp /tmp/fh-backend-XXXX.sh)
chmod +x "$BACKEND_SCRIPT"
{
    echo "#!/usr/bin/env bash"
    printf 'BACKEND_DIR=%q\n'  "$BACKEND_DIR"
    printf 'BUILD_DIR=%q\n'    "$BUILD_DIR"
    printf 'PROJECT_ROOT=%q\n' "$PROJECT_ROOT"
    printf 'INSTALL_DIR=%q\n'  "$INSTALL_DIR"
    printf 'ENV_FILE=%q\n'     "$ENV_FILE"
    printf 'CONN_STR=%q\n'     "$CONN_STR"
    printf 'MODE=%q\n'         "$MODE"

    if [[ "$MODE" == "production" ]]; then
        cat <<'BODY'

trap 'rm -f "$0"' EXIT

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }

step "Building .NET API"
cd "$BACKEND_DIR"
rm -rf bin obj
dotnet publish \
    -c Release \
    -o "$BUILD_DIR/api" \
    /p:DebugType=None \
    /p:DebugSymbols=false
cp "$PROJECT_ROOT/scripts/pdfExtractor.py" "$BUILD_DIR/api/"
ok "Backend built"

step "Deploying API"
sudo rsync -a --delete --exclude "*.pdb" --exclude "wwwroot" \
    "$BUILD_DIR/api/" "$INSTALL_DIR/"
rm -rf "$BUILD_DIR/api"
sudo chown -R financehub:financehub "$INSTALL_DIR"
ok "Deployed"

step "Running migrations"
cd "$BACKEND_DIR"
if ! dotnet ef --version &>/dev/null 2>&1; then
    dotnet tool install --global dotnet-ef --quiet
    export PATH="$PATH:$HOME/.dotnet/tools"
fi
ConnectionStrings__DefaultConnection="$CONN_STR" dotnet ef database update
ok "Migrations applied"

step "Starting backend"
echo -e "    Loading environment from $ENV_FILE\n"
while IFS= read -r line; do
    [[ -z "$line" || "$line" == \#* ]] && continue
    export "$line"
done < "$ENV_FILE"
echo -e "    \033[0;32m✔ Environment loaded — dotnet starting...\033[0m"
echo -e "    Watch for 'Now listening on: http://0.0.0.0:5000'\n"
dotnet "$INSTALL_DIR/FinanceHub.Api.dll"

echo -e "\nBackend stopped. Press Enter to close."
read -r
BODY
    else
        cat <<'BODY'

trap 'rm -f "$0"' EXIT

GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }

step "Loading environment"
while IFS= read -r line; do
    [[ -z "$line" || "$line" == \#* ]] && continue
    export "$line"
done < "$ENV_FILE"
export ApiKey="${ApiKey:-dev-only-key}"
export ASPNETCORE_ENVIRONMENT=Development
ok "Environment loaded (ApiKey=${ApiKey})"

step "Running migrations"
cd "$BACKEND_DIR"
if ! dotnet ef --version &>/dev/null 2>&1; then
    dotnet tool install --global dotnet-ef --quiet
    export PATH="$PATH:$HOME/.dotnet/tools"
fi
ConnectionStrings__DefaultConnection="$CONN_STR" dotnet ef database update
ok "Migrations applied"

step "Starting .NET API (development)"
echo -e "    API:     http://localhost:5098"
echo -e "    Swagger: http://localhost:5098/swagger\n"
cd "$BACKEND_DIR"
dotnet run

echo -e "\nBackend stopped. Press Enter to close."
read -r
BODY
    fi
} > "$BACKEND_SCRIPT"

# ── Frontend terminal script ──────────────────────────────────────────────────
FRONTEND_SCRIPT=$(mktemp /tmp/fh-frontend-XXXX.sh)
chmod +x "$FRONTEND_SCRIPT"
{
    echo "#!/usr/bin/env bash"
    printf 'FRONTEND_DIR=%q\n' "$FRONTEND_DIR"
    printf 'BUILD_DIR=%q\n'    "$BUILD_DIR"
    printf 'INSTALL_DIR=%q\n'  "$INSTALL_DIR"
    printf 'API_KEY=%q\n'      "$API_KEY"
    printf 'PROD_ENV=%q\n'     "$FRONTEND_DIR/src/environments/environment.prod.ts"
    printf 'MODE=%q\n'         "$MODE"

    if [[ "$MODE" == "production" ]]; then
        cat <<'BODY'

trap 'rm -f "$0"' EXIT

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }

export NVM_DIR="$HOME/.nvm"
[[ -s "$NVM_DIR/nvm.sh" ]] && source "$NVM_DIR/nvm.sh"

step "Building Angular frontend"
sed -i "s/FINANCE_HUB_API_KEY_PLACEHOLDER/$API_KEY/" "$PROD_ENV"
cd "$FRONTEND_DIR"
npx ng build --configuration=production --output-path="$BUILD_DIR/wwwroot"
sed -i "s/$API_KEY/FINANCE_HUB_API_KEY_PLACEHOLDER/" "$PROD_ENV"
ok "Frontend built (placeholder restored)"

step "Deploying frontend"
sudo rsync -a --delete "$BUILD_DIR/wwwroot/" "$INSTALL_DIR/wwwroot/"
rm -rf "$BUILD_DIR/wwwroot"
sudo chown -R financehub:financehub "$INSTALL_DIR/wwwroot"
sudo chmod -R 755 "$INSTALL_DIR/wwwroot"
ok "Deployed"

step "Starting Nginx"
sudo nginx &
sleep 1
if sudo nginx -t 2>/dev/null; then
    echo -e "\n    \033[0;32m✔ Nginx is running\033[0m — frontend live at http://$(tailscale ip -4 2>/dev/null || hostname -I | awk '{print $1}')"
    echo -e "    Silence below = healthy. Errors will appear here.\n"
else
    echo -e "\n    \033[0;31m✘ Nginx failed to start — check config above\033[0m"
fi
sudo nginx -s stop 2>/dev/null || true
sudo nginx -g 'daemon off;'

echo -e "\nNginx stopped. Press Enter to close."
read -r
BODY
    else
        cat <<'BODY'

trap 'rm -f "$0"' EXIT

GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }

export NVM_DIR="$HOME/.nvm"
[[ -s "$NVM_DIR/nvm.sh" ]] && source "$NVM_DIR/nvm.sh"

step "Waiting for backend (port 5098)"
until nc -z 127.0.0.1 5098 2>/dev/null; do
    echo -n "."
    sleep 1
done
echo ""
ok "Backend is ready"

step "Starting Angular dev server"
echo -e "    App: http://localhost:4200"
echo -e "    API requests proxied to http://localhost:5098 via proxy.conf.json\n"
cd "$FRONTEND_DIR"
npx ng serve

echo -e "\nFrontend stopped. Press Enter to close."
read -r
BODY
    fi
} > "$FRONTEND_SCRIPT"

# ── Launch terminals ──────────────────────────────────────────────────────────
step "Launching terminals"
gnome-terminal --title="Beacon — API ($MODE)" -- bash "$BACKEND_SCRIPT"
gnome-terminal --title="Beacon — Web ($MODE)" -- bash "$FRONTEND_SCRIPT"

# wmctrl positions windows by pixel — gnome-terminal ignores +X+Y in --geometry
if command -v wmctrl &>/dev/null; then
    # Use the workarea (screen minus panels) so windows fill edge-to-edge
    WA=$(wmctrl -d | head -1 | grep -oP 'WA: \K\S+ \S+')
    WA_X=$(echo "$WA" | cut -d' ' -f1 | cut -d',' -f1)
    WA_Y=$(echo "$WA" | cut -d' ' -f1 | cut -d',' -f2)
    WA_W=$(echo "$WA" | cut -d' ' -f2 | cut -d'x' -f1)
    WA_H=$(echo "$WA" | cut -d' ' -f2 | cut -d'x' -f2)
    HALF_W=$((WA_W / 2))
    HALF_H=$((WA_H / 2))
    RIGHT_X=$((WA_X + HALF_W))

    # Poll until both windows appear, then grab their IDs
    # (-r title matching fails on em-dash; -i with explicit ID is reliable)
    BACKEND_WID=""; FRONTEND_WID=""
    for _ in {1..40}; do
        [[ -z "$BACKEND_WID" ]]  && BACKEND_WID=$(wmctrl -l  | grep "API ($MODE)"  | tail -1 | awk '{print $1}')
        [[ -z "$FRONTEND_WID" ]] && FRONTEND_WID=$(wmctrl -l | grep "Web ($MODE)" | tail -1 | awk '{print $1}')
        [[ -n "$BACKEND_WID" && -n "$FRONTEND_WID" ]] && break
        sleep 0.05
    done

    if [[ -n "$BACKEND_WID" && -n "$FRONTEND_WID" ]]; then
        # GTK draws invisible shadows outside the visible window border.
        # wmctrl moves the outer frame (incl. shadows), so expand the target rect
        # by the shadow extents and shift origin inward so visible content is flush.
        EXTENTS=$(xprop -id "$BACKEND_WID" _GTK_FRAME_EXTENTS 2>/dev/null | grep -oP '\d+' | tr '\n' ' ')
        SL=0; SR=0; ST=0; SB=0
        if [[ -n "$EXTENTS" ]]; then
            SL=$(echo "$EXTENTS" | awk '{print $1}')
            SR=$(echo "$EXTENTS" | awk '{print $2}')
            ST=$(echo "$EXTENTS" | awk '{print $3}')
            SB=$(echo "$EXTENTS" | awk '{print $4}')
        fi

        wmctrl -i -r "$BACKEND_WID"  -b remove,maximized_vert,maximized_horz
        wmctrl -i -r "$FRONTEND_WID" -b remove,maximized_vert,maximized_horz
        wmctrl -i -r "$BACKEND_WID"  -e "0,$((RIGHT_X - SL)),$((WA_Y - ST)),$((HALF_W + SL + SR)),$((HALF_H + ST + SB))"
        wmctrl -i -r "$FRONTEND_WID" -e "0,$((RIGHT_X - SL)),$((WA_Y + HALF_H - ST)),$((HALF_W + SL + SR)),$((WA_H - HALF_H + ST + SB))"
    fi
else
    ok "wmctrl not found — install it with: sudo apt install wmctrl"
fi

ok "Backend terminal launched"
ok "Frontend terminal launched"

if [[ "$MODE" == "production" ]]; then
    TAILSCALE_IP=$(tailscale ip -4 2>/dev/null || echo "not connected")
    echo -e "
    App:  http://$TAILSCALE_IP
    API:  http://$TAILSCALE_IP/api

    Close the Backend or Web terminal to stop those servers.
"
else
    echo -e "
    App:     http://localhost:4200
    API:     http://localhost:5098
    Swagger: http://localhost:5098/swagger

    Close a terminal to stop that server.
"
fi
