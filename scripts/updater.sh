#!/bin/sh
# The ControlRoom updater: run by the LaunchAgent local.controlroom.updater (every 6 h, never at load) as
#   /bin/sh <support>/updater.sh --factory-root <root>
# where <support> is ~/Library/Application Support/ControlRoom. When the user turned on "Install updates
# automatically" (settings.json is exactly {"autoUpdate":true} or {"autoUpdate":true,"channel":"stable"|"develop"}),
# the install is a release (installed-release holds its tag) and no factory job is running, it installs a newer
# GitHub release (on the develop channel also a -develop prerelease) with the same download and checks as
# `launch-agent.sh install --release`, swaps it in, confirms it through the public GET /health route (counted only
# while launchd runs the server and its pid is the one listening on the port) and rolls back when it doesn't
# answer or the marker can't be updated. It writes update-result.json for the server's push and prints exactly one line,
# "<UTC ISO-8601> <word>[ <tags>]", which launchd appends to ~/Library/Logs/ControlRoom/updater.log.
# POSIX sh. Runs only launchctl, curl, lsof, tar and shasum (plus standard tools), all with fixed arguments.
# Never reads, copies or sends the access token. Nothing from the download runs here.

set -u
umask 077
# Only the one log line reaches the log (fd 3); every other output is dropped.
exec 3>&1 1>/dev/null 2>/dev/null

LABEL="local.controlroom.server"
# The one repository releases come from (the same as launch-agent.sh).
RELEASES="https://github.com/nothisisnotdaniel/controlroom/releases"
# The develop channel's one release list (newest first; the API never shows drafts without a token).
LIST_URL="https://api.github.com/repos/nothisisnotdaniel/controlroom/releases?per_page=100"
MAX_LIST_BYTES=1048576
# settings.json is at most this long: the longest form with its line break is 41 bytes.
MAX_SETTINGS_BYTES=64
# The texts the shared functions below fail with are the log words here.
LATEST_FAILED=latest_failed
DOWNLOAD_FAILED=download_failed
RELEASE_BAD=check_failed
# The same size caps as launch-agent.sh --release.
MAX_TARBALL_BYTES=209715200
MAX_SUMS_BYTES=4096
MAX_UNPACKED_BYTES=536870912
MAX_ENTRIES=10000
# The public, token-free route the swapped-in server must answer on: 15 tries, 2 s apart, 2 s each.
HEALTH_URL="http://127.0.0.1:8100/health"
HEALTH_TRIES=15
# A lock older than this (minutes) is left over from a run that died, and is removed.
LOCK_MINUTES=60
# launchctl print indents the service's own fields with one tab (nested ones with more).
TAB=$(printf '\t')
# A job's state.json larger than this counts as a running job (it is never read).
MAX_STATE_BYTES=65536

INSTALLED=""
TAG=""
WORK=""
LOCKED=no

# Prints the one log line: the time, the word, then the installed and the latest tag once they are known and
# checked. Only v+SemVer tags ever follow the word.
log_line() {
    details=""
    if [ -n "$INSTALLED" ]; then
        details=" $INSTALLED"
    fi
    if [ -n "$TAG" ]; then
        details="$details $TAG"
    fi
    printf '%s %s%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$details" >&3
}

# Logs the word and ends the run (the EXIT trap removes the work folder and the lock).
finish() {
    log_line "$1"
    exit 0
}

# What the shared functions call on a failed fetch or check: a failed check is also recorded as a result.
fail() {
    if [ "$1" = "$RELEASE_BAD" ]; then
        write_result update_failed check
    fi
    finish "$1"
}

cleanup() {
    if [ -n "$WORK" ]; then
        rm -rf "$WORK"
    fi
    if [ "$LOCKED" = yes ]; then
        rmdir "$LOCK"
    fi
}

# True for a regular file (not a symlink) with mode exactly 0600, owned by this user, of at most $2 bytes.
private_file() {
    if [ ! -f "$1" ] || [ -L "$1" ]; then
        return 1
    fi
    [ -n "$(find "$1" -prune -type f -perm 600 -user "$USER_ID")" ] || return 1
    size=$(wc -c <"$1") || return 1
    [ $((size + 0)) -le "$2" ]
}

