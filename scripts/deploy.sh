#!/usr/bin/env bash
# Beacon - Build and run the app
# Usage:
#   ./scripts/deploy.sh                  # development (default)
#   ./scripts/deploy.sh --development    # development
#   ./scripts/deploy.sh --production     # production (headless-safe)
#   ./scripts/deploy.sh --rollback       # restore the previous production release

set -euo pipefail

MODE="development"
for arg in "$@"; do
    case "$arg" in
        --production)  MODE="production"  ;;
        --development) MODE="development" ;;
        --rollback)    MODE="rollback"    ;;
        *) echo "Unknown flag: $arg"; exit 1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BACKEND_DIR="$PROJECT_ROOT/api/Beacon.Api"
FRONTEND_DIR="$PROJECT_ROOT/web"
LOCAL_DIR="$PROJECT_ROOT/local"
if [[ "$MODE" == "development" ]]; then
    ENV_FILE="$LOCAL_DIR/environment.dev"
else
    ENV_FILE="$LOCAL_DIR/environment"
fi
INSTALL_DIR="/opt/beacon"
PREV_DIR="/opt/beacon.prev"
BUILD_DIR="$PROJECT_ROOT/.build"
PLACEHOLDER="FINANCE_HUB_API_KEY_PLACEHOLDER"

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }
err()  { echo -e "    ${RED}[ERROR]${NC} $1" >&2; exit 1; }

probe_port() {
    local urls
    urls=$(grep '^ASPNETCORE_URLS=' "$ENV_FILE" | cut -d'=' -f2- | tr -d '\r' || true)
    if [[ "$urls" =~ :([0-9]+) ]]; then
        echo "${BASH_REMATCH[1]}"
    else
        echo "5000"
    fi
}

restart_services() {
    sudo systemctl daemon-reload
    sudo systemctl restart beacon

    local port
    port=$(probe_port)
    step "Verifying backend on :$port"
    for _ in {1..30}; do
        code=$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$port/api/statements" 2>/dev/null || true)
        [[ "$code" == "401" || "$code" == "200" ]] && break
        sleep 1
    done
    [[ "$code" == "401" || "$code" == "200" ]] \
        || err "Backend not answering on :$port (last status: ${code:-none}). If the service binds a different port, set ASPNETCORE_URLS in $ENV_FILE. Logs: journalctl -u beacon -e"
    ok "Backend answering on :$port (HTTP $code)"

    step "Restarting Nginx"
    sudo nginx -t
    if systemctl list-unit-files nginx.service &>/dev/null; then
        sudo systemctl restart nginx
    else
        sudo nginx -s stop 2>/dev/null || true
        sudo nginx
    fi
    ok "Nginx running"
}

# ── Rollback ──────────────────────────────────────────────────────────────────
if [[ "$MODE" == "rollback" ]]; then
    echo -e "\nBeacon - Rollback\n"
    [[ -d "$PREV_DIR" ]] || err "No previous release found at $PREV_DIR"
    step "Restoring previous release"
    sudo rsync -a --delete "$PREV_DIR/" "$INSTALL_DIR/"
    sudo chown -R beacon:beacon "$INSTALL_DIR"
    ok "Previous release restored"
    restart_services
    echo -e "\n    Rolled back. Note: database migrations are NOT reverted automatically."
    exit 0
fi

echo -e "\nBeacon - ${MODE^}\n"

# ── Preflight ─────────────────────────────────────────────────────────────────
step "Preflight checks"
[[ -f "$ENV_FILE" ]] || err "Missing $ENV_FILE"
API_KEY=$(grep  '^ApiKey='                               "$ENV_FILE" | cut -d'=' -f2- | tr -d '\r' || true)
CONN_STR=$(grep '^ConnectionStrings__DefaultConnection=' "$ENV_FILE" | cut -d'=' -f2- | tr -d '\r' || true)

if [[ "$MODE" == "production" ]]; then
    [[ -n "$API_KEY"  && "$API_KEY"  != *REPLACE* ]] || err "ApiKey not set in $ENV_FILE"
fi
[[ -n "$CONN_STR" && "$CONN_STR" != *REPLACE* ]] || err "ConnectionStrings not set in $ENV_FILE"

command -v dotnet &>/dev/null || err "dotnet not found"
command -v node   &>/dev/null || err "node not found - install via nvm"

if [[ "$MODE" == "production" ]]; then
    command -v nginx &>/dev/null || err "nginx not found"
    command -v rsync &>/dev/null || err "rsync not found"
    [[ -f /etc/systemd/system/beacon.service ]] || err "systemd unit /etc/systemd/system/beacon.service not found"
else
    command -v gnome-terminal &>/dev/null || err "gnome-terminal not found (development mode opens terminals)"
fi
ok "All checks passed"

