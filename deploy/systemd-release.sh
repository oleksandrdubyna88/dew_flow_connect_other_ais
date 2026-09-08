#!/usr/bin/env bash
# Release (and roll back) the Team server on a systemd host — coai.remsoft.dev's shape.
#
# The container shape has update.sh next door; this is the one for a host that runs the
# Native AOT binary under systemd. Both obey the same family rule
# (.claude/rules/shared/common/development-workflow.md): an immutable version-addressed
# artefact, at least the last three retrievable, a rollback that BUILDS NOTHING and is one
# command, and a look inside the artefact before it is trusted.
#
#   ./systemd-release.sh 0.5.4       publish, inspect, switch, canary — rolls itself back on failure
#   ./systemd-release.sh --from <path> 0.5.4
#                                    the same, from an artefact CI already built for this RID —
#                                    a directory or the release .tar.gz. What deploy-server.yml uses.
#   ./systemd-release.sh --rollback  pop one deployment off the trail, no build, seconds
#   ./systemd-release.sh --list      what is retained and what is live
#
# The canary needs a Team-server session token. Give it as COAI_TOKEN_FILE — a path to a 0600
# file holding the token — in preference to COAI_TOKEN, because a value on the command line
# reaches `ps aux` and `~/.bash_history` and a path does not. Either way curl reads it from a
# config file rather than from argv.
set -euo pipefail

ROOT=${COAI_ROOT:-/opt/coai}
RELEASES="$ROOT/releases"
TRAIL="$RELEASES/.trail"
LIVE="$ROOT/bin"
KEEP=4                                  # the current deployment plus the three the rule requires
SERVICE=coai-server
URL=${COAI_URL:-https://coai.remsoft.dev}

say() { printf '%s\n' "$*" >&2; }
die() { printf 'release: %s\n' "$*" >&2; exit 1; }

# ── the trail ────────────────────────────────────────────────────────────────────────────
# Newest first, one release directory per line. A rollback POPS from it rather than
# overwriting a single "previous" pointer: the measured defect in this family was a state file
# that recorded only where it came from, so a second consecutive rollback returned to the place
# it had just left.
trail_push() {
    local entry=$1
    printf '%s\n%s' "$entry" "$(cat "$TRAIL" 2>/dev/null || true)" | sed '/^$/d' >"$TRAIL.tmp"
    mv -f "$TRAIL.tmp" "$TRAIL"
}

trail_at() {
    local index=$1
    sed -n "$((index + 1))p" "$TRAIL" 2>/dev/null || true
}

trail_pop() { sed -i '1d' "$TRAIL"; }

# ── switching ────────────────────────────────────────────────────────────────────────────
# `bin` is a SYMLINK to a release directory, swapped with `ln -sfn` + `mv -T`, which is one
# rename syscall: there is no instant at which the tree is half-new. Copying file by file over
# the live directory — what this used to do — leaves old and new assemblies mixed if the copy
# is interrupted, and the unit then starts a combination nobody has ever tested.
#
# Returns non-zero rather than dying when the restart fails: under `set -e` a `die` here would
# leave the script with $LIVE already pointing at the new release and no rollback run at all.
# Every caller checks. (CodeRabbit, PR 93.)
switch_to() {
    local release=$1
    [[ -x "$release/$SERVICE" ]] || { say "no $SERVICE binary in $release"; return 1; }
    ln -sfn "$release" "$LIVE.next"
    mv -Tf "$LIVE.next" "$LIVE"
    systemctl restart "$SERVICE" || { say "the unit refused to restart on $release"; return 1; }
}

# ── the canary ───────────────────────────────────────────────────────────────────────────
# `systemctl is-active` and /api/health say the process started. They said exactly that every
# day this server ran without ever completing a single review. What proves a release is one
# real review per configured vendor, reaching `done` with a non-empty answer.

# The canary carries a live session token, so the destination is CHECKED rather than trusted:
# COAI_URL is an override, and an override that can name any host is a way to make this script
# post the company's token somewhere else. (CodeRabbit, PR 93.)
require_https_origin() {
    [[ "$URL" =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?/?$ ]] \
        || die "COAI_URL must be a bare https origin, not '$URL' — the canary sends a session token to it"
}

# A path beats a value: COAI_TOKEN_FILE keeps the credential out of the process table and out of
# shell history, which COAI_TOKEN=… on the command line cannot.

session_token() {
    if [[ -n "${COAI_TOKEN_FILE:-}" ]]; then
        [[ -r "$COAI_TOKEN_FILE" ]] || die "COAI_TOKEN_FILE is set but $COAI_TOKEN_FILE cannot be read"
        tr -d '\r\n' <"$COAI_TOKEN_FILE"
        return 0
    fi

    printf '%s' "${COAI_TOKEN:?neither COAI_TOKEN_FILE nor COAI_TOKEN is set — the canary needs a session token}"
}

canary() {
    local vendor=$1 model=$2 budget=${3:-180} id status body waited cfg
    cfg=$(mktemp)
    chmod 600 "$cfg"
    printf 'header = "Authorization: Bearer %s"\n' "$(session_token)" >"$cfg"
    trap 'rm -f "$cfg"' RETURN

    id=$(curl -sS --connect-timeout 10 --max-time 30 -K "$cfg" -X POST "$URL/api/reviews" -H 'Content-Type: application/json' \
        -d "{\"vendor\":\"$vendor\",\"model\":\"$model\",\"prompt\":\"Answer with exactly this JSON and nothing else: {\\\"findings\\\":[]}\",\"role\":\"PlanCritique\",\"timeoutSeconds\":$budget}" \
        | python3 -c 'import sys,json;print(json.load(sys.stdin).get("id",""))')
    [[ -n "$id" && "$id" != "None" ]] || { say "  $vendor: the server refused the review"; return 1; }

    # Polled to a TERMINAL state, never judged on one bounded wait: a `wait` shorter than the
    # review's own budget returns `running` for a healthy slow reviewer, and calling that a
    # failed release rolls back a deployment that was fine. (codex, code round.)
    waited=0
    while [[ "$waited" -lt "$((budget + 30))" ]]; do
        # --max-time comfortably past the 20s long poll: a stalled connection would otherwise
        # hold here for ever and `waited` would never advance. (CodeRabbit, PR 93.)
        body=$(curl -sS --connect-timeout 10 --max-time 45 -K "$cfg" "$URL/api/reviews/$id?wait=20") || {
            say "  $vendor: the poll did not come back; treating as a failure"
            return 1
        }
        status=$(printf '%s' "$body" | python3 -c 'import sys,json;print(json.load(sys.stdin)["status"])')
        case "$status" in
            done)
                printf '%s' "$body" \
                    | python3 -c 'import sys,json;sys.exit(0 if json.load(sys.stdin)["answer"].strip() else 1)' \
                    && { say "  $vendor: done"; return 0; }
                say "  $vendor: done with an EMPTY answer"
                return 1
                ;;
            failed)
                say "  $vendor: failed — $(printf '%s' "$body" | python3 -c 'import sys,json;print(json.load(sys.stdin)["reason"][:160])')"
                return 1
                ;;
            *)
                : # queued or running — keep polling until the budget is spent
                ;;
        esac
        waited=$((waited + 20))
    done

    say "  $vendor: still $status after ${waited}s — inconclusive, treating as a failure"
    return 1
}