# True for a folder (not a symlink) with mode exactly 0700, owned by this user.
private_folder() {
    if [ ! -d "$1" ] || [ -L "$1" ]; then
        return 1
    fi
    [ -n "$(find "$1" -prune -type d -perm 700 -user "$USER_ID")" ]
}

# Finds a program on PATH as an absolute path, or fails.
find_program() {
    found=$(command -v "$1") || return 1
    case $found in
        /*) printf '%s' "$found" ;;
        *) return 1 ;;
    esac
}

# Writes $2 into the support file $1 atomically: a 0600 temp file in the support folder, then rename.
write_private() {
    if [ -d "$1" ]; then
        return 1
    fi
    temp=$(mktemp "$SUPPORT/.updater.XXXXXX") || return 1
    if printf '%s\n' "$2" >"$temp" && chmod 600 "$temp" && mv -f "$temp" "$1"; then
        return 0
    fi
    rm -f "$temp"
    return 1
}

# Writes update-result.json: $1 is updated or update_failed, $2 the failure code.
write_result() {
    run=$(date -u +%Y%m%dT%H%M%SZ)
    if [ "$1" = updated ]; then
        json="{\"run\":\"$run\",\"result\":\"updated\",\"version\":\"${TAG#v}\",\"previous\":\"${INSTALLED#v}\"}"
    else
        json="{\"run\":\"$run\",\"result\":\"update_failed\",\"version\":\"${TAG#v}\",\"reason\":\"$2\"}"
    fi
    write_private "$RESULT" "$json"
}

# True when $1 is greater than $2, both numbers without leading zeros (compared as text, so any length works).
number_greater() {
    if [ ${#1} -ne ${#2} ]; then
        [ ${#1} -gt ${#2} ]
        return
    fi
    [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | LC_ALL=C sort | tail -n 1)" = "$1" ]
}

# True when $1 is ASCII-greater than $2 (two different prerelease identifiers that are not numbers).
text_greater() {
    [ "$(printf '%s\n%s\n' "$1" "$2" | LC_ALL=C sort | tail -n 1)" = "$1" ]
}

# True when tag $1 is newer than tag $2 by SemVer precedence: the higher X.Y.Z; for the same X.Y.Z, no prerelease
# beats a prerelease; two prereleases compare identifier by identifier (numbers as numbers and below names, names
# in ASCII order, and more identifiers win when all before are equal). Both tags passed valid_tag.
semver_newer() {
    a=${1#v}
    b=${2#v}
    a_core=${a%%-*}
    b_core=${b%%-*}
    a_pre=""
    b_pre=""
    case $a in
        *-*) a_pre=${a#*-} ;;
    esac
    case $b in
        *-*) b_pre=${b#*-} ;;
    esac
    a_rest=$a_core.
    b_rest=$b_core.
    while [ -n "$a_rest" ]; do
        a_id=${a_rest%%.*}
        b_id=${b_rest%%.*}
        a_rest=${a_rest#*.}
        b_rest=${b_rest#*.}
        if [ "$a_id" != "$b_id" ]; then
            number_greater "$a_id" "$b_id"
            return
        fi
    done
    if [ -z "$a_pre" ] || [ -z "$b_pre" ]; then
        [ -z "$a_pre" ] && [ -n "$b_pre" ]
        return
    fi
    a_rest=$a_pre.
    b_rest=$b_pre.
    while :; do
        if [ -z "$a_rest" ]; then
            return 1
        fi
        if [ -z "$b_rest" ]; then
            return 0
        fi
        a_id=${a_rest%%.*}
        b_id=${b_rest%%.*}
        a_rest=${a_rest#*.}
        b_rest=${b_rest#*.}
        if [ "$a_id" = "$b_id" ]; then
            continue
        fi
        case $a_id in
            *[!0-9]*) a_number=no ;;
            *) a_number=yes ;;
        esac
        case $b_id in
            *[!0-9]*) b_number=no ;;
            *) b_number=yes ;;
        esac
        if [ "$a_number" = yes ] && [ "$b_number" = yes ]; then
            number_greater "$a_id" "$b_id"
        elif [ "$a_number" = yes ]; then
            return 1
        elif [ "$b_number" = yes ]; then
            return 0
        else
            text_greater "$a_id" "$b_id"
        fi
        return
    done
}

# True for a tag the develop channel installs: exactly vX.Y.Z or vX.Y.Z-develop.N (after valid_tag).
channel_tag() {
    valid_tag "$1" || return 1
    printf '%s\n' "$1" \
        | LC_ALL=C grep -Eq '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-develop\.[1-9][0-9]*)?$'
}

# The develop channel: lists the releases (one fixed GET, no Authorization, no redirects, 1 MiB at most) and sets
# TAG to the newest channel_tag among their tag_name values. Any failure, or no such tag, is latest_failed.
develop_tag() {
    # The trailing x keeps the answer's last line break, so nothing is cut before the tags are read.
    answer=$("$CURL" --proto =https --tlsv1.2 -fsS --max-time 30 --max-filesize "$MAX_LIST_BYTES" "$LIST_URL" \
        && printf x) || finish "$LATEST_FAILED"
    answer=${answer%x}
    [ ${#answer} -le "$MAX_LIST_BYTES" ] || finish "$LATEST_FAILED"
    best=""
    # Only "tag_name":"<value>" pairs: in a JSON string a quote is escaped, so text inside one never matches.
    names=$(printf '%s\n' "$answer" | LC_ALL=C grep -Eo '"tag_name"[[:space:]]*:[[:space:]]*"[^"\\]*"' \
        | LC_ALL=C sed -E 's/^"tag_name"[[:space:]]*:[[:space:]]*"([^"]*)"$/\1/')
    # Read line by line from a here-document: no word splitting or globbing of the names.
    while IFS= read -r name; do
        channel_tag "$name" || continue
        if [ -z "$best" ] || semver_newer "$name" "$best"; then
            best=$name
        fi
    done <<NAMES
$names
NAMES
    [ -n "$best" ] || finish "$LATEST_FAILED"
    TAG=$best
}

# True when no job is in an active stage. A missing jobs folder, or a state.json that is not a regular file, is
# larger than MAX_STATE_BYTES or can't be read, counts as a running job, so the updater never swaps the server
# under one.
jobs_idle() {
    if [ ! -d "$ROOT/factory/jobs" ]; then
        return 1
    fi
    for state in "$ROOT"/factory/jobs/*/state.json; do
        if [ ! -e "$state" ] && [ ! -L "$state" ]; then
            continue
        fi
        if [ ! -f "$state" ] || [ -L "$state" ]; then
            return 1
        fi
        size=$(wc -c <"$state") || return 1
        [ $((size + 0)) -le "$MAX_STATE_BYTES" ] || return 1
        LC_ALL=C grep -Eq '"stage"[[:space:]]*:[[:space:]]*"(spike|spec|build|review)"' "$state"
        case $? in
            1) ;;
            *) return 1 ;;
        esac
    done
    return 0
}

