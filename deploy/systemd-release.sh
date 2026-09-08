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
switch_to() {
    local release=$1
    [[ -x "$release/$SERVICE" ]] || die "no $SERVICE binary in $release"
    ln -sfn "$release" "$LIVE.next"
    mv -Tf "$LIVE.next" "$LIVE"
    systemctl restart "$SERVICE"
}

# ── the canary ───────────────────────────────────────────────────────────────────────────
# `systemctl is-active` and /api/health say the process started. They said exactly that every
# day this server ran without ever completing a single review. What proves a release is one
# real review per configured vendor, reaching `done` with a non-empty answer.
# A path beats a value: COAI_TOKEN_FILE keeps the credential out of the process table and out
# of shell history, which COAI_TOKEN=… on the command line cannot.
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

    id=$(curl -sS -K "$cfg" -X POST "$URL/api/reviews" -H 'Content-Type: application/json' \
        -d "{\"vendor\":\"$vendor\",\"model\":\"$model\",\"prompt\":\"Answer with exactly this JSON and nothing else: {\\\"findings\\\":[]}\",\"role\":\"PlanCritique\",\"timeoutSeconds\":$budget}" \
        | python3 -c 'import sys,json;print(json.load(sys.stdin).get("id",""))')
    [[ -n "$id" && "$id" != "None" ]] || { say "  $vendor: the server refused the review"; return 1; }

    # Polled to a TERMINAL state, never judged on one bounded wait: a `wait` shorter than the
    # review's own budget returns `running` for a healthy slow reviewer, and calling that a
    # failed release rolls back a deployment that was fine. (codex, code round.)
    waited=0
    while [[ "$waited" -lt "$((budget + 30))" ]]; do
        body=$(curl -sS -K "$cfg" "$URL/api/reviews/$id?wait=20")
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

roll_back_to() {
    local previous=$1
    [[ -n "$previous" ]] || return 0
    trail_pop
    switch_to "$previous"
    say "rolled back to $previous"
}

# ── commands ─────────────────────────────────────────────────────────────────────────────
case "${1:-}" in
    --list)
        say "live:     $(readlink -f "$LIVE" 2>/dev/null || echo "$LIVE (not a symlink yet)")"
        say "retained:"
        nl -ba "$TRAIL" 2>/dev/null || say "  (no trail yet)"
        exit 0
        ;;
    --rollback)
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
        die "usage: $0 <version> | --rollback | --list"
        ;;
    --*)
        die "unknown option '$1' — usage: $0 <version> | --rollback | --list"
        ;;
    *)
        : # a version to release; handled below
        ;;
esac

VERSION=$1
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
RELEASE="$RELEASES/$VERSION-$STAMP"
mkdir -p "$RELEASES"

say "publishing $VERSION → $RELEASE"
dotnet publish "$ROOT/src/src_server/src/CoaiServer.csproj" -c Release -o "$RELEASE" -p:Version="$VERSION"

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
FAILED=0
while read -r vendor model; do
    canary "$vendor" "$model" || FAILED=1
done < <(vendors_from_config)

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