# Every vendor the catalog offers, not a hard-coded one: a canary that asked only about claude
# would have missed the two whose CLIs need a schema file on disk, which is exactly what happened.
vendors_from_config() {
    python3 -c '
import json, sys
for v in json.load(open(sys.argv[1])):
    print(v["id"], v["models"][0])
' "$ROOT/data/vendors.json"
}

# With no predecessor there is nothing to go back TO, and leaving the rejected build serving is
# the one outcome worse than being down: it answers, wrongly, and every signal reads healthy. So
# the unit is stopped and the trail emptied, which `--list` then says plainly.
# (CodeRabbit, PR 93.)
roll_back_to() {
    local previous=$1
    if [[ -z "$previous" ]]; then
        say "no earlier deployment to roll back to — stopping $SERVICE rather than leaving a rejected build serving"
        systemctl stop "$SERVICE" || true
        : >"$TRAIL"
        return 0
    fi

    trail_pop
    switch_to "$previous" || say "the rollback to $previous did not start either — $SERVICE is DOWN"
    say "rolled back to $previous"
}

# One release or rollback at a time. Two concurrent runs can interleave a trail write with a
# symlink swap and leave the trail describing an order that never happened, after which a rollback
# selects the wrong release. (CodeRabbit, PR 93.)
take_the_lock() {
    mkdir -p "$RELEASES"
    exec 9>"$RELEASES/.lock"
    flock -n 9 || die "another release or rollback is running (it holds $RELEASES/.lock)"
}

# ── commands ─────────────────────────────────────────────────────────────────────────────
# ── where the artefact comes from ────────────────────────────────────────────────────────
# Empty means BUILD it here, which is how this script started and stays the default: an offline
# or air-gapped release must remain possible, and so must releasing a commit no tag names.
#
# `--from` takes what CI already built, tested and smoked for this RID — a directory, or the
# release archive itself. That matters on this host specifically: the unit next door carries
# `MemoryMax=1500M` on a 3.8 GB box, and a Native AOT link is the heaviest thing that ever runs
# here. The artefact inspection below is deliberately NOT skipped for this path; it matters more,
# because a download landing the wrong file is a failure a build cannot have.
FROM=""
if [[ "${1:-}" == "--from" ]]; then
    FROM=${2:-}
    [[ -n "$FROM" ]] || die "--from needs a directory or a .tar.gz — usage: $0 --from <path> <version>"
    [[ -e "$FROM" ]] || die "--from $FROM does not exist"
    shift 2
    [[ "${1:-}" != --* ]] || die "--from takes a version, not '$1'"
fi

