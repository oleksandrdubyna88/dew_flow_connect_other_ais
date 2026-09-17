#!/usr/bin/env bash
# Release (and roll back) `coai-bugs` on a systemd host.
#
# ---------------------------------------------------------------------------
# ITS OWN SCRIPT, not a parameter on `deploy/systemd-release.sh`, by operator decision: the bugs
# server is a FULLY INDEPENDENT deployment. Sharing one script would mean one blast radius — a
# change made for the ingest server could break the Team server's release on a machine where that
# one is live and this one is not even installed yet.
#
# What IS shared is the shape, because the Team server paid for it: an immutable version-addressed
# directory, `bin` swapped by ONE rename, a trail a second consecutive rollback still reads, the
# last three retained, and a canary that decides rather than `systemctl is-active`.
#
# What is deliberately NOT shared is the canary. The Team server's runs a real review with a
# session token, which this server has no concept of. Here the canary is the two things that have
# actually broken a `coai-bugs` build:
#
#   * `--waiting` opens the database through the SQLite P/Invoke. That is the call that threw
#     `DllNotFoundException` when `mcp-v0.18.1` shipped its executable without `e_sqlite3`, and
#     this server reads its key table on EVERY request — so without it every upload answers 401
#     and the log says nothing about why.
#   * `/health` answers only after the embedded keyword list has parsed, because the list is read
#     before any route is mapped. A published binary that answers it is carrying its own word
#     list, and nothing short of running it can establish that.
# ---------------------------------------------------------------------------
set -euo pipefail

ROOT=${COAI_BUGS_ROOT:-/opt/coai-bugs}
RELEASES="$ROOT/releases"
TRAIL="$RELEASES/.trail"
LIVE="$ROOT/bin"
KEEP=4                                  # the current deployment plus the three the rule requires
SERVICE=coai-bugs
PORT=${COAI_BUGS_PORT:-8110}

# The deploy account is NOT root. The Team server next door runs its forced command as root and
# its unit as root; this one does neither, because it is the service that faces the public
# internet. `coai-bugs-deploy` may restart exactly this unit and nothing else
# (/etc/sudoers.d/coai-bugs-deploy), and the service itself runs as `coai-bugs`, which owns only
# its data directory.
SYSTEMCTL="sudo -n systemctl"

say() { printf '[coai-bugs-release] %s\n' "$*" >&2; }
die() { say "$*"; exit 1; }

# One deploy at a time. Two racing would interleave a trail write with a symlink swap.
exec 9>"$ROOT/.release.lock"
flock -n 9 || die "another release is running"

# ---------------------------------------------------------------------------
# The canary: what proves this build works, rather than that it started.
# ---------------------------------------------------------------------------
canary() {
    local release=$1

    # 1. The database, through the real binary, on a throwaway directory so a broken build cannot
    #    touch the live corpus on its way to being rejected.
    local scratch
    scratch=$(mktemp -d)
    # shellcheck disable=SC2064
    trap "rm -rf '$scratch'" RETURN

    if ! COAI_BUGS_SECRET=canary-not-a-real-secret COAI_BUGS_DATA="$scratch" \
        "$release/$SERVICE" --waiting >/dev/null 2>&1; then
        say "the build cannot open a database — check that e_sqlite3 sits beside the binary"
        return 1
    fi

    # 2. The keyword list, which only a listening server can prove it has.
    for attempt in $(seq 1 30); do
        if curl -fsS --max-time 2 "http://127.0.0.1:$PORT/health" | grep -q ok; then
            say "canary: the service answers /health and opens its database"
            return 0
        fi
        $SYSTEMCTL is-active --quiet "$SERVICE" || {
            say "the unit stopped during the canary"
            journalctl -u "$SERVICE" -n 30 --no-pager >&2 || true
            return 1
        }
        sleep 1
    done

    say "the service never answered /health in 30s"
    journalctl -u "$SERVICE" -n 30 --no-pager >&2 || true
    return 1
}

# ONE rename. A `cp` over a running binary is a partially written file somebody executes.
switch_to() {
    local release=$1
    [[ -x "$release/$SERVICE" ]] || { say "no $SERVICE binary in $release"; return 1; }

    ln -sfn "$release" "$LIVE.new"
    mv -Tf "$LIVE.new" "$LIVE"
    $SYSTEMCTL restart "$SERVICE" || { say "the unit refused to restart on $release"; return 1; }
    canary "$release"
}

rollback() {
    local previous
    # TWO entries are required, and the count is checked rather than assumed. `tail -n 2 | head -n 1`
    # on a trail holding ONE line returns that line — the release being rolled back OUT of — so the
    # first deployment would have "rolled back" onto itself, restarted the rejected build, and called
    # that a recovery. It is the first deployment where a canary failure is most likely.
    local entries
    entries=$(wc -l < "$TRAIL" 2>/dev/null || echo 0)
    previous=""
    if [[ "$entries" -ge 2 ]]; then
        previous=$(tail -n 2 "$TRAIL" | head -n 1)
    fi

    if [[ -z "$previous" || ! -d "$previous" ]]; then
        say "no earlier deployment to roll back to — stopping $SERVICE rather than leaving a rejected build serving"
        $SYSTEMCTL stop "$SERVICE" || true
        return 1
    fi

    say "rolling back to $previous"
    # The trail loses its last entry FIRST, so a second consecutive rollback reads correctly.
    sed -i '$d' "$TRAIL"
    # `switch_to … || say …` returned the status of `say`, which is 0. So a rollback whose canary
    # also failed exited 0, `--rollback` reported success to the workflow, and the one outcome
    # worth shouting about — the service is DOWN and nothing here could bring it back — arrived as
    # a green step. The message is the same; the STATUS is now the truth. (CodeRabbit, #328.)
    if ! switch_to "$previous"; then
        say "the rollback to $previous did not start either — $SERVICE is DOWN"
        return 1
    fi
}

