#!/bin/sh
# Installs, removes and reports on the macOS LaunchAgent that starts the ControlRoom server at login.
# Usage: scripts/launch-agent.sh install|uninstall|status
#        scripts/launch-agent.sh install --tailnet-host <mac>.<tailnet>.ts.net  (adds --TailnetHost <name>)
#        scripts/launch-agent.sh install --release [vX.Y.Z] [--tailnet-host <name>]
#          (installs the ready-built release from GitHub instead of publishing: the latest one, or vX.Y.Z,
#          plus the updater LaunchAgent local.controlroom.updater that runs <support>/updater.sh every 6 h)
# POSIX sh. Runs only dotnet (publish), launchctl, sleep and, with --release, curl, tar and shasum, all with fixed
# arguments. Never reads the access token file. After every bootout it waits until launchd has removed the service,
# and a failed bootstrap is tried again a few times.

set -u

LABEL="local.controlroom.server"
UPDATER_LABEL="local.controlroom.updater"
# Every 6 h; launchd never starts it at load.
UPDATER_INTERVAL=21600
UPDATE_RUNNING="An update is running. Try again in a few minutes."
USAGE="usage: scripts/launch-agent.sh install [--release [vX.Y.Z]] [--tailnet-host <name>]|uninstall|status"
# The one repository releases come from (renaming it breaks --release).
RELEASES="https://github.com/nothisisnotdaniel/controlroom/releases"
LATEST_FAILED="Can't find the latest release. Nothing was changed."
DOWNLOAD_FAILED="Download failed. Nothing was changed."
RELEASE_BAD="The release download doesn't check out. Nothing was changed."
# Size caps for --release (the real tarball is about 42 MiB, unpacked about 112 MiB in under 400 entries).
MAX_TARBALL_BYTES=209715200
MAX_SUMS_BYTES=4096
MAX_UNPACKED_BYTES=536870912
MAX_ENTRIES=10000
# After a bootout, launchctl print is polled until the service is gone: at most this many polls, 0.5 s apart (15 s).
WAIT_POLLS=30
# A failed bootstrap is tried again this many times, 1 s apart (launchd may still be removing the old service).
BOOTSTRAP_RETRIES=5
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

# True for "v" plus a SemVer version (prerelease part allowed, so also a develop build such as
# v1.2.4-develop.7; no build metadata) on one line. Only such a tag goes into a URL or path.
valid_tag() {
    case $1 in
        v*) ;;
        *) return 1 ;;
    esac
    case $1 in
        *[!0-9A-Za-z.-]*) return 1 ;;
    esac
    printf '%s\n' "${1#v}" \
        | LC_ALL=C grep -Eq '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$'
}

case ${1:-} in
    install | uninstall | status) ;;
    *) usage ;;
esac
COMMAND=$1
shift
TAILNET_HOST=""
HAS_TAILNET=no
RELEASE=no
TAG=""
if [ "$COMMAND" = install ]; then
    # install [--release [vX.Y.Z]] [--tailnet-host <name>]: in either order, each at most once.
    while [ $# -gt 0 ]; do
        case $1 in
            --tailnet-host)
                if [ "$HAS_TAILNET" = yes ] || [ $# -lt 2 ]; then
                    usage
                fi
                if ! valid_tailnet_host "$2"; then
                    fail "--tailnet-host must be a name like mac.tailnet.ts.net. Nothing was changed."
                fi
                HAS_TAILNET=yes
                TAILNET_HOST=$2
                shift 2
                ;;
            --release)
                if [ "$RELEASE" = yes ]; then
                    usage
                fi
                RELEASE=yes
                shift
                if [ $# -gt 0 ] && [ "$1" != --tailnet-host ]; then
                    valid_tag "$1" || usage
                    TAG=$1
                    shift
                fi
                ;;
            *) usage ;;
        esac
    done