# Prints the version of the last update_failed result with reason check or no_answer, if any.
last_skipped_version() {
    private_file "$RESULT" 4096 || return 1
    LC_ALL=C sed -En \
        's/^[{]"run":"[0-9]{8}T[0-9]{6}Z","result":"update_failed","version":"([0-9A-Za-z.-]+)","reason":"(check|no_answer)"[}]$/\1/p' \
        "$RESULT"
}

# True when launchd runs the server (top-level "state = running" in launchctl print) and its pid is the only
# process listening on port 8100 that lsof shows, so an answer from another local process that took the port
# after the bootout is never counted. Fails closed on anything else.
server_owns_port() {
    printed=$("$LAUNCHCTL" print "$SERVICE") || return 1
    printf '%s\n' "$printed" | LC_ALL=C grep -qx "${TAB}state = running" || return 1
    pid=$(printf '%s\n' "$printed" | LC_ALL=C sed -n "s/^${TAB}pid = \([1-9][0-9]*\)\$/\1/p")
    case $pid in
        "" | *[!0-9]*) return 1 ;;
    esac
    listeners=$("$LSOF" -nP -iTCP:8100 -sTCP:LISTEN -t) || return 1
    [ "$listeners" = "$pid" ]
}

# True when GET /health answers exactly {"version":"$1"} from the launchd server within HEALTH_TRIES tries. No
# Authorization header and no curl config file (-q): the route is public.
health_is() {
    tries=0
    while :; do
        tries=$((tries + 1))
        body=$("$CURL" -q -fsS --max-time 2 --noproxy '*' "$HEALTH_URL" && printf x) && body=${body%x} \
            && [ "$body" = "{\"version\":\"$1\"}" ] && server_owns_port && return 0
        if [ "$tries" -ge "$HEALTH_TRIES" ]; then
            return 1
        fi
        sleep 2
    done
}