retain() {
    # Keep the newest $KEEP, remove the rest. The trail is the order, not the mtime: a directory
    # restored from a backup has a misleading timestamp and the trail does not.
    local old
    while read -r old; do
        [[ -d "$old" ]] && rm -rf "$old"
    done < <(tac "$TRAIL" 2>/dev/null | tail -n +$((KEEP + 1)) || true)
    if [[ -f "$TRAIL" ]]; then
        tail -n "$KEEP" "$TRAIL" > "$TRAIL.tmp" && mv -f "$TRAIL.tmp" "$TRAIL"
    fi
}

case "${1:-}" in
    --rollback) mkdir -p "$RELEASES"; rollback; exit $?;;
    --list)
        printf 'live -> %s\n' "$(readlink -f "$LIVE" 2>/dev/null || echo 'nothing')"
        printf 'retained:\n'; cat "$TRAIL" 2>/dev/null || printf '  (none)\n'
        exit 0;;
    # Anything else is a version, handled below. Spelled out rather than left implicit: a
    # `case` whose default is "fall through the bottom" reads as an oversight to everybody
    # who did not write it, and to SonarCloud (S131).
    *) ;;
esac

FROM=""
if [[ "${1:-}" == "--from" ]]; then
    FROM=${2:?--from needs a path}
    shift 2
fi

VERSION=${1:?usage: release.sh [--from <archive|dir>] <version> | --rollback | --list}
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "'$VERSION' is not a version"

mkdir -p "$RELEASES"
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
RELEASE="$RELEASES/$VERSION-$STAMP"
mkdir -p "$RELEASE"

if [[ -n "$FROM" ]]; then
    say "installing $VERSION from $FROM"
    scratch=$(mktemp -d)
    if [[ -d "$FROM" ]]; then
        cp -a "$FROM/." "$scratch/"
    else
        tar xzf "$FROM" -C "$scratch"
    fi

    # The workflow packages `coai-bugs-<version>-<rid>/coai-bugs`, so accept the binary at the top
    # level or exactly one below it.
    found="$scratch"
    [[ -f "$scratch/$SERVICE" ]] || found=$(dirname "$(find "$scratch" -mindepth 2 -maxdepth 2 -name "$SERVICE" -type f | head -1)")
    [[ -f "$found/$SERVICE" ]] || die "$FROM carries no $SERVICE at its top level or one below it"
    cp -a "$found/." "$RELEASE/"
    rm -rf "$scratch"
else
    say "building $VERSION from $ROOT/src (this takes several minutes)"
    dotnet publish "$ROOT/src/src_bugs/src/CoaiBugs.csproj" \
        -c Release -r linux-x64 -p:Version="$VERSION" -o "$RELEASE" \
        || die "the publish failed"
fi

# THE GROUP, PUT BACK, because the copy above took it away.
#
# `releases` is setgid (2750) so that a release directory created under it inherits the group
# `coai-bugs` and the service reaches its binary by GROUP. That is what the ownership model is for:
# the deploy account writes, the service reads, and nothing else has any access at all.
#
# `cp -a "$found/." "$RELEASE/"` undoes it. The trailing `/.` makes cp apply the SOURCE directory's
# mode, group and timestamps to the destination, so `$RELEASE` came out `0755` in the deploy
# account's own group — and the running service then reached the binary through the WORLD bits.
# It worked, for a reason nobody chose, and it would have stopped working the day an archive was
# packed with a directory mode of 0750.
#
# Measured on the host, in a scratch directory, because a claim about setgid is worth checking:
# a plain `mkdir` under the 2750 parent came out `coai-bugs 2755`; after
# `cp -a source/. copied/` the same directory read `root 755`.
#
# `--reference` takes the group from the parent rather than naming it, so this stays correct if the
# service account is ever renamed. `-R` because the FILES have to be group-readable too — the
# binary to execute and `e_sqlite3` to dlopen.
chgrp -R --reference="$RELEASES" "$RELEASE" \
    || say "could not set the group on $RELEASE; the service may not be able to read it"
chmod 0750 "$RELEASE"

# INSPECTED before it is trusted, because a release that ships without this is the one defect this
# server cannot report about itself.
[[ -f "$RELEASE/$SERVICE" ]] || die "$RELEASE carries no $SERVICE"
ls "$RELEASE"/*e_sqlite3* >/dev/null 2>&1 \
    || die "$RELEASE carries no e_sqlite3 — every request reads the key table, so this build would answer 401 to everybody"
chmod +x "$RELEASE/$SERVICE"

# `readlink` WITHOUT -f, and the distinction matters: `-f` resolves a path and answers one even when
# nothing is there, so on a first deployment PREVIOUS came back non-empty and the failure path took
# the rollback branch instead of the "nothing to roll back to" one. Plain `readlink` answers only for
# a symlink that exists.
PREVIOUS=$(readlink "$LIVE" 2>/dev/null || true)
printf '%s\n' "$RELEASE" >> "$TRAIL"

if switch_to "$RELEASE"; then
    retain
    say "live: $VERSION ($RELEASE)"
    "$RELEASE/$SERVICE" --waiting 2>&1 | tail -1 >&2 || true
else
    say "the canary rejected $VERSION"
    if [[ -n "$PREVIOUS" ]]; then
        rollback
    else
        sed -i '$d' "$TRAIL" 2>/dev/null || true
        $SYSTEMCTL stop "$SERVICE" || true
        say "nothing to roll back to; $SERVICE is stopped rather than serving a rejected build"
    fi
    exit 1
fi