# Copy a published artefact into the new release directory. An archive is unpacked past its top
# level, which is the shape `release.yml` packages: `coai-server-<version>-<rid>/coai-server`.
stage_from() {
    local source=$1 target=$2
    mkdir -p "$target"
    if [[ -d "$source" ]]; then
        cp -a "$source/." "$target/"
    else
        tar xzf "$source" -C "$target" --strip-components=1 \
            || die "$source did not unpack — is it the release .tar.gz?"
    fi

    # Said HERE rather than left to the inspection below, because the two failures need different
    # sentences: "the archive had no top-level directory, so --strip-components=1 removed
    # everything" is a packaging mistake, and "the binary does not contain this version" is a wrong
    # download. One message for both would send the reader to the wrong half.
    [[ -f "$target/$SERVICE" ]] \
        || die "$source produced no $SERVICE in $target — a flat archive, or the wrong file"
    # No `|| true`: a staged binary that cannot be made executable is a release that will not
    # start, and swallowing it here means finding out from `switch_to` two steps later.
    chmod +x "$target/$SERVICE" || die "cannot make $target/$SERVICE executable"
}

case "${1:-}" in
    --list)
        say "live:     $(readlink -f "$LIVE" 2>/dev/null || echo "$LIVE (not a symlink yet)")"
        say "retained:"
        nl -ba "$TRAIL" 2>/dev/null || say "  (no trail yet)"
        exit 0
        ;;
    --rollback)
        take_the_lock
        [[ -s "$TRAIL" ]] || die "no trail — nothing to roll back to"
        PREVIOUS=$(trail_at 1)
        [[ -n "$PREVIOUS" ]] || die "the trail holds only the live deployment — nothing further back"
        [[ -d "$PREVIOUS" ]] || die "$PREVIOUS is gone; pick one from --list by hand"
        say "rolling back to $PREVIOUS"
        roll_back_to "$PREVIOUS"
        sleep 3
        curl -sS "$URL/api/health"
        echo
        exit 0
        ;;
    "")
        die "usage: $0 [--from <path>] <version> | --rollback | --list"
        ;;
    --*)
        die "unknown option '$1' — usage: $0 [--from <path>] <version> | --rollback | --list"
        ;;
    *)
        : # a version to release; handled below
        ;;
esac

# One version and nothing else. `--from X 0.5.5 --canary-only` used to run a plain release of
# 0.5.5 with the third argument dropped in silence, which is the shape of a flag that was typed,
# ignored, and believed.
[[ $# -eq 1 ]] || die "expected one version, got: $*"

VERSION=$1
take_the_lock
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
RELEASE="$RELEASES/$VERSION-$STAMP"
mkdir -p "$RELEASES"

if [[ -n "$FROM" ]]; then
    say "staging $VERSION from $FROM → $RELEASE"
    stage_from "$FROM" "$RELEASE"
else
    say "publishing $VERSION → $RELEASE"
    dotnet publish "$ROOT/src/src_server/src/CoaiServer.csproj" -c Release -o "$RELEASE" -p:Version="$VERSION"
fi

# Look INSIDE the artefact before trusting it. A build that reported success while silently
# copying nothing is the measured trap this rule exists for, and a clean `0 error(s)` is not
# evidence about the file that is about to serve.
say "inspecting the artefact"
[[ -x "$RELEASE/$SERVICE" ]] || die "no binary at $RELEASE/$SERVICE"
grep -qa -- "$VERSION" "$RELEASE/$SERVICE" \
    || die "$SERVICE does not contain the string '$VERSION' — a stale or wrong build"

trail_push "$RELEASE"
PREVIOUS=$(trail_at 1)
switch_to "$RELEASE"
sleep 3

if ! systemctl is-active --quiet "$SERVICE"; then
    say "the unit did not start"
    roll_back_to "$PREVIOUS"
    die "release $VERSION rejected"
fi
say "health: $(curl -sS "$URL/api/health")"

say "canary — one real review per configured vendor"

# Materialised and CHECKED before the loop. In a process substitution a python3 failure is
# invisible to the parent: the loop reads EOF, FAILED stays 0, and the release passes having
# canaried nothing — the exact "green because it never ran" shape this whole change is about.
# (CodeRabbit, PR 93.)
if ! VENDORS=$(vendors_from_config) || [[ -z "$VENDORS" ]]; then
    say "could not read a vendor list from $ROOT/data/vendors.json — nothing to canary"
    roll_back_to "$PREVIOUS"
    die "release $VERSION rejected"
fi

FAILED=0
while read -r vendor model; do
    canary "$vendor" "$model" || FAILED=1
done <<<"$VENDORS"

if [[ "$FAILED" -ne 0 ]]; then
    say "the canary failed; rolling back"
    roll_back_to "$PREVIOUS"
    die "release $VERSION rejected"
fi

# Only now is the oldest droppable — and never below the three the rule requires.
tail -n +$((KEEP + 1)) "$TRAIL" 2>/dev/null | while read -r old; do
    [[ -d "$old" ]] && rm -rf "$old" && say "pruned $old"
done
head -n "$KEEP" "$TRAIL" >"$TRAIL.tmp" && mv -f "$TRAIL.tmp" "$TRAIL"

say "released $VERSION"