bootout_server() {
    "$LAUNCHCTL" bootout "$SERVICE"
}

bootstrap_server() {
    "$LAUNCHCTL" bootstrap "$DOMAIN" "$PLIST"
}

# The old server could not be brought back, or does not answer.
end_rollback_failed() {
    write_result update_failed rollback_failed
    finish rollback_failed
}

# A swap rename failed: $1 is yes when server was already moved to server.previous (it is moved back).
end_swap_failed() {
    if [ "$1" = yes ]; then
        if [ -e "$SERVER" ] || [ -L "$SERVER" ] || ! mv "$PREVIOUS" "$SERVER"; then
            bootstrap_server
            end_rollback_failed
        fi
    fi
    bootstrap_server
    if health_is "${INSTALLED#v}"; then
        write_result update_failed swap
        finish swap_failed
    fi
    end_rollback_failed
}

# Swaps the checked release in, confirms it and rolls back when it does not answer.
swap() {
    # From here on a signal must not leave the server half swapped.
    trap '' HUP INT TERM
    bootout_server
    rm -rf "$PREVIOUS"
    if [ -e "$PREVIOUS" ] || [ -L "$PREVIOUS" ] || ! mv "$SERVER" "$PREVIOUS"; then
        end_swap_failed no
    fi
    if ! mv "$WORK/out/$TOP" "$SERVER"; then
        end_swap_failed yes
    fi
    bootstrap_server
    if health_is "${TAG#v}"; then
        # The marker must name the running release, else the next run installs it again: when it can't be
        # written, the old version (which the marker still names) is put back as for a failed swap.
        if write_private "$MARKER" "$TAG"; then
            write_result updated
            finish updated
        fi
        roll_back swap_failed swap
    fi
    roll_back rolled_back no_answer
}

# Puts server.previous back in place of the new server and confirms the old version; logs $1 and writes result
# reason $2, or rollback_failed when the rename fails or the old version does not answer.
roll_back() {
    bootout_server
    rm -rf "$SERVER"
    if [ -e "$SERVER" ] || [ -L "$SERVER" ] || ! mv "$PREVIOUS" "$SERVER"; then
        bootstrap_server
        end_rollback_failed
    fi
    bootstrap_server
    if health_is "${INSTALLED#v}"; then
        write_result update_failed "$2"
        finish "$1"
    fi
    end_rollback_failed
}

# --- Shared with launch-agent.sh (kept byte-identical; UpdaterScriptTests compares them) -------------------

valid_tag() {
    case $1 in
        v*) ;;
        *) return 1 ;;
    esac
    case $1 in
        *[!0-9A-Za-z.-]*) return 1 ;;
    esac
    printf '%s\n' "${1#v}" \
        | LC_ALL=C grep -Eq '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
}

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

download() {
    "$CURL" --proto =https --proto-redir =https --tlsv1.2 -fsSL --max-time 300 --max-filesize "$2" \
        -o "$WORK/$1" "$RELEASES/download/$TAG/$1" || fail "$DOWNLOAD_FAILED"
    if [ ! -f "$WORK/$1" ] || [ -L "$WORK/$1" ]; then
        fail "$DOWNLOAD_FAILED"
    fi
}

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

# --- Main -----------------------------------------------------------------------------------------------------

