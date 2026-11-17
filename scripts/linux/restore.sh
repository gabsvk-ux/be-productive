#!/usr/bin/env bash
# Undoes everything Be Productive has changed on this machine. The last resort when the app won't let go.
#
#   sudo ./restore.sh [--data-dir DIR] [--root DIR] [--keep-service-enabled]
#
# 0. Writes a panic file first, so a running service stops enforcing on its next tick.
# 1. Disables and stops the service (stopping it also makes it undo its own changes), killing it if it lingers.
# 2. Removes the app's section from /etc/hosts (found by its marker comments; nothing else is touched).
# 3. Deletes the app's own Chrome / Chromium policy files (be-productive.json; nobody else writes them).
# 4. Clears the change journal and flushes the DNS cache.
#
# It needs nothing but bash and coreutils, and doesn't depend on the panic file being enabled. Keep a copy
# somewhere outside the machine. From a recovery shell (the GRUB "recovery mode" root shell, or a live USB
# with --root pointing at the mounted disk), it works without the service ever starting.
set -uo pipefail

DATA_DIR=/var/lib/be-productive
ROOT=""
keep_enabled=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        --data-dir) DATA_DIR="$2"; shift ;;
        --root) ROOT="${2%/}"; shift ;;
        --keep-service-enabled) keep_enabled=true ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

if [[ $EUID -ne 0 ]]; then
    echo "Run this as root (sudo)." >&2
    exit 1
fi

# Must match HostsSection.cs and LinuxEnforcementPolicies.cs.
BEGIN_MARKER='# BEGIN BeProductive (managed automatically, do not edit)'
END_MARKER='# END BeProductive'
HOSTS="$ROOT/etc/hosts"
POLICY_FILES=(
    "$ROOT/etc/opt/chrome/policies/managed/be-productive.json"
    "$ROOT/etc/chromium/policies/managed/be-productive.json"
)
data="$ROOT$DATA_DIR"
failures=0
disabled=false
step() { printf '\033[36m-> %s\033[0m\n' "$*"; }

step "Writing a panic file so a running service stops enforcing"
mkdir -p "$data" && touch "$data/PANIC" || true

if [[ -z "$ROOT" ]] && command -v systemctl >/dev/null && [[ -d /run/systemd/system ]]; then
    if $keep_enabled; then
        step "Stopping the service"
        systemctl stop be-productive.service 2>/dev/null || true
    else
        step "Disabling and stopping the service"
        systemctl disable --now be-productive.service 2>/dev/null && disabled=true
    fi
fi
if pgrep -f 'BeProductive.Service' >/dev/null 2>&1; then
    step "The service is still running; killing it"
    pkill -KILL -f 'BeProductive.Service' || true
    sleep 1
fi

step "Removing the app's section from $HOSTS"
if [[ -f "$HOSTS" ]] && grep -qxF "$BEGIN_MARKER" "$HOSTS"; then
    # Drop the lines from the begin marker to the end marker. If the end marker is missing, stop at the first
    # line that isn't one of ours (a 0.0.0.0 entry), like HostsSection.Remove. Written in place so the
    # file keeps its owner, mode and SELinux label.
    filtered="$(awk -v begin="$BEGIN_MARKER" -v end="$END_MARKER" '
        !inside && $0 == begin { inside = 1; next }
        inside && $0 == end { inside = 0; next }
        inside && $0 ~ /^0\.0\.0\.0[ \t]/ { next }
        inside { inside = 0 }
        { print }
    ' "$HOSTS")" && printf '%s\n' "$filtered" > "$HOSTS" || { echo "Couldn't rewrite $HOSTS" >&2; failures=$((failures + 1)); }
else
    echo "   nothing to remove"
fi

step "Removing the app's browser policy files"
for file in "${POLICY_FILES[@]}"; do
    if [[ -f "$file" ]]; then
        rm -f "$file" && echo "   removed $file" || failures=$((failures + 1))
    fi
done

step "Clearing the change journal"
rm -f "$data/journal.json" "$data/journal.json.tmp"

if [[ -z "$ROOT" ]]; then
    step "Flushing the DNS cache"
    resolvectl flush-caches 2>/dev/null || nscd --invalidate=hosts 2>/dev/null || echo "   no DNS cache to flush"
fi

rm -f "$data/PANIC"
if [[ $failures -gt 0 ]]; then
    echo "Done with $failures problem(s) (see above)." >&2
    exit 1
fi
echo "Done. Everything Be Productive changed has been undone."
! $disabled || echo "The service is disabled; re-enable it with: sudo systemctl enable --now be-productive"