elif [ $# -ne 0 ]; then
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
PREVIOUS="$SUPPORT/server.previous"
UPDATER_PLIST="$AGENTS/$UPDATER_LABEL.plist"
UPDATER_SCRIPT="$SUPPORT/updater.sh"
UPDATER_LOG="$LOG_DIR/updater.log"
MARKER="$SUPPORT/installed-release"
UPDATE_LOCK="$SUPPORT/.update.lock"

LAUNCHCTL=$(command -v launchctl) || fail "launchctl not found on PATH."
case $LAUNCHCTL in
    /*) ;;
    *) fail "launchctl not found on PATH." ;;
esac
USER_ID=$(id -u) || fail "Can't read the user id."
DOMAIN="gui/$USER_ID"
SERVICE="$DOMAIN/$LABEL"
UPDATER_SERVICE="$DOMAIN/$UPDATER_LABEL"
if [ "$COMMAND" != status ]; then
    SLEEP=$(command -v sleep) || fail "sleep not found on PATH."
    case $SLEEP in
        /*) ;;
        *) fail "sleep not found on PATH." ;;
    esac
fi

# Polls launchctl print for service $1 until it no longer shows it (exits non-zero), 0.5 s after each poll that
# still shows it, at most WAIT_POLLS polls; then goes on anyway.
wait_gone() {
    polls=0
    while [ "$polls" -lt "$WAIT_POLLS" ]; do
        polls=$((polls + 1))
        "$LAUNCHCTL" print "$1" >/dev/null 2>&1 || return 0
        "$SLEEP" 0.5
    done
    return 0
}

# Boots out service $1 (not loaded is fine) and waits until launchd has removed it.
bootout_service() {
    "$LAUNCHCTL" bootout "$1" >/dev/null 2>&1
    wait_gone "$1"
}

# Bootstraps plist $1, trying again 1 s later up to BOOTSTRAP_RETRIES times; returns the last status.
bootstrap_plist() {
    attempts=0
    while :; do
        "$LAUNCHCTL" bootstrap "$DOMAIN" "$1" >/dev/null && return 0
        boot_status=$?
        attempts=$((attempts + 1))
        if [ "$attempts" -gt "$BOOTSTRAP_RETRIES" ]; then
            return "$boot_status"
        fi
        "$SLEEP" 1
    done
}

# Stops while the updater holds its lock (a lock older than 1 h is left over and does not count).
refuse_while_updating() {
    if [ -e "$UPDATE_LOCK" ] || [ -L "$UPDATE_LOCK" ]; then
        if [ -z "$(find "$UPDATE_LOCK" -prune -mmin +60 2>/dev/null)" ]; then
            fail "$UPDATE_RUNNING"
        fi
    fi
}

# Writes stdin into $1 with mode $2 through a temp file in the same folder, then rename; refuses a symlink.
put_file() {
    if [ -L "$1" ] || [ -d "$1" ]; then
        return 1
    fi
    put_temp=$(mktemp "$(dirname "$1")/.controlroom.XXXXXX") || return 1
    if cat >"$put_temp" && chmod "$2" "$put_temp" && mv -f "$put_temp" "$1"; then
        return 0
    fi
    rm -f "$put_temp"
    return 1
}

# Boots out the updater and removes its plist, the copied script and the release marker.
remove_updater() {
    bootout_service "$UPDATER_SERVICE"
    rm -f "$UPDATER_PLIST" "$UPDATER_SCRIPT" "$MARKER" || fail "Can't remove the updater."
}

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
    if [ -e "$UPDATER_PLIST" ] || [ -L "$UPDATER_PLIST" ]; then
        printf 'updater: yes\n'
    else
        printf 'updater: no\n'
    fi
    printf 'log: %s\n' "$LOG"
    exit 0
}

do_uninstall() {
    refuse_link "$HOME/Library"
    refuse_link "$AGENTS"
    refuse_link "$HOME/Library/Application Support"
    refuse_link "$SUPPORT"
    refuse_link "$UPDATER_PLIST"
    refuse_while_updating
    remove_updater
    bootout_service "$SERVICE"
    rm -f "$PLIST" || fail "Can't remove $PLIST"
    rm -rf "$SERVER" "$SERVER_NEW" "$PREVIOUS" || fail "Can't remove $SERVER"
    printf 'Uninstalled. The log stays at %s\n' "$LOG"
    exit 0
}

# Finds a program on PATH as an absolute path without control characters, or stops.
find_program() {
    found=$(command -v "$1") || fail "$1 not found on PATH."
    case $found in
        /*) ;;
        *) fail "$1 not found on PATH." ;;
    esac
    check_text "$found" "The $1 path contains a control character."
    printf '%s' "$found"
}

# Publishes the server from the sources into server.new; launchd runs it with dotnet.
publish_server() {
    if ! "$DOTNET" publish "$REPO/src/ControlRoom.Server" -c Release -o "$SERVER_NEW" >&2 \
        || [ ! -f "$SERVER_NEW/ControlRoom.Server.dll" ]; then
        rm -rf "$SERVER_NEW"
        fail "dotnet publish failed. Nothing was changed."
    fi
    PROGRAM=$DOTNET
    PROGRAM_FILE="$SERVER/ControlRoom.Server.dll"
}

# Removes the release work folder on every way out of the script.
cleanup_work() {
    if [ -n "${WORK:-}" ]; then
        rm -rf "$WORK"
    fi
}

# Sets TAG from the redirect of .../releases/latest (the redirect is not followed, nothing is downloaded).
latest_tag() {
    # The trailing x keeps a trailing line break in the answer, so only the exact URL passes.
    answer=$("$CURL" --proto =https --tlsv1.2 -fsS --max-time 30 -o /dev/null -w '%{redirect_url}' \
        "$RELEASES/latest" && printf x) || fail "$LATEST_FAILED"
    answer=${answer%x}
    case $answer in
        "$RELEASES/tag/"*) ;;
        *) fail "$LATEST_FAILED" ;;
    esac
    found=${answer#"$RELEASES/tag/"}
    valid_tag "$found" || fail "$LATEST_FAILED"
    TAG=$found
}

# Downloads one file of release TAG, at most $2 bytes, into the work folder.
download() {
    "$CURL" --proto =https --proto-redir =https --tlsv1.2 -fsSL --max-time 300 --max-filesize "$2" \
        -o "$WORK/$1" "$RELEASES/download/$TAG/$1" || fail "$DOWNLOAD_FAILED"
    if [ ! -f "$WORK/$1" ] || [ -L "$WORK/$1" ]; then
        fail "$DOWNLOAD_FAILED"
    fi
}

# Checks the tarball against its one SHA256SUMS line, checks every archive entry before extracting, extracts
# into $WORK/out and checks the result again.
check_and_extract() {
    lines=0
    expected=""
    while IFS= read -r line || [ -n "$line" ]; do
        case $line in
            *"  $TARBALL")
                lines=$((lines + 1))
                expected=${line%"  $TARBALL"}
                ;;
        esac
    done <"$WORK/SHA256SUMS"
    [ "$lines" -eq 1 ] || fail "$RELEASE_BAD"
    case $expected in
        "" | *[!0-9a-f]*) fail "$RELEASE_BAD" ;;
    esac
    [ ${#expected} -eq 64 ] || fail "$RELEASE_BAD"
    actual=$("$SHASUM" -a 256 "$WORK/$TARBALL") || fail "$RELEASE_BAD"
    [ "${actual%% *}" = "$expected" ] || fail "$RELEASE_BAD"

    # Every name is the top folder or inside it (so never absolute), with no empty, . or .. part.
    "$TAR" -tzf "$WORK/$TARBALL" >"$WORK/names" 2>/dev/null || fail "$RELEASE_BAD"
    [ -s "$WORK/names" ] || fail "$RELEASE_BAD"
    while IFS= read -r name || [ -n "$name" ]; do
        case $name in
            "$TOP" | "$TOP/" | "$TOP/"*) ;;
            *) fail "$RELEASE_BAD" ;;
        esac
        case /$name/ in
            */../* | */./* | *//?*) fail "$RELEASE_BAD" ;;
        esac
        case $name in
            *[[:cntrl:]]*) fail "$RELEASE_BAD" ;;
        esac
    done <"$WORK/names"
    # Every entry is a regular file or a folder: no symlink, hard link or device.
    "$TAR" -tvzf "$WORK/$TARBALL" >"$WORK/types" 2>/dev/null || fail "$RELEASE_BAD"
    while IFS= read -r entry || [ -n "$entry" ]; do
        case $entry in
            -* | d*) ;;
            *) fail "$RELEASE_BAD" ;;
        esac
    done <"$WORK/types"
    # Size: at most MAX_ENTRIES entries and MAX_UNPACKED_BYTES of file content, counted by unpacking to a pipe
    # (never to disk) and stopping one byte past the cap.
    entries=$(wc -l <"$WORK/names") || fail "$RELEASE_BAD"
    [ $((entries + 0)) -le "$MAX_ENTRIES" ] || fail "$RELEASE_BAD"
    unpacked=$("$TAR" -xOzf "$WORK/$TARBALL" 2>/dev/null | head -c $((MAX_UNPACKED_BYTES + 1)) | wc -c) \
        || fail "$RELEASE_BAD"
    [ $((unpacked + 0)) -le "$MAX_UNPACKED_BYTES" ] || fail "$RELEASE_BAD"

    mkdir "$WORK/out" || fail "$RELEASE_BAD"
    "$TAR" -xzf "$WORK/$TARBALL" -C "$WORK/out" >/dev/null 2>&1 || fail "$RELEASE_BAD"
    # Checked again: only the top folder, only files and folders in it, the server executable and the app shell.
    [ "$(ls -A "$WORK/out")" = "$TOP" ] || fail "$RELEASE_BAD"
    if [ ! -d "$WORK/out/$TOP" ] || [ -L "$WORK/out/$TOP" ]; then
        fail "$RELEASE_BAD"
    fi
    others=$(find "$WORK/out/$TOP" ! -type f ! -type d) || fail "$RELEASE_BAD"
    [ -z "$others" ] || fail "$RELEASE_BAD"
    if [ ! -f "$WORK/out/$TOP/ControlRoom.Server" ] || [ ! -x "$WORK/out/$TOP/ControlRoom.Server" ] \
        || [ ! -f "$WORK/out/$TOP/wwwroot/index.html" ]; then
        fail "$RELEASE_BAD"
    fi
}