ROOT=""
if [ $# -eq 2 ] && [ "$1" = --factory-root ]; then
    case $2 in
        /*) ROOT=$2 ;;
    esac
fi

case ${HOME:-} in
    /*) ;;
    *) finish off ;;
esac
USER_ID=$(id -u) || finish off
SUPPORT="$HOME/Library/Application Support/ControlRoom"
SETTINGS="$SUPPORT/settings.json"
MARKER="$SUPPORT/installed-release"
RESULT="$SUPPORT/update-result.json"
LOCK="$SUPPORT/.update.lock"
SERVER="$SUPPORT/server"
PREVIOUS="$SUPPORT/server.previous"
AGENTS="$HOME/Library/LaunchAgents"
PLIST="$AGENTS/$LABEL.plist"

# 1. The setting: exactly {"autoUpdate":true} (stable), {"autoUpdate":true,"channel":"stable"} or
# {"autoUpdate":true,"channel":"develop"}, with or without one line break, in the private support folder.
if ! private_folder "$SUPPORT" || ! private_file "$SETTINGS" "$MAX_SETTINGS_BYTES"; then
    finish off
fi
settings=$(cat "$SETTINGS") || finish off
case $settings in
    '{"autoUpdate":true}' | '{"autoUpdate":true,"channel":"stable"}') CHANNEL=stable ;;
    '{"autoUpdate":true,"channel":"develop"}') CHANNEL=develop ;;
    *) finish off ;;
esac
# $(cat) drops every trailing line break: the size allows the text plus at most one.
settings_size=$(wc -c <"$SETTINGS") || finish off
[ $((settings_size + 0)) -le $((${#settings} + 1)) ] || finish off

# 2. A release install: the marker holds a valid tag, and the server folder and plist are in place.
private_file "$MARKER" 200 || finish not_release
installed=$(cat "$MARKER") || finish not_release
valid_tag "$installed" || finish not_release
INSTALLED=$installed
if [ ! -d "$SERVER" ] || [ -L "$SERVER" ] || [ -L "$PREVIOUS" ] || [ -L "$AGENTS" ] \
    || [ ! -f "$PLIST" ] || [ -L "$PLIST" ]; then
    finish not_release
fi
LAUNCHCTL=$(find_program launchctl) || finish not_release
LSOF=$(find_program lsof) || finish not_release
DOMAIN="gui/$USER_ID"
SERVICE="$DOMAIN/$LABEL"

# 3. One run at a time (launch-agent.sh install/uninstall wait for the lock too).
trap cleanup EXIT
trap 'exit 1' HUP INT TERM
if mkdir "$LOCK"; then
    LOCKED=yes
elif [ -d "$LOCK" ] && [ ! -L "$LOCK" ] && [ -n "$(find "$LOCK" -prune -mmin +"$LOCK_MINUTES")" ]; then
    rm -rf "$LOCK"
    mkdir "$LOCK" || finish busy
    LOCKED=yes
else
    finish busy
fi

# 4. Never while a factory job is running.
if [ -z "$ROOT" ] || ! jobs_idle; then
    finish job_running
fi

# 5. Only a newer release that has not just failed its check or start: on stable the latest final release, on
# develop the newest vX.Y.Z or vX.Y.Z-develop.N.
CURL=$(find_program curl) || finish latest_failed
if [ "$CHANNEL" = develop ]; then
    develop_tag
else
    latest_tag
    case $TAG in
        *-*) finish current ;;
    esac
fi
semver_newer "$TAG" "$INSTALLED" || finish current
skipped=$(last_skipped_version) && [ -n "$skipped" ] && [ "v$skipped" = "$TAG" ] && finish skipped_failed

# 6. Download and check exactly like launch-agent.sh install --release; nothing is touched before this passes.
WORK=$(mktemp -d "$SUPPORT/.release.XXXXXX") || {
    WORK=""
    finish download_failed
}
TOP="controlroom-${TAG#v}-osx-arm64"
TARBALL="$TOP.tar.gz"
TAR=$(find_program tar) || fail "$RELEASE_BAD"
SHASUM=$(find_program shasum) || fail "$RELEASE_BAD"
download "$TARBALL" "$MAX_TARBALL_BYTES"
download SHA256SUMS "$MAX_SUMS_BYTES"
check_and_extract

# 7. Swap, confirm, roll back.
swap
