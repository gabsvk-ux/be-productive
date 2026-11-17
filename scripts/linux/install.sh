#!/usr/bin/env bash
# Installs Be Productive on Linux: the service (systemd, as root), the app (started at every sign-in), and
# `bp` on the PATH. Run from a clone of the repository with the .NET 8 SDK installed:
#
#   sudo ./scripts/linux/install.sh
#   sudo ./scripts/linux/install.sh --uninstall --code XXXX-XXXX-XXXX-XXXX
#
# The service starts in DRY RUN (it only logs what it would block). Switch to live blocking in
# /var/lib/be-productive/settings.json once you've watched the log for a while (see docs/linux.md).
set -euo pipefail

INSTALL_DIR=/opt/be-productive
DATA_DIR=/var/lib/be-productive
UNIT=/etc/systemd/system/be-productive.service
AUTOSTART=/etc/xdg/autostart/be-productive.desktop
LAUNCHER=/usr/share/applications/be-productive.desktop
BP_LINK=/usr/local/bin/bp
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

uninstall=false
code=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --uninstall) uninstall=true ;;
        --code) code="${2:-}"; shift ;;
        -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

if [[ $EUID -ne 0 ]]; then
    echo "Run this as root (sudo)." >&2
    exit 1
fi

step() { printf '\033[36m-> %s\033[0m\n' "$*"; }

if $uninstall; then
    # Require a recovery code to uninstall, when codes are set up. The installed CLI decides and consumes one.
    if [[ -x "$INSTALL_DIR/Cli/bp" ]]; then
        if ! "$INSTALL_DIR/Cli/bp" uninstall-authorize "$code"; then
            echo "Uninstall not authorized. Supply a valid recovery code with --code, or run scripts/linux/restore.sh to just stop blocking." >&2
            exit 1
        fi
    else
        echo "Warning: the CLI is missing, so a recovery code couldn't be checked; uninstalling anyway." >&2
    fi
fi

# Stop what's running. Stopping the service undoes every change it made (the safety net runs on stop).
if systemctl list-unit-files be-productive.service >/dev/null 2>&1 && [[ -f "$UNIT" ]]; then
    step "Stopping the existing service"
    systemctl disable --now be-productive.service || true
fi
pkill -x BeProductive 2>/dev/null || true

if $uninstall; then
    rm -f "$UNIT" "$AUTOSTART" "$LAUNCHER" "$BP_LINK"
    systemctl daemon-reload
    rm -rf "$INSTALL_DIR"
    echo "Uninstalled. Your data in $DATA_DIR was kept."
    exit 0
fi

command -v dotnet >/dev/null || { echo "The .NET 8 SDK is needed to build (e.g. sudo apt install dotnet-sdk-8.0)." >&2; exit 1; }

publish() {
    step "Publishing $1"
    dotnet publish "$REPO_ROOT/src/$1" -c Release --self-contained false -o "$INSTALL_DIR/$2" --nologo -v quiet
}
publish BeProductive.Service Service
publish BeProductive.App App
publish BeProductive.Cli Cli
install -D -m 0644 "$REPO_ROOT/docs/linux.md" "$INSTALL_DIR/docs/linux.md"
install -D -m 0755 "$REPO_ROOT/scripts/linux/restore.sh" "$INSTALL_DIR/restore.sh"

step "Installing the systemd unit, the autostart entry and bp"
install -D -m 0644 "$REPO_ROOT/scripts/linux/be-productive.service" "$UNIT"
install -D -m 0644 "$REPO_ROOT/scripts/linux/be-productive.desktop" "$AUTOSTART"
install -D -m 0644 "$REPO_ROOT/scripts/linux/be-productive.desktop" "$LAUNCHER"
ln -sf "$INSTALL_DIR/Cli/bp" "$BP_LINK"

mkdir -p "$DATA_DIR"
chmod 0755 "$DATA_DIR"
if [[ ! -f "$DATA_DIR/settings.json" ]]; then
    # Dry run until you decide otherwise. The panic file stays on while you test; turn it off for real use.
    cat > "$DATA_DIR/settings.json" <<'JSON'
{
  "Enforcement": {
    "DryRun": true,
    "PanicFileEnabled": true
  }
}
JSON
fi

systemctl daemon-reload
systemctl enable --now be-productive.service

# Start the app for whoever is at the screen now, as them (not as root). Later sign-ins use the autostart entry.
if [[ -n "${SUDO_USER:-}" && "$SUDO_USER" != root ]]; then
    systemd-run --user --machine="$SUDO_USER@" --collect --quiet -- "$INSTALL_DIR/App/BeProductive" 2>/dev/null \
        || echo "Couldn't start the app now; it starts at your next sign-in."
fi

echo
echo "Installed to $INSTALL_DIR and started the service (DRY RUN: it only logs) and the app."
echo "Watch what it would do:   journalctl -u be-productive -f"
echo "Set up recovery codes:    sudo bp codes init"
echo "Go live:                  set Enforcement:DryRun to false in $DATA_DIR/settings.json, then"
echo "                          sudo systemctl restart be-productive"
echo "Emergency undo:           sudo $INSTALL_DIR/restore.sh   (keep a copy of scripts/linux/restore.sh elsewhere)"
echo "Remove:                   sudo ./scripts/linux/install.sh --uninstall --code <recovery code>"