# Downloads, checks and extracts release TAG (the latest one when empty) into server.new; launchd runs its
# ControlRoom.Server directly. Nothing from the download runs here.
install_release() {
    WORK=$(mktemp -d "$SUPPORT/.release.XXXXXX") || fail "$DOWNLOAD_FAILED"
    trap cleanup_work EXIT
    trap 'exit 1' HUP INT TERM
    if [ -z "$TAG" ]; then
        latest_tag
    fi
    TOP="controlroom-${TAG#v}-osx-arm64"
    TARBALL="$TOP.tar.gz"
    download "$TARBALL" "$MAX_TARBALL_BYTES"
    download SHA256SUMS "$MAX_SUMS_BYTES"
    check_and_extract
    mv "$WORK/out/$TOP" "$SERVER_NEW" || fail "$RELEASE_BAD"
    PROGRAM="$SERVER/ControlRoom.Server"
    PROGRAM_FILE=""
}

do_install() {
    SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd) || fail "Can't find the script folder."
    REPO=$(cd "$SCRIPT_DIR/.." && pwd) || fail "Can't find the ControlRoom folder."
    [ -f "$REPO/ControlRoom.slnx" ] || fail "Can't find ControlRoom.slnx next to the scripts folder."
    ROOT=$(cd "$REPO/.." && pwd) || fail "Can't find the factory root."
    check_text "$ROOT" "The factory root path contains a control character."

    if [ "$RELEASE" = yes ]; then
        UPDATER_SOURCE="$SCRIPT_DIR/updater.sh"
        if [ ! -f "$UPDATER_SOURCE" ] || [ -L "$UPDATER_SOURCE" ]; then
            fail "Can't find scripts/updater.sh. Nothing was changed."
        fi
        CURL=$(find_program curl) || exit 1
        TAR=$(find_program tar) || exit 1
        SHASUM=$(find_program shasum) || exit 1
    else
        DOTNET=$(command -v dotnet) || fail "dotnet not found on PATH."
        case $DOTNET in
            /*) ;;
            *) fail "dotnet not found on PATH." ;;
        esac
        check_text "$DOTNET" "The dotnet path contains a control character."
    fi

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
    refuse_link "$PREVIOUS"
    refuse_link "$UPDATER_PLIST"
    refuse_link "$UPDATER_SCRIPT"
    refuse_link "$UPDATER_LOG"
    refuse_link "$MARKER"
    refuse_while_updating

    mkdir -p "$AGENTS" || fail "Can't create $AGENTS"
    mkdir -p "$LOG_DIR" || fail "Can't create $LOG_DIR"
    chmod 700 "$LOG_DIR" || fail "Can't set the mode of $LOG_DIR"
    if [ ! -d "$SUPPORT" ]; then
        mkdir -p "$HOME/Library/Application Support" || fail "Can't create $SUPPORT"
        mkdir -m 700 "$SUPPORT" || fail "Can't create $SUPPORT"
    fi

    rm -rf "$SERVER_NEW" || fail "Can't remove $SERVER_NEW"
    if [ "$RELEASE" = yes ]; then
        install_release
    else
        publish_server
    fi

    # The updater is stopped first: a release install installs it again below, a source install has none.
    if [ "$RELEASE" = yes ] || [ -e "$UPDATER_PLIST" ]; then
        remove_updater
    fi
    # Not loaded is fine: the agent may not be installed yet.
    bootout_service "$SERVICE"

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
        printf '        <string>%s</string>\n' "$(xml_escape "$PROGRAM")"
        if [ -n "$PROGRAM_FILE" ]; then
            printf '        <string>%s</string>\n' "$(xml_escape "$PROGRAM_FILE")"
        fi
        printf '        <string>--FactoryRoot</string>\n'
        printf '        <string>%s</string>\n' "$(xml_escape "$ROOT")"
        if [ -n "$TAILNET_HOST" ]; then
            printf '        <string>--TailnetHost</string>\n'
            printf '        <string>%s</string>\n' "$(xml_escape "$TAILNET_HOST")"
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

    bootstrap_plist "$PLIST" \
        || fail "launchctl bootstrap failed. Check: launchctl print $SERVICE"

    if [ "$RELEASE" = yes ]; then
        install_updater
        printf 'Installed %s: %s\n' "$TAG" "$PLIST"
    else
        printf 'Installed: %s\n' "$PLIST"
    fi
    printf 'Log: %s\n' "$LOG"
    if [ "$RELEASE" = yes ]; then
        printf 'Updater: %s\n' "$UPDATER_PLIST"
    fi
    exit 0
}

# Copies updater.sh (0700) and writes the release marker (the tag, 0600) and the updater plist (0644), then
# bootstraps it. The updater runs only when "Install updates automatically" is on in the app.
install_updater() {
    put_file "$UPDATER_SCRIPT" 700 <"$UPDATER_SOURCE" || fail "Can't write $UPDATER_SCRIPT"
    printf '%s\n' "$TAG" | put_file "$MARKER" 600 || fail "Can't write $MARKER"
    {
        printf '<?xml version="1.0" encoding="UTF-8"?>\n'
        printf '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">\n'
        printf '<plist version="1.0">\n'
        printf '<dict>\n'
        printf '    <key>Label</key>\n'
        printf '    <string>%s</string>\n' "$UPDATER_LABEL"
        printf '    <key>ProgramArguments</key>\n'
        printf '    <array>\n'
        printf '        <string>/bin/sh</string>\n'
        printf '        <string>%s</string>\n' "$(xml_escape "$UPDATER_SCRIPT")"
        printf '        <string>--factory-root</string>\n'
        printf '        <string>%s</string>\n' "$(xml_escape "$ROOT")"
        printf '    </array>\n'
        printf '    <key>EnvironmentVariables</key>\n'
        printf '    <dict>\n'
        printf '        <key>PATH</key>\n'
        printf '        <string>%s</string>\n' "$FIXED_PATH"
        printf '    </dict>\n'
        printf '    <key>StartInterval</key>\n'
        printf '    <integer>%s</integer>\n' "$UPDATER_INTERVAL"
        printf '    <key>StandardOutPath</key>\n'
        printf '    <string>%s</string>\n' "$(xml_escape "$UPDATER_LOG")"
        printf '    <key>StandardErrorPath</key>\n'
        printf '    <string>%s</string>\n' "$(xml_escape "$UPDATER_LOG")"
        printf '</dict>\n'
        printf '</plist>\n'
    } | put_file "$UPDATER_PLIST" 644 || fail "Can't write $UPDATER_PLIST"
    bootstrap_plist "$UPDATER_PLIST" \
        || fail "launchctl bootstrap failed. Check: launchctl print $UPDATER_SERVICE"
}

case $COMMAND in
    install) do_install ;;
    uninstall) do_uninstall ;;
    status) do_status ;;
esac
