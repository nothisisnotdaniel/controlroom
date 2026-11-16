#!/bin/sh
# Installs, removes and reports on the macOS LaunchAgent that starts the ControlRoom server at login.
# Usage: scripts/launch-agent.sh install|uninstall|status
#        scripts/launch-agent.sh install --tailnet-host <mac>.<tailnet>.ts.net  (adds --TailnetHost <name>)
# POSIX sh. Runs only dotnet (publish) and launchctl with fixed arguments. Never reads the access token file.

set -u

LABEL="local.controlroom.server"
USAGE="usage: scripts/launch-agent.sh install|uninstall|status"
FIXED_PATH="/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

# Refuses values that can't be written into the plist as plain text.
check_text() {
    case $1 in
        *[[:cntrl:]]*) fail "$2" ;;
    esac
}

# XML-escapes one value (no control characters, checked before).
xml_escape() {
    printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g' -e "s/'/\&apos;/g"
}

# Refuses a path that is a symlink.
refuse_link() {
    if [ -L "$1" ]; then
        fail "Refusing to write through a symlink: $1"
    fi
}

usage() {
    printf '%s\n' "$USAGE" >&2
    exit 2
}

# True for a lower-case DNS name of 1-63-character labels (a-z, 0-9, -, no label starting or ending with -)
# ending in .ts.net with at least two labels before it: the same rule as HostCheck.IsValidTailnetHost.
valid_tailnet_host() {
    case $1 in
        *.ts.net) ;;
        *) return 1 ;;
    esac
    if [ ${#1} -gt 253 ]; then
        return 1
    fi
    rest=${1%.ts.net}
    labels=0
    while :; do
        label=${rest%%.*}
        if [ -z "$label" ] || [ ${#label} -gt 63 ]; then
            return 1
        fi
        case $label in
            -* | *- | *[!abcdefghijklmnopqrstuvwxyz0123456789-]*) return 1 ;;
        esac
        labels=$((labels + 1))
        if [ "$label" = "$rest" ]; then
            break
        fi
        rest=${rest#*.}
    done
    [ "$labels" -ge 2 ]
}

case ${1:-} in
    install | uninstall | status) ;;
    *) usage ;;
esac
COMMAND=$1
TAILNET_HOST=""
if [ "$COMMAND" = install ] && [ $# -eq 3 ] && [ "$2" = --tailnet-host ]; then
    if ! valid_tailnet_host "$3"; then
        fail "--tailnet-host must be a name like mac.tailnet.ts.net. Nothing was changed."
    fi
    TAILNET_HOST=$3
elif [ $# -ne 1 ]; then
    usage
fi

case ${HOME:-} in
    /*) ;;
    *) fail "HOME must be an absolute path." ;;
esac
check_text "$HOME" "HOME contains a control character."

AGENTS="$HOME/Library/LaunchAgents"
PLIST="$AGENTS/$LABEL.plist"
LOG_DIR="$HOME/Library/Logs/ControlRoom"
LOG="$LOG_DIR/server.log"
SUPPORT="$HOME/Library/Application Support/ControlRoom"
SERVER="$SUPPORT/server"
SERVER_NEW="$SUPPORT/server.new"

LAUNCHCTL=$(command -v launchctl) || fail "launchctl not found on PATH."
case $LAUNCHCTL in
    /*) ;;
    *) fail "launchctl not found on PATH." ;;
esac
USER_ID=$(id -u) || fail "Can't read the user id."
DOMAIN="gui/$USER_ID"
SERVICE="$DOMAIN/$LABEL"

do_status() {
    if [ -e "$PLIST" ] || [ -L "$PLIST" ]; then
        printf 'installed: yes\n'
    else
        printf 'installed: no\n'
    fi
    if "$LAUNCHCTL" print "$SERVICE" >/dev/null 2>&1; then
        printf 'running: yes\n'
    else
        printf 'running: no\n'
    fi
    printf 'log: %s\n' "$LOG"
    exit 0
}

do_uninstall() {
    refuse_link "$HOME/Library"
    refuse_link "$AGENTS"
    refuse_link "$HOME/Library/Application Support"
    refuse_link "$SUPPORT"
    "$LAUNCHCTL" bootout "$SERVICE" >/dev/null 2>&1
    rm -f "$PLIST" || fail "Can't remove $PLIST"
    rm -rf "$SERVER" "$SERVER_NEW" || fail "Can't remove $SERVER"
    printf 'Uninstalled. The log stays at %s\n' "$LOG"
    exit 0
}

do_install() {
    SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd) || fail "Can't find the script folder."
    REPO=$(cd "$SCRIPT_DIR/.." && pwd) || fail "Can't find the ControlRoom folder."
    [ -f "$REPO/ControlRoom.slnx" ] || fail "Can't find ControlRoom.slnx next to the scripts folder."
    ROOT=$(cd "$REPO/.." && pwd) || fail "Can't find the factory root."
    check_text "$ROOT" "The factory root path contains a control character."

    DOTNET=$(command -v dotnet) || fail "dotnet not found on PATH."
    case $DOTNET in
        /*) ;;
        *) fail "dotnet not found on PATH." ;;
    esac
    check_text "$DOTNET" "The dotnet path contains a control character."

    refuse_link "$HOME/Library"
    refuse_link "$AGENTS"
    refuse_link "$PLIST"
    refuse_link "$HOME/Library/Logs"
    refuse_link "$LOG_DIR"
    refuse_link "$LOG"
    refuse_link "$HOME/Library/Application Support"
    refuse_link "$SUPPORT"
    refuse_link "$SERVER"
    refuse_link "$SERVER_NEW"

    mkdir -p "$AGENTS" || fail "Can't create $AGENTS"
    mkdir -p "$LOG_DIR" || fail "Can't create $LOG_DIR"
    chmod 700 "$LOG_DIR" || fail "Can't set the mode of $LOG_DIR"
    if [ ! -d "$SUPPORT" ]; then
        mkdir -p "$HOME/Library/Application Support" || fail "Can't create $SUPPORT"
        mkdir -m 700 "$SUPPORT" || fail "Can't create $SUPPORT"
    fi

    rm -rf "$SERVER_NEW" || fail "Can't remove $SERVER_NEW"
    if ! "$DOTNET" publish "$REPO/src/ControlRoom.Server" -c Release -o "$SERVER_NEW" >&2 \
        || [ ! -f "$SERVER_NEW/ControlRoom.Server.dll" ]; then
        rm -rf "$SERVER_NEW"
        fail "dotnet publish failed. Nothing was changed."
    fi

    # Not loaded is fine: the agent may not be installed yet.
    "$LAUNCHCTL" bootout "$SERVICE" >/dev/null 2>&1

    rm -rf "$SERVER" || fail "Can't remove $SERVER"
    mv "$SERVER_NEW" "$SERVER" || fail "Can't move the published server into place."

    refuse_link "$AGENTS"
    refuse_link "$PLIST"
    TEMP=$(mktemp "$AGENTS/.$LABEL.XXXXXX") || fail "Can't write $PLIST"
    {
        printf '<?xml version="1.0" encoding="UTF-8"?>\n'
        printf '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">\n'
        printf '<plist version="1.0">\n'
        printf '<dict>\n'
        printf '    <key>Label</key>\n'
        printf '    <string>%s</string>\n' "$LABEL"
        printf '    <key>ProgramArguments</key>\n'
        printf '    <array>\n'
        printf '        <string>%s</string>\n' "$(xml_escape "$DOTNET")"
        printf '        <string>%s</string>\n' "$(xml_escape "$SERVER/ControlRoom.Server.dll")"
        printf '        <string>--FactoryRoot</string>\n'
        printf '        <string>%s</string>\n' "$(xml_escape "$ROOT")"
        if [ -n "$TAILNET_HOST" ]; then
            printf '        <string>--TailnetHost</string>\n'
            printf '        <string>%s</string>\n' "$TAILNET_HOST"
        fi
        printf '    </array>\n'
        printf '    <key>WorkingDirectory</key>\n'
        printf '    <string>%s</string>\n' "$(xml_escape "$SERVER")"
        printf '    <key>EnvironmentVariables</key>\n'
        printf '    <dict>\n'
        printf '        <key>PATH</key>\n'
        printf '        <string>%s</string>\n' "$FIXED_PATH"
        printf '    </dict>\n'
        printf '    <key>RunAtLoad</key>\n'
        printf '    <true/>\n'
        printf '    <key>KeepAlive</key>\n'
        printf '    <true/>\n'
        printf '    <key>ThrottleInterval</key>\n'
        printf '    <integer>10</integer>\n'
        printf '    <key>StandardOutPath</key>\n'
        printf '    <string>%s</string>\n' "$(xml_escape "$LOG")"
        printf '    <key>StandardErrorPath</key>\n'
        printf '    <string>%s</string>\n' "$(xml_escape "$LOG")"
        printf '</dict>\n'
        printf '</plist>\n'
    } >"$TEMP" || {
        rm -f "$TEMP"
        fail "Can't write $PLIST"
    }
    chmod 644 "$TEMP" || {
        rm -f "$TEMP"
        fail "Can't set the mode of $PLIST"
    }
    mv -f "$TEMP" "$PLIST" || {
        rm -f "$TEMP"
        fail "Can't write $PLIST"
    }

    "$LAUNCHCTL" bootstrap "$DOMAIN" "$PLIST" >/dev/null \
        || fail "launchctl bootstrap failed. Check: launchctl print $SERVICE"

    printf 'Installed: %s\n' "$PLIST"
    printf 'Log: %s\n' "$LOG"
    exit 0
}

case $COMMAND in
    install) do_install ;;
    uninstall) do_uninstall ;;
    status) do_status ;;
esac