# ══════════════════════════════════════════════════════════════════════════════
# PRODUCTION - sequential, headless-safe, fail-loud (set -e applies throughout).
# Both artifacts are built BEFORE the live service is touched.
# ══════════════════════════════════════════════════════════════════════════════
if [[ "$MODE" == "production" ]]; then
    step "Building .NET API"
    rm -rf "$BUILD_DIR"
    cd "$BACKEND_DIR"
    rm -rf bin obj
    dotnet publish -c Release -o "$BUILD_DIR/api" /p:DebugType=None /p:DebugSymbols=false
    cp "$PROJECT_ROOT/scripts/pdfExtractor.py" "$BUILD_DIR/api/"
    ok "Backend built"

    step "Building Angular frontend"
    export NVM_DIR="$HOME/.nvm"
    [[ -s "$NVM_DIR/nvm.sh" ]] && source "$NVM_DIR/nvm.sh"
    cd "$FRONTEND_DIR"
    npx ng build --configuration=production --output-path="$BUILD_DIR/wwwroot"
    ok "Frontend built"

    step "Injecting API key into built bundle"
    ESCAPED_KEY=$(printf '%s' "$API_KEY" | sed -e 's/[&|\\]/\\&/g')
    mapfile -t KEY_FILES < <(grep -rl "$PLACEHOLDER" "$BUILD_DIR/wwwroot" || true)
    [[ ${#KEY_FILES[@]} -gt 0 ]] \
        || err "Placeholder '$PLACEHOLDER' not found in the built bundle - check environment.prod.ts"
    for f in "${KEY_FILES[@]}"; do
        sed -i "s|$PLACEHOLDER|$ESCAPED_KEY|g" "$f"
    done
    grep -rq "$PLACEHOLDER" "$BUILD_DIR/wwwroot" \
        && err "Placeholder still present after injection - aborting"
    ok "Key injected into ${#KEY_FILES[@]} file(s); tracked sources untouched"

    step "Running migrations"
    cd "$BACKEND_DIR"
    if ! dotnet ef --version &>/dev/null; then
        dotnet tool install --global dotnet-ef
        export PATH="$PATH:$HOME/.dotnet/tools"
    fi
    ConnectionStrings__DefaultConnection="$CONN_STR" dotnet ef database update
    ok "Migrations applied"

    step "Setting local/ permissions"
    sudo chown -R "$(whoami):beacon" "$LOCAL_DIR"
    sudo chmod 750 "$LOCAL_DIR"
    sudo chmod 640 "$ENV_FILE"
    sudo mkdir -p "$LOCAL_DIR/Backups" "$LOCAL_DIR/Statements"
    sudo chmod 770 "$LOCAL_DIR/Backups" "$LOCAL_DIR/Statements"
    ok "Permissions set"

    step "Snapshotting current release"
    if [[ -d "$INSTALL_DIR" ]] && [[ -n "$(sudo ls -A "$INSTALL_DIR" 2>/dev/null)" ]]; then
        sudo rsync -a --delete "$INSTALL_DIR/" "$PREV_DIR/"
        ok "Previous release kept at $PREV_DIR (restore with: ./scripts/deploy.sh --rollback)"
    else
        ok "No existing release to snapshot"
    fi

    grep -q '^EnvironmentFile=' /etc/systemd/system/beacon.service \
        || err "beacon.service has no EnvironmentFile= line to update"

    trap 'echo -e "\n    Deploy failed after the service was stopped. Recover with: ./scripts/deploy.sh --rollback" >&2' ERR

    step "Stopping service"
    sudo systemctl stop beacon 2>/dev/null || true
    ok "Stopped"

    step "Deploying"
    sudo mkdir -p "$INSTALL_DIR/wwwroot"
    sudo rsync -a --delete --exclude "*.pdb" --exclude "wwwroot" "$BUILD_DIR/api/" "$INSTALL_DIR/"
    sudo rsync -a --delete "$BUILD_DIR/wwwroot/" "$INSTALL_DIR/wwwroot/"
    sudo chown -R beacon:beacon "$INSTALL_DIR"
    sudo chmod -R 755 "$INSTALL_DIR/wwwroot"
    rm -rf "$BUILD_DIR"
    ok "Deployed to $INSTALL_DIR"

    sudo sed -i "s|EnvironmentFile=.*|EnvironmentFile=$ENV_FILE|" /etc/systemd/system/beacon.service

    restart_services
    trap - ERR

    TAILSCALE_IP=$(tailscale ip -4 2>/dev/null || hostname -I | awk '{print $1}')
    echo -e "
    App:  http://$TAILSCALE_IP
    API:  http://$TAILSCALE_IP/api
    Logs: journalctl -u beacon -f
    Roll back: ./scripts/deploy.sh --rollback
"
    exit 0
fi

# ══════════════════════════════════════════════════════════════════════════════
# DEVELOPMENT - two terminals (API + Web), tiled with wmctrl when available.
# ══════════════════════════════════════════════════════════════════════════════

BACKEND_SCRIPT=$(mktemp /tmp/beacon-backend-XXXX.sh)
chmod +x "$BACKEND_SCRIPT"
{
    echo "#!/usr/bin/env bash"
    echo "set -uo pipefail"
    printf 'BACKEND_DIR=%q\n' "$BACKEND_DIR"
    printf 'ENV_FILE=%q\n'    "$ENV_FILE"
    printf 'CONN_STR=%q\n'    "$CONN_STR"
    cat <<'BODY'

trap 'rm -f "$0"' EXIT

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }
fail() { echo -e "    ${RED}[ERROR]${NC} $1" >&2; read -r -p "Press Enter to close."; exit 1; }

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
if ! dotnet ef --version &>/dev/null; then
    dotnet tool install --global dotnet-ef || fail "Could not install dotnet-ef"
    export PATH="$PATH:$HOME/.dotnet/tools"
fi
ConnectionStrings__DefaultConnection="$CONN_STR" dotnet ef database update \
    || fail "Migrations failed - not starting the API against a stale schema"
ok "Migrations applied"

step "Starting .NET API (development)"
echo -e "    API:     http://localhost:5098"
echo -e "    Swagger: http://localhost:5098/swagger\n"
dotnet run

echo -e "\nBackend stopped. Press Enter to close."
read -r
BODY
} > "$BACKEND_SCRIPT"

FRONTEND_SCRIPT=$(mktemp /tmp/beacon-frontend-XXXX.sh)
chmod +x "$FRONTEND_SCRIPT"
{
    echo "#!/usr/bin/env bash"
    echo "set -uo pipefail"
    printf 'FRONTEND_DIR=%q\n' "$FRONTEND_DIR"
    cat <<'BODY'

trap 'rm -f "$0"' EXIT

GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}[OK]${NC} $1"; }

export NVM_DIR="$HOME/.nvm"
[[ -s "$NVM_DIR/nvm.sh" ]] && source "$NVM_DIR/nvm.sh"

step "Waiting for backend (port 5098)"
until (exec 3<>/dev/tcp/127.0.0.1/5098) 2>/dev/null; do
    echo -n "."
    sleep 1
done
exec 3>&- 2>/dev/null || true
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
} > "$FRONTEND_SCRIPT"

step "Launching terminals"
gnome-terminal --title="Beacon - API ($MODE)" -- bash "$BACKEND_SCRIPT"
gnome-terminal --title="Beacon - Web ($MODE)" -- bash "$FRONTEND_SCRIPT"

# wmctrl positions windows by pixel - gnome-terminal ignores +X+Y in --geometry
tile_windows() {
    set +e
    WA=$(wmctrl -d | head -1 | grep -oP 'WA: \K\S+ \S+')
    [[ -n "$WA" ]] || return 0
    WA_X=$(echo "$WA" | cut -d' ' -f1 | cut -d',' -f1)
    WA_Y=$(echo "$WA" | cut -d' ' -f1 | cut -d',' -f2)
    WA_W=$(echo "$WA" | cut -d' ' -f2 | cut -d'x' -f1)
    WA_H=$(echo "$WA" | cut -d' ' -f2 | cut -d'x' -f2)
    HALF_W=$((WA_W / 2))
    HALF_H=$((WA_H / 2))
    RIGHT_X=$((WA_X + HALF_W))

    BACKEND_WID=""; FRONTEND_WID=""
    for _ in {1..40}; do
        [[ -z "$BACKEND_WID" ]]  && BACKEND_WID=$(wmctrl -l  | grep "API ($MODE)"  | tail -1 | awk '{print $1}' || true)
        [[ -z "$FRONTEND_WID" ]] && FRONTEND_WID=$(wmctrl -l | grep "Web ($MODE)" | tail -1 | awk '{print $1}' || true)
        [[ -n "$BACKEND_WID" && -n "$FRONTEND_WID" ]] && break
        sleep 0.05
    done

    if [[ -n "$BACKEND_WID" && -n "$FRONTEND_WID" ]]; then
        # GTK draws invisible shadows outside the visible window border.
        EXTENTS=$(xprop -id "$BACKEND_WID" _GTK_FRAME_EXTENTS 2>/dev/null | grep -oP '\d+' | tr '\n' ' ' || true)
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
    return 0
}

if command -v wmctrl &>/dev/null; then
    ( tile_windows ) || true
else
    ok "wmctrl not found - install it with: sudo apt install wmctrl"
fi

ok "Backend terminal launched"
ok "Frontend terminal launched"

echo -e "
    App:     http://localhost:4200
    API:     http://localhost:5098
    Swagger: http://localhost:5098/swagger

    Close a terminal to stop that server.
"
